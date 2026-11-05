using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class IndexedSearchTests
{
    [Fact]
    public async Task IndexBuildUsesFts5AndSupportsScopedPhraseModesWithoutChangingLiteralSemantics()
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7); var key = new MailMessageKey(mailbox.AccountId, "INBOX", 7, 1);
        await cache.SaveSummariesAsync(mailbox, [Row(key, "Alpha beta plan")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(mailbox, new(key, "Saved phrase needle"));
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "phrase needle") { Field = MailHeaderSearchField.IndexedBodyPreview, MailboxId = "INBOX" };
        await Assert.ThrowsAsync<MailSearchIndexUnavailableException>(() => cache.SearchHeadersAsync(query));
        await cache.RebuildSearchIndexAsync(); var info = await cache.GetSearchIndexInfoAsync(); Assert.True(info.Ready); Assert.Equal(1, info.Documents);
        var reopened = new SqliteMailCache(path); Assert.Equal(key, (await reopened.SearchHeadersAsync(query)).Messages.Single().Key);
        Assert.Empty((await reopened.SearchHeadersAsync(query with { Field = MailHeaderSearchField.IndexedSubject })).Messages);
        Assert.Single((await reopened.SearchHeadersAsync(query with { Text = "alpha BETA", Field = MailHeaderSearchField.IndexedSubject })).Messages);
        Assert.Single((await reopened.SearchHeadersAsync(query with { Text = "pha", Field = MailHeaderSearchField.Subject })).Messages);
        Assert.Empty((await reopened.SearchHeadersAsync(query with { Text = "pha", Field = MailHeaderSearchField.IndexedSubject })).Messages);
        Assert.Empty((await reopened.SearchHeadersAsync(query with { AccountId = AccountId.New() })).Messages);
        Assert.Empty((await reopened.SearchHeadersAsync(query with { Text = "\" OR *" })).Messages);
    }

    [Fact]
    public async Task HeaderBodyMoveDeleteAndFlagsKeepIndexTransactionalAndCurrent()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = AccountId.New();
        var inbox = new MailboxKey(account, "INBOX", 7); var one = new MailMessageKey(account, "INBOX", 7, 1);
        await cache.SaveSummariesAsync(inbox, [Row(one, "old term")], DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        await cache.SaveSummariesAsync(inbox, [Row(one, "new phrase")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(inbox, new(one, "cached bodyword"));
        var query = new MailHeaderSearchQuery(account, "bodyword") { Field = MailHeaderSearchField.IndexedHeadersOrBodyPreview, MailboxId = "INBOX" };
        Assert.Single((await cache.SearchHeadersAsync(query)).Messages); Assert.Empty((await cache.SearchHeadersAsync(query with { Text = "old term" })).Messages);
        await cache.UpdateReadStateAsync(one, true); Assert.Single((await cache.SearchHeadersAsync(query with { IsRead = true })).Messages);
        var target = new MailMessageKey(account, "Archive", 9, 900); await cache.ReconcileMoveAsync(one, target);
        Assert.Empty((await cache.SearchHeadersAsync(query)).Messages); Assert.Equal(target, (await cache.SearchHeadersAsync(query with { MailboxId = "Archive" })).Messages.Single().Key);
        await cache.ReconcilePermanentDeleteAsync(target); Assert.Empty((await cache.SearchHeadersAsync(query with { MailboxId = "Archive" })).Messages); Assert.Equal(0, (await cache.GetSearchIndexInfoAsync()).Documents);
    }

    [Fact]
    public async Task StaleDerivedIndexIsRejectedWhileLiteralSearchAndRebuildRemainAvailable()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path); var account = AccountId.New();
        var mailbox = new MailboxKey(account, "INBOX", 7); var key = new MailMessageKey(account, "INBOX", 7, 1);
        await cache.SaveSummariesAsync(mailbox, [Row(key, "original")], DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE messages SET subject='replacement'"; command.ExecuteNonQuery(); }
        Assert.False((await cache.GetSearchIndexInfoAsync()).Ready);
        await Assert.ThrowsAsync<MailSearchIndexUnavailableException>(() => cache.SearchHeadersAsync(new(account, "replacement") { Field = MailHeaderSearchField.IndexedSubject }));
        Assert.Single((await cache.SearchHeadersAsync(new(account, "replacement"))).Messages);
        await cache.RebuildSearchIndexAsync(); Assert.Single((await cache.SearchHeadersAsync(new(account, "replacement") { Field = MailHeaderSearchField.IndexedSubject })).Messages);
        await cache.DisableSearchIndexAsync(); Assert.False(File.Exists(path + ".search.db")); Assert.Single(await cache.ReadSummariesAsync(mailbox, 10));
    }

    [Fact]
    public async Task CancelledRebuildPreservesWorkingIndexAndMigrationBacksUpVersionTwo()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(new(mailbox.AccountId, "INBOX", 7, 1), "needle")], DateTimeOffset.UtcNow);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE mailbox_snapshots; DROP TABLE mailbox_snapshot_uids; DROP TRIGGER search_insert; DROP TRIGGER search_update; DROP TRIGGER search_delete; DROP TABLE cache_search_state; PRAGMA user_version=2;"; command.ExecuteNonQuery(); }
        await cache.RebuildSearchIndexAsync(); Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v2-*.backup"));
        byte[] before = await File.ReadAllBytesAsync(path + ".search.db"); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.RebuildSearchIndexAsync(new CancellationToken(true)));
        Assert.Equal(before, await File.ReadAllBytesAsync(path + ".search.db")); Assert.True((await cache.GetSearchIndexInfoAsync()).Ready);
    }

    [Fact]
    public async Task IndexFailureRollsBackSourceAndDisableAllowsRetry()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7); var key = new MailMessageKey(mailbox.AccountId, "INBOX", 7, 1);
        await cache.SaveSummariesAsync(mailbox, [Row(key, "original")], DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        using (var connection = new SqliteConnection($"Data Source={path}.search.db;Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER reject_write BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'synthetic index failure'); END"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<IOException>(() => cache.SaveSummariesAsync(mailbox, [Row(key, "replacement")], DateTimeOffset.UtcNow));
        Assert.Equal("original", (await cache.ReadSummariesAsync(mailbox, 10)).Single().Subject);
        Assert.True((await cache.GetSearchIndexInfoAsync()).Ready);
        Assert.Single((await cache.SearchHeadersAsync(new(mailbox.AccountId, "original") { Field = MailHeaderSearchField.IndexedSubject })).Messages);
        await cache.DisableSearchIndexAsync(); await cache.SaveSummariesAsync(mailbox, [Row(key, "replacement")], DateTimeOffset.UtcNow);
        Assert.Equal("replacement", (await cache.ReadSummariesAsync(mailbox, 10)).Single().Subject);
    }

    [Fact]
    public async Task EpochChangeExcludesOldIndexedRowsAndCorruptionLeavesLiteralSearchAvailable()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(new(mailbox.AccountId, "INBOX", 7, 1), "old epoch")], DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        var newer = new MailboxKey(mailbox.AccountId, "INBOX", 8);
        await cache.SaveSummariesAsync(newer, [Row(new(mailbox.AccountId, "INBOX", 8, 1), "new epoch")], DateTimeOffset.UtcNow);
        Assert.Empty((await cache.SearchHeadersAsync(new(mailbox.AccountId, "old epoch") { Field = MailHeaderSearchField.IndexedSubject })).Messages);
        Assert.Single((await cache.SearchHeadersAsync(new(mailbox.AccountId, "new epoch") { Field = MailHeaderSearchField.IndexedSubject })).Messages);
        File.WriteAllText(path + ".search.db", "broken derived data");
        Assert.False((await cache.GetSearchIndexInfoAsync()).Ready);
        Assert.Single((await cache.SearchHeadersAsync(new(mailbox.AccountId, "new epoch"))).Messages);
        await cache.RebuildSearchIndexAsync(); Assert.True((await cache.GetSearchIndexInfoAsync()).Ready);
    }

    private static MailMessageSummary Row(MailMessageKey key, string subject) => new() { Key = key, Sender = "sender@example.test", Subject = subject };
}
