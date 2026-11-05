using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class FlagRecoveryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task FlagIntentsPreserveTypedOutcomeAndNeverDeleteOrReplay(bool starred, bool desired, bool lost)
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db");
        await using var server = new LocalImapServer(TransportSecurity.Tls) { DropAfterReadStateStore = lost };
        server.Messages.Add(new(1, "Content-Type: text/plain\r\n\r\nBody", !desired, !desired));
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore(); await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var intents = new MutationIntentJournal(path); var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5), intents);
        var key = new MailMessageKey(account.Id, "INBOX", 7, 1); var mailbox = new MailboxKey(account.Id, "INBOX", 7); var cache = new SqliteMailCache(path);
        await cache.SaveSummariesAsync(mailbox, [new() { Key = key, Sender = "Fixture", Subject = "Flags", IsRead = !desired, IsStarred = !desired }], DateTimeOffset.UnixEpoch);
        await cache.SaveBodyAsync(mailbox, new(key, "Retained body"));
        var status = starred ? (await receiver.SetStarStateAsync(account, key, desired)).Status : (await receiver.SetReadStateAsync(account, key, desired)).Status;
        Assert.Equal(lost ? MailFlagChangeStatus.Unknown : MailFlagChangeStatus.Confirmed, status); Assert.Equal(1, cache.PendingRecoveryCount);
        Assert.Equal(lost ? 1 : 0, (await intents.ReadAsync()).Count);
        if (!lost) { var receipt = (await intents.Receipts.ReadAsync()).Single(); Assert.Equal("flag", receipt.Operation); Assert.Equal(starred ? "flagged" : "seen", receipt.Flag); Assert.Equal(desired, receipt.IsSet); }
        int commands = server.ReadStateStoreCount; var reopened = new SqliteMailCache(path); var report = await reopened.RepairAsync();
        Assert.Equal(lost ? 1 : 0, report.Pending); Assert.Equal(commands, server.ReadStateStoreCount);
        var summary = (await reopened.ReadSummariesAsync(mailbox, 10)).Single();
        Assert.Equal(starred || lost ? !desired : desired, summary.IsRead); Assert.Equal(!starred || lost ? !desired : desired, summary.IsStarred);
        Assert.Equal("Retained body", (await reopened.ReadBodyAsync(mailbox, 1))!.PlainText); Assert.Equal(DateTimeOffset.UnixEpoch, (await reopened.GetLatestMailboxAsync(account.Id, "INBOX"))!.ReceivedAt);
        if (lost)
        {
            string label = (await reopened.ReadUncertainActionsAsync()).Single().Description;
            Assert.Contains(starred ? (desired ? "Star" : "Unstar") : (desired ? "Mark read" : "Mark unread"), label);
        }
    }

    [Fact]
    public async Task MissingHeaderRetainsConfirmedFlagReceiptUntilHeaderIsReceived()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var account = AccountId.New();
        var key = new MailMessageKey(account, "INBOX", 7, 1); var cache = new SqliteMailCache(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.UpdateReadStateAsync(key, true)); Assert.Equal(1, cache.PendingRecoveryCount);
        var reopened = new SqliteMailCache(path); Assert.Equal(1, (await reopened.RepairAsync()).Pending);
        await reopened.SaveSummariesAsync(new(account, "INBOX", 7), [new() { Key = key, Sender = "Fixture", Subject = "Later header", IsStarred = true }], DateTimeOffset.UnixEpoch);
        Assert.Equal(1, (await reopened.RepairAsync()).Repaired); var row = (await reopened.ReadSummariesAsync(new(account, "INBOX", 7), 10)).Single(); Assert.True(row.IsRead); Assert.True(row.IsStarred);
    }

    [Fact]
    public async Task IncompleteFlagPayloadCannotBecomePermanentDeletion()
    {
        using var directory = new TestDirectory(); string path = directory.File("mail.db"); var account = AccountId.New();
        var key = new MailMessageKey(account, "INBOX", 7, 1); var cache = new SqliteMailCache(path);
        await cache.SaveSummariesAsync(new(account, "INBOX", 7), [new() { Key = key, Sender = "Fixture", Subject = "Keep" }], DateTimeOffset.UtcNow);
        await new CacheRecoveryJournal(path + ".recovery.json").RecordFlagAsync(key, "seen", false);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path + ".recovery.json"))!;
        json["data"]!["entries"]![0]!.AsObject().Remove("isSet"); await File.WriteAllTextAsync(path + ".recovery.json", json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.RepairAsync()); Assert.Single(await cache.ReadSummariesAsync(new(account, "INBOX", 7), 10));
    }
}
