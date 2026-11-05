using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Windows.Hosting;

internal static class CacheRecoverySmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath);
        await new JsonAccountStore(profile.AccountsPath).SaveAsync(account);
        var mailbox = new MailboxKey(account.Id, "INBOX", 7); var one = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var two = new MailMessageKey(account.Id, "INBOX", 7, 2); var target = new MailMessageKey(account.Id, "Archive", 9, 900);
        await cache.SaveSummariesAsync(mailbox, [new() { Key = one, Sender = "Fixture", Subject = "Move receipt" },
            new() { Key = two, Sender = "Fixture", Subject = "Delete receipt" }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(one, "Recovered move body"));
        var journal = new CacheRecoveryJournal(profile.CachePath + ".recovery.json"); await journal.RecordAsync(one, target); await journal.RecordAsync(two, null);
        var reopened = new SqliteMailCache(profile.CachePath); var report = await reopened.RepairAsync();
        if (report.Repaired != 2 || report.Pending != 0 || (await reopened.ReadSummariesAsync(mailbox, 10)).Count != 0 ||
            (await reopened.ReadBodyAsync(new(account.Id, "Archive", 9), 900))?.PlainText != "Recovered move body")
            throw new InvalidOperationException("Published confirmed move/delete receipt recovery failed.");
        await journal.RecordAsync(one, target);
        if ((await reopened.RepairAsync()).Repaired != 1) throw new InvalidOperationException("Published receipt replay was not idempotent.");
        await journal.RecordAsync(target, null);
        var storage = new ProfileStorageService(profile, reopened);
        string backup = await storage.CreateBackupAsync(directory + "-backup");
        string restored = await storage.RestoreBackupAsync(backup, directory + "-restored");
        var restoredCache = new SqliteMailCache(Path.Combine(restored, "mail-cache.db"));
        if ((await restoredCache.RepairAsync()).Repaired != 1 || await restoredCache.ReadBodyAsync(new(account.Id, "Archive", 9), 900) is not null ||
            await reopened.ReadBodyAsync(new(account.Id, "Archive", 9), 900) is null)
            throw new InvalidOperationException("Published receipt backup/restore isolation failed.");
        var unknownKey = new MailMessageKey(account.Id, "INBOX", 7, 3);
        await reopened.SaveSummariesAsync(mailbox, [new() { Key = unknownKey, Sender = "Fixture", Subject = "Uncertain fixture" }], DateTimeOffset.UtcNow);
        var flagKey = unknownKey;
        var readReceipt = await journal.RecordFlagAsync(flagKey, "seen", true);
        var starReceipt = await journal.RecordFlagAsync(flagKey, "flagged", false);
        var flagsRepaired = await reopened.RepairAsync();
        var flagRow = (await reopened.ReadSummariesAsync(mailbox, 10)).Single();
        if (flagsRepaired.Repaired < 2 || !flagRow.IsRead || flagRow.IsStarred) throw new InvalidOperationException("Published typed flag repair failed.");
        var intents = new MutationIntentJournal(profile.CachePath); var unknownId = await intents.BeginAsync(unknownKey, null, flag: "flagged", isSet: true);
        var uncertainRepair = await reopened.RepairAsync();
        if (uncertainRepair.Pending != 1 || (await reopened.ReadSummariesAsync(mailbox, 10)).Single().Key != unknownKey)
            throw new InvalidOperationException("Published startup repair replayed an uncertain action.");
        using var review = new Broiler.Mail.Application.ViewModels.ProfileStorageViewModel(storage, new Broiler.UI.Standard.ImmediateUiDispatcher());
        await review.ReviewUncertainActionsAsync(); review.SelectUncertainAction(unknownId);
        await review.DismissUncertainActionAsync();
        if ((await intents.ReadAsync()).Count != 1) throw new InvalidOperationException("Published uncertain dismissal skipped confirmation.");
        review.RequestUncertainDismissal(); await review.DismissUncertainActionAsync();
        if ((await intents.ReadAsync()).Count != 0 || (await reopened.ReadSummariesAsync(mailbox, 10)).Single().Key != unknownKey)
            throw new InvalidOperationException("Published uncertain dismissal changed cached mail.");

    }
}
