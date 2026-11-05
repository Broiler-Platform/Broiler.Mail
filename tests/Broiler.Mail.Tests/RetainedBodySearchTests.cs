using System.Text;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class RetainedBodySearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecodedTailSurvivesRestartAndIsIndexedWithoutChangingPreviewSearch(bool html)
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var key = new MailMessageKey(AccountId.New(), "INBOX", 7, 1); var box = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        string text = new string('a', 34000) + " retained tail needle";
        var body = await Decode(key, "Content-Type: text/" + (html ? "html" : "plain") + "; charset=utf-8\r\n\r\n" + (html ? "<p>" + text + "</p><script>excluded script needle</script>" : text));
        Assert.True(body.IsTruncated); Assert.True(body.PlainText.Length <= 32000); Assert.DoesNotContain("tail needle", body.PlainText);
        Assert.Contains("retained tail needle", body.RetainedBodyText); Assert.DoesNotContain("excluded script", body.RetainedBodyText);
        await cache.SaveSummariesAsync(box, [Row(key, "header phrase"), Row(key with { Uid = 2 }, "header phrase")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, body); await cache.RebuildSearchIndexAsync();
        var reopened = new SqliteMailCache(path); Assert.Equal(body.RetainedBodyText, (await reopened.ReadBodyAsync(box, 1))!.RetainedBodyText);
        var query = new MailHeaderSearchQuery(key.AccountId, "retained tail needle") { Field = MailHeaderSearchField.IndexedRetainedBody };
        Assert.Equal(key, Assert.Single((await reopened.SearchHeadersAsync(query)).Messages).Key);
        foreach (var mode in new[] { MailHeaderSearchField.IndexedBodyPreview, MailHeaderSearchField.IndexedHeadersOrBodyPreview, MailHeaderSearchField.BodyPreview, MailHeaderSearchField.IndexedHeaders })
            Assert.Empty((await reopened.SearchHeadersAsync(query with { Field = mode })).Messages);
        Assert.Empty((await reopened.SearchHeadersAsync(query with { AccountId = AccountId.New() })).Messages);
        Assert.Empty((await reopened.SearchHeadersAsync(query with { Text = "header phrase" })).Messages);
        Assert.Equal(2, (await reopened.SearchHeadersAsync(query with { Text = "header phrase", Field = MailHeaderSearchField.IndexedHeadersOrRetainedBody })).Messages.Count);
        var info = await reopened.GetSearchIndexInfoAsync(); Assert.Equal(1, info.RetainedBodyDocuments); Assert.Contains("1 of 2", info.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetentionLimitNeverIndexesAPartialBodyAsComplete(bool overLimit)
    {
        var key = new MailMessageKey(AccountId.New(), "INBOX", 1, 1);
        var body = await Decode(key, "Content-Type: text/plain\r\n\r\n" + new string('x', MailMessageBody.MaximumRetainedTextCharacters + (overLimit ? 1 : 0)));
        Assert.True(body.IsTruncated); Assert.Equal(32000, body.PlainText.Length);
        if (overLimit) Assert.Null(body.RetainedBodyText); else Assert.Equal(MailMessageBody.MaximumRetainedTextCharacters, body.RetainedBodyText!.Length);
    }

    [Fact]
    public async Task IncompleteMimeIsNotClaimedCompleteAndAttachmentTextIsExcluded()
    {
        var key = new MailMessageKey(AccountId.New(), "INBOX", 1, 1);
        var incomplete = await Decode(key, "Content-Type: multipart/mixed\r\n\r\npartial visible text"); Assert.Null(incomplete.RetainedBodyText);
        var body = await Decode(key, "Content-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\nSGVsbG8g8J+YgA==\r\n--x\r\nContent-Type: text/plain\r\nContent-Disposition: attachment; filename=secret.txt\r\n\r\nsecret attachment needle\r\n--x--\r\n");
        Assert.Contains("Hello 😀", body.RetainedBodyText); Assert.DoesNotContain("secret attachment", body.RetainedBodyText);
        var empty = await Decode(key, "Content-Type: application/octet-stream\r\n\r\nopaque"); Assert.Equal("", empty.RetainedBodyText);
    }

    [Fact]
    public async Task OldPreviewOnlyBodiesStayExplicitlyUncoveredAndFilterOnlyFullBodySearchRequiresCoverage()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var box = new MailboxKey(AccountId.New(), "INBOX", 1); var one = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1); var two = one with { Uid = 2 };
        await cache.SaveSummariesAsync(box, [Row(one, "legacy"), Row(two, "retained")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(one, "legacy preview needle")); await cache.SaveBodyAsync(box, new(two, "preview") { RetainedBodyText = "retained body needle" });
        await cache.RebuildSearchIndexAsync();
        var query = new MailHeaderSearchQuery(box.AccountId, "needle") { Field = MailHeaderSearchField.IndexedRetainedBody };
        Assert.Equal(two, Assert.Single((await cache.SearchHeadersAsync(query)).Messages).Key);
        Assert.Equal(one, Assert.Single((await cache.SearchHeadersAsync(query with { Field = MailHeaderSearchField.IndexedBodyPreview })).Messages).Key);
        Assert.Equal(two, Assert.Single((await cache.SearchHeadersAsync(query with { Text = "", IsRead = false })).Messages).Key);
        Assert.Empty((await cache.SearchHeadersAsync(query with { IsBodyCached = false })).Messages);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SearchHeadersAsync(query, new(true)));
        Assert.Null((await cache.ReadBodyAsync(box, 1))!.RetainedBodyText);
    }

    [Fact]
    public async Task FullBodyUpdatesMovesDeletesAndEpochChangesMaintainExactIndexIdentity()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var box = new MailboxKey(AccountId.New(), "INBOX", 1); var key = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1);
        await cache.SaveSummariesAsync(box, [Row(key, "subject")], DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        await cache.SaveBodyAsync(box, new(key, "preview") { RetainedBodyText = "old body phrase" });
        await cache.SaveBodyAsync(box, new(key, "preview") { RetainedBodyText = "new body phrase" });
        var query = new MailHeaderSearchQuery(box.AccountId, "new body phrase") { Field = MailHeaderSearchField.IndexedRetainedBody };
        Assert.Empty((await cache.SearchHeadersAsync(query with { Text = "old body phrase" })).Messages);
        var destination = key with { MailboxId = "Archive", UidValidity = 9, Uid = 42 }; await cache.ReconcileMoveAsync(key, destination);
        Assert.Equal(destination, Assert.Single((await cache.SearchHeadersAsync(query)).Messages).Key);
        Assert.Equal("new body phrase", (await cache.ReadBodyAsync(new(box.AccountId, "Archive", 9), 42))!.RetainedBodyText);
        await cache.SaveSummariesAsync(new(box.AccountId, "Archive", 10), [Row(destination with { UidValidity = 10 }, "new epoch")], DateTimeOffset.UtcNow);
        Assert.Empty((await cache.SearchHeadersAsync(query)).Messages);
        await cache.ReconcilePermanentDeleteAsync(destination); Assert.Equal(0, (await cache.GetSearchIndexInfoAsync()).RetainedBodyDocuments);
    }

    [Fact]
    public async Task VersionOneIndexIsRejectedWithoutDeletionAndRebuildDoesNotInventMissingText()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var box = new MailboxKey(AccountId.New(), "INBOX", 1); var key = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1);
        await cache.SaveSummariesAsync(box, [Row(key, "saved subject")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(box, new(key, "preview only")); await cache.RebuildSearchIndexAsync();
        using (var db = new SqliteConnection($"Data Source={path}.search.db;Pooling=False")) { db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version=1"; command.ExecuteNonQuery(); }
        byte[] previous = await File.ReadAllBytesAsync(path + ".search.db"); Assert.False((await cache.GetSearchIndexInfoAsync()).Ready); Assert.Equal(previous, await File.ReadAllBytesAsync(path + ".search.db"));
        await Assert.ThrowsAsync<MailSearchIndexUnavailableException>(() => cache.SearchHeadersAsync(new(box.AccountId, "preview") { Field = MailHeaderSearchField.IndexedRetainedBody }));
        Assert.Single((await cache.SearchHeadersAsync(new(box.AccountId, "saved subject"))).Messages);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.RebuildSearchIndexAsync(new(true))); Assert.Equal(previous, await File.ReadAllBytesAsync(path + ".search.db"));
        await cache.RebuildSearchIndexAsync(); Assert.True((await cache.GetSearchIndexInfoAsync()).Ready); Assert.Equal(0, (await cache.GetSearchIndexInfoAsync()).RetainedBodyDocuments);
    }

    [Fact]
    public async Task OverLimitRetainedTextIsRefusedWithoutChangingSavedTextOrIndex()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = new MailboxKey(AccountId.New(), "INBOX", 1); var key = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1);
        await cache.SaveSummariesAsync(box, [Row(key, "subject")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(box, new(key, "preview") { RetainedBodyText = "saved body" }); await cache.RebuildSearchIndexAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveBodyAsync(box, new(key, "preview") { RetainedBodyText = new string('x', MailMessageBody.MaximumRetainedTextCharacters + 1) }));
        Assert.Equal("saved body", (await cache.ReadBodyAsync(box, 1))!.RetainedBodyText); Assert.Single((await cache.SearchHeadersAsync(new(box.AccountId, "saved body") { Field = MailHeaderSearchField.IndexedRetainedBody })).Messages);
    }

    [Fact]
    public async Task DisabledAccountCanFindARetainedTailOfflineWithoutFetchingOrClaimingPreviewCoverage()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile() with { IsEnabled = false }; var box = new MailboxKey(account.Id, "INBOX", 1); var key = new MailMessageKey(account.Id, "INBOX", 1, 1);
        await cache.SaveSummariesAsync(box, [Row(key, "subject")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(box, new(key, "short display preview") { RetainedBodyText = "retained tail needle" }); await cache.RebuildSearchIndexAsync();
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new NoNetworkReceiver(), dispatcher, cache); model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearchField(MailHeaderSearchField.IndexedRetainedBody); model.SetSearch("tail needle"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(key, Assert.Single(model.Messages).Key); Assert.Contains("Older preview-only", model.SearchNotice); Assert.Contains("beyond the preview", model.SearchNotice);
        await model.SelectAsync(key); dispatcher.Drain(); Assert.Equal("short display preview", model.Body!.PlainText); Assert.True(model.IsBodyFromCache);
    }

    private static async Task<MailMessageBody> Decode(MailMessageKey key, string source)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)); return await MessageTextDecoder.DecodeAsync(key, stream, default); }
    [Fact]
    public async Task VersionThreeUpgradeKeepsVerifiedRecoveryCopyAndFutureFormatsAreNeverReset()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var box = new MailboxKey(AccountId.New(), "INBOX", 1); var key = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1);
        await cache.SaveSummariesAsync(box, [Row(key, "legacy")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(box, new(key, "legacy preview"));
        static long Version(string file, int? set = null) { using var db = new SqliteConnection($"Data Source={file};Pooling=False"); db.Open(); using var command = db.CreateCommand(); command.CommandText = set is {} value ? $"PRAGMA user_version={value}" : "PRAGMA user_version"; return Convert.ToInt64(command.ExecuteScalar()); }
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False")) { db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE mailbox_snapshots; DROP TABLE mailbox_snapshot_uids"; command.ExecuteNonQuery(); }
        Version(path, 3); var reopened = new SqliteMailCache(path); Assert.Equal("legacy preview", (await reopened.ReadBodyAsync(box, 1))!.PlainText);
        string backup = Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v3-*.backup")); Assert.Equal(3, Version(backup)); Assert.Equal(5, Version(path));
        using (var db = new SqliteConnection($"Data Source={backup};Pooling=False")) { db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT body FROM messages"; Assert.DoesNotContain("RetainedBodyText", Encoding.UTF8.GetString((byte[])command.ExecuteScalar()!)); }
        await reopened.SaveBodyAsync(box, new(key, "preview") { RetainedBodyText = "retained body" }); Assert.Single(Directory.GetFiles(directory.Root, "mail.db.schema-v3-*.backup"));
        Version(path, 6); await Assert.ThrowsAsync<InvalidDataException>(() => reopened.ReadBodyAsync(box, 1)); Assert.Equal(6, Version(path));
    }

    [Fact]
    public async Task SourceQuotaFailureRollsBackRetainedTextAndItsSearchDocument()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"), 256 * 1024);
        var box = new MailboxKey(AccountId.New(), "INBOX", 1); var key = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1);
        await cache.SaveSummariesAsync(box, [Row(key, "subject")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(box, new(key, "preview") { RetainedBodyText = "saved retained phrase" }); await cache.RebuildSearchIndexAsync();
        await Assert.ThrowsAsync<IOException>(() => cache.SaveBodyAsync(box, new(key, "replacement") { RetainedBodyText = new string('x', 300000) + " rejected phrase" }));
        Assert.Equal("saved retained phrase", (await cache.ReadBodyAsync(box, 1))!.RetainedBodyText);
        Assert.Single((await cache.SearchHeadersAsync(new(key.AccountId, "saved retained phrase") { Field = MailHeaderSearchField.IndexedRetainedBody })).Messages);
        Assert.Empty((await cache.SearchHeadersAsync(new(key.AccountId, "rejected phrase") { Field = MailHeaderSearchField.IndexedRetainedBody })).Messages);
    }
    [Fact]
    public async Task RetainedTextRestoresWithSourceMailWhileDisposableIndexMustBeRebuilt()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.File("profile"));
        var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache);
        var box = new MailboxKey(AccountId.New(), "INBOX", 1); var key = new MailMessageKey(box.AccountId, box.MailboxId, 1, 1);
        string text = new string('x', 34000) + " restored tail phrase";
        await cache.SaveSummariesAsync(box, [Row(key, "restored")], DateTimeOffset.UtcNow); await cache.SaveBodyAsync(box, new(key, "short preview") { RetainedBodyText = text }); await cache.RebuildSearchIndexAsync();
        string backup = await storage.CreateBackupAsync(directory.File("backup")); string restored = await storage.RestoreBackupAsync(backup, directory.File("restored"));
        using var reopened = MailProfile.Open(restored); var restoredCache = new SqliteMailCache(reopened.CachePath);
        Assert.False((await restoredCache.GetSearchIndexInfoAsync()).Ready); Assert.Equal(text, (await restoredCache.ReadBodyAsync(box, 1))!.RetainedBodyText);
        await restoredCache.RebuildSearchIndexAsync(); Assert.Equal(key, Assert.Single((await restoredCache.SearchHeadersAsync(new(key.AccountId, "restored tail phrase") { Field = MailHeaderSearchField.IndexedRetainedBody })).Messages).Key);
        Assert.DoesNotContain(Directory.GetFiles(backup, "*", SearchOption.AllDirectories), file => file.EndsWith(".search.db", StringComparison.Ordinal));
    }
    private static MailMessageSummary Row(MailMessageKey key, string subject) => new() { Key = key, Subject = subject, Sender = "sender@example.test" };
    private sealed class NoNetworkReceiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network allowed.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network allowed.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No network allowed.");
    }
}
