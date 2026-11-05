using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class StarCacheMigrationTests
{
    [Fact]
    public async Task VersionOneSnapshotRemainsUntouchedUntilOpenedAndMigratesWithoutLosingMail()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(TestDirectory.Profile().Id, "Archive", 7);
        var summary = SqliteMailCacheTests.Summary(mailbox, 1) with { IsRead = true }; var time = DateTimeOffset.UtcNow.AddDays(-1);
        await cache.SaveSummariesAsync(mailbox, [summary], time); await cache.SaveBodyAsync(mailbox, new(summary.Key, "Preserved body"));
        Execute(path, "DROP TABLE mailbox_snapshots; DROP TABLE mailbox_snapshot_uids; DROP TRIGGER search_insert; DROP TRIGGER search_update; DROP TRIGGER search_delete; DROP TABLE cache_search_state; ALTER TABLE messages DROP COLUMN is_starred; PRAGMA user_version=1;");
        byte[] before = File.ReadAllBytes(path); SqliteMailCache.ValidateSnapshot(path); Assert.Equal(before, File.ReadAllBytes(path));
        string legacyBackup = directory.File("legacy-backup.db"); File.Copy(path, legacyBackup);
        Assert.False(Assert.Single(await cache.ReadSummariesAsync(mailbox, 1)).IsStarred);
        Assert.True((await cache.ReadSummariesAsync(mailbox, 1))[0].IsRead);
        string migrationBackup = Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v1-*.backup"));
        SqliteMailCache.ValidateSnapshot(migrationBackup);
        Assert.Equal(1L, Scalar(migrationBackup, "PRAGMA user_version"));
        Assert.Equal(1L, Scalar(migrationBackup, "SELECT count(*) FROM messages WHERE body IS NOT NULL AND is_read=1"));
        Assert.Equal(5L, Scalar(path, "PRAGMA user_version")); Assert.Equal(time, (await cache.GetLatestMailboxAsync(mailbox.AccountId, "Archive"))!.ReceivedAt);
        Assert.Equal("Preserved body", (await cache.ReadBodyAsync(mailbox, 1))!.PlainText);
        await cache.UpdateStarStateAsync(summary.Key, true);
        var reopened = new SqliteMailCache(path); var saved = Assert.Single(await reopened.ReadSummariesAsync(mailbox, 1));
        Assert.True(saved.IsStarred); Assert.True(saved.IsRead); SqliteMailCache.ValidateSnapshot(path);
        string restored = directory.File("restored.db"); File.Copy(legacyBackup, restored);
        Assert.False(Assert.Single(await new SqliteMailCache(restored).ReadSummariesAsync(mailbox, 1)).IsStarred);
        Assert.Equal(before, File.ReadAllBytes(legacyBackup));
    }

    [Fact]
    public async Task FailedMigrationDoesNotAdvanceSchemaOrRemoveExistingRows()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(TestDirectory.Profile().Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        Execute(path, "PRAGMA user_version=1"); // A malformed v1 database already has the new column.
        await Assert.ThrowsAsync<IOException>(() => cache.ReadSummariesAsync(mailbox, 1));
        Assert.Equal(1L, Scalar(path, "PRAGMA user_version")); Assert.Equal(1L, Scalar(path, "SELECT count(*) FROM messages"));
    }

    [Fact]
    public async Task StarUpdateDoesNotAffectOtherMailboxEpochOrReceiveTime()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile().Id;
        var time = DateTimeOffset.UtcNow.AddDays(-1);
        foreach (string name in new[] { "INBOX", "Archive" })
            await cache.SaveSummariesAsync(new(account, name, 7), [SqliteMailCacheTests.Summary(new(account, name, 7), 1)], time);
        await cache.UpdateStarStateAsync(new(account, "Archive", 7, 1), true);
        Assert.False((await cache.ReadSummariesAsync(new(account, "INBOX", 7), 1))[0].IsStarred);
        Assert.True((await cache.ReadSummariesAsync(new(account, "Archive", 7), 1))[0].IsStarred);
        Assert.Equal(time, (await cache.GetLatestMailboxAsync(account, "Archive"))!.ReceivedAt);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.UpdateStarStateAsync(new(account, "Archive", 8, 1), true));
    }

    private static object? Scalar(string path, string sql)
    { using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    private static void Execute(string path, string sql) => _ = Scalar(path, sql);
}
