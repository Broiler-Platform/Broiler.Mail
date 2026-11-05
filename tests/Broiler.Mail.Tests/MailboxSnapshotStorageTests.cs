using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class MailboxSnapshotStorageTests
{
    [Fact]
    public async Task FullSnapshotRefreshesHeadersButPreservesAbsentBodiesOtherAccountsAndExactCheckpointOnRestart()
    {
        using var dir = new TestDirectory(); string path = dir.File("mail.db"); var cache = new SqliteMailCache(path); var box = Box(); var now = DateTimeOffset.UtcNow;
        await cache.SaveSummariesAsync(box, [Row(box, 1, "old"), Row(box, 2, "retained absent")], now.AddMinutes(-1)); await cache.SaveBodyAsync(box, new(Key(box, 2), "irreplaceable saved preview"));
        var other = Box(); await cache.SaveSummariesAsync(other, [Row(other, 1, "other account")], now.AddMinutes(-1)); await cache.RebuildSearchIndexAsync();
        var checkpoint = await cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1, "updated") with { IsRead = true, IsStarred = true }, Row(box, 3, "new")], now));
        Assert.Equal(2, checkpoint.ServerMessages); Assert.Equal(1, checkpoint.RetainedAbsentMessages); Assert.Equal(64, checkpoint.UidSetSha256.Length);
        var reopened = new SqliteMailCache(path); Assert.Equal(checkpoint, await reopened.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId));
        Assert.Equal("irreplaceable saved preview", (await reopened.ReadBodyAsync(box, 2))!.PlainText); Assert.Equal(3, (await reopened.ReadSummariesAsync(box, 10)).Count);
        var updated = (await reopened.ReadSummariesAsync(box, 10)).Single(row => row.Key.Uid == 1); Assert.True(updated.IsRead); Assert.True(updated.IsStarred);
        Assert.Equal("other account", Assert.Single(await reopened.ReadSummariesAsync(other, 10)).Subject);
        Assert.Single((await reopened.SearchHeadersAsync(new(box.AccountId, "updated") { Field = MailHeaderSearchField.IndexedSubject })).Messages);
    }
    [Fact]
    public async Task EmptySnapshotAndEpochChangesAreCompleteObservationsWithoutDeletingDownloadedMail()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var box = Box(); var now = DateTimeOffset.UtcNow;
        await cache.SaveSummariesAsync(box, [Row(box, 1, "retained")], now.AddMinutes(-1)); await cache.SaveBodyAsync(box, new(Key(box, 1), "saved"));
        var empty = await cache.ApplyMailboxSnapshotAsync(new(box, [], now)); Assert.Equal(0, empty.ServerMessages); Assert.Equal(1, empty.RetainedAbsentMessages); Assert.Equal("saved", (await cache.ReadBodyAsync(box, 1))!.PlainText);
        var next = new MailboxKey(box.AccountId, box.MailboxId, box.UidValidity + 1); var changed = await cache.ApplyMailboxSnapshotAsync(new(next, [Row(next, 1, "new epoch")], now.AddSeconds(1)));
        Assert.Equal(next, (await cache.GetLatestMailboxAsync(box.AccountId, box.MailboxId))!.Key); Assert.Equal(changed, await cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal(0, changed.RetainedAbsentMessages);
        Assert.Equal("saved", (await cache.ReadBodyAsync(box, 1))!.PlainText); Assert.Null(await cache.ReadBodyAsync(next, 1));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("oversize")]
    public async Task InvalidCompleteSnapshotsNeverAdvanceOrPartiallyPublish(string mode)
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var box = Box(); var original = new MailMailboxSnapshot(box, [Row(box, 1, "original")], DateTimeOffset.UtcNow); var checkpoint = await cache.ApplyMailboxSnapshotAsync(original);
        IReadOnlyList<MailMessageSummary> rows = mode switch { "duplicate" => [Row(box, 1, "bad"), Row(box, 1, "again")], "foreign" => [Row(Box(), 2, "foreign")], _ => Enumerable.Range(1, MailMailboxSnapshot.MaximumMessages + 1).Select(uid => Row(box, (uint)uid, "too many")).ToArray() };
        await Assert.ThrowsAsync<ArgumentException>(() => cache.ApplyMailboxSnapshotAsync(original with { Messages = rows, ObservedAt = original.ObservedAt.AddSeconds(1) }));
        Assert.Equal(checkpoint, await cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal("original", Assert.Single(await cache.ReadSummariesAsync(box, 10)).Subject);
    }
    [Fact]
    public async Task CancellationAndOlderObservationsCannotReplaceANewerCheckpoint()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var box = Box(); var snapshot = new MailMailboxSnapshot(box, [Row(box, 1, "original")], DateTimeOffset.UtcNow); var checkpoint = await cache.ApplyMailboxSnapshotAsync(snapshot);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.ApplyMailboxSnapshotAsync(snapshot with { Messages = [Row(box, 2, "older")], ObservedAt = snapshot.ObservedAt.AddSeconds(-1) }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ApplyMailboxSnapshotAsync(snapshot with { Messages = [Row(box, 2, "canceled")], ObservedAt = snapshot.ObservedAt.AddSeconds(1) }, new(true)));
        Assert.Equal(checkpoint, await cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Single(await cache.ReadSummariesAsync(box, 10));
    }
    [Fact]
    public async Task FailureAfterHeaderAndIndexUpdatesRollsBackAllSourceCensusAndIndexChanges()
    {
        using var dir = new TestDirectory(); string path = dir.File("mail.db"); var cache = new SqliteMailCache(path); var box = Box(); var now = DateTimeOffset.UtcNow;
        var checkpoint = await cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1, "original phrase")], now)); await cache.RebuildSearchIndexAsync();
        Execute(path, "CREATE TRIGGER fail_census BEFORE INSERT ON mailbox_snapshot_uids BEGIN SELECT RAISE(ABORT,'synthetic checkpoint failure'); END");
        await Assert.ThrowsAsync<IOException>(() => cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1, "replacement phrase"), Row(box, 2, "new")], now.AddSeconds(1))));
        Assert.Equal(checkpoint, await cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal("original phrase", Assert.Single(await cache.ReadSummariesAsync(box, 10)).Subject);
        Assert.Single((await cache.SearchHeadersAsync(new(box.AccountId, "original phrase") { Field = MailHeaderSearchField.IndexedSubject })).Messages); Assert.Empty((await cache.SearchHeadersAsync(new(box.AccountId, "replacement phrase") { Field = MailHeaderSearchField.IndexedSubject })).Messages);
    }
    [Fact]
    public async Task SourceQuotaFailureKeepsThePreviousCompleteCheckpointAndHeaderSet()
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db"), 256 * 1024); var box = Box(); var now = DateTimeOffset.UtcNow;
        var checkpoint = await cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1, "original")], now));
        var large = Enumerable.Range(1, 100).Select(uid => Row(box, (uint)uid, new string('x', 8192))).ToArray();
        await Assert.ThrowsAsync<IOException>(() => cache.ApplyMailboxSnapshotAsync(new(box, large, now.AddSeconds(1))));
        Assert.Equal(checkpoint, await cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal("original", Assert.Single(await cache.ReadSummariesAsync(box, 10)).Subject);
    }
    [Fact]
    public async Task ChangedPersistedCensusIsDetectedWithoutResettingOrGuessingMembership()
    {
        using var dir = new TestDirectory(); string path = dir.File("mail.db"); var cache = new SqliteMailCache(path); var box = Box();
        await cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1, "saved")], DateTimeOffset.UtcNow)); Execute(path, "UPDATE mailbox_snapshot_uids SET uid=2");
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal("saved", Assert.Single(await cache.ReadSummariesAsync(box, 10)).Subject);
    }
    [Fact]
    public async Task VerifiedProfileRestoreIncludesCheckpointAndAccountAndWholeCleanupRemoveOnlyTheirCheckpoints()
    {
        using var dir = new TestDirectory(); using var profile = MailProfile.Open(dir.File("profile")); var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache); var box = Box(); var other = Box(); var now = DateTimeOffset.UtcNow;
        var checkpoint = await cache.ApplyMailboxSnapshotAsync(new(box, [Row(box, 1, "first")], now)); var second = await cache.ApplyMailboxSnapshotAsync(new(other, [Row(other, 2, "other")], now));
        var cleanup = await storage.ClearAccountDownloadedMailAsync(box.AccountId, dir.File("account-backup")); Assert.Null(await cache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal(second, await cache.ReadMailboxCheckpointAsync(other.AccountId, other.MailboxId));
        string restored = await storage.RestoreBackupAsync(cleanup.BackupDirectory, dir.File("restored")); using var reopened = MailProfile.Open(restored); var restoredCache = new SqliteMailCache(reopened.CachePath); Assert.Equal(checkpoint, await restoredCache.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Equal(second, await restoredCache.ReadMailboxCheckpointAsync(other.AccountId, other.MailboxId));
        await storage.ClearDownloadedMailAsync(dir.File("all-backup")); Assert.Null(await cache.ReadMailboxCheckpointAsync(other.AccountId, other.MailboxId));
    }
    [Fact]
    public async Task VersionFourMigrationCreatesVerifiedBackupAndCanDeferUnderAnExistingFullQuota()
    {
        using var dir = new TestDirectory(); string path = dir.File("mail.db"); var cache = new SqliteMailCache(path); var box = Box();
        await cache.SaveSummariesAsync(box, [Row(box, 1, "saved")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 1), "saved body") { EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["fixture"] = new("fixture", "image/png", new byte[300000]) } });
        Execute(path, "DROP TABLE mailbox_snapshots; DROP TABLE mailbox_snapshot_uids; PRAGMA user_version=4");
        var limited = new SqliteMailCache(path, new FileInfo(path).Length); Assert.Null(await limited.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); Assert.Single(await limited.ReadSummariesAsync(box, 10));
        await Assert.ThrowsAsync<MailMailboxSnapshotUnavailableException>(() => limited.ApplyMailboxSnapshotAsync(new(box, [], DateTimeOffset.UtcNow))); Assert.Empty(Directory.GetFiles(dir.Root, "mail.db.schema-v4-*.backup"));
        var upgraded = new SqliteMailCache(path); Assert.Null(await upgraded.ReadMailboxCheckpointAsync(box.AccountId, box.MailboxId)); string backup = Assert.Single(Directory.GetFiles(dir.Root, "mail.db.schema-v4-*.backup")); Assert.Equal("saved body", (await upgraded.ReadBodyAsync(box, 1))!.PlainText);
        using var db = new SqliteConnection($"Data Source={backup};Pooling=False"); db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version"; Assert.Equal(4L, command.ExecuteScalar());
    }
    private static MailboxKey Box() => new(AccountId.New(), "INBOX", 7);
    private static MailMessageKey Key(MailboxKey box, uint uid) => new(box.AccountId, box.MailboxId, box.UidValidity, uid);
    private static MailMessageSummary Row(MailboxKey box, uint uid, string subject) => new() { Key = Key(box, uid), Sender = "sender@example.test", Subject = subject };
    private static void Execute(string path, string sql) { using var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
}
