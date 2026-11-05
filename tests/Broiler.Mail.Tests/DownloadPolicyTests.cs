using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class DownloadPolicyTests
{
    [Fact]
    public async Task HeadersOnlyReadIsEphemeralButExplicitDownloadCanKeepTheAlreadyOpenBodyOffline()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db")); int bodyCalls = 0;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1)], null) { Mailbox = mailbox }),
            Body = (key, _) => { bodyCalls++; return Task.FromResult(new MailMessageBody(key, "Ephemeral body")); },
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync(); await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        Assert.Equal("Ephemeral body", model.Body!.PlainText); Assert.False(model.SelectedMessage!.IsBodyCached);
        Assert.Null(await cache.ReadBodyAsync(mailbox, 1)); Assert.Contains("not saved", model.Status);
        receiver.Body = (_, _) => throw new MailConnectionException("Now offline");
        await model.DownloadLoadedBodiesAsync();
        Assert.Equal(1, bodyCalls); Assert.Equal("Ephemeral body", (await cache.ReadBodyAsync(mailbox, 1))!.PlainText);
        Assert.True(model.SelectedMessage!.IsBodyCached); Assert.Equal(1, model.DownloadCompleted);
    }

    [Fact]
    public async Task PartialFailureKeepsCommittedBodiesAndRetrySkipsThemWithoutLoadingMoreHeaders()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db")); var calls = new List<uint>(); bool fail = true; int headerCalls = 0;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => { headerCalls++; return Task.FromResult(new MailInboxPage(Enumerable.Range(1, 3).Select(uid => SqliteMailCacheTests.Summary(mailbox, (uint)uid)).ToArray(), null) { Mailbox = mailbox }); },
            Body = (key, _) => { calls.Add(key.Uid); if (fail && key.Uid == 2) throw new MailConnectionException("Interrupted"); return Task.FromResult(new MailMessageBody(key, $"Body {key.Uid}")); },
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync(); await model.DownloadLoadedBodiesAsync();
        Assert.Equal(1, model.DownloadCompleted); Assert.True(model.ProblemIsBodyDownload); Assert.Contains("Saved bodies were kept", model.Problem);
        Assert.NotNull(await cache.ReadBodyAsync(mailbox, 1)); Assert.Null(await cache.ReadBodyAsync(mailbox, 2));
        fail = false; await model.RetryAsync();
        Assert.Equal(new uint[] { 1, 2, 2, 3 }, calls); Assert.Equal(1, headerCalls);
        Assert.Equal(3, model.DownloadCompleted); Assert.All(model.Messages, row => Assert.True(row.IsBodyCached));
    }

    [Fact]
    public async Task CancellationAndAccountSwitchRejectLateBodyResults()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db")); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
            Body = (key, _) => key.Uid == 1 ? Task.FromResult(new MailMessageBody(key, "Keep completed")) : WaitForLate(),
        };
        Task<MailMessageBody> WaitForLate() { entered.SetResult(); return late.Task; }
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync(); var downloading = model.DownloadLoadedBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.Cancel();
        Assert.Contains("Saved bodies were kept", model.Problem); Assert.False(model.IsDownloadingBodies);
        model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask;
        late.SetResult(new(SqliteMailCacheTests.Key(mailbox, 2), "Too late")); await downloading;
        Assert.Empty(model.Messages); Assert.Null(await cache.ReadBodyAsync(mailbox, 2));
        Assert.Equal("Keep completed", (await cache.ReadBodyAsync(mailbox, 1))!.PlainText);
    }

    [Fact]
    public async Task PolicyChangeQuiescesCurrentDownloadBeforeNewPolicyApplies()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        using var model = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask;
        model.BeginStorageMaintenance(); Assert.False(model.CanDownloadLoaded); Assert.False(model.CanSelect);
        model.SetDownloadPolicy(OfflineDownloadPolicy.HeadersOnly); model.EndStorageMaintenance(false);
        Assert.Equal(OfflineDownloadPolicy.HeadersOnly, model.DownloadPolicy); Assert.True(model.CanDownloadLoaded);
        Assert.Single(model.Messages);
    }

    [Fact]
    public async Task QueuedProgressCannotRelabelAnotherAccountAfterDownloadFinishes()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new TestMailReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        await model.DownloadLoadedBodiesAsync(); // Both progress and completion are still queued.
        model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; dispatcher.Drain();
        Assert.Empty(model.Messages); Assert.Equal(0, model.DownloadCompleted); Assert.Equal(0, model.DownloadTotal);
    }
}
