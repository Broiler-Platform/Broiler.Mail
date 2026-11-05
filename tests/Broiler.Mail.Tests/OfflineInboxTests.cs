using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class OfflineInboxTests
{
    [Fact]
    public async Task ReceiveSelectedBodyCloseAndRestartOfflineKeepsHeadersBodyAndClearUncachedState()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile();
        var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var online = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 2), SqliteMailCacheTests.Summary(mailbox, 1)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Downloaded content", "<p>Downloaded content</p>")),
        };
        using (var model = new InboxViewModel(online, new ImmediateUiDispatcher(), new SqliteMailCache(directory.File("mail.db"))))
        {
            model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync();
            await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
            Assert.NotNull(model.Body); Assert.Contains("saved for offline", model.Status);
        }
        var offline = new TestMailReceiver
        { Inbox = (_, _) => throw new MailConnectionException("Offline"), Body = (_, _) => throw new MailConnectionException("Offline") };
        using var reopened = new InboxViewModel(offline, new ImmediateUiDispatcher(), new SqliteMailCache(directory.File("mail.db")));
        reopened.SetAccount(account); await reopened.CacheLoadTask;
        Assert.Equal(2, reopened.Messages.Count); Assert.True(reopened.IsShowingCachedMail);
        await reopened.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        Assert.Equal("Downloaded content", reopened.Body!.PlainText); Assert.True(reopened.IsBodyFromCache);
        await reopened.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
        Assert.Null(reopened.Body); Assert.Contains("not been downloaded", reopened.Problem);
        await reopened.ReceiveAsync(); Assert.Equal(2, reopened.Messages.Count); Assert.True(reopened.IsShowingCachedMail);
    }

    [Fact]
    public async Task SavedHeadersPageLocallyAndAccountSwitchNeverMixesRows()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var account = TestDirectory.Profile(); var other = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, Enumerable.Range(1, 60).Select(uid => SqliteMailCacheTests.Summary(mailbox, (uint)uid)).ToArray(), DateTimeOffset.UtcNow);
        using var model = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask;
        Assert.Equal(50, model.Messages.Count); Assert.True(model.CanLoadOlder);
        await model.LoadOlderAsync(); Assert.Equal(60, model.Messages.Count); Assert.False(model.CanLoadOlder);
        model.SetAccount(account with { DisplayName = "Changed" }); var abandoned = model.CacheLoadTask;
        model.SetAccount(other); await model.CacheLoadTask; await abandoned;
        Assert.Empty(model.Messages); Assert.Null(model.Body);
    }
}
