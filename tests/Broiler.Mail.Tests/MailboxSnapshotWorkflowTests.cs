using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class MailboxSnapshotWorkflowTests
{
    [Fact]
    public async Task CapturePersistsAllHeadersButDisplaysABoundedSavedPageAndInspectDoesNotConnect()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var account = TestDirectory.Profile(); var receiver = new Receiver(501);
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(account); await model.CacheLoadTask;
        Assert.True(model.CanCaptureMailboxSnapshot); await model.CaptureMailboxSnapshotAsync(); Assert.Null(model.ListProblem); Assert.Equal(50, model.Messages.Count); Assert.True(model.CanLoadOlder); Assert.True(model.IsShowingCachedMail);
        Assert.Contains("501 server headers", model.MailboxSnapshotNotice); Assert.Contains("historical membership", model.MailboxSnapshotNotice); Assert.Equal(1, receiver.Calls);
        Assert.Equal(501, (await cache.ReadMailboxCheckpointAsync(account.Id, "INBOX"))!.ServerMessages); await model.InspectMailboxSnapshotAsync(); Assert.Equal(1, receiver.Calls);
        await model.LoadOlderAsync(); Assert.Equal(100, model.Messages.Count); Assert.Equal(1, receiver.Calls);
    }
    [Fact]
    public async Task DisabledAccountCanInspectButCannotCaptureAndSearchCannotStartACapture()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var account = TestDirectory.Profile() with { IsEnabled = false }; var receiver = new Receiver();
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(account); await model.CacheLoadTask;
        Assert.False(model.CanCaptureMailboxSnapshot); Assert.True(model.CanInspectMailboxSnapshot); await model.CaptureMailboxSnapshotAsync(); await model.InspectMailboxSnapshotAsync(); Assert.Contains("No complete header checkpoint", model.MailboxSnapshotNotice); Assert.Equal(0, receiver.Calls);
        model.SetAccount(account with { IsEnabled = true }); await model.CacheLoadTask; model.SetSearch("needle"); await model.SearchSavedHeadersAsync(); Assert.True(model.IsSearchActive); Assert.False(model.CanCaptureMailboxSnapshot);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("dispose")]
    public async Task IgnoredCancellationNeverCommitsCapturedMailOrRepaintsAChangedContext(string mode)
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var account = TestDirectory.Profile(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new Receiver(); CancellationToken observed = default;
        receiver.Get = async (profile, folder, token) => { observed = token; started.SetResult(); await release.Task; return Receiver.Snapshot(profile, folder, 1); };
        var queue = new TestQueueDispatcher(); using var model = new InboxViewModel(receiver, queue, cache); model.SetAccount(account); await model.CacheLoadTask; queue.Drain();
        var capture = model.CaptureMailboxSnapshotAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode == "cancel") model.Cancel(); else if (mode == "account") { model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; queue.Drain(); } else model.Dispose();
        Assert.True(observed.IsCancellationRequested); release.SetResult(); await capture; queue.Drain();
        Assert.Null(await cache.ReadMailboxCheckpointAsync(account.Id, "INBOX")); Assert.Empty(model.Messages); Assert.DoesNotContain("Last complete header observation", model.MailboxSnapshotNotice);
        if (mode == "cancel") { Assert.True(model.CanRetry); Assert.Contains("may already have committed", model.ListProblem!.Text); }
    }
    [Fact]
    public async Task FailedCaptureRetriesTheSameActionWithoutFallingBackToOrdinaryReceive()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var account = TestDirectory.Profile(); var receiver = new Receiver();
        int attempts = 0; receiver.Get = (profile, folder, _) => ++attempts == 1 ? throw new MailConnectionException("Controlled census failure") : Task.FromResult(Receiver.Snapshot(profile, folder, 1));
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(account); await model.CacheLoadTask;
        await model.CaptureMailboxSnapshotAsync(); Assert.True(model.CanRetry); Assert.Contains("Controlled census failure", model.ListProblem!.Text);
        await model.RetryAsync(); Assert.Equal(2, attempts); Assert.Null(model.ListProblem); Assert.Single(model.Messages);
    }
    [Fact]
    public async Task ForeignSnapshotCannotReachTheCacheAndANewerActionRejectsQueuedCaptureResults()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var account = TestDirectory.Profile(); var receiver = new Receiver();
        receiver.Get = (profile, folder, _) => Task.FromResult(Receiver.Snapshot(profile with { Id = AccountId.New() }, folder, 1));
        var queue = new TestQueueDispatcher(); using var model = new InboxViewModel(receiver, queue, cache); model.SetAccount(account); await model.CacheLoadTask; queue.Drain();
        await model.CaptureMailboxSnapshotAsync(); queue.Drain(); Assert.NotNull(model.ListProblem); Assert.Null(await cache.ReadMailboxCheckpointAsync(account.Id, "INBOX"));
        receiver.Get = (profile, folder, _) => Task.FromResult(Receiver.Snapshot(profile, folder, 1)); await model.CaptureMailboxSnapshotAsync();
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; queue.Drain(); Assert.Empty(model.Messages); Assert.DoesNotContain("Last complete header observation", model.MailboxSnapshotNotice);
        Assert.NotNull(await cache.ReadMailboxCheckpointAsync(account.Id, "INBOX")); // Durable old-account commit is retained, but never painted in the new account.
    }
    [Fact]
    public async Task DisabledInspectionRetryStaysLocalAfterThePersistedCensusIsRepaired()
    {
        using var dir = new TestDirectory(); string path = dir.File("mail.db"); var cache = new SqliteMailCache(path); var account = TestDirectory.Profile() with { IsEnabled = false }; var receiver = new Receiver();
        await cache.ApplyMailboxSnapshotAsync(Receiver.Snapshot(account, "INBOX", 1));
        static void SetUid(string file, int uid) { using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"); db.Open(); using var command = db.CreateCommand(); command.CommandText = $"UPDATE mailbox_snapshot_uids SET uid={uid}"; command.ExecuteNonQuery(); }
        SetUid(path, 2); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(account); await model.CacheLoadTask;
        await model.InspectMailboxSnapshotAsync(); Assert.NotNull(model.ListProblem); Assert.True(model.CanRetry);
        SetUid(path, 1); await model.RetryAsync(); Assert.Null(model.ListProblem); Assert.Contains("Last complete header observation", model.MailboxSnapshotNotice); Assert.Equal(0, receiver.Calls);
    }
    [Fact]
    public async Task SnapshotQuotaGuidanceSurvivesTheSharedReaderErrorHandler()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var receiver = new Receiver { Get = (_, _, _) => throw new MailMailboxSnapshotUnavailableException("Increase the cache quota to enable complete mailbox checkpoints; saved mail was kept.") };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; await model.CaptureMailboxSnapshotAsync();
        Assert.Contains("Increase the cache quota", model.ListProblem!.Text); Assert.Contains("Inspect saved header snapshot", model.ListProblem.Text);
    }
    internal sealed class Receiver(int count = 1) : IMailReceiver, IMailMailboxSnapshotReceiver
    {
        public int Calls; public Func<AccountProfile, string, CancellationToken, Task<MailMailboxSnapshot>>? Get;
        public Task<MailMailboxSnapshot> GetMailboxSnapshotAsync(AccountProfile account, string folder, CancellationToken token = default) { Calls++; return Get?.Invoke(account, folder, token) ?? Task.FromResult(Snapshot(account, folder, count)); }
        internal static MailMailboxSnapshot Snapshot(AccountProfile account, string folder, int count) => new(new(account.Id, folder, 7), Enumerable.Range(1, count).Select(uid => new MailMessageSummary { Key = new(account.Id, folder, 7, (uint)uid), Sender = "sender@example.test", Subject = "snapshot header" }).ToArray(), DateTimeOffset.UtcNow);
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No connection test in snapshot fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No ordinary receive in snapshot fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No body request in snapshot fixture.");
    }
}
