using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MimeKit;

namespace Broiler.Mail.Tests;

public sealed class SmtpSubmissionTests
{
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task SendsUnicodeReplyWithPrivateBccEnvelopeAndDurableAcceptance(TransportSecurity security)
    {
        await using var server = new LocalSmtpServer(security);
        var account = Profile(server, security);
        var credentials = await Credentials(account);
        using var directory = new TestDirectory();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using var composer = new ComposerViewModel(store, sender: Sender(credentials, server));
        composer.SetAccount(account);
        composer.StartFromMessage(new(new(account.Id, "INBOX", 1, 1), "old body")
        { Composition = new() { From = ["to@example.test"], MessageId = "parent@example.test", References = ["root@example.test"] } }, CompositionKind.Reply);
        composer.Edit("to@example.test", "cc@example.test", "hidden@example.test, to@example.test", "Grüße & café", "Body 😀\n.dot\nLast line");
        Assert.True(composer.CanSend);
        await composer.SendAsync();
        Assert.Equal(DraftSubmissionState.Accepted, composer.SubmissionState);
        Assert.False(composer.CanSend);
        Assert.Contains("Delivery is not guaranteed", composer.Status);
        Assert.DoesNotContain(LocalSmtpServer.Password, composer.Status);
        Assert.Equal(DraftSubmissionState.Accepted, (await store.LoadAsync()).Draft!.State);
        Assert.True(server.AuthenticatedOverTls);
        Assert.Equal(3, server.Recipients.Count);
        Assert.Contains(server.Recipients, value => value.Contains("hidden@example.test", StringComparison.Ordinal));
        Assert.Contains(account.EmailAddress, server.Sender!);
        using var message = MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(server.RawMessage!)));
        Assert.Empty(message.Bcc);
        Assert.False(message.Headers.Contains(HeaderId.Bcc));
        Assert.DoesNotContain("hidden@example.test", server.RawMessage!);
        Assert.Equal("Grüße & café", message.Subject);
        Assert.Equal("Body 😀\n.dot\nLast line", message.TextBody!.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
        Assert.Equal("parent@example.test", message.InReplyTo);
        Assert.Equal(new[] { "root@example.test", "parent@example.test" }, message.References);
        Assert.Equal($"{composer.DraftId:N}@broiler.mail", message.MessageId);
        await composer.SendAsync();
        Assert.Equal(1, server.DataCount);
    }

    [Fact]
    public async Task BccOnlyMessageHasNoRecipientHeaderLeak()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls);
        var account = Profile(server);
        var draft = Draft(account) with { To = [], Cc = [], Bcc = ["hidden@example.test"] };
        var result = await Sender(await Credentials(account), server).SendAsync(account, draft);
        Assert.Equal(SubmissionStatus.Accepted, result.Status);
        Assert.Single(server.Recipients);
        Assert.DoesNotContain("hidden@example.test", server.RawMessage!);
    }

    [Theory]
    [InlineData(SmtpFixtureOutcome.RejectAuthentication, SubmissionStatus.Rejected)]
    [InlineData(SmtpFixtureOutcome.RejectRecipient, SubmissionStatus.Rejected)]
    [InlineData(SmtpFixtureOutcome.RejectMessage, SubmissionStatus.Rejected)]
    [InlineData(SmtpFixtureOutcome.DropAfterData, SubmissionStatus.Unknown)]
    [InlineData(SmtpFixtureOutcome.MalformedAfterData, SubmissionStatus.Unknown)]
    public async Task DistinguishesRejectionFromLostAcknowledgementWithoutLeakingDiagnostics(SmtpFixtureOutcome outcome, SubmissionStatus expected)
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls, outcome);
        var account = Profile(server);
        var result = await Sender(await Credentials(account), server).SendAsync(account, Draft(account));
        Assert.Equal(expected, result.Status);
        Assert.DoesNotContain(LocalSmtpServer.Password, result.ToString());
        if (outcome is SmtpFixtureOutcome.RejectAuthentication or SmtpFixtureOutcome.RejectRecipient)
            Assert.Equal(0, server.DataCount);
        else Assert.Equal(1, server.DataCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrTimeoutAfterDataRetainsUnknownAcrossRestart(bool cancel)
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls, SmtpFixtureOutcome.StallAfterData);
        var account = Profile(server);
        using var directory = new TestDirectory();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using var composer = new ComposerViewModel(store, sender: Sender(await Credentials(account), server, cancel ? 10 : 2));
        composer.SetAccount(account); composer.StartNew(); composer.Edit("to@example.test", "", "", "pending", "preserve me");
        using var cancellation = new CancellationTokenSource();
        var sending = composer.SendAsync(cancellation.Token);
        await server.DataReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DraftSubmissionState.Sending, (await store.LoadAsync()).Draft!.State);
        Assert.False(await composer.PrepareCloseAsync());
        if (cancel) cancellation.Cancel();
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DraftSubmissionState.Unknown, composer.SubmissionState);
        using var recovered = new ComposerViewModel(store, await store.LoadAsync(), sender: Sender(await Credentials(account), server));
        recovered.SetAccount(account);
        Assert.False(recovered.CanSend);
        Assert.Equal("preserve me", recovered.PlainText);
        Assert.Equal(1, server.DataCount);
    }

    [Fact]
    public async Task UntrustedCertificateNeverAuthenticates()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.Tls);
        var account = Profile(server);
        var result = await new SmtpMailSender(await Credentials(account)).SendAsync(account, Draft(account));
        Assert.Equal(SubmissionStatus.Rejected, result.Status);
        Assert.DoesNotContain("AUTH", server.Commands);
    }

    [Fact]
    public async Task MissingRequiredStartTlsNeverAuthenticatesInPlaintext()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, advertiseStartTls: false);
        var account = Profile(server, TransportSecurity.StartTls);
        var result = await Sender(await Credentials(account), server).SendAsync(account, Draft(account));
        Assert.Equal(SubmissionStatus.Rejected, result.Status);
        Assert.DoesNotContain("AUTH", server.Commands);
    }

    [Fact]
    public async Task TimeoutBeforeSubmissionIsSafeToRetry()
    {
        await using var server = new LocalSmtpServer(TransportSecurity.StartTls, silent: true);
        var account = Profile(server, TransportSecurity.StartTls);
        var result = await Sender(await Credentials(account), server, 0.3).SendAsync(account, Draft(account));
        Assert.Equal(SubmissionStatus.Rejected, result.Status);
        Assert.Equal(0, server.DataCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("binding")]
    [InlineData("identity")]
    [InlineData("recipient")]
    [InlineData("subject")]
    [InlineData("thread")]
    [InlineData("body")]
    [InlineData("oauth")]
    [InlineData("disabled")]
    [InlineData("canceled")]
    public async Task InvalidInputOrCredentialsCannotOpenConnection(string problem)
    {
        var account = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test", Security = TransportSecurity.StartTls } };
        var credentials = await Credentials(account);
        var draft = Draft(account);
        using var cancellation = new CancellationTokenSource();
        switch (problem)
        {
            case "missing": await credentials.DeleteAsync(CredentialKey.For(account, MailProtocol.Smtp)); break;
            case "binding": account = account with { OutgoingServer = account.OutgoingServer with { Host = "changed.example.test" } }; break;
            case "identity": draft = draft with { AccountId = AccountId.New() }; break;
            case "recipient": draft = draft with { To = ["to@example.test\r\nBcc: other@example.test"] }; break;
            case "subject": draft = draft with { Subject = "hello\r\nBcc: other@example.test" }; break;
            case "thread": draft = draft with { InReplyTo = "parent@example.test\r\nBcc: other@example.test" }; break;
            case "body": draft = draft with { PlainText = new string('x', 100_001) }; break;
            case "oauth": account = account with { OutgoingServer = account.OutgoingServer with { Authentication = AuthenticationMethod.OAuth2 } }; break;
            case "disabled": account = account with { IsEnabled = false }; break;
            case "canceled": cancellation.Cancel(); break;
        }
        bool created = false;
        var sender = new SmtpMailSender(credentials, () => { created = true; return new(); }, TimeSpan.FromSeconds(1));
        var result = await sender.SendAsync(account, draft, cancellation.Token);
        Assert.Equal(SubmissionStatus.Rejected, result.Status);
        Assert.False(created);
        Assert.DoesNotContain(LocalSmtpServer.Password, result.ToString());
    }

    private static AccountProfile Profile(LocalSmtpServer server, TransportSecurity security = TransportSecurity.Tls) =>
        TestDirectory.Profile() with { OutgoingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = security } };
    private static MailDraft Draft(AccountProfile account) => MailComposition.Create(account) with
    { To = ["to@example.test"], Cc = ["cc@example.test"], Subject = "Fixture", PlainText = "Synthetic text" };
    private static async Task<TestCredentialStore> Credentials(AccountProfile account)
    {
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), "different-imap-secret");
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);
        return credentials;
    }
    private static SmtpMailSender Sender(ICredentialStore credentials, LocalSmtpServer server, double seconds = 5) =>
        new(credentials, () => new SmtpClient { ServerCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == server.Certificate.GetCertHashString() }, TimeSpan.FromSeconds(seconds));
}
