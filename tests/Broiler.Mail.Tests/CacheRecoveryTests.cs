using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class CacheRecoveryTests
{
    [Fact]
    public async Task FailedConfirmedMoveKeepsReceiptAndBodyThenRepairsAfterReopen()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var account = AccountId.New();
        var source = new MailMessageKey(account, "INBOX", 7, 1); var target = new MailMessageKey(account, "Archive", 9, 900);
        var mailbox = new MailboxKey(account, "INBOX", 7); var cache = new SqliteMailCache(path);
        await Seed(cache, mailbox, 1); await cache.SaveBodyAsync(mailbox, new(source, "Preserved body"));
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE messages SET body=x'7B'"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.ReconcileMoveAsync(source, target));
        Assert.Equal(1, cache.PendingRecoveryCount); Assert.Single(await new CacheRecoveryJournal(path + ".recovery.json").ReadAsync());
        var reopened = new SqliteMailCache(path); var failed = await reopened.RepairAsync(); Assert.Equal(1, failed.Pending); Assert.Equal(0, failed.Repaired);
        await reopened.SaveBodyAsync(mailbox, new(source, "Preserved body"));
        var repaired = await reopened.RepairAsync(); Assert.Equal(1, repaired.Repaired); Assert.Equal(0, repaired.Pending);
        Assert.Null(await reopened.ReadBodyAsync(mailbox, 1)); Assert.Equal("Preserved body", (await reopened.ReadBodyAsync(new(account, "Archive", 9), 900))!.PlainText);
        Assert.Empty(await new CacheRecoveryJournal(path + ".recovery.json").ReadAsync());
        Assert.Equal(0, (await reopened.RepairAsync()).Repaired);
    }

    [Fact]
    public async Task ReceiptBeforeDeleteAndReceiptAfterAppliedMoveAreBothIdempotentOnRestart()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var account = AccountId.New();
        var mailbox = new MailboxKey(account, "INBOX", 7); var cache = new SqliteMailCache(path); await Seed(cache, mailbox, 1, 2);
        var source = new MailMessageKey(account, "INBOX", 7, 1); var target = new MailMessageKey(account, "Archive", 9, 900);
        await cache.SaveBodyAsync(mailbox, new(source, "Body")); await cache.ReconcileMoveAsync(source, target);
        var journal = new CacheRecoveryJournal(path + ".recovery.json"); await journal.RecordAsync(source, target);
        await journal.RecordAsync(new(account, "INBOX", 7, 2), null);
        var reopened = new SqliteMailCache(path); var report = await reopened.RepairAsync();
        Assert.Equal(2, report.Repaired); Assert.Equal(0, report.Pending); Assert.Empty(await reopened.ReadSummariesAsync(mailbox, 10));
        Assert.Equal("Body", (await reopened.ReadBodyAsync(new(account, "Archive", 9), 900))!.PlainText);
    }

    [Fact]
    public async Task BackupRestoreIncludesReceiptsAndCleanupCannotErasePendingWork()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.File("profile"));
        var cache = new SqliteMailCache(profile.CachePath); var account = AccountId.New(); var mailbox = new MailboxKey(account, "INBOX", 7);
        await Seed(cache, mailbox, 1, 2); await new CacheRecoveryJournal(profile.CachePath + ".recovery.json").RecordAsync(new(account, "INBOX", 7, 1), null);
        var storage = new ProfileStorageService(profile, cache);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.ClearDownloadedMailAsync(directory.File("denied-cleanup")));
        string backup = await storage.CreateBackupAsync(directory.File("backup")); Assert.True(File.Exists(Path.Combine(backup, "mail-cache.db.recovery.json")));
        string restored = await storage.RestoreBackupAsync(backup, directory.File("restored"));
        var restoredCache = new SqliteMailCache(Path.Combine(restored, "mail-cache.db")); Assert.Equal(1, (await restoredCache.RepairAsync()).Repaired);
        Assert.Equal(2u, (await restoredCache.ReadSummariesAsync(mailbox, 10)).Single().Key.Uid); Assert.Equal(2, (await cache.ReadSummariesAsync(mailbox, 10)).Count);
        using var model = new ProfileStorageViewModel(storage, new ImmediateUiDispatcher()); using var view = new ProfileStorageView(model).CreateContent();
        Assert.Contains(Descendants(view).OfType<StandardButton>(), button => button.Text == "Repair confirmed local mail changes");
        bool started = false, finished = false; model.CleanupStarting += () => started = true; model.CleanupFinished += success => finished = success;
        await model.RepairAsync(); Assert.True(started); Assert.True(finished); Assert.Contains("1 completed; 0 pending", model.Status); Assert.Contains("No server operations", model.Status);
        Assert.Equal(2u, (await cache.ReadSummariesAsync(mailbox, 10)).Single().Key.Uid);
    }

    [Fact]
    public async Task JournalDeduplicatesReceiptsAndRefusesCorruptOrFutureStateWithoutReset()
    {
        using var directory = new TestDirectory(); string path = directory.File("recovery.json"); var key = new MailMessageKey(AccountId.New(), "INBOX", 7, 1);
        var one = new CacheRecoveryJournal(path); var id = await one.RecordAsync(key, null);
        Assert.Equal(id, await new CacheRecoveryJournal(path).RecordAsync(key, null)); Assert.Single(await one.ReadAsync());
        string text = await File.ReadAllTextAsync(path);
        var malformed = System.Text.Json.Nodes.JsonNode.Parse(text)!; malformed["data"]!["entries"]![0]!.AsObject().Remove("destination");
        await File.WriteAllTextAsync(path, malformed.ToJsonString()); await Assert.ThrowsAsync<InvalidDataException>(() => one.ReadAsync());
        await File.WriteAllTextAsync(path, text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal));
        byte[] before = File.ReadAllBytes(path); await Assert.ThrowsAsync<InvalidDataException>(() => one.RecordAsync(key, null)); Assert.Equal(before, File.ReadAllBytes(path));
        await File.WriteAllTextAsync(path, "{"); before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => one.ReadAsync()); Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task CancelBeforeReceiptLeavesCacheAndJournalUnchanged()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        var cache = new SqliteMailCache(path); await Seed(cache, mailbox, 1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ReconcilePermanentDeleteAsync(new(mailbox.AccountId, "INBOX", 7, 1), new CancellationToken(true)));
        Assert.Single(await cache.ReadSummariesAsync(mailbox, 10)); Assert.Empty(await new CacheRecoveryJournal(path + ".recovery.json").ReadAsync());
    }

    [Fact]
    public async Task PendingRepairKeepsOfflineReadingAndReceiveButBlocksNewServerMutations()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); string path = directory.File("mail.db");
        var mailbox = new MailboxKey(account.Id, "INBOX", 7); var cache = new SqliteMailCache(path); await Seed(cache, mailbox, 1);
        var key = new MailMessageKey(account.Id, "INBOX", 7, 1); await cache.SaveBodyAsync(mailbox, new(key, "saved body"));
        await new CacheRecoveryJournal(path + ".recovery.json").RecordAsync(key, null); await cache.GetPendingRecoveryCountAsync();
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new ReadStateChangeTests.Writer(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); await model.SelectAsync(key); dispatcher.Drain();
        Assert.Equal("saved body", model.Body!.PlainText); Assert.True(model.CanReceive); Assert.False(model.CanChangeReadState); Assert.False(model.CanChangeStarState);
        Assert.Contains("saved changes need attention", model.RecoveryNotice);
    }

    private static Task Seed(SqliteMailCache cache, MailboxKey mailbox, params uint[] uids) => cache.SaveSummariesAsync(mailbox,
        uids.Select(uid => new MailMessageSummary { Key = new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, uid), Sender = "Fixture", Subject = "Recovery" }).ToArray(), DateTimeOffset.UtcNow);
    private static IEnumerable<UiElement> Descendants(UiElement root)
    { foreach (var child in root.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
}
