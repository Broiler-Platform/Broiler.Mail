using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class FolderNavigationTests
{
    [Fact]
    public async Task SavedFoldersSurviveRestartAndOverlappingUidsNeverMixBodies()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        foreach (string name in new[] { "INBOX", "Archive" })
        {
            var key = new MailboxKey(account.Id, name, 7);
            await cache.SaveSummariesAsync(key, [SqliteMailCacheTests.Summary(key, 1)], DateTimeOffset.UtcNow);
            await cache.SaveBodyAsync(key, new(new(account.Id, name, 7, 1), name + " body"));
        }
        var receiver = new FolderReceiver { Read = (_, _, _, _) => throw new MailConnectionException("Offline") };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), new SqliteMailCache(directory.File("mail.db")));
        model.SetAccount(account); await model.CacheLoadTask;
        Assert.Contains(model.Folders, folder => folder.MailboxId == "Archive");
        Assert.True(model.IsSavedOnlyFolder("Archive"));
        await model.SelectAsync(model.Messages[0].Key); Assert.Equal("INBOX body", model.Body!.PlainText);
        Assert.True(model.SelectFolder("Archive")); Assert.Null(model.Body); await model.CacheLoadTask;
        Assert.Equal("Archive", Assert.Single(model.Messages).Key.MailboxId);
        await model.SelectAsync(model.Messages[0].Key); Assert.Equal("Archive body", model.Body!.PlainText);
        Assert.True(model.IsBodyFromCache);
        await model.ReceiveAsync(); Assert.True(model.IsShowingCachedMail);
        Assert.Equal("Archive", Assert.Single(model.Messages).Key.MailboxId);
        Assert.True(model.SelectFolder("INBOX")); await model.CacheLoadTask;
        await model.SelectAsync(model.Messages[0].Key); Assert.Equal("INBOX body", model.Body!.PlainText);
        Assert.Equal(0, receiver.BodyCalls);
    }

    [Fact]
    public async Task CatalogAndCachedNamesAreAccountScopedIncludingEmptyFolders()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var other = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(new(account.Id, "Saved", 7), [], DateTimeOffset.UtcNow);
        await cache.SaveSummariesAsync(new(other.Id, "Private", 7), [], DateTimeOffset.UtcNow);
        var receiver = new FolderReceiver();
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync();
        Assert.Contains(model.Folders, folder => folder.MailboxId == "Saved");
        Assert.DoesNotContain(model.Folders, folder => folder.MailboxId == "Private");
        Assert.True(model.SelectFolder("Saved")); await model.CacheLoadTask;
        Assert.True(model.HasLoaded); Assert.Empty(model.Messages);
        model.SetAccount(other); await model.CacheLoadTask;
        Assert.Equal("INBOX", model.SelectedMailboxId);
        Assert.DoesNotContain(model.Folders, folder => folder.MailboxId is "Saved" or "Archive");
        Assert.Contains(model.Folders, folder => folder.MailboxId == "Private");
    }

    [Fact]
    public async Task DiscoveryFailureRetriesDiscoveryAndNonselectableFolderCannotBeOpened()
    {
        var receiver = new FolderReceiver(); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(TestDirectory.Profile()); await model.RefreshFoldersAsync();
        Assert.False(model.SelectFolder("Container")); Assert.Equal("INBOX", model.SelectedMailboxId);
        Assert.True(model.SelectFolder("Archive")); await model.ReceiveAsync();
        receiver.Discover = _ => throw new MailConnectionException("Offline");
        await model.RefreshFoldersAsync(); Assert.True(model.ProblemIsFolderDiscovery);
        Assert.Equal("Archive", Assert.Single(model.Messages).Key.MailboxId);
        int reads = receiver.ReadCalls;
        receiver.Discover = _ => Task.FromResult<IReadOnlyList<MailFolder>>([]);
        await model.RetryAsync(InboxProblemScope.List);
        Assert.Equal(reads, receiver.ReadCalls); Assert.Null(model.ListProblem);
        Assert.Equal("Archive", model.SelectedMailboxId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderSwitchDiscardsReceiveOrBodyThatIgnoresCancellation(bool body)
    {
        var receiver = new FolderReceiver(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher);
        var account = TestDirectory.Profile(); model.SetAccount(account);
        await model.RefreshFoldersAsync(); dispatcher.Drain();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task pending;
        if (body)
        {
            await model.ReceiveAsync(); dispatcher.Drain();
            receiver.Body = async key => { started.SetResult(); await release.Task; return new(key, "Stale body"); };
            pending = model.SelectAsync(model.Messages[0].Key);
        }
        else
        {
            receiver.Read = async (profile, name, _, _) => { started.SetResult(); await release.Task; return Page(profile, name); };
            pending = model.ReceiveAsync();
        }
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.SelectFolder("Archive")); Assert.Null(model.Body); Assert.Empty(model.Messages);
        release.SetResult(); await pending; dispatcher.Drain();
        Assert.Null(model.Body); Assert.Empty(model.Messages); Assert.Equal("Archive", model.SelectedMailboxId);
        receiver.Read = (profile, name, _, _) => Task.FromResult(Page(profile, name));
        await model.ReceiveAsync(); dispatcher.Drain();
        Assert.Equal("Archive", Assert.Single(model.Messages).Key.MailboxId);
    }

    [Fact]
    public async Task WrongFolderPageIsRejectedBeforeCacheWrite()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var receiver = new FolderReceiver();
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync();
        model.SelectFolder("Archive"); await model.CacheLoadTask;
        receiver.Read = (profile, _, _, _) => Task.FromResult(Page(profile, "INBOX"));
        await model.ReceiveAsync(); Assert.NotNull(model.ListProblem); Assert.Empty(model.Messages);
        Assert.Null(await cache.GetLatestMailboxAsync(account.Id, "Archive"));
        Assert.Null(await cache.GetLatestMailboxAsync(account.Id, "INBOX"));
    }

    [Fact]
    public async Task DisabledAccountCanReadSavedFolderButCannotDiscoverOrReceive()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile() with { IsEnabled = false };
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(new(account.Id, "Archive", 7), [], DateTimeOffset.UtcNow);
        var receiver = new FolderReceiver(); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask;
        Assert.True(model.HasLoaded); Assert.False(model.CanReceive); Assert.False(model.CanRefreshFolders);
        await model.RefreshFoldersAsync(); await model.ReceiveAsync(); Assert.Equal(0, receiver.ReadCalls);
        model.BeginStorageMaintenance(); Assert.False(model.SelectFolder("INBOX"));
    }

    [Fact]
    public async Task NamedFolderPagingAndExplicitOfflineBatchKeepTheirMailboxScope()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var receiver = new FolderReceiver();
        var requestedBodies = new List<MailMessageKey>();
        receiver.Read = (profile, name, older, _) =>
        {
            Assert.Equal("Archive", name);
            var mailbox = new MailboxKey(profile.Id, name, 7);
            if (older is not null) Assert.Equal("Archive", older.MailboxId);
            return Task.FromResult(new MailInboxPage(older is null
                ? [SqliteMailCacheTests.Summary(mailbox, 3), SqliteMailCacheTests.Summary(mailbox, 2)]
                : [SqliteMailCacheTests.Summary(mailbox, 1)], older is null
                    ? new(profile.Id, 7, 4, 3, 0) { MailboxId = name } : null) { Mailbox = mailbox });
        };
        receiver.Body = key => { requestedBodies.Add(key); return Task.FromResult(new MailMessageBody(key, key.MailboxId)); };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync();
        model.SelectFolder("Archive"); await model.CacheLoadTask; await model.ReceiveAsync(); await model.LoadOlderAsync();
        Assert.Equal(new uint[] { 3, 2, 1 }, model.Messages.Select(message => message.Key.Uid));
        await model.DownloadLoadedBodiesAsync();
        Assert.Equal(3, model.DownloadCompleted); Assert.All(requestedBodies, key => Assert.Equal("Archive", key.MailboxId));
        Assert.Null(await cache.GetLatestMailboxAsync(account.Id, "INBOX"));
        model.SelectFolder("INBOX"); await model.CacheLoadTask; Assert.Empty(model.Messages);
        model.SelectFolder("Archive"); await model.CacheLoadTask;
        Assert.Equal(3, model.Messages.Count); Assert.All(model.Messages, message => Assert.True(message.IsBodyCached));
    }

    [Fact]
    public async Task QueuedDiscoveryCannotPublishAfterAccountSwitch()
    {
        var receiver = new FolderReceiver(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher);
        model.SetAccount(TestDirectory.Profile()); await model.RefreshFoldersAsync();
        model.SetAccount(TestDirectory.Profile()); dispatcher.Drain();
        Assert.Single(model.Folders); Assert.Equal("INBOX", model.Folders[0].MailboxId);
        Assert.False(model.IsBusy);
    }

    internal static MailInboxPage Page(AccountProfile account, string name)
    {
        var key = new MailboxKey(account.Id, name, 7);
        return new([SqliteMailCacheTests.Summary(key, 1)], null) { Mailbox = key };
    }

    internal sealed class FolderReceiver : IMailFolderReceiver
    {
        public int ReadCalls { get; private set; }
        public int BodyCalls { get; private set; }
        public Func<CancellationToken, Task<IReadOnlyList<MailFolder>>> Discover { get; set; } = _ =>
            Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", "/", true, []), new("Archive", "/", true, []), new("Container", "/", false, [])]);
        public Func<AccountProfile, string, MailInboxCursor?, CancellationToken, Task<MailInboxPage>> Read { get; set; } =
            (account, name, _, _) => Task.FromResult(Page(account, name));
        public Func<MailMessageKey, Task<MailMessageBody>> Body { get; set; } = key => Task.FromResult(new MailMessageBody(key, "Body"));
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default) => Discover(cancellationToken);
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => GetMailboxAsync(account, "INBOX", maximumCount, older, cancellationToken);
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        { ReadCalls++; return Read(account, mailboxId, older, cancellationToken); }
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default)
        { BodyCalls++; return Body(message); }
    }
}
