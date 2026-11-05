using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class PermanentDeleteWorkflowTests
{
    [Fact]
    public async Task RequestNeedsConfirmedTrashAndConfirmationIsBoundToCurrentMessage()
    {
        var deleter = new Deleter(); using var model = new InboxViewModel(deleter, new ImmediateUiDispatcher()); model.SetAccount(TestDirectory.Profile());
        await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key); Assert.False(model.RequestPermanentDelete());
        await model.RefreshFoldersAsync(); model.SelectFolder("Trash"); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        await model.ConfirmPermanentDeleteAsync(); Assert.Equal(0, deleter.Calls);
        Assert.True(model.RequestPermanentDelete()); Assert.Contains("Trash", model.PermanentDeleteConfirmationText);
        Assert.Contains("cannot be undone", model.PermanentDeleteConfirmationText); model.CancelPermanentDeleteConfirmation();
        await model.ConfirmPermanentDeleteAsync(); Assert.Equal(0, deleter.Calls);
        Assert.True(model.RequestPermanentDelete()); model.SelectFolder("INBOX"); await model.ConfirmPermanentDeleteAsync(); Assert.Equal(0, deleter.Calls);
        model.SelectFolder("Trash"); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key); model.RequestPermanentDelete();
        await model.ReceiveAsync(); await model.ConfirmPermanentDeleteAsync(); Assert.Equal(0, deleter.Calls);
        await model.SelectAsync(model.Messages[0].Key); model.RequestPermanentDelete(); model.SetAccount(TestDirectory.Profile());
        await model.ConfirmPermanentDeleteAsync(); Assert.Equal(0, deleter.Calls);
    }

    [Theory]
    [InlineData(MailDeleteStatus.Confirmed)]
    [InlineData(MailDeleteStatus.NotApplied)]
    [InlineData(MailDeleteStatus.Unknown)]
    public async Task OnlyConfirmedDeletionRemovesTheActiveCachedCopy(MailDeleteStatus outcome)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var deleter = new Deleter { Status = outcome }; using var model = new InboxViewModel(deleter, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); model.SelectFolder("Trash"); await model.CacheLoadTask;
        await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key); var body = model.Body;
        Assert.True(model.RequestPermanentDelete()); await model.ConfirmPermanentDeleteAsync();
        Assert.Equal(1, deleter.Calls); Assert.Null(model.PendingPermanentDelete); Assert.False(model.CanRetry);
        if (outcome == MailDeleteStatus.Confirmed)
        { Assert.Empty(model.Messages); Assert.False(model.HasLoaded); Assert.Null(model.Body); Assert.False(model.CanLoadOlder); Assert.Null(await cache.ReadBodyAsync(new(account.Id, "Trash", 9), 1)); }
        else { Assert.Single(model.Messages); Assert.Same(body, model.Body); Assert.NotNull(await cache.ReadBodyAsync(new(account.Id, "Trash", 9), 1)); }
        await model.ConfirmPermanentDeleteAsync(); Assert.Equal(1, deleter.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrFolderSwitchDiscardsLateDeletionWithoutReplaying(bool switchFolder)
    {
        var dispatcher = new TestQueueDispatcher(); var deleter = new Deleter(); using var model = new InboxViewModel(deleter, dispatcher);
        model.SetAccount(TestDirectory.Profile()); await model.RefreshFoldersAsync(); dispatcher.Drain(); model.SelectFolder("Trash");
        await model.ReceiveAsync(); dispatcher.Drain(); await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        deleter.Wait = async () => { started.SetResult(); await release.Task; };
        model.RequestPermanentDelete(); var pending = model.ConfirmPermanentDeleteAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (switchFolder) model.SelectFolder("INBOX"); else model.Cancel();
        release.SetResult(); await pending; dispatcher.Drain(); Assert.Equal(1, deleter.Calls); Assert.False(model.CanConfirmPermanentDelete);
        Assert.Contains("may be gone", model.DeleteNotice); if (!switchFolder) Assert.Single(model.Messages);
    }

    [Fact]
    public async Task LocalCleanupRetryNeverSendsAnotherServerDeletion()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var deleter = new Deleter(); using var model = new InboxViewModel(deleter, new ImmediateUiDispatcher(), cache); var account = TestDirectory.Profile();
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); model.SelectFolder("Trash"); await model.CacheLoadTask;
        await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        Execute(path, "CREATE TRIGGER block_delete BEFORE DELETE ON messages BEGIN SELECT RAISE(ABORT,'fixture'); END;");
        model.RequestPermanentDelete(); await model.ConfirmPermanentDeleteAsync(); Assert.True(model.CanRetryDeleteCache);
        Assert.Empty(model.Messages); Assert.Equal(1, deleter.Calls); Assert.NotNull(await cache.ReadBodyAsync(new(account.Id, "Trash", 9), 1));
        Execute(path, "DROP TRIGGER block_delete"); await model.RetryDeleteCacheAsync();
        Assert.False(model.CanRetryDeleteCache); Assert.Equal(1, deleter.Calls); Assert.Null(await cache.ReadBodyAsync(new(account.Id, "Trash", 9), 1));
    }

    [Fact]
    public async Task CacheDeletionPreservesOtherAccountsFoldersEpochsAndReceiveTimes()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile().Id; var time = DateTimeOffset.UtcNow.AddDays(-1);
        MailboxKey[] mailboxes = [new(account, "Trash", 9), new(account, "Trash", 10), new(account, "INBOX", 7), new(AccountId.New(), "Trash", 9)];
        foreach (var mailbox in mailboxes)
        { await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], time); await cache.SaveBodyAsync(mailbox, new(new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, 1), "Body")); }
        var deleted = new MailMessageKey(account, "Trash", 9, 1); await cache.ReconcilePermanentDeleteAsync(deleted); await cache.ReconcilePermanentDeleteAsync(deleted);
        Assert.Null(await cache.ReadBodyAsync(mailboxes[0], 1));
        foreach (var mailbox in mailboxes.Skip(1)) { Assert.Single(await cache.ReadSummariesAsync(mailbox, 10)); Assert.NotNull(await cache.ReadBodyAsync(mailbox, 1)); }
        Assert.Equal(time, (await cache.GetLatestMailboxAsync(account, "Trash"))!.ReceivedAt);
    }

    private static void Execute(string path, string sql)
    { using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }

    internal sealed class Deleter : IMailFolderReceiver, IMailPermanentDeleter, IMailMover, IMailReadStateWriter, IMailStarStateWriter
    {
        // Match the production reader's full action layout during confirmation/focus tests.
        public Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey key, string destinationMailboxId, CancellationToken token = default) =>
            Task.FromResult(new MailMoveResult(key, destinationMailboxId, MailMoveStatus.NotApplied, null, "Unused fixture move"));
        public Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead, CancellationToken token = default) =>
            Task.FromResult(new ReadStateChangeResult(key, isRead, MailFlagChangeStatus.NotApplied, "Unused fixture read state"));
        public Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred, CancellationToken token = default) =>
            Task.FromResult(new MailFlagChangeResult(key, isStarred, MailFlagChangeStatus.NotApplied, "Unused fixture star state"));
        public int Calls { get; private set; } public MailDeleteStatus Status { get; set; } = MailDeleteStatus.Confirmed;
        public Func<Task> Wait { get; set; } = () => Task.CompletedTask;
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) => Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", null, true, []), new("Trash", null, true, ["\\Trash"])]);
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default)
        { var mailbox = new MailboxKey(account.Id, mailboxId, mailboxId == "Trash" ? 9u : 7u); return Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1)], new(account.Id, mailbox.UidValidity, 3, 2, 0) { MailboxId = mailboxId }) { Mailbox = mailbox }); }
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => GetMailboxAsync(account, "INBOX", maximumCount, older, token);
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => Task.FromResult(new MailMessageBody(key, "Body"));
        public async Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default)
        { Calls++; await Wait(); return new(key, Status, "Synthetic deletion " + Status); }
    }
}
