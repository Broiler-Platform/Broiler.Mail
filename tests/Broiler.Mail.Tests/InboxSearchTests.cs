using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class InboxSearchTests
{
    [Fact]
    public async Task SearchReadsSavedBodiesAndNeverFetchesMissingBodiesOrChangesServerMail()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var one = Row(mailbox, 1); var two = Row(mailbox, 2);
        await cache.SaveSummariesAsync(mailbox, [one, two], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(one.Key, "saved body"));
        var receiver = new NoNetworkReceiver(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearch("match"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.True(model.IsSearchActive); Assert.Equal(2, model.Messages.Count);
        Assert.False(model.CanReceive); Assert.False(model.CanLoadOlder); Assert.False(model.CanDownloadLoaded);
        await model.ReceiveAsync(); await model.LoadOlderAsync(); await model.DownloadLoadedBodiesAsync();
        await model.SelectAsync(one.Key); dispatcher.Drain(); Assert.Equal("saved body", model.Body!.PlainText);
        await model.SelectAsync(two.Key); dispatcher.Drain(); Assert.Null(model.Body);
        Assert.Contains("not saved", model.MessageProblem!.Text);
        Assert.Contains("sender and subject only", model.SearchNotice);
        Assert.Equal(0, receiver.Calls);
        await model.ClearSearchAsync(); dispatcher.Drain();
        Assert.False(model.IsSearchActive); Assert.True(model.CanReceive); Assert.Equal(2, model.Messages.Count);
    }

    [Fact]
    public async Task DisabledAccountCanSearchAndFiltersAreAppliedWithoutClaimingAnEmptyMailbox()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile() with { IsEnabled = false };
        var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1), Row(mailbox, 2) with { IsRead = true },
            Row(mailbox, 3) with { IsStarred = false }], DateTimeOffset.UtcNow);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearch("match", true, true); Assert.True(model.CanSearch);
        await model.SearchSavedHeadersAsync(); dispatcher.Drain(); Assert.Equal(1u, Assert.Single(model.Messages).Key.Uid);
        model.SetSearch("absent"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Empty(model.Messages); Assert.False(model.HasLoaded); Assert.Contains("0 saved headers matched", model.Status);
        Assert.False(model.CanReceive);
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; dispatcher.Drain();
        Assert.False(model.IsSearchActive); Assert.Equal("", model.SearchText); Assert.Empty(model.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrAccountSwitchDiscardsLateSearchResults(bool switchAccount)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var storage = new SqliteMailCache(directory.File("mail.db")); var cache = new DelayedSearchCache(storage);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); model.SetSearch("match");
        var pending = model.SearchSavedHeadersAsync(); await cache.Started.Task;
        if (switchAccount) { model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; dispatcher.Drain(); }
        else model.Cancel();
        cache.Release.SetResult(new([Row(new(account.Id, "INBOX", 7), 1)], false));
        await pending; dispatcher.Drain(); Assert.Empty(model.Messages); Assert.False(model.IsBusy);
        if (switchAccount) Assert.False(model.IsSearchActive);
        else { Assert.True(model.IsSearchActive); Assert.True(model.CanRetryIn(InboxProblemScope.List)); }
    }

    [Fact]
    public async Task RollingDateFilterIsSubmittedExplicitlyAndResetsWithAccount()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var now = DateTimeOffset.UtcNow;
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1) with { ReceivedAt = now.AddDays(-1) },
            Row(mailbox, 2) with { ReceivedAt = now.AddDays(-10) }, Row(mailbox, 3),
            Row(mailbox, 4) with { ReceivedAt = now.AddDays(1) }], now);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); model.SetSearch("match");
        model.SetSearchReceivedWithinDays(7); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(1u, Assert.Single(model.Messages).Key.Uid); Assert.Contains("unknown dates excluded", model.SearchNotice);
        model.SetSearchReceivedWithinDays(null); Assert.Single(model.Messages);
        await model.SearchSavedHeadersAsync(); dispatcher.Drain(); Assert.Equal(4, model.Messages.Count);
        Assert.Contains("including unknown dates", model.SearchNotice);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.SetSearchReceivedWithinDays(8));
        model.SetSearchReceivedWithinDays(30); model.SetAccount(account with { Id = AccountId.New() });
        await model.CacheLoadTask; dispatcher.Drain(); Assert.Null(model.SearchReceivedWithinDays);
    }

    [Fact]
    public async Task NextPageAndRetryKeepSubmittedQueryInsteadOfEditedControls()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var storage = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var rows = Enumerable.Range(1, 501).Select(uid => Row(mailbox, (uint)uid) with { ReceivedAt = DateTimeOffset.UtcNow.AddDays(-1) }).ToArray();
        await storage.SaveSummariesAsync(mailbox, rows.Take(500).ToArray(), DateTimeOffset.UtcNow);
        await storage.SaveSummariesAsync(mailbox, rows.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        var cache = new DelayedSearchCache(storage, false); var receiver = new NoNetworkReceiver(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); model.SetSearch("match", true, true); model.SetSearchField(MailHeaderSearchField.Subject); model.SetSearchBodyAvailability(false); model.SetSearchCustomDateRange("0001-01-01..9999-12-31");
        await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(500, model.Messages.Count); Assert.Equal(1, model.SearchPageNumber); Assert.True(model.CanLoadNextSearchPage);
        var first = cache.Queries.Single(); model.SetSearchBodyAvailability(true); model.SetSearchField(MailHeaderSearchField.Sender); model.SetSearch("", false, false); model.SetSearchReceivedWithinDays(null);
        cache.FailNext = true; await model.LoadNextSearchPageAsync(); dispatcher.Drain(); Assert.NotNull(model.ListProblem);
        Assert.True(model.CanRetryIn(InboxProblemScope.List));
        await model.RetryAsync(InboxProblemScope.List); dispatcher.Drain();
        Assert.Equal(1u, Assert.Single(model.Messages).Key.Uid); Assert.Equal(2, model.SearchPageNumber); Assert.False(model.CanLoadNextSearchPage);
        Assert.Equal(first with { BeforeMessage = rows[1].Key }, cache.Queries[1]);
        Assert.Equal(cache.Queries[1], cache.Queries[2]); Assert.Equal(0, receiver.Calls);
        Assert.True(model.CanLoadPreviousSearchPage); await model.LoadPreviousSearchPageAsync(); dispatcher.Drain();
        Assert.Equal(500, model.Messages.Count); Assert.Equal(1, model.SearchPageNumber); Assert.False(model.CanLoadPreviousSearchPage);
        Assert.Equal(first, cache.Queries[^1]); await model.LoadNextSearchPageAsync(); dispatcher.Drain();
        Assert.Contains("match", model.SearchNotice); Assert.Contains("Page 2", model.SearchNotice);
        model.SetSearch("match"); model.SetSearchBodyAvailability(false); model.SetSearchField(MailHeaderSearchField.Subject); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(1, model.SearchPageNumber); Assert.Equal(500, model.Messages.Count); Assert.Null(cache.Queries[^1].BeforeMessage);
    }

    [Fact]
    public async Task FilterOnlyAndFieldSearchAreExplicitAndResetWithAccount()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1) with { Sender = "needle" },
            Row(mailbox, 2) with { Subject = "needle", IsRead = true }], DateTimeOffset.UtcNow);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); Assert.False(model.CanSearch);
        model.SetSearch(" ", true); Assert.True(model.CanSearch); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(1u, model.Messages.Single().Key.Uid); Assert.Contains("No keyword", model.SearchNotice);
        model.SetSearch("needle"); model.SetSearchField(MailHeaderSearchField.Sender); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(1u, model.Messages.Single().Key.Uid); Assert.Contains("sender only", model.SearchNotice);
        model.SetSearchField(MailHeaderSearchField.Subject); Assert.Equal(1u, model.Messages.Single().Key.Uid);
        await model.SearchSavedHeadersAsync(); dispatcher.Drain(); Assert.Equal(2u, model.Messages.Single().Key.Uid);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.SetSearchField((MailHeaderSearchField)99));
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; dispatcher.Drain();
        Assert.Equal(MailHeaderSearchField.SenderOrSubject, model.SearchField); Assert.False(model.CanSearch);
    }

    [Fact]
    public async Task BodySearchFindsOnlyDownloadedPreviewAndExplainsItsCoverage()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var one = Row(mailbox, 1); var two = Row(mailbox, 2) with { Subject = "body needle" };
        await cache.SaveSummariesAsync(mailbox, [one, two], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(one.Key, "body needle") { IsTruncated = true });
        var receiver = new NoNetworkReceiver(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearch("needle"); model.SetSearchField(MailHeaderSearchField.BodyPreview);
        await model.SearchSavedHeadersAsync(); dispatcher.Drain(); Assert.Equal(one.Key, model.Messages.Single().Key);
        Assert.Contains("32,000", model.SearchNotice); Assert.Contains("uncached bodies", model.SearchNotice);
        await model.SelectAsync(one.Key); dispatcher.Drain(); Assert.True(model.Body!.IsTruncated); Assert.True(model.IsBodyFromCache);
        model.SetSearchField(MailHeaderSearchField.HeadersOrBodyPreview); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(2, model.Messages.Count); Assert.Equal(0, receiver.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("2026-02-30..2026-03-01")]
    [InlineData("11/01/2026..11/02/2026")]
    [InlineData("2026-11-02..2026-11-01")]
    [InlineData("2026-11-01..2026-11-01")]
    [InlineData("2026-11-01...2026-11-02")]
    [InlineData("2026-11-01..2026-11-02\n")]
    public async Task InvalidCustomDatesDisableSubmissionWithoutChangingExistingResults(string value)
    {
        using var directory = new TestDirectory(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, new SqliteMailCache(directory.File("mail.db")));
        model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; dispatcher.Drain(); model.SetSearch("match");
        model.SetSearchCustomDateRange(value); Assert.NotNull(model.SearchDateError); Assert.False(model.CanSearch);
        await model.SearchSavedHeadersAsync(); Assert.False(model.IsSearchActive); Assert.False(model.IsBusy);
        model.SetSearchReceivedWithinDays(7); Assert.Null(model.SearchDateError); Assert.True(model.CanSearch);
    }

    [Fact]
    public async Task CustomUtcCalendarBoundsSupportOpenEndsAndResetWithAccount()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1) with { ReceivedAt = start },
            Row(mailbox, 2) with { ReceivedAt = start.AddDays(1) }, Row(mailbox, 3)], start);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearchCustomDateRange("2026-11-01..2026-11-02"); Assert.True(model.CanSearch);
        await model.SearchSavedHeadersAsync(); dispatcher.Drain(); Assert.Equal(1u, model.Messages.Single().Key.Uid);
        model.SetSearchCustomDateRange("2026-11-02.."); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(2u, model.Messages.Single().Key.Uid); Assert.Contains("any later date", model.SearchNotice);
        model.SetSearchCustomDateRange("..2026-11-02"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(1u, model.Messages.Single().Key.Uid); Assert.Contains("any earlier date", model.SearchNotice);
        model.SetSearchCustomDateRange("2024-02-29..2024-03-01"); Assert.Null(model.SearchDateError);
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; dispatcher.Drain();
        Assert.False(model.SearchUsesCustomDates); Assert.Equal("", model.SearchCustomDateRange);
    }

    [Fact]
    public async Task ThreeFilterControlsWorkWithoutKeywordAndResetWithAccount()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var one = Row(mailbox, 1) with { IsRead = true, IsStarred = false }; var two = Row(mailbox, 2);
        await cache.SaveSummariesAsync(mailbox, [one, two], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(mailbox, new(one.Key, "body"));
        var receiver = new NoNetworkReceiver(); var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(receiver, dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); model.SetSearchFilters(true, false, true); Assert.True(model.CanSearch);
        await model.SearchSavedHeadersAsync(); dispatcher.Drain(); Assert.Equal(one.Key, model.Messages.Single().Key);
        Assert.Contains("Read only", model.SearchNotice); Assert.Contains("Unstarred only", model.SearchNotice); Assert.Contains("Downloaded bodies only", model.SearchNotice);
        model.SetSearchText("body"); Assert.Equal(true, model.SearchReadState); Assert.Equal(false, model.SearchStarState); Assert.Equal(true, model.SearchBodyCached);
        model.SetSearchText(""); model.SetSearchFilters(null, null, false); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(two.Key, model.Messages.Single().Key); Assert.Contains("Bodies not downloaded", model.SearchNotice); Assert.Equal(0, receiver.Calls);
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; dispatcher.Drain();
        Assert.Null(model.SearchReadState); Assert.Null(model.SearchStarState); Assert.Null(model.SearchBodyCached); Assert.False(model.CanSearch);
    }

    internal sealed class NoNetworkReceiver : IMailReceiver
    {
        public int Calls { get; private set; }
        private Exception Called() { Calls++; return new InvalidOperationException("Unexpected network request."); }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw Called();
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw Called();
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw Called();
    }

    private sealed class DelayedSearchCache(SqliteMailCache storage, bool delay = true) : IMailCache, IMailLocalSearch
    {
        public bool FailNext { get; set; }
        public List<MailHeaderSearchQuery> Queries { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MailHeaderSearchResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MailHeaderSearchResult> SearchHeadersAsync(MailHeaderSearchQuery query, CancellationToken token = default)
        {
            Queries.Add(query); Started.TrySetResult();
            if (FailNext) { FailNext = false; throw new IOException("Synthetic search read failure."); }
            return delay ? Release.Task : storage.SearchHeadersAsync(query, token);
        }
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account, string mailbox, CancellationToken token = default) => storage.GetLatestMailboxAsync(account, mailbox, token);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox, int count, uint? after = null, CancellationToken token = default) => storage.ReadSummariesAsync(mailbox, count, after, token);
        public Task SaveSummariesAsync(MailboxKey mailbox, IReadOnlyList<MailMessageSummary> rows, DateTimeOffset time, CancellationToken token = default) => storage.SaveSummariesAsync(mailbox, rows, time, token);
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox, uint uid, CancellationToken token = default) => storage.ReadBodyAsync(mailbox, uid, token);
        public Task SaveBodyAsync(MailboxKey mailbox, MailMessageBody body, CancellationToken token = default) => storage.SaveBodyAsync(mailbox, body, token);
    }

    private static MailMessageSummary Row(MailboxKey mailbox, uint uid) => new()
    { Key = new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, uid), Sender = "Sender", Subject = "match", IsStarred = true };
}
