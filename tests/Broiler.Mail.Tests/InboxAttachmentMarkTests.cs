using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.1c: a message's row and reader know it has attachments once a body for it was loaded.</summary>
public sealed class InboxAttachmentMarkTests
{
    private static IReadOnlyList<MailAttachmentDescriptor> Files(int count) =>
        Enumerable.Range(0, count).Select(index => new MailAttachmentDescriptor(index + 2, $"file{index}.pdf", "application/pdf", 10)).ToArray();

    private static MailMessageBody WithFiles(MailMessageKey key, int count) => new(key, "Body") { Attachments = Files(count) };

    [Theory]
    [InlineData(OfflineDownloadPolicy.OnDemand)]
    [InlineData(OfflineDownloadPolicy.HeadersOnly)]
    public async Task OpeningAMessageWithAttachmentsMarksItsRow(OfflineDownloadPolicy policy)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 2), SqliteMailCacheTests.Summary(mailbox, 1)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(key.Uid == 1 ? WithFiles(key, 2) : new MailMessageBody(key, "Plain") { Attachments = [] }),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, policy);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync();
        Assert.All(model.Messages, row => Assert.False(row.HasAttachments));

        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        Assert.True(model.SelectedMessage!.HasAttachments);
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.Equal(policy == OfflineDownloadPolicy.OnDemand, model.SelectedMessage.IsBodyCached);
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
        Assert.False(model.SelectedMessage!.HasAttachments);
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        // Only a saved body marks the saved header.
        var saved = await cache.ReadSummariesAsync(mailbox, 10);
        Assert.Equal(policy == OfflineDownloadPolicy.OnDemand, saved.Single(row => row.Key.Uid == 1).HasAttachments);
    }

    [Fact]
    public async Task ReceivingAgainKeepsMarksFromTheCache()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 2), SqliteMailCacheTests.Summary(mailbox, 1)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(WithFiles(key, 1)),
        };
        using (var first = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache))
        {
            first.SetAccount(account); await first.CacheLoadTask; await first.ReceiveAsync();
            await first.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        }
        // A new session receives the same headers, which carry no mark; the cache supplies it.
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask;
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        await model.ReceiveAsync();
        Assert.False(model.IsShowingCachedMail);
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.False(model.Messages.Single(row => row.Key.Uid == 2).HasAttachments);

        // A mark saved after this session listed the rows (by another download) reaches them on the next receive.
        await cache.SaveBodyAsync(mailbox, WithFiles(SqliteMailCacheTests.Key(mailbox, 2), 1));
        Assert.False(model.Messages.Single(row => row.Key.Uid == 2).HasAttachments);
        await model.ReceiveAsync();
        Assert.True(model.Messages.Single(row => row.Key.Uid == 2).HasAttachments);
    }

    [Fact]
    public async Task ReceivingAgainKeepsSessionMarksWithoutACache()
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 2), SqliteMailCacheTests.Summary(mailbox, 1)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(WithFiles(key, 1)),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account); await model.ReceiveAsync();
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        await model.ReceiveAsync();
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.True(model.SelectedMessage!.HasAttachments);
        Assert.False(model.Messages.Single(row => row.Key.Uid == 2).HasAttachments);
    }

    [Fact]
    public async Task OfflineDownloadsMarkRowsWithAttachments()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        bool legacy = false;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage(Enumerable.Range(1, 3).Reverse().Select(uid => SqliteMailCacheTests.Summary(mailbox, (uint)uid)).ToArray(), null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(key.Uid switch
            {
                1 => WithFiles(key, 3),
                2 => new MailMessageBody(key, "No files") { Attachments = [] },
                // Decoded before attachment lists existed: says nothing either way.
                _ => legacy ? new MailMessageBody(key, "Legacy") : WithFiles(key, 1),
            }),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync();
        // Read message 3 without saving it, then move on so the download fetches it again.
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 3));
        Assert.True(model.SelectedMessage!.HasAttachments); Assert.False(model.SelectedMessage.IsBodyCached);
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
        legacy = true;

        await model.DownloadLoadedBodiesAsync();
        Assert.Equal(3, model.DownloadCompleted);
        Assert.All(model.Messages, row => Assert.True(row.IsBodyCached));
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.False(model.Messages.Single(row => row.Key.Uid == 2).HasAttachments);
        // A legacy body never clears a mark.
        Assert.True(model.Messages.Single(row => row.Key.Uid == 3).HasAttachments);
        var saved = await cache.ReadSummariesAsync(mailbox, 10);
        Assert.True(saved.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.False(saved.Single(row => row.Key.Uid == 2).HasAttachments);
    }

    [Theory]
    [InlineData(1, false, false, " · 1 attachment")]
    [InlineData(3, false, false, " · 3 attachments")]
    [InlineData(32, true, false, " · 32+ attachments")]
    [InlineData(0, false, true, " · Has attachments")]
    [InlineData(0, false, false, null)]
    public async Task TheReaderMetaLineCountsAttachments(int count, bool incomplete, bool markedOnly, string? expected)
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var header = SqliteMailCacheTests.Summary(mailbox, 1) with { HasAttachments = markedOnly };
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([header], null) { Mailbox = mailbox }),
            // Marked only: a legacy body without a list keeps the row's mark.
            Body = (key, _) => Task.FromResult(markedOnly ? new MailMessageBody(key, "Body")
                : new MailMessageBody(key, "Body") { Attachments = Files(count), IsAttachmentListIncomplete = incomplete }),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account); await model.ReceiveAsync();
        await model.SelectAsync(header.Key);
        var content = new InboxView(model).CreateContent();

        var meta = Descendants(content).OfType<StandardLabel>().Single(label => label.Text.StartsWith("Received ", StringComparison.Ordinal));
        if (expected is null)
            Assert.DoesNotContain("attachment", meta.Text, StringComparison.OrdinalIgnoreCase);
        else
        {
            Assert.Contains(expected, meta.Text, StringComparison.Ordinal);
            Assert.Single(meta.Text.Split(" · "), part => part.Contains("ttachment", StringComparison.Ordinal));
        }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var nested in Descendants(child))
                yield return nested;
    }
}
