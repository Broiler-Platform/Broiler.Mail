using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class MoveWorkflowTests
{
    [Fact]
    public async Task CacheMoveRekeysBodyAndPreservesFlagsMetadataAndOtherRowsAcrossRestart()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var account = TestDirectory.Profile().Id;
        var source = new MailMessageKey(account, "INBOX", 7, 1); var destination = new MailMessageKey(account, "Archive", 9, 900);
        var from = new MailboxKey(account, "INBOX", 7); var to = new MailboxKey(account, "Archive", 9); var time = DateTimeOffset.UtcNow.AddDays(-1);
        await cache.SaveSummariesAsync(from, [SqliteMailCacheTests.Summary(from, 1) with { IsRead = true, IsStarred = true }, SqliteMailCacheTests.Summary(from, 2)], time);
        await cache.SaveBodyAsync(from, new(source, "Body", "<p>HTML</p>") { EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["cid"] = new("cid", "image/png", [1, 2, 3]) } });
        await cache.ReconcileMoveAsync(source, destination); await cache.ReconcileMoveAsync(source, destination);
        cache = new SqliteMailCache(path);
        Assert.Equal(2u, Assert.Single(await cache.ReadSummariesAsync(from, 10)).Key.Uid);
        Assert.Null(await cache.ReadBodyAsync(from, 1));
        var summary = Assert.Single(await cache.ReadSummariesAsync(to, 10)); Assert.True(summary.IsRead); Assert.True(summary.IsStarred);
        Assert.True(summary.IsBodyCached); var body = (await cache.ReadBodyAsync(to, 900))!;
        Assert.Equal(destination, body.Key); Assert.Equal("Body", body.PlainText); Assert.Equal("<p>HTML</p>", body.HtmlText); Assert.Equal(new byte[] { 1, 2, 3 }, body.EmbeddedImages["cid"].Data);
        Assert.Equal(time, (await cache.GetLatestMailboxAsync(account, "INBOX"))!.ReceivedAt);
        Assert.Equal(time, (await cache.GetLatestMailboxAsync(account, "Archive"))!.ReceivedAt);
    }

    [Fact]
    public async Task CacheFailureRollsBackDestinationAndKeepsSourceBody()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var account = TestDirectory.Profile().Id; var from = new MailboxKey(account, "INBOX", 7);
        var source = new MailMessageKey(account, "INBOX", 7, 1);
        await cache.SaveSummariesAsync(from, [SqliteMailCacheTests.Summary(from, 1)], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(from, new(source, "Kept"));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER block_move BEFORE DELETE ON messages BEGIN SELECT RAISE(ABORT,'fixture'); END;"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<IOException>(() => cache.ReconcileMoveAsync(source, new(account, "Archive", 9, 900)));
        Assert.Equal("Kept", (await cache.ReadBodyAsync(from, 1))!.PlainText);
        Assert.Null(await cache.GetLatestMailboxAsync(account, "Archive"));
        Assert.Empty(await cache.ReadSummariesAsync(new(account, "Archive", 9), 10));
    }

    [Theory]
    [InlineData(MailMoveStatus.Confirmed)]
    [InlineData(MailMoveStatus.NotApplied)]
    [InlineData(MailMoveStatus.Unknown)]
    public async Task ReaderOnlyReconcilesConfirmedMovesAndNeverQueuesRetry(MailMoveStatus status)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mover = new Mover { Status = status }; using var model = new InboxViewModel(mover, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        var body = model.Body; Assert.True(model.CanLoadOlder); Assert.True(model.CanArchive); Assert.True(model.CanTrash);
        await model.ArchiveSelectedAsync(); Assert.Equal(1, mover.Calls); Assert.False(model.CanRetry);
        if (status == MailMoveStatus.Confirmed)
        {
            Assert.Empty(model.Messages); Assert.Null(model.Body); Assert.Null(model.SelectedMessage); Assert.False(model.CanLoadOlder);
            Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask;
            Assert.Equal(900u, Assert.Single(model.Messages).Key.Uid); await model.SelectAsync(model.Messages[0].Key);
            Assert.Equal("Body", model.Body!.PlainText); Assert.True(model.IsBodyFromCache);
        }
        else { Assert.Same(body, model.Body); Assert.Single(model.Messages); Assert.NotNull(await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1)); }
    }

    [Fact]
    public async Task FailedCacheReconciliationCanRetryLocallyWithoutRepeatingServerMove()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(new(account.Id, "Archive", 8), [], DateTimeOffset.UtcNow);
        var mover = new Mover(); using var model = new InboxViewModel(mover, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        await model.ArchiveSelectedAsync(); Assert.True(model.CanRetryMoveCache); Assert.False(model.CanUndoMove); Assert.Empty(model.Messages);
        Assert.Contains("server move is confirmed", model.MoveNotice); Assert.Equal(1, mover.Calls);
        Assert.NotNull(await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1));
        model.SelectFolder("Archive"); await model.CacheLoadTask; model.SelectFolder("INBOX"); await model.CacheLoadTask;
        await model.SelectAsync(model.Messages[0].Key); Assert.NotNull(model.Body); // Old saved source remains reachable until local reconciliation succeeds.
        await cache.SaveSummariesAsync(new(account.Id, "Archive", 9), [], DateTimeOffset.UtcNow); // Explicit destination refresh resolves the epoch conflict.
        await model.RetryMoveCacheAsync(); await model.CacheLoadTask;
        Assert.Empty(model.Messages); Assert.Null(model.SelectedMessage); Assert.Null(model.Body); Assert.False(model.CanRetryMoveCache); Assert.True(model.CanUndoMove); Assert.Equal(1, mover.Calls);
        Assert.Null(await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1));
        Assert.Equal("Body", (await cache.ReadBodyAsync(new(account.Id, "Archive", 9), 900))!.PlainText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledOrSupersededMoveCannotPublishStaleResults(bool switchFolder)
    {
        var dispatcher = new TestQueueDispatcher(); var mover = new Mover(); using var model = new InboxViewModel(mover, dispatcher);
        model.SetAccount(TestDirectory.Profile()); await model.RefreshFoldersAsync(); dispatcher.Drain(); await model.ReceiveAsync(); dispatcher.Drain();
        await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mover.Wait = async () => { started.SetResult(); await release.Task; };
        var pending = model.ArchiveSelectedAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (switchFolder) model.SelectFolder("Archive"); else model.Cancel();
        release.SetResult(); await pending; dispatcher.Drain();
        Assert.Contains("may have moved", model.MoveNotice); Assert.Equal(1, mover.Calls); Assert.False(model.CanRetry);
        if (switchFolder) Assert.Empty(model.Messages); else Assert.Single(model.Messages);
    }

    [Fact]
    public async Task MoveRolesAndDestinationRequireFreshSelectableDiscovery()
    {
        var mover = new Mover(); using var model = new InboxViewModel(mover, new ImmediateUiDispatcher()); model.SetAccount(TestDirectory.Profile());
        await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        Assert.False(model.CanArchive); Assert.False(model.CanTrash); await model.MoveSelectedAsync("Archive"); Assert.Equal(0, mover.Calls);
        await model.RefreshFoldersAsync(); Assert.True(model.CanArchive); Assert.True(model.CanTrash);
        await model.MoveSelectedAsync("Container"); Assert.Equal(0, mover.Calls);
        model.ChooseMoveDestination("Trash"); Assert.True(model.CanMoveToDestination); await model.TrashSelectedAsync();
        Assert.Equal("Trash", mover.LastDestination); Assert.Equal(1, mover.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDestinationKeepsFresherHeadersAndBodyWhileMissingBodyIsFilled(bool hasDestinationBody)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile().Id;
        var from = new MailboxKey(account, "INBOX", 7); var to = new MailboxKey(account, "Archive", 9);
        var source = new MailMessageKey(account, "INBOX", 7, 1); var destination = new MailMessageKey(account, "Archive", 9, 900);
        var time = DateTimeOffset.UtcNow;
        await cache.SaveSummariesAsync(from, [SqliteMailCacheTests.Summary(from, 1)], time.AddDays(-1));
        await cache.SaveBodyAsync(from, new(source, "Source body"));
        await cache.SaveSummariesAsync(to, [SqliteMailCacheTests.Summary(to, 900) with { Subject = "Fresh header", IsRead = true, IsStarred = true }], time);
        if (hasDestinationBody) await cache.SaveBodyAsync(to, new(destination, "Fresh destination body"));
        await cache.ReconcileMoveAsync(source, destination);
        var saved = Assert.Single(await cache.ReadSummariesAsync(to, 10)); Assert.Equal("Fresh header", saved.Subject); Assert.True(saved.IsRead); Assert.True(saved.IsStarred);
        Assert.Equal(time, (await cache.GetLatestMailboxAsync(account, "Archive"))!.ReceivedAt);
        Assert.Equal(hasDestinationBody ? "Fresh destination body" : "Source body", (await cache.ReadBodyAsync(to, 900))!.PlainText);
    }

    [Fact]
    public async Task UndoUsesVerifiedDestinationAndNewReturnedUidAndPreservesOfflineBody()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mover = new Mover(); using var model = new InboxViewModel(mover, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        var original = model.SelectedMessage!.Key;
        await model.ArchiveSelectedAsync(); Assert.True(model.CanUndoMove); mover.NextUid = 1000;
        await model.UndoLastMoveAsync(); await model.CacheLoadTask;
        Assert.Equal(new MailMessageKey(account.Id, "Archive", 9, 900), mover.LastSource);
        Assert.Equal("INBOX", mover.LastDestination); Assert.Equal(2, mover.Calls); Assert.False(model.CanUndoMove);
        var restored = Assert.Single(model.Messages); Assert.Equal(1000u, restored.Key.Uid); Assert.NotEqual(original, restored.Key);
        Assert.Null(await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1));
        Assert.Null(await cache.ReadBodyAsync(new(account.Id, "Archive", 9), 900));
        await model.SelectAsync(restored.Key); Assert.True(model.IsBodyFromCache); Assert.Equal("Body", model.Body!.PlainText);
        Assert.Equal(restored.Key, model.Body.Key);
        await model.UndoLastMoveAsync(); Assert.Equal(2, mover.Calls);
    }

    [Theory]
    [InlineData(MailMoveStatus.Unknown)]
    [InlineData(MailMoveStatus.NotApplied)]
    public async Task UnconfirmedUndoKeepsLocalMailAndConsumesTheEntry(MailMoveStatus outcome)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mover = new Mover(); using var model = new InboxViewModel(mover, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        await model.ArchiveSelectedAsync(); model.SelectFolder("Archive"); await model.CacheLoadTask; await model.SelectAsync(model.Messages[0].Key);
        var body = model.Body; mover.Status = outcome;
        await model.UndoLastMoveAsync(); Assert.False(model.CanUndoMove); Assert.Equal(2, mover.Calls); Assert.Same(body, model.Body);
        Assert.NotNull(await cache.ReadBodyAsync(new(account.Id, "Archive", 9), 900));
        await model.UndoLastMoveAsync(); Assert.Equal(2, mover.Calls); Assert.Contains("Undo move", model.MoveNotice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledOrSupersededUndoCannotRestoreItsEntryOrPublishStaleRows(bool switchFolder)
    {
        var dispatcher = new TestQueueDispatcher(); var mover = new Mover(); using var model = new InboxViewModel(mover, dispatcher);
        model.SetAccount(TestDirectory.Profile()); await model.RefreshFoldersAsync(); dispatcher.Drain(); await model.ReceiveAsync(); dispatcher.Drain();
        await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain(); await model.ArchiveSelectedAsync(); dispatcher.Drain();
        Assert.True(model.CanUndoMove);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mover.Wait = async () => { started.SetResult(); await release.Task; };
        var pending = model.UndoLastMoveAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (switchFolder) model.SelectFolder("Archive"); else model.Cancel();
        release.SetResult(); await pending; dispatcher.Drain();
        Assert.False(model.CanUndoMove); Assert.Equal(2, mover.Calls); Assert.Empty(model.Messages); Assert.Contains("may have moved", model.MoveNotice);
    }

    [Fact]
    public async Task AccountChangeClearsUndoAndUnrelatedReaderSurvivesReverseMove()
    {
        var mover = new Mover(); using var model = new InboxViewModel(mover, new ImmediateUiDispatcher()); var account = TestDirectory.Profile();
        model.SetAccount(account); await model.RefreshFoldersAsync(); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        await model.ArchiveSelectedAsync(); model.SelectFolder("Trash"); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        var key = model.SelectedMessage!.Key; var body = model.Body; Assert.True(model.CanLoadOlder);
        await model.UndoLastMoveAsync(); Assert.Equal(key, model.SelectedMessage!.Key); Assert.Same(body, model.Body); Assert.True(model.CanLoadOlder);
        await model.TrashSelectedAsync(); // Same destination is refused; no new undo appears.
        Assert.False(model.CanUndoMove);
        model.SelectFolder("INBOX"); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key); await model.ArchiveSelectedAsync();
        Assert.True(model.CanUndoMove); model.SetAccount(TestDirectory.Profile()); Assert.False(model.CanUndoMove);
    }

    [Fact]
    public async Task ConfirmedUndoWithCacheFailureCanRetryLocallyWithoutAnotherReverseMove()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mover = new Mover(); using var model = new InboxViewModel(mover, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        await model.ArchiveSelectedAsync();
        await cache.SaveSummariesAsync(new(account.Id, "INBOX", 8), [], DateTimeOffset.UtcNow); // Deliberately conflicting local epoch.
        mover.NextUid = 1000; await model.UndoLastMoveAsync();
        Assert.False(model.CanUndoMove); Assert.True(model.CanRetryMoveCache); Assert.Equal(2, mover.Calls);
        Assert.NotNull(await cache.ReadBodyAsync(new(account.Id, "Archive", 9), 900));
        await cache.SaveSummariesAsync(new(account.Id, "INBOX", 7), [], DateTimeOffset.UtcNow);
        await model.RetryMoveCacheAsync();
        Assert.Equal(2, mover.Calls); Assert.False(model.CanRetryMoveCache); Assert.False(model.CanUndoMove);
        Assert.Equal("Body", (await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1000))!.PlainText);
        Assert.Null(await cache.ReadBodyAsync(new(account.Id, "Archive", 9), 900));
    }

    internal sealed class Mover : IMailFolderReceiver, IMailMover
    {
        public uint NextUid { get; set; } = 900; public MailMessageKey? LastSource { get; private set; }
        public int Calls { get; private set; } public string? LastDestination { get; private set; }
        public MailMoveStatus Status { get; set; } = MailMoveStatus.Confirmed;
        public Func<Task> Wait { get; set; } = () => Task.CompletedTask;
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) => Task.FromResult<IReadOnlyList<MailFolder>>
            ([new("INBOX", null, true, []), new("Archive", null, true, ["\\Archive"]), new("Trash", null, true, ["\\Trash"]), new("Container", null, false, [])]);
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default)
        {
            var mailbox = new MailboxKey(account.Id, mailboxId, mailboxId == "INBOX" ? 7u : 9u);
            return Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1)], new(account.Id, mailbox.UidValidity, 3, 2, 0) { MailboxId = mailboxId }) { Mailbox = mailbox });
        }
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => GetMailboxAsync(account, "INBOX", maximumCount, older, token);
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => Task.FromResult(new MailMessageBody(key, "Body"));
        public async Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId, CancellationToken token = default)
        { Calls++; LastSource = source; LastDestination = destinationMailboxId; await Wait(); return new(source, destinationMailboxId, Status,
            Status == MailMoveStatus.Confirmed ? new(account.Id, destinationMailboxId, destinationMailboxId == "INBOX" ? 7u : 9u, NextUid) : null, "Synthetic " + Status); }
    }
}
