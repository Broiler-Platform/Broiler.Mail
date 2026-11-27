using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class AttachmentSchemaMigrationTests
{
    private static readonly MailAttachmentDescriptor Report = new(2, "report.pdf", "application/pdf", 1234);

    [Fact]
    public async Task VersionFiveMigratesToSixWithVerifiedBackup()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var box = AttachmentCacheTests.Box();
        var cache = new SqliteMailCache(path);
        await cache.SaveSummariesAsync(box, [AttachmentCacheTests.Row(box, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new MailMessageBody(AttachmentCacheTests.Key(box, 1), "kept body"));
        CacheSchemaFixture.DowngradeTo(path, 5);

        var upgraded = new SqliteMailCache(path);
        Assert.False(Assert.Single(await upgraded.ReadSummariesAsync(box, 1)).HasAttachments);
        Assert.Equal(6L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        string backup = Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v5-*.backup"));
        SqliteMailCache.ValidateSnapshot(backup);
        Assert.Equal(5L, CacheSchemaFixture.Scalar(backup, "PRAGMA user_version"));
        Assert.Equal(1L, CacheSchemaFixture.Scalar(backup, "SELECT count(*) FROM messages WHERE body IS NOT NULL"));
        // No backfill: the existing row stays unknown until its body is decoded again.
        Assert.Equal(1L, CacheSchemaFixture.Scalar(path, "SELECT has_attachments IS NULL FROM messages"));
        Assert.Equal("kept body", (await upgraded.ReadBodyAsync(box, 1))!.PlainText);
        await upgraded.SaveBodyAsync(box, new MailMessageBody(AttachmentCacheTests.Key(box, 1), "kept body") { Attachments = [Report] });
        Assert.True(Assert.Single(await upgraded.ReadSummariesAsync(box, 1)).HasAttachments);
        Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v5-*.backup"));
    }

    [Fact]
    public async Task VersionFourUpgradesThroughFiveToSixInOneOpen()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var box = AttachmentCacheTests.Box();
        await new SqliteMailCache(path).SaveSummariesAsync(box, [AttachmentCacheTests.Row(box, 1)], DateTimeOffset.UtcNow);
        CacheSchemaFixture.DowngradeTo(path, 4);

        Assert.Single(await new SqliteMailCache(path).ReadSummariesAsync(box, 1));
        Assert.Equal(6L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        Assert.Equal(4L, CacheSchemaFixture.Scalar(Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v4-*.backup")), "PRAGMA user_version"));
        Assert.Equal(5L, CacheSchemaFixture.Scalar(Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v5-*.backup")), "PRAGMA user_version"));
        Assert.Equal(1L, CacheSchemaFixture.Scalar(path, "SELECT count(*) FROM sqlite_master WHERE name='mailbox_snapshots'"));
        SqliteMailCache.ValidateSnapshot(path);
    }

    [Fact]
    public async Task MigrationDefersUnderAFullQuotaAndBodiesStillSave()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var box = AttachmentCacheTests.Box();
        await new SqliteMailCache(path).SaveSummariesAsync(box, [AttachmentCacheTests.Row(box, 1)], DateTimeOffset.UtcNow);
        CacheSchemaFixture.DowngradeTo(path, 5);
        long quota = CacheSchemaFixture.FillSoMigrationDefers(path);

        var limited = new SqliteMailCache(path, quota);
        await limited.SaveBodyAsync(box, new MailMessageBody(AttachmentCacheTests.Key(box, 1), "saved while deferred") { Attachments = [Report], IsAttachmentListIncomplete = true });
        Assert.Equal(5L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        Assert.Empty(Directory.GetFiles(directory.Root, "mail.db.schema-v5-*.backup"));
        var deferred = await limited.ReadBodyAsync(box, 1);
        Assert.Equal("saved while deferred", deferred!.PlainText); Assert.Null(deferred.Attachments); Assert.False(deferred.IsAttachmentListIncomplete);
        Assert.False(Assert.Single(await limited.ReadSummariesAsync(box, 1)).HasAttachments);
        Assert.Empty(await limited.ReadUidsWithAttachmentsAsync(box, [1]));

        // Once the quota allows it, the next open migrates; the body saved meanwhile stays unknown rather than "none".
        var roomy = new SqliteMailCache(path);
        Assert.Null((await roomy.ReadBodyAsync(box, 1))!.Attachments);
        Assert.Equal(6L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v5-*.backup"));
        Assert.Equal(1L, CacheSchemaFixture.Scalar(path, "SELECT has_attachments IS NULL FROM messages"));
    }

    [Fact]
    public async Task BackupValidationAcceptsVersionSixAndOpenRefusesSeven()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var box = AttachmentCacheTests.Box();
        await new SqliteMailCache(path).SaveSummariesAsync(box, [AttachmentCacheTests.Row(box, 1)], DateTimeOffset.UtcNow);
        SqliteMailCache.ValidateSnapshot(path);

        // A v6 label without the v6 column is not a valid backup.
        string damaged = directory.File("damaged.db"); File.Copy(path, damaged);
        CacheSchemaFixture.Scalar(damaged, "ALTER TABLE messages DROP COLUMN has_attachments");
        Assert.Throws<InvalidDataException>(() => SqliteMailCache.ValidateSnapshot(damaged));

        CacheSchemaFixture.Scalar(path, "PRAGMA user_version=7");
        Assert.Throws<InvalidDataException>(() => SqliteMailCache.ValidateSnapshot(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteMailCache(path).ReadSummariesAsync(box, 1));
        Assert.Equal(7L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        Assert.Equal(1L, CacheSchemaFixture.Scalar(path, "SELECT count(*) FROM messages"));
    }

    [Fact]
    public async Task SearchIndexAttachesOnVersionSix()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var box = AttachmentCacheTests.Box();
        var cache = new SqliteMailCache(path);
        await cache.SaveSummariesAsync(box, [AttachmentCacheTests.Row(box, 1), AttachmentCacheTests.Row(box, 2)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new MailMessageBody(AttachmentCacheTests.Key(box, 1), "indexed body") { Attachments = [Report] });
        await cache.RebuildSearchIndexAsync();
        Assert.Equal(6L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        Assert.True((await new SqliteMailCache(path).GetSearchIndexInfoAsync()).Ready);
        var found = await cache.SearchHeadersAsync(new(box.AccountId, "Message 1") { Field = MailHeaderSearchField.IndexedSubject });
        var row = Assert.Single(found.Messages);
        Assert.Equal(MailSearchEngine.Indexed, found.Engine); Assert.True(row.HasAttachments);
    }
}
