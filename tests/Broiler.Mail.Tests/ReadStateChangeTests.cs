using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class ReadStateChangeTests
{
    [Fact]
    public async Task ConfirmedReadChangeUpdatesOnlyItsCachedRowAndPreservesReceiveTimeAndBody()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var selected = new MailMessageKey(account.Id, "Archive", 7, 1); var received = DateTimeOffset.UtcNow.AddDays(-1);
        foreach (var mailbox in new[] { new MailboxKey(account.Id, "Archive", 7), new(account.Id, "INBOX", 7), new(account.Id, "Archive", 8), new(AccountId.New(), "Archive", 7) })
        {
            await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], received);
            await cache.SaveBodyAsync(mailbox, new(new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, 1), "Body"));
        }
        await cache.UpdateReadStateAsync(selected, true);
        foreach (string name in new[] { "Archive", "INBOX" })
        {
            var latest = (await cache.GetLatestMailboxAsync(account.Id, name))!;
            Assert.Equal(received, latest.ReceivedAt); Assert.False((await cache.ReadSummariesAsync(latest.Key, 1))[0].IsRead);
        }
        Assert.True((await cache.ReadSummariesAsync(new(account.Id, "Archive", 7), 1))[0].IsRead);
        Assert.Equal("Body", (await cache.ReadBodyAsync(new(account.Id, "Archive", 7), 1))!.PlainText);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.UpdateReadStateAsync(selected with { Uid = 999 }, true));
    }

    [Theory]
    [InlineData(MailFlagChangeStatus.Confirmed, false)]
    [InlineData(MailFlagChangeStatus.Unknown, false)]
    [InlineData(MailFlagChangeStatus.NotApplied, false)]
    [InlineData(MailFlagChangeStatus.Confirmed, true)]
    [InlineData(MailFlagChangeStatus.Unknown, true)]
    [InlineData(MailFlagChangeStatus.NotApplied, true)]
    public async Task ModelCommitsOnlyConfirmedFlagState(MailFlagChangeStatus status, bool starred)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db")); var receiver = new Writer { Status = status };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        if (starred) await model.SetSelectedStarStateAsync(true); else await model.SetSelectedReadStateAsync(true);
        Assert.Equal(status == MailFlagChangeStatus.Confirmed, (starred ? model.SelectedMessage!.IsStarred : model.SelectedMessage!.IsRead));
        Assert.Equal(status == MailFlagChangeStatus.Confirmed, (starred ? model.Messages[0].IsStarred : model.Messages[0].IsRead));
        Assert.Equal("Body", model.Body!.PlainText); Assert.Equal(1, receiver.Calls);
        Assert.NotNull(model.FlagChangeNotice); Assert.False(model.CanRetry);
        var saved = (await cache.ReadSummariesAsync(new(account.Id, "INBOX", 7), 1))[0];
        Assert.Equal(status == MailFlagChangeStatus.Confirmed, (starred ? saved.IsStarred : saved.IsRead));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CanceledOrSupersededFlagChangeCannotPublishAStaleFlag(bool folderSwitch, bool starred)
    {
        var receiver = new Writer(); var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher); model.SetAccount(TestDirectory.Profile());
        await model.RefreshFoldersAsync(); dispatcher.Drain(); await model.ReceiveAsync(); dispatcher.Drain();
        await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.Wait = async () => { started.SetResult(); await release.Task; };
        var pending = starred ? model.SetSelectedStarStateAsync(true) : model.SetSelectedReadStateAsync(true); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (folderSwitch) Assert.True(model.SelectFolder("Archive")); else model.Cancel();
        release.SetResult(); await pending; dispatcher.Drain();
        Assert.Equal(1, receiver.Calls); Assert.False(model.CanRetry);
        if (folderSwitch) { Assert.Null(model.SelectedMessage); Assert.Null(model.FlagChangeNotice); }
        else { Assert.False((starred ? model.SelectedMessage!.IsStarred : model.SelectedMessage!.IsRead)); Assert.Contains("may have applied", model.FlagChangeNotice); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedServerChangeRemainsConfirmedWhenTheCacheRowDisappears(bool starred)
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); using var model = new InboxViewModel(new Writer(), new ImmediateUiDispatcher(), cache);
        model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; await model.ReceiveAsync(); await model.SelectAsync(model.Messages[0].Key);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM messages"; command.ExecuteNonQuery();
        }
        if (starred) await model.SetSelectedStarStateAsync(true); else await model.SetSelectedReadStateAsync(true);
        Assert.True((starred ? model.SelectedMessage!.IsStarred : model.SelectedMessage!.IsRead)); Assert.Contains(starred ? "cached starred state could not be saved" : "cached read state could not be saved", model.FlagChangeNotice);
        Assert.False(model.CanRetry);
    }

    internal sealed class Writer : IMailFolderReceiver, IMailReadStateWriter, IMailStarStateWriter
    {
        public int Calls { get; private set; }
        public MailFlagChangeStatus Status { get; set; } = MailFlagChangeStatus.Confirmed;
        public Func<Task> Wait { get; set; } = () => Task.CompletedTask;
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", null, true, []), new("Archive", null, true, [])]);
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => Task.FromResult(FolderNavigationTests.Page(account, mailboxId));
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => GetMailboxAsync(account, "INBOX", maximumCount, older, cancellationToken);
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default) => Task.FromResult(new MailMessageBody(key, "Body"));
        public async Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred, CancellationToken cancellationToken = default)
        { Calls++; await Wait(); return new(key, isStarred, Status, "Synthetic " + Status); }
        public async Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead, CancellationToken cancellationToken = default)
        { Calls++; await Wait(); return new(key, isRead, Status, "Synthetic " + Status); }
    }
}
