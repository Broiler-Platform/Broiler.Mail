using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class SearchPagingSmokeCheck
{
    internal static async Task RunAsync(string path, AccountProfile account)
    {
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var rows = Enumerable.Range(1, 501).Select(uid => new MailMessageSummary
        {
            Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "Synthetic sender", Subject = "Paging fixture",
            ReceivedAt = DateTimeOffset.UtcNow.AddDays(-1), IsStarred = true,
        }).ToArray();
        await cache.SaveSummariesAsync(mailbox, rows.Take(500).ToArray(), DateTimeOffset.UtcNow);
        await cache.SaveSummariesAsync(mailbox, rows.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(rows[0].Key, "Older saved paging body") { Composition = new() { MessageId = "paging@example.test" } });
        using var model = new InboxViewModel(new NoNetworkReceiver(), new ImmediateUiDispatcher(), new SqliteMailCache(path));
        model.SetAccount(account with { IsEnabled = false }); await model.CacheLoadTask;
        model.SetSearch("Paging", true, true); model.SetSearchField(MailHeaderSearchField.Subject); model.SetSearchReceivedWithinDays(7);
        await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 500 || !model.CanLoadNextSearchPage || model.SearchPageNumber != 1)
            throw new InvalidOperationException("Published saved-search first page failed.");
        model.SetSearchField(MailHeaderSearchField.Sender); model.SetSearch("different edited query"); model.SetSearchReceivedWithinDays(null);
        await model.LoadNextSearchPageAsync();
        if (model.Messages.Single().Key != rows[0].Key || model.CanLoadNextSearchPage || model.SearchPageNumber != 2)
            throw new InvalidOperationException("Published saved-search continuation failed.");
        await model.SelectAsync(rows[0].Key);
        if (model.Body?.PlainText != "Older saved paging body" || !model.IsBodyFromCache)
            throw new InvalidOperationException("Published saved-search continuation body failed.");
        if (!Broiler.Mail.Application.Views.ParsedMessageHeaders.Lines(model.Messages.Single(), model.Body!.Composition, true).Any(line => line == "Message-ID: paging@example.test"))
            throw new InvalidOperationException("Published parsed-header inspection failed.");
        await model.LoadPreviousSearchPageAsync();
        if (model.SearchPageNumber != 1 || model.Messages.Count != 500 || model.CanLoadPreviousSearchPage)
            throw new InvalidOperationException("Published previous search page failed.");
        await model.LoadNextSearchPageAsync();
        if (model.SearchPageNumber != 2 || model.Messages.Single().Key != rows[0].Key)
            throw new InvalidOperationException("Published next page after previous failed.");
        model.SetSearch("Synthetic sender"); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 500) throw new InvalidOperationException("Published sender-only search failed.");
        model.SetSearchField(MailHeaderSearchField.Subject); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 0) throw new InvalidOperationException("Published subject-only search included sender matches.");
        model.SetSearch("", true, true); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 500) throw new InvalidOperationException("Published filter-only search failed.");
        model.SetSearch("Older saved"); model.SetSearchField(MailHeaderSearchField.BodyPreview);
        await model.SearchSavedHeadersAsync();
        if (model.Messages.Single().Key != rows[0].Key) throw new InvalidOperationException("Published body-preview search failed.");
        await model.SelectAsync(rows[0].Key);
        if (model.Body?.PlainText != "Older saved paging body" || !model.IsBodyFromCache)
            throw new InvalidOperationException("Published body-preview search could not open its saved match.");
        model.SetSearch("Paging fixture"); model.SetSearchField(MailHeaderSearchField.HeadersOrBodyPreview);
        await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 500) throw new InvalidOperationException("Published combined header/body search failed.");
        model.SetSearchCustomDateRange("0001-01-01..9999-12-31");
        await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 500 || model.SearchDateError is not null) throw new InvalidOperationException("Published custom UTC dates failed.");
        model.SetSearchCustomDateRange("2026-11-02..2026-11-01");
        if (model.CanSearch || model.SearchDateError is null || model.Messages.Count != 500)
            throw new InvalidOperationException("Published invalid UTC dates changed search results.");
        await cache.UpdateReadStateAsync(rows[0].Key, true); await cache.UpdateStarStateAsync(rows[0].Key, false);
        model.SetSearchReceivedWithinDays(null); model.SetSearchText(""); model.SetSearchField(MailHeaderSearchField.SenderOrSubject);
        model.SetSearchFilters(true, false, true); await model.SearchSavedHeadersAsync();
        if (model.Messages.Single().Key != rows[0].Key) throw new InvalidOperationException("Published read/unstarred/downloaded filters failed.");
        model.SetSearchFilters(true, false, false); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 0) throw new InvalidOperationException("Published missing-body filter ignored saved body.");
        await cache.RebuildSearchIndexAsync();
        model.SetSearchFilters(null, null, null); model.SetSearch("Paging fixture");
        model.SetSearchField(MailHeaderSearchField.IndexedSubject); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 500 || !model.CanLoadNextSearchPage)
            throw new InvalidOperationException("Published FTS5 indexed first page failed.");
        await model.LoadNextSearchPageAsync();
        if (model.Messages.Single().Key != rows[0].Key)
            throw new InvalidOperationException("Published FTS5 indexed continuation failed.");
        model.SetSearch("Older saved"); model.SetSearchField(MailHeaderSearchField.IndexedBodyPreview);
        await model.SearchSavedHeadersAsync();
        if (model.Messages.Single().Key != rows[0].Key)
            throw new InvalidOperationException("Published FTS5 saved-preview search failed.");
        await cache.SaveBodyAsync(mailbox, new(rows[0].Key, "Updated indexed preview"));
        model.SetSearch("Updated indexed"); await model.SearchSavedHeadersAsync();
        if (model.Messages.Single().Key != rows[0].Key || !(await cache.GetSearchIndexInfoAsync()).Ready)
            throw new InvalidOperationException("Published transactional index maintenance failed.");
        await cache.SaveBodyAsync(mailbox, new(rows[1].Key, "Bounded retained-body preview") { RetainedBodyText = new string('x', 34000) + " full retained tail phrase" });
        model.SetSearch("full retained tail phrase"); model.SetSearchSenderText("SYNTHETIC sender"); model.SetSearchField(MailHeaderSearchField.IndexedRetainedBody); await model.SearchSavedHeadersAsync();
        if (model.Messages.Single().Key != rows[1].Key || !model.SearchNotice!.Contains("beyond the preview") || (await cache.GetSearchIndexInfoAsync()).RetainedBodyDocuments != 1)
            throw new InvalidOperationException("Published retained-body search/coverage failed.");
        await model.SelectAsync(rows[1].Key);
        if (model.Body?.PlainText != "Bounded retained-body preview" || !model.IsBodyFromCache) throw new InvalidOperationException("Published retained-body hit changed preview or fetched mail.");
        model.SetSearchSenderText("unmatched sender"); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 0) throw new InvalidOperationException("Published full-body phrase ignored sender filter.");
        model.SetSearchSenderText("SYNTHETIC sender");
        model.SetSearchField(MailHeaderSearchField.IndexedHeadersOrBodyPreview); await model.SearchSavedHeadersAsync();
        if (model.Messages.Count != 0) throw new InvalidOperationException("Published preview search included retained-only text.");
        await cache.DisableSearchIndexAsync();
        if ((await cache.GetSearchIndexInfoAsync()).Ready || (await cache.ReadBodyAsync(mailbox, rows[0].Key.Uid))?.PlainText != "Updated indexed preview")
            throw new InvalidOperationException("Published derived-index removal changed saved mail.");
        await model.ClearSearchAsync();
        if (model.IsSearchActive || model.SearchPageNumber != 0)
            throw new InvalidOperationException("Published saved-search continuation exit failed.");
    }

    private sealed class NoNetworkReceiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in paging fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in paging fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No network in paging fixture.");
    }
}
