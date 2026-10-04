using System.Diagnostics;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using MailKit.Net.Smtp;

namespace Broiler.Mail.Tests;

/// <summary>
/// The non-sending SMTP sign-in test against a loopback fixture. Fixtures in connection-test mode fail the test
/// on any command besides EHLO, STARTTLS, AUTH and QUIT, so no submission command can pass unnoticed.
/// </summary>
public sealed class SmtpConnectionTests
{
    private static readonly string[] SignInCommands = ["EHLO", "STARTTLS", "AUTH", "QUIT"];

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task AuthenticatesOverTlsAndStartTlsThenQuitsWithoutSubmitting(TransportSecurity security)
    {
        await using var server = new LocalSmtpServer(security, connectionTestOnly: true);
        var account = Profile(server, security);
        await Tester(await Credentials(account), server).TestConnectionAsync(account);
        Assert.True(server.AuthenticatedOverTls);
        Assert.All(server.Commands, command => Assert.Contains(command, SignInCommands));
        Assert.Contains("QUIT", server.Commands);
        Assert.Equal(security == TransportSecurity.StartTls, server.Commands.Contains("STARTTLS"));
        Assert.Equal(0, server.DataCount);
        Assert.Null(server.Sender);
        Assert.Empty(server.Recipients);
    }

    [Fact]
    public async Task ProductionConstructorRejectsAnUntrustedCertificate()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.Tls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => new SmtpConnectionTester(credentials).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.TlsVerification, error.Failure);
        Assert.DoesNotContain("AUTH", server.Commands);
    }

    [Fact]
    public async Task RequiredStartTlsNeverAuthenticatesInPlaintext()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, advertiseStartTls: false, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.StartTls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.TlsUnavailable, error.Failure);
        Assert.Contains("password was not sent", error.Message);
        Assert.DoesNotContain("AUTH", server.Commands);
    }

    [Fact]
    public async Task ImplicitTlsAgainstAStartTlsPortFailsWithoutAuthAndNamesThePort()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.Tls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.TlsVerification, error.Failure);
        Assert.Contains("port", error.Message);
        Assert.Contains("connection security", error.Message);
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task RejectedSignInDoesNotExposeServerTextOrTheSecret()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls, SmtpFixtureOutcome.RejectAuthentication, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.Tls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.AuthenticationRejected, error.Failure);
        // The fixture's 535 reply echoes the password; none of it reaches the error.
        Assert.DoesNotContain(LocalSmtpServer.Password, error.ToString());
        Assert.DoesNotContain("Rejected", error.ToString());
        Assert.Contains("AUTH", server.Commands);
        Assert.All(server.Commands, command => Assert.Contains(command, SignInCommands));
    }

    [Theory]
    [InlineData(null, MailConnectionFailure.AuthenticationUnavailable)]
    [InlineData("XOAUTH2", MailConnectionFailure.UnsupportedSignIn)]
    [InlineData("XOAUTH2 OAUTHBEARER", MailConnectionFailure.UnsupportedSignIn)]
    public async Task AServerWithoutPasswordSignInIsNamedAndNeverReceivesThePassword(string? mechanisms, MailConnectionFailure expected)
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, authMechanisms: mechanisms, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.StartTls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server).TestConnectionAsync(account));
        // An OAuth-only server is not reported as a wrong password.
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("AUTH", server.Commands);
    }

    [Fact]
    public async Task RefusedGreetingIsServerRefusedWithoutServerText()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, SmtpFixtureOutcome.RefuseGreeting, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.StartTls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.ServerRefused, error.Failure);
        Assert.DoesNotContain(LocalSmtpServer.Password, error.ToString());
        Assert.DoesNotContain("No service", error.ToString());
        Assert.DoesNotContain("AUTH", server.Commands);
    }

    [Fact]
    public async Task AConnectionLostDuringSignInIsInterrupted()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls, SmtpFixtureOutcome.DropOnAuthentication, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.Tls);
        var credentials = await Credentials(account);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.Interrupted, error.Failure);
        Assert.False(server.AuthenticatedOverTls);
    }

    [Fact]
    public async Task AClosedPortIsUnreachable()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var account = TestDirectory.Profile() with { OutgoingServer = new() { Host = "127.0.0.1", Port = port, UserName = "test", Security = TransportSecurity.StartTls } };
        var credentials = await Credentials(account);
        // Windows retries a refused loopback connection for about two seconds; the deadline leaves room for that.
        var tester = new SmtpConnectionTester(credentials, () => new SmtpClient(), TimeSpan.FromSeconds(15));
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => tester.TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.Unreachable, error.Failure);
    }

    [Theory]
    [InlineData("missing", MailConnectionFailure.MissingPassword)]
    [InlineData("host", MailConnectionFailure.MissingPassword)]
    [InlineData("port", MailConnectionFailure.MissingPassword)]
    [InlineData("user", MailConnectionFailure.MissingPassword)]
    [InlineData("security", MailConnectionFailure.MissingPassword)]
    [InlineData("imap-only", MailConnectionFailure.MissingPassword)]
    [InlineData("store", MailConnectionFailure.CredentialStore)]
    [InlineData("oauth", MailConnectionFailure.UnsupportedSignIn)]
    [InlineData("no-outgoing", MailConnectionFailure.Setup)]
    [InlineData("disabled", MailConnectionFailure.Setup)]
    [InlineData("canceled", null)]
    public async Task InvalidSetupOrCredentialsNeverOpenAConnection(string problem, MailConnectionFailure? expected)
    {
        var account = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test", Security = TransportSecurity.StartTls } };
        ICredentialStore credentials = await Credentials(account);
        using var cancellation = new CancellationTokenSource();
        var outgoing = account.OutgoingServer;
        switch (problem)
        {
            case "missing": await credentials.DeleteAsync(CredentialKey.For(account, MailProtocol.Smtp)); break;
            // A password saved for the old server details is never released to new ones.
            case "host": account = account with { OutgoingServer = outgoing with { Host = "changed.example.test" } }; break;
            case "port": account = account with { OutgoingServer = outgoing with { Port = 465 } }; break;
            case "user": account = account with { OutgoingServer = outgoing with { UserName = "someone-else" } }; break;
            case "security": account = account with { OutgoingServer = outgoing with { Security = TransportSecurity.Tls } }; break;
            case "imap-only":
                // The same server identity for both protocols: the IMAP password still does not sign in to SMTP.
                account = account with { OutgoingServer = account.IncomingServer };
                credentials = new TestCredentialStore();
                await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalSmtpServer.Password);
                break;
            case "store": credentials = new UnreadableCredentialStore(); break;
            case "oauth": account = account with { OutgoingServer = outgoing with { Authentication = AuthenticationMethod.OAuth2 } }; break;
            case "no-outgoing": account = account with { OutgoingServer = null }; break;
            case "disabled": account = account with { IsEnabled = false }; break;
            case "canceled": cancellation.Cancel(); break;
        }
        bool created = false;
        var tester = new SmtpConnectionTester(credentials, () => { created = true; return new(); }, TimeSpan.FromSeconds(1));
        if (expected is null)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tester.TestConnectionAsync(account, cancellation.Token));
        else
        {
            var error = await Assert.ThrowsAsync<MailConnectionException>(() => tester.TestConnectionAsync(account, cancellation.Token));
            Assert.Equal(expected, error.Failure);
            Assert.DoesNotContain(LocalSmtpServer.Password, error.ToString());
        }
        Assert.False(created);
    }

    [Fact]
    public async Task ASilentServerTimesOutWithinTheBound()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, silent: true, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.StartTls);
        var credentials = await Credentials(account);
        var clock = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => Tester(credentials, server, TimeSpan.FromMilliseconds(300)).TestConnectionAsync(account));
        Assert.Equal(MailConnectionFailure.Timeout, error.Failure);
        // Generous: slow CI machines must not turn the bound into a flake.
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(300) + TimeSpan.FromSeconds(3));
        Assert.Empty(server.Commands);
    }

    [Theory]
    [InlineData("greeting")]
    [InlineData("authentication")]
    [InlineData("quit")]
    public async Task CallerCancellationWinsInEveryPhase(string phase)
    {
        await using var server = phase switch
        {
            "greeting" => new LocalSmtpServer(TransportSecurity.StartTls, silent: true, connectionTestOnly: true),
            "authentication" => new LocalSmtpServer(TransportSecurity.StartTls, SmtpFixtureOutcome.StallAuthentication, connectionTestOnly: true),
            _ => new LocalSmtpServer(TransportSecurity.StartTls, SmtpFixtureOutcome.StallQuit, connectionTestOnly: true),
        };
        var account = Profile(server, TransportSecurity.StartTls);
        // Long budgets, so only the caller can end the test early.
        var tester = Tester(await Credentials(account), server, TimeSpan.FromSeconds(15), quitBudget: TimeSpan.FromSeconds(15));
        using var cancellation = new CancellationTokenSource();
        var testing = tester.TestConnectionAsync(account, cancellation.Token);
        switch (phase)
        {
            case "greeting": await Task.Delay(200); break;
            case "authentication": await server.AuthenticationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)); break;
            default: await server.QuitReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)); break;
        }
        cancellation.Cancel();
        // Even after the server accepted the sign-in, Cancel never ends as a pass.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => testing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(phase == "quit", server.AuthenticatedOverTls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQuitWithoutAnswerStillPasses(bool deadlineEndsQuit)
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls, SmtpFixtureOutcome.StallQuit, connectionTestOnly: true);
        var account = Profile(server, TransportSecurity.Tls);
        // Either QUIT's own budget or the overall deadline ends the wait; neither turns the sign-in into a failure.
        var tester = deadlineEndsQuit
            ? Tester(await Credentials(account), server, TimeSpan.FromSeconds(3), quitBudget: TimeSpan.FromSeconds(30))
            : Tester(await Credentials(account), server, TimeSpan.FromSeconds(30), quitBudget: TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        await tester.TestConnectionAsync(account);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(deadlineEndsQuit ? 8 : 5));
        Assert.True(server.AuthenticatedOverTls);
        Assert.True(server.QuitReceived.Task.IsCompleted);
    }

    private static AccountProfile Profile(LocalSmtpServer server, TransportSecurity security) =>
        TestDirectory.Profile() with { OutgoingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = security } };

    private static async Task<TestCredentialStore> Credentials(AccountProfile account)
    {
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), "different-imap-secret");
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);
        return credentials;
    }

    // Only this fixture's ephemeral certificate is trusted. No OS trust store is changed.
    private static SmtpConnectionTester Tester(ICredentialStore credentials, LocalSmtpServer server, TimeSpan? timeout = null, TimeSpan? quitBudget = null) =>
        new(credentials, () => new SmtpClient
        {
            ServerCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == server.Certificate.GetCertHashString(),
        }, timeout ?? TimeSpan.FromSeconds(5), quitBudget);

    private sealed class UnreadableCredentialStore : ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default) =>
            Task.FromException<string?>(new IOException("Windows Credential Manager could not read the password (error 1312)."));
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
