using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using MailKit.Net.Imap;

namespace Broiler.Mail.Tests;

public sealed class ImapConnectionTests
{
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task AuthenticatesOverEncryptedConnectionWithoutReadingMessages(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security);
        var credentials = new TestCredentialStore();
        var profile = Profile(server, security);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        await Receiver(credentials, server).TestConnectionAsync(profile);
        Assert.True(server.Authenticated);
        Assert.True(server.AuthenticatedOverTls);
        Assert.DoesNotContain(server.Commands, command => command is "SELECT" or "EXAMINE" or "FETCH" or "STORE");
        if (security == TransportSecurity.StartTls) Assert.Contains("STARTTLS", server.Commands);
    }

    [Fact]
    public async Task ProductionCertificateValidationRejectsUntrustedCertificate()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        var credentials = new TestCredentialStore();
        var profile = Profile(server, TransportSecurity.Tls);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => new ImapMailReceiver(credentials).TestConnectionAsync(profile));
        Assert.Contains("TLS", error.Message);
        Assert.DoesNotContain("AUTHENTICATE", server.Commands);
        Assert.False(server.Authenticated);
    }

    [Fact]
    public async Task RequiredStartTlsNeverFallsBackToPlaintextAuthentication()
    {
        await using var server = new LocalImapServer(TransportSecurity.StartTls, advertiseStartTls: false);
        var credentials = new TestCredentialStore();
        var profile = Profile(server, TransportSecurity.StartTls);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        await Assert.ThrowsAsync<MailConnectionException>(() => Receiver(credentials, server).TestConnectionAsync(profile));
        Assert.DoesNotContain("AUTHENTICATE", server.Commands);
        Assert.DoesNotContain("LOGIN", server.Commands);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotExposeServerResponseOrSecret()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls, rejectAuthentication: true);
        var credentials = new TestCredentialStore();
        var profile = Profile(server, TransportSecurity.Tls);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Receiver(credentials, server).TestConnectionAsync(profile));
        Assert.Contains("Authentication", error.Message);
        Assert.DoesNotContain(LocalImapServer.Password, error.ToString());
    }

    [Fact]
    public async Task MissingOrChangedCredentialBindingDoesNotOpenNetworkConnection()
    {
        var credentials = new TestCredentialStore();
        var profile = TestDirectory.Profile();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        var changed = profile with { IncomingServer = profile.IncomingServer with { Host = "different.example.test" } };
        bool clientCreated = false;
        var receiver = new ImapMailReceiver(credentials, () => { clientCreated = true; return new(); }, TimeSpan.FromSeconds(1));
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.TestConnectionAsync(changed));
        Assert.Contains("No password", error.Message);
        Assert.False(clientCreated);
    }

    [Fact]
    public async Task SilentServerTimesOut()
    {
        await using var server = new LocalImapServer(TransportSecurity.StartTls, silent: true);
        var credentials = new TestCredentialStore();
        var profile = Profile(server, TransportSecurity.StartTls);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Receiver(credentials, server, TimeSpan.FromMilliseconds(300)).TestConnectionAsync(profile));
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task CallerCanCancelAConnectionTest()
    {
        await using var server = new LocalImapServer(TransportSecurity.StartTls, silent: true);
        var credentials = new TestCredentialStore();
        var profile = Profile(server, TransportSecurity.StartTls);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Receiver(credentials, server).TestConnectionAsync(profile, cancellation.Token));
    }

    [Fact]
    public async Task OAuthIsRejectedBeforeAnyNetworkActivity()
    {
        var profile = TestDirectory.Profile();
        profile = profile with { IncomingServer = profile.IncomingServer with { Authentication = AuthenticationMethod.OAuth2 } };
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => new ImapMailReceiver(new TestCredentialStore()).TestConnectionAsync(profile));
        Assert.Contains("OAuth", error.Message);
    }

    private static ImapMailReceiver Receiver(TestCredentialStore credentials, LocalImapServer server, TimeSpan? timeout = null) =>
        new(credentials, () => new ImapClient
        {
            // Only this fixture's ephemeral certificate is trusted. No OS trust store is changed.
            ServerCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == server.Certificate.GetCertHashString(),
        }, timeout ?? TimeSpan.FromSeconds(5));

    private static AccountProfile Profile(LocalImapServer server, TransportSecurity security) => TestDirectory.Profile() with
    {
        IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = security },
    };
}
