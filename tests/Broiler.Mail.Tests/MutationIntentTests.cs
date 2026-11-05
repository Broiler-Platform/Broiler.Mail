using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class MutationIntentTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConfirmedAndLostMoveOrDeletePersistBeforeReturningAndNeverReplay(bool delete, bool lost)
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AdvertiseMove = true, AdvertiseUidPlus = true, DropAfterMove = !delete && lost, DropAfterUidExpunge = delete && lost,
          FolderListResponse = "* LIST () \"/\" INBOX\r\n* LIST () \"/\" Archive" };
        server.Messages.Add(new(1, "From: sender@example.test\r\nContent-Type: text/plain\r\n\r\nBody"));
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore(); await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var journal = new MutationIntentJournal(path);
        var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5), journal);
        var key = new MailMessageKey(account.Id, "INBOX", 7, 1); var cache = new SqliteMailCache(path);
        var mailbox = new MailboxKey(account.Id, "INBOX", 7); await cache.SaveSummariesAsync(mailbox, [new() { Key = key, Sender = "Sender", Subject = "Synthetic action" }], DateTimeOffset.UtcNow);
        if (delete) Assert.Equal(lost ? MailDeleteStatus.Unknown : MailDeleteStatus.Confirmed, (await receiver.PermanentlyDeleteAsync(account, key)).Status);
        else Assert.Equal(lost ? MailMoveStatus.Unknown : MailMoveStatus.Confirmed, (await receiver.MoveAsync(account, key, "Archive")).Status);
        Assert.Equal(1, cache.PendingRecoveryCount);
        Assert.Equal(lost ? 1 : 0, (await new MutationIntentJournal(path).ReadAsync()).Count);
        Assert.Equal(lost ? 0 : 1, (await journal.Receipts.ReadAsync()).Count);
        int commands = server.MoveCount + server.UidExpungeCount;
        var report = await new SqliteMailCache(path).RepairAsync(); Assert.Equal(lost ? 1 : 0, report.Pending);
        Assert.Equal(commands, server.MoveCount + server.UidExpungeCount);
        if (lost) { Assert.Single(await cache.ReadSummariesAsync(mailbox, 10)); Assert.Single(await cache.ReadUncertainActionsAsync()); }
    }

    [Fact]
    public async Task ConflictingIntentReceiptCannotBeRepairedOrSilentlyDismissed()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        var key = new MailMessageKey(AccountId.New(), "INBOX", 7, 1); var intents = new MutationIntentJournal(path);
        var id = await intents.BeginAsync(key, null); await intents.Receipts.ConfirmIntentAsync(id, key with { Uid = 2 }, null);
        var cache = new SqliteMailCache(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.RepairAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.ReadUncertainActionsAsync());
        Assert.Single(await intents.ReadAsync()); Assert.Single(await intents.Receipts.ReadAsync());
    }

    [Fact]
    public async Task UnwriteableIntentStopsBeforeAnyNetworkAndDefinitePreflightRefusalClearsIntent()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        await using var server = new LocalImapServer(TransportSecurity.Tls); var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore(); await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var journal = new MutationIntentJournal(path); var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5), journal);
        var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        Assert.Equal(MailDeleteStatus.NotApplied, (await receiver.PermanentlyDeleteAsync(account, key)).Status); Assert.Empty(await journal.ReadAsync());
        int count = server.Commands.Count; await File.WriteAllTextAsync(path + ".intents.json", "{");
        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.MoveAsync(account, key, "Archive")); Assert.Equal(count, server.Commands.Count);
    }

    [Fact]
    public async Task UncertainReviewRequiresExplicitConfirmationKeepsCacheAndBacksUpExactRecord()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.File("profile")); var account = TestDirectory.Profile();
        await new JsonAccountStore(profile.AccountsPath).SaveAsync(account); var cache = new SqliteMailCache(profile.CachePath);
        var key = new MailMessageKey(account.Id, "INBOX", 7, 1); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [new() { Key = key, Sender = "Sender", Subject = "Checked message" }], DateTimeOffset.UtcNow);
        var intents = new MutationIntentJournal(profile.CachePath); var id = await intents.BeginAsync(key, "Archive");
        Assert.Equal(1, (await new SqliteMailCache(profile.CachePath).RepairAsync()).Pending);
        var storage = new ProfileStorageService(profile, cache); using var model = new ProfileStorageViewModel(storage, new ImmediateUiDispatcher());
        await model.ReviewUncertainActionsAsync(); Assert.Contains(account.EmailAddress, model.UncertainActions.Single().Description); Assert.Contains("Checked message", model.UncertainActions[0].Description);
        model.SelectUncertainAction(id); await model.DismissUncertainActionAsync(); Assert.Single(await intents.ReadAsync());
        model.RequestUncertainDismissal(); Assert.NotNull(model.PendingUncertainDismissal); model.CancelUncertainDismissal(); Assert.Single(await intents.ReadAsync());
        model.RequestUncertainDismissal(); await model.DismissUncertainActionAsync(); Assert.Empty(await intents.ReadAsync()); Assert.Single(await cache.ReadSummariesAsync(mailbox, 10));
        Assert.Contains("No server action was repeated", model.Status);
        var backup = Directory.GetDirectories(Path.Combine(profile.DirectoryPath, "backups"), "uncertain-*").Single();
        Assert.Single(await new MutationIntentJournal(Path.Combine(backup, "mail-cache.db")).ReadAsync());
    }

    [Fact]
    public async Task ActiveOperationCannotBeDismissedAndConfirmedReceiptIsNotTreatedAsUncertain()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var key = new MailMessageKey(AccountId.New(), "INBOX", 7, 1);
        var journal = new MutationIntentJournal(path); var id = await journal.BeginAsync(key, null); var cache = new SqliteMailCache(path);
        using (journal.Enter(key)) { Assert.Equal(1, cache.PendingRecoveryCount); await Assert.ThrowsAsync<InvalidOperationException>(() => cache.DismissUncertainActionAsync(id)); }
        await journal.Receipts.ConfirmIntentAsync(id, key, null); Assert.Empty(await cache.ReadUncertainActionsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.DismissUncertainActionAsync(id)); Assert.Equal(1, (await cache.RepairAsync()).Repaired);
        Assert.Empty(await journal.ReadAsync()); Assert.Equal(0, cache.PendingRecoveryCount);
    }
}
