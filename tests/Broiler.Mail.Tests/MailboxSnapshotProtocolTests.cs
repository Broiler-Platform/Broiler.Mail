using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;

namespace Broiler.Mail.Tests;

public sealed class MailboxSnapshotProtocolTests
{
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task CompleteCensusFetchesEveryHeaderInBoundedBatchesWithoutBodiesOrMutations(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security);
        for (uint uid = 1; uid <= 501; uid++) server.Messages.Add(new(uid * 2, "fixture", uid == 2, uid == 3));
        var (receiver, account) = await Setup(server, security); var snapshot = await receiver.GetMailboxSnapshotAsync(account, "台北");
        Assert.Equal(501, snapshot.Messages.Count); Assert.Equal("台北", snapshot.Mailbox.MailboxId); Assert.Equal(account.Id, snapshot.Mailbox.AccountId);
        Assert.Equal(1002u, snapshot.Messages[0].Key.Uid); Assert.Equal(2u, snapshot.Messages[^1].Key.Uid);
        Assert.True(snapshot.Messages.Single(row => row.Key.Uid == 4).IsRead); Assert.True(snapshot.Messages.Single(row => row.Key.Uid == 6).IsStarred);
        Assert.Equal(2, server.SnapshotCensusCount); Assert.Contains(server.MailCommands, command => command.StartsWith("FETCH 1:500 ")); Assert.Contains(server.MailCommands, command => command.StartsWith("FETCH 501:501 "));
        Assert.All(server.MailCommands, command => Assert.True(command.StartsWith("EXAMINE ") || command == "UID SEARCH ALL" || command.StartsWith("FETCH "), command));
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("BODY"));
    }
    [Fact]
    public async Task EmptyFolderStillRequiresTwoCompleteCensusesAndCanonicalInboxIdentity()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); var (receiver, account) = await Setup(server);
        var snapshot = await receiver.GetMailboxSnapshotAsync(account, "inbox"); Assert.Empty(snapshot.Messages); Assert.Equal("INBOX", snapshot.Mailbox.MailboxId); Assert.Equal(2, server.SnapshotCensusCount); Assert.DoesNotContain(server.MailCommands, command => command.StartsWith("FETCH "));
    }
    [Theory]
    [InlineData("* SEARCH 0")]
    [InlineData("* SEARCH 1 1")]
    [InlineData("* SEARCH +1")]
    [InlineData("* SEARCH 1:2")]
    [InlineData("* OK missing census")]
    [InlineData("* SEARCH 1\r\n* SEARCH 1")]
    [InlineData("* ESEARCH UID COUNT 1 ALL 1")]
    [InlineData("* 1 EXPUNGE\r\n* SEARCH 1")]
    public async Task InvalidOrAmbiguousCensusFailsClosed(string response)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.Add(new(1, "fixture")); server.SnapshotCensusResponse = _ => response;
        var (receiver, account) = await Setup(server); await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetMailboxSnapshotAsync(account, "INBOX"));
    }
    [Fact]
    public async Task CensusOrderIsNotAssumedButMembershipMustRemainIdentical()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.AddRange([new(1, "a"), new(2, "b")]); server.SnapshotCensusResponse = _ => "* SEARCH 2 1";
        var (receiver, account) = await Setup(server); Assert.Equal(new uint[] { 2, 1 }, (await receiver.GetMailboxSnapshotAsync(account, "INBOX")).Messages.Select(row => row.Key.Uid));
    }
    [Theory]
    [InlineData("census")]
    [InlineData("fetch")]
    [InlineData("flags")]
    public async Task MembershipRacesAndIncompleteMetadataNeverProduceACompleteSnapshot(string mode)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.AddRange([new(1, "a"), new(2, "b")]);
        if (mode == "census") server.BeforeSnapshotCensus = count => { if (count == 2) { server.Messages.RemoveAt(0); server.Messages.Add(new(3, "new")); } };
        if (mode == "fetch") server.BeforeSnapshotHeaderFetch = (_, _) => server.Messages[1] = new(3, "replacement");
        if (mode == "flags") server.OmitSnapshotFlags = true;
        var (receiver, account) = await Setup(server); await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetMailboxSnapshotAsync(account, "INBOX"));
    }
    [Fact]
    public async Task OversizeFolderIsRejectedBeforeFetchingAndCancellationDoesNotBecomeACompleteSnapshot()
    {
        await using (var server = new LocalImapServer(TransportSecurity.Tls))
        {
            for (uint uid = 1; uid <= MailMailboxSnapshot.MaximumMessages + 1; uid++) server.Messages.Add(new(uid, "fixture"));
            var (receiver, account) = await Setup(server); var failure = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetMailboxSnapshotAsync(account, "INBOX")); Assert.Contains("4,096", failure.Message); Assert.Equal(0, server.SnapshotCensusCount);
        }
        await using var stalled = new LocalImapServer(TransportSecurity.Tls) { StallOnFetch = true }; stalled.Messages.Add(new(1, "fixture")); var (client, profile) = await Setup(stalled);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMailboxSnapshotAsync(profile, "INBOX", cancellation.Token));
    }
    [Theory]
    [InlineData("line")]
    [InlineData("count")]
    public async Task OversizeProtocolLinesAndExcessiveUnsolicitedResponsesAreBounded(string mode)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.Add(new(1, "fixture"));
        server.SnapshotCensusResponse = _ => mode == "line" ? "* SEARCH " + new string('1', 128 * 1024) : string.Concat(Enumerable.Repeat("* OK unsolicited\r\n", 35)) + "* SEARCH 1";
        var (receiver, account) = await Setup(server); await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetMailboxSnapshotAsync(account, "INBOX"));
    }
    private static async Task<(ImapMailReceiver, AccountProfile)> Setup(LocalImapServer server, TransportSecurity security = TransportSecurity.Tls)
    {
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = security } }; var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        return (new(credentials, (_, certificate, _, _) => certificate?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(8)), account);
    }
}
