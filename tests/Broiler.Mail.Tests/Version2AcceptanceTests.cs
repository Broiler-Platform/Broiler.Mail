using System.Text;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.UI.Standard;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;

namespace Broiler.Mail.Tests;

public sealed class Version2AcceptanceTests
{
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task FullEndToEndSendReceiveReplyAndBccOmission(TransportSecurity security)
    {
        using var directory = new TestDirectory();
        await using var imapServer = new LocalImapServer(security);
        await using var smtpServer = new LocalSmtpServer(security);

        const string parentMessageId = "<parent-discussion-123@example.test>";
        imapServer.Messages.Add(new(1, $"MIME-Version: 1.0\r\nMessage-ID: {parentMessageId}\r\nFrom: Colleague <colleague@example.test>\r\nSubject: Project Roadmap\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nLet's discuss version 2 progress."));

        var credentials = new TestCredentialStore();
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        var settings = new JsonSettingsStore(directory.File("settings.json"));
        var drafts = new JsonDraftStore(directory.File("drafts.json"));

        var account = new AccountProfile
        {
            Id = AccountId.New(),
            DisplayName = "Test User",
            EmailAddress = "test@example.test",
            IncomingServer = new()
            {
                Host = "127.0.0.1",
                Port = imapServer.Port,
                UserName = "test",
                Security = security,
            },
            OutgoingServer = new()
            {
                Host = "127.0.0.1",
                Port = smtpServer.Port,
                UserName = "test",
                Security = security,
            },
            SentCopyMode = SentCopyMode.ProviderManaged,
        };
        await accounts.SaveAsync(account);
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);

        var receiver = new ImapMailReceiver(credentials, () => new ImapClient
        {
            ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == imapServer.Certificate.GetCertHashString()
        }, TimeSpan.FromSeconds(5));

        var sender = new SmtpMailSender(credentials, () => new SmtpClient
        {
            ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == smtpServer.Certificate.GetCertHashString()
        }, TimeSpan.FromSeconds(5));

        var app = new MailApplication(accounts, settings, receiver, sender, credentials, drafts);
        await app.InitializeAsync();

        // 1. Receive incoming message
        using var inbox = new InboxViewModel(app.Receiver, new ImmediateUiDispatcher());
        inbox.SetAccount(app.LoadedAccount);
        await inbox.ReceiveAsync();
        Assert.Single(inbox.Messages);
        await inbox.SelectAsync(inbox.Messages[0].Key);
        Assert.NotNull(inbox.Body?.Composition);
        Assert.Equal("Project Roadmap", inbox.Body!.Composition!.Subject);

        // 2. Start Reply in Composer
        using var composer = new ComposerViewModel(app.Drafts, app.LoadedDraft, new ImmediateUiDispatcher(), app.Sender);
        composer.SetAccount(app.LoadedAccount);
        composer.StartFromMessage(inbox.Body, CompositionKind.Reply);

        Assert.Equal("colleague@example.test", composer.To);
        Assert.Equal("Re: Project Roadmap", composer.Subject);
        Assert.Contains("Let's discuss version 2 progress.", composer.PlainText);

        // 3. Add Bcc recipient and body additions
        composer.Edit(composer.To, cc: "", bcc: "auditor@example.test", composer.Subject, composer.PlainText + "\n\nLooks great, approving.");
        Assert.True(composer.CanSend);

        // 4. Send through SMTP
        await composer.SendAsync();
        Assert.Equal(DraftSubmissionState.Accepted, composer.SubmissionState);

        // 5. Inspect transmitted SMTP transaction
        await smtpServer.DataReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("MAIL FROM:<test@example.test>", smtpServer.Sender);

        // Deduplicated envelope contains both To and Bcc
        Assert.Contains("RCPT TO:<colleague@example.test>", smtpServer.Recipients);
        Assert.Contains("RCPT TO:<auditor@example.test>", smtpServer.Recipients);

        // MIME headers contain threading and To, but omit Bcc
        Assert.NotNull(smtpServer.RawMessage);
        Assert.Contains("To: colleague@example.test", smtpServer.RawMessage);
        Assert.Contains($"In-Reply-To: {parentMessageId}", smtpServer.RawMessage);
        Assert.Contains($"References: {parentMessageId}", smtpServer.RawMessage);
        Assert.DoesNotContain("auditor@example.test", smtpServer.RawMessage);
        Assert.DoesNotContain("Bcc:", smtpServer.RawMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedSmtpSubmissionPreservesDraftForCorrection()
    {
        using var directory = new TestDirectory();
        await using var smtpServer = new LocalSmtpServer(TransportSecurity.Tls, SmtpFixtureOutcome.RejectMessage);

        var credentials = new TestCredentialStore();
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        var settings = new JsonSettingsStore(directory.File("settings.json"));
        var drafts = new JsonDraftStore(directory.File("drafts.json"));

        var account = new AccountProfile
        {
            Id = AccountId.New(),
            DisplayName = "Test User",
            EmailAddress = "test@example.test",
            IncomingServer = new() { Host = "127.0.0.1", Port = 993, UserName = "test", Security = TransportSecurity.Tls },
            OutgoingServer = new() { Host = "127.0.0.1", Port = smtpServer.Port, UserName = "test", Security = TransportSecurity.Tls },
        };
        await accounts.SaveAsync(account);
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);

        var sender = new SmtpMailSender(credentials, () => new SmtpClient
        {
            ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == smtpServer.Certificate.GetCertHashString()
        }, TimeSpan.FromSeconds(5));

        using var composer = new ComposerViewModel(drafts, new(0, null), new ImmediateUiDispatcher(), sender);
        composer.SetAccount(account);
        composer.StartNew();
        composer.Edit("recipient@example.test", "", "", "Urgent notification", "Please review.");

        await composer.SendAsync();
        Assert.Equal(DraftSubmissionState.Failed, composer.SubmissionState);
        Assert.True(composer.HasDraft);
        Assert.Equal("Urgent notification", composer.Subject);
        Assert.Equal("Please review.", composer.PlainText);

        // Draft is preserved durably in drafts.json
        var persisted = await drafts.LoadAsync();
        Assert.NotNull(persisted.Draft);
        Assert.Equal("Urgent notification", persisted.Draft!.Draft.Subject);
        Assert.Equal(DraftSubmissionState.Failed, persisted.Draft.State);
    }

    [Fact]
    public async Task UncertainSmtpOutcomeRecoversAsUnknownAndNeverResendsAutomatically()
    {
        using var directory = new TestDirectory();
        await using var smtpServer = new LocalSmtpServer(TransportSecurity.Tls, SmtpFixtureOutcome.DropAfterData);

        var credentials = new TestCredentialStore();
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        var settings = new JsonSettingsStore(directory.File("settings.json"));
        var drafts = new JsonDraftStore(directory.File("drafts.json"));

        var account = new AccountProfile
        {
            Id = AccountId.New(),
            DisplayName = "Test User",
            EmailAddress = "test@example.test",
            IncomingServer = new() { Host = "127.0.0.1", Port = 993, UserName = "test", Security = TransportSecurity.Tls },
            OutgoingServer = new() { Host = "127.0.0.1", Port = smtpServer.Port, UserName = "test", Security = TransportSecurity.Tls },
        };
        await accounts.SaveAsync(account);
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);

        var sender = new SmtpMailSender(credentials, () => new SmtpClient
        {
            ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == smtpServer.Certificate.GetCertHashString()
        }, TimeSpan.FromSeconds(5));

        using var composer = new ComposerViewModel(drafts, new(0, null), new ImmediateUiDispatcher(), sender);
        composer.SetAccount(account);
        composer.StartNew();
        composer.Edit("recipient@example.test", "", "", "Status Update", "Message transmitted before drop.");

        await composer.SendAsync();
        Assert.Equal(DraftSubmissionState.Unknown, composer.SubmissionState);
        Assert.False(composer.CanSend);

        // Restart application: recovered draft is Unknown and cannot be resent
        var restartedDraftState = await drafts.LoadAsync();
        Assert.NotNull(restartedDraftState.Draft);
        Assert.Equal(DraftSubmissionState.Unknown, restartedDraftState.Draft!.State);

        using var restartedComposer = new ComposerViewModel(drafts, restartedDraftState, new ImmediateUiDispatcher(), sender);
        restartedComposer.SetAccount(account);
        Assert.Equal(DraftSubmissionState.Unknown, restartedComposer.SubmissionState);
        Assert.False(restartedComposer.CanSend, "An accepted or outcome-unknown draft must not be resent automatically.");
    }

    [Fact]
    public void IsolatedHtmlPreviewRendersBoundedCidImagesAndBlocksActiveContent()
    {
        byte[] fakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var embedded = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["diagram@test"] = new MailEmbeddedImage("diagram@test", "image/png", fakePng)
        };

        const string hostileHtml = "<html><head><script>alert(1)</script></head><body><h1>Report</h1><img src=\"cid:diagram@test\" alt=\"Chart\"><img src=\"https://example.test/remote.jpg\" alt=\"External\"><iframe src=\"about:blank\"></iframe><a href=\"https://example.test/portal\">Link</a></body></html>";

        // Default: remote images blocked, active content stripped, cid resolved
        var document = HtmlPreviewPolicy.Create(hostileHtml, embedded, allowRemoteImages: false);
        Assert.Contains("<h1>Report</h1>", document.Html);
        Assert.Contains("<img src=\"data:image/png;base64,", document.Html);
        Assert.Contains("alt=\"Chart\"", document.Html);
        Assert.Contains("[Remote image blocked: External]", document.Html);
        Assert.DoesNotContain("<script", document.Html);
        Assert.DoesNotContain("<iframe", document.Html);
        Assert.Contains("default-src 'none'", document.Html);
        Assert.Equal("https://example.test/portal", Assert.Single(document.ExternalLinks));
        Assert.Equal("https://example.test/remote.jpg", Assert.Single(document.RemoteImageUrls));

        // When remote images are allowed: remote image tag is rendered
        var allowedDocument = HtmlPreviewPolicy.Create(hostileHtml, embedded, allowRemoteImages: true);
        Assert.Contains("<img src=\"https://example.test/remote.jpg\" alt=\"External\"", allowedDocument.Html);
        Assert.Contains("img-src 'self' data: https: http:;", allowedDocument.Html);
    }
}
