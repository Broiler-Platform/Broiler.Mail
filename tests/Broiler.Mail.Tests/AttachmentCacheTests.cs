using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class AttachmentCacheTests
{
    private static readonly MailAttachmentDescriptor Report = new(2, "report.pdf", "application/pdf", 1234);

    [Fact]
    public async Task SavingABodyWithAttachmentsMarksItsSummary()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1), Row(box, 2)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, Body(box, 1, [Report]));
        var rows = await cache.ReadSummariesAsync(box, 10);
        Assert.True(rows.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.False(rows.Single(row => row.Key.Uid == 2).HasAttachments);
        var found = await cache.SearchHeadersAsync(new(box.AccountId, "") { IsBodyCached = true });
        Assert.True(Assert.Single(found.Messages).HasAttachments);
        var scoped = await cache.SearchHeadersAsync(new(box.AccountId, "Message") { MailboxId = box.MailboxId, Scope = MailSearchScope.SenderOrSubject });
        Assert.Equal(new[] { true, false }, scoped.Messages.OrderBy(row => row.Key.Uid).Select(row => row.HasAttachments));
    }

    [Fact]
    public async Task SavingABodyWithoutAttachmentsLeavesTheSummaryUnmarked()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, Body(box, 1, []));
        Assert.False(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
        // A complete, empty list is knowledge too: it clears an earlier mark.
        await cache.SaveBodyAsync(box, Body(box, 1, [Report]));
        await cache.SaveBodyAsync(box, Body(box, 1, []));
        Assert.False(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
        Assert.Equal(0L, CacheSchemaFixture.Scalar(directory.File("mail.db"), "SELECT has_attachments FROM messages"));
    }

    [Fact]
    public async Task AnIncompleteEmptyListIsStoredAsUnknownSoTheServerHintStillMarks()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1)], DateTimeOffset.UtcNow);
        // A structure cut off before any attachment was reached lists nothing, but that is not "no attachments".
        await cache.SaveBodyAsync(box, Body(box, 1, []) with { IsAttachmentListIncomplete = true });
        Assert.Equal(1L, CacheSchemaFixture.Scalar(path, "SELECT has_attachments IS NULL FROM messages"));
        Assert.Empty(await cache.ReadAttachmentMarksAsync(box, [1]));
        await cache.SaveSummariesAsync(box, [Row(box, 1) with { HasAttachments = true }], DateTimeOffset.UtcNow);
        Assert.True(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
        // An incomplete list never clears a mark the server's structure hint set either.
        await cache.SaveBodyAsync(box, Body(box, 1, []) with { IsAttachmentListIncomplete = true });
        Assert.True(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
    }

    [Fact]
    public async Task ALegacyBodyWithoutInventoryDoesNotClearAMark()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, Body(box, 1, [Report]));
        await cache.SaveBodyAsync(box, new MailMessageBody(Key(box, 1), "decoded before inventories existed"));
        Assert.True(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
        Assert.Null((await cache.ReadBodyAsync(box, 1))!.Attachments);
    }

    [Fact]
    public async Task ReceivingHeadersAgainKeepsTheAttachmentMark()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box(); var now = DateTimeOffset.UtcNow;
        await cache.SaveSummariesAsync(box, [Row(box, 1)], now);
        await cache.SaveBodyAsync(box, Body(box, 1, [Report]));
        await cache.SaveSummariesAsync(box, [Row(box, 1) with { Subject = "Renamed", IsRead = true }], now.AddMinutes(1));
        Assert.True(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
        await cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1) with { IsStarred = true }], now.AddMinutes(2)));
        var saved = Assert.Single(await cache.ReadSummariesAsync(box, 1));
        Assert.True(saved.HasAttachments); Assert.True(saved.IsStarred);
    }

    [Fact]
    public async Task AMovedMessageKeepsItsAttachmentMark()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        var archive = new MailboxKey(box.AccountId, "Archive", 9);
        await cache.SaveSummariesAsync(box, [Row(box, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, Body(box, 1, [Report]));
        await cache.ReconcileMoveAsync(Key(box, 1), Key(archive, 40));
        Assert.Empty(await cache.ReadSummariesAsync(box, 10));
        Assert.True(Assert.Single(await cache.ReadSummariesAsync(archive, 10)).HasAttachments);
        Assert.Equal(new uint[] { 40 }, await cache.ReadUidsWithAttachmentsAsync(archive, [40]));
        Assert.Equal(new[] { Report }, (await cache.ReadBodyAsync(archive, 40))!.Attachments!);
    }

    [Fact]
    public async Task BodyAttachmentsRoundTrip()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1)], DateTimeOffset.UtcNow);
        MailAttachmentDescriptor[] list = [Report, new(3, "photo.jpg", "image/jpeg", 0)];
        await cache.SaveBodyAsync(box, Body(box, 1, list) with { IsAttachmentListIncomplete = true });
        var loaded = await new SqliteMailCache(directory.File("mail.db")).ReadBodyAsync(box, 1);
        Assert.Equal(list, loaded!.Attachments!.ToArray());
        Assert.True(loaded.IsAttachmentListIncomplete);
    }

    [Fact]
    public async Task ReadUidsWithAttachmentsReturnsOnlyMarkedRequestedUids()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        var other = new MailboxKey(box.AccountId, "Other", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1), Row(box, 2), Row(box, 3), Row(box, 5)], DateTimeOffset.UtcNow);
        await cache.SaveSummariesAsync(other, [Row(other, 2)], DateTimeOffset.UtcNow);
        foreach (uint uid in new uint[] { 1, 3, 5 }) await cache.SaveBodyAsync(box, Body(box, uid, [Report]));
        await cache.SaveBodyAsync(other, Body(other, 2, [Report]));
        await cache.SaveBodyAsync(box, Body(box, 2, []));
        Assert.Equal(new uint[] { 1, 3 }, (await cache.ReadUidsWithAttachmentsAsync(box, [1, 2, 3, 9])).Order());
        Assert.Empty(await cache.ReadUidsWithAttachmentsAsync(box, []));
        Assert.Empty(await cache.ReadUidsWithAttachmentsAsync(new(box.AccountId, box.MailboxId, 8), [1, 3]));
        Assert.Equal(new uint[] { 2 }, await cache.ReadUidsWithAttachmentsAsync(other, [2]));
        // 501 is the bound, so a full summary page plus its continuation probe fits.
        Assert.Equal(3, (await cache.ReadUidsWithAttachmentsAsync(box, Enumerable.Range(1, 501).Select(uid => (uint)uid).ToArray())).Count);
    }

    [Fact]
    public async Task ReadUidsWithAttachmentsRejectsOversizedRequestsAndUidZero()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = Box();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.ReadUidsWithAttachmentsAsync(box, Enumerable.Range(1, 502).Select(uid => (uint)uid).ToArray()));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.ReadUidsWithAttachmentsAsync(box, [0, 1]));
        Assert.Equal(501, IMailAttachmentStateCache.MaximumRequestedUids);
    }

    [Fact]
    public async Task LegacyCachedBodyJsonReadsAsUnknown()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new MailMessageBody(Key(box, 1), "legacy body"));
        Assert.Equal(0L, CacheSchemaFixture.Scalar(path, "SELECT count(*) FROM messages WHERE instr(CAST(body AS TEXT),'Attachments')>0"));
        Assert.Equal(1L, CacheSchemaFixture.Scalar(path, "SELECT has_attachments IS NULL FROM messages"));
        var loaded = await cache.ReadBodyAsync(box, 1);
        Assert.Equal("legacy body", loaded!.PlainText); Assert.Null(loaded.Attachments); Assert.False(loaded.IsAttachmentListIncomplete);
        Assert.False(Assert.Single(await cache.ReadSummariesAsync(box, 1)).HasAttachments);
        Assert.Empty(await cache.ReadUidsWithAttachmentsAsync(box, [1]));
    }

    internal static MailboxKey Box() => new(AccountId.New(), "INBOX", 7);
    internal static MailMessageKey Key(MailboxKey box, uint uid) => new(box.AccountId, box.MailboxId, box.UidValidity, uid);
    internal static MailMessageSummary Row(MailboxKey box, uint uid) => new() { Key = Key(box, uid), Sender = "sender@example.test", Subject = $"Message {uid}" };
    internal static MailMessageBody Body(MailboxKey box, uint uid, IReadOnlyList<MailAttachmentDescriptor> attachments) =>
        new(Key(box, uid), $"body {uid}") { Attachments = attachments };
}
