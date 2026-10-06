using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class SentCopyTests
{
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task SmtpAcceptanceThenOneImapAppendPreservesContentAndPrivateBcc(TransportSecurity security)
    {
        await using var imap = new LocalImapServer(security);
        await using var smtp = new LocalSmtpServer(security);
        var profile = Profile(imap, security) with
        { OutgoingServer = new() { Host = "127.0.0.1", Port = smtp.Port, UserName = "test", Security = security } };
        var credentials = await Credentials(profile);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Smtp), LocalSmtpServer.Password);
        var sender = new SmtpMailSender(credentials, (_, cert, _, _) => cert?.GetCertHashString() == smtp.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5));
        using var directory = new TestDirectory();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        var writer = new Copy(async (account, draft) =>
        {
            var state = (await store.LoadAsync()).Draft!;
            Assert.Equal(DraftSubmissionState.Accepted, state.State);
            Assert.Equal(SentCopyState.Pending, state.SentCopy);
            return await Writer(credentials, imap).AppendAsync(account, draft);
        });
        using var model = Compose(profile, store, sender, writer);
        await model.SendAsync();
        Assert.Equal(DraftSubmissionState.Accepted, model.SubmissionState);
        Assert.Equal(SentCopyState.Saved, model.SentCopy);
        Assert.Equal(SentCopyState.Saved, (await store.LoadAsync()).Draft!.SentCopy);
        Assert.Equal(1, smtp.DataCount); Assert.Equal(1, imap.AppendCount);
        Assert.Contains(imap.MailCommands, command => command.Contains("APPEND", StringComparison.Ordinal) && command.Contains("Sent", StringComparison.Ordinal) && command.Contains("\\Seen", StringComparison.Ordinal));
        Assert.DoesNotContain(imap.Commands, command => command is "CREATE" or "SELECT" or "EXAMINE");
        var submitted = Parse(smtp.RawMessage!);
        var copied = Parse(imap.AppendedMessage!);
        Assert.Equal(submitted.MessageId, copied.MessageId);
        Assert.Equal(submitted.Date, copied.Date);
        Assert.Equal(submitted.TextBody!.TrimEnd('\r', '\n'), copied.TextBody!.TrimEnd('\r', '\n'));
        Assert.Equal(submitted.Subject, copied.Subject);
        Assert.Equal(submitted.InReplyTo, copied.InReplyTo);
        Assert.Equal(submitted.References, copied.References);
        Assert.Empty(submitted.Bcc);
        Assert.Equal("hidden@example.test", Assert.Single(copied.Bcc));
        await model.SendAsync();
        using var recovered = new ComposerViewModel(store, await store.LoadAsync(), sender: sender, sentCopies: writer);
        recovered.SetAccount(profile);
        await recovered.SendAsync();
        Assert.False(recovered.CanSend);
        Assert.Equal(SentCopyState.Saved, recovered.SentCopy);
        Assert.Equal(1, writer.Calls);
    }

    [Theory]
    [InlineData("folder", SentCopyState.Failed)]
    [InlineData("permission", SentCopyState.Failed)]
    [InlineData("drop", SentCopyState.Unknown)]
    [InlineData("timeout", SentCopyState.Unknown)]
    public async Task CopyFailureNeverChangesAcceptanceOrEnablesResending(string failure, SentCopyState expected)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { MissingSentFolder = failure == "folder", RejectAppend = failure == "permission", DropAfterAppend = failure == "drop", StallAfterAppend = failure == "timeout" };
        var profile = Profile(server);
        var sender = new Sender(SubmissionStatus.Accepted);
        using var directory = new TestDirectory();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using var model = Compose(profile, store, sender, Writer(await Credentials(profile), server, failure == "timeout" ? 1 : 5));
        await model.SendAsync();
        Assert.Equal(expected, model.SentCopy);
        Assert.Equal(DraftSubmissionState.Accepted, model.SubmissionState);
        Assert.False(model.CanEdit); Assert.False(model.CanSend);
        Assert.Equal("Body Grüße\nsecond line", model.PlainText);
        Assert.DoesNotContain(LocalImapServer.Password, model.SentCopyText);
        using var recovered = new ComposerViewModel(store, await store.LoadAsync(), sender: sender);
        recovered.SetAccount(profile);
        Assert.Equal(expected, recovered.SentCopy);
        await model.SendAsync(); await recovered.SendAsync();
        Assert.Equal(1, sender.Calls);
        Assert.Equal(failure == "folder" ? 0 : 1, server.AppendCount);
    }

    [Theory]
    [InlineData(SentCopyMode.NotConfigured, SubmissionStatus.Accepted, SentCopyState.NotRequested)]
    [InlineData(SentCopyMode.ProviderManaged, SubmissionStatus.Accepted, SentCopyState.ProviderManaged)]
    [InlineData(SentCopyMode.AppendToFolder, SubmissionStatus.Rejected, SentCopyState.NotRequested)]
    [InlineData(SentCopyMode.AppendToFolder, SubmissionStatus.Unknown, SentCopyState.NotRequested)]
    public async Task OnlyExplicitAppendAndAcceptedSubmissionCanInvokeWriter(SentCopyMode mode, SubmissionStatus submission, SentCopyState expected)
    {
        var profile = TestDirectory.Profile() with { OutgoingServer = Smtp(), SentCopyMode = mode, SentFolder = mode == SentCopyMode.AppendToFolder ? "Sent" : null };
        var writer = new Copy((_, _) => throw new InvalidOperationException("Must not append."));
        using var model = Compose(profile, new MemoryDraftStore(), new Sender(submission), writer);
        await model.SendAsync();
        Assert.Equal(0, writer.Calls);
        Assert.Equal(expected, model.SentCopy);
    }

    [Fact]
    public async Task CancelAfterAppendStartsIsUnknownAndCannotRetry()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { StallAfterAppend = true };
        var profile = Profile(server);
        using var cancellation = new CancellationTokenSource();
        var sender = new Sender(SubmissionStatus.Accepted);
        using var model = Compose(profile, new MemoryDraftStore(), sender, Writer(await Credentials(profile), server));
        var task = model.SendAsync(cancellation.Token);
        await server.AppendReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsBusy); Assert.False(await model.PrepareCloseAsync());
        cancellation.Cancel();
        await task;
        Assert.Equal(SentCopyState.Unknown, model.SentCopy);
        Assert.Equal(DraftSubmissionState.Accepted, model.SubmissionState);
        Assert.Equal(1, sender.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceFailuresBeforeOrAfterCopyNeverRepeatSendOrAppend(bool afterCopy)
    {
        var store = new FailingStore(afterCopy ? SentCopyState.Saved : SentCopyState.Pending);
        var writer = new Copy((_, _) => Task.FromResult(SentCopyState.Saved));
        var sender = new Sender(SubmissionStatus.Accepted);
        var profile = TestDirectory.Profile() with { OutgoingServer = Smtp(), SentCopyMode = SentCopyMode.AppendToFolder, SentFolder = "Sent" };
        using var model = Compose(profile, store, sender, writer);
        await model.SendAsync();
        Assert.Equal(DraftSubmissionState.Accepted, model.SubmissionState);
        Assert.False(model.CanSend);
        Assert.Equal(afterCopy ? 1 : 0, writer.Calls);
        var saved = await store.LoadAsync();
        Assert.Equal(afterCopy ? DraftSubmissionState.Accepted : DraftSubmissionState.Sending, saved.Draft!.State);
        using var recovered = new ComposerViewModel(store, saved, sender: sender, sentCopies: writer);
        recovered.SetAccount(profile);
        Assert.Equal(afterCopy ? DraftSubmissionState.Accepted : DraftSubmissionState.Unknown, recovered.SubmissionState);
        if (afterCopy) Assert.Equal(SentCopyState.Unknown, recovered.SentCopy);
        await recovered.SaveAsync(); await recovered.SendAsync();
        Assert.Equal(1, sender.Calls);
        Assert.Equal(afterCopy ? 1 : 0, writer.Calls);
    }

    [Theory]
    [InlineData("certificate")]
    [InlineData("starttls")]
    [InlineData("credentials")]
    [InlineData("oauth")]
    public async Task FailedImapSetupNeverAppends(string failure)
    {
        var security = failure == "starttls" ? TransportSecurity.StartTls : TransportSecurity.Tls;
        await using var server = new LocalImapServer(security, advertiseStartTls: failure != "starttls");
        var profile = Profile(server, security);
        var credentials = await Credentials(profile);
        if (failure == "credentials") await credentials.DeleteAsync(CredentialKey.For(profile, MailProtocol.Imap));
        if (failure == "oauth") profile = profile with { IncomingServer = profile.IncomingServer with { Authentication = AuthenticationMethod.OAuth2 } };
        var writer = failure == "certificate" ? new ImapSentCopyWriter(credentials) : Writer(credentials, server);
        var result = await writer.AppendAsync(profile, MailComposition.Create(profile) with { To = ["to@example.test"] });
        Assert.Equal(SentCopyState.Failed, result);
        Assert.Equal(0, server.AppendCount);
        Assert.DoesNotContain("AUTHENTICATE", server.Commands);
    }

    private static ComposerViewModel Compose(AccountProfile profile, IDraftStore store, IMailSender sender, ISentCopyWriter copies)
    {
        var model = new ComposerViewModel(store, sender: sender, sentCopies: copies);
        model.SetAccount(profile);
        model.StartFromMessage(new(new(profile.Id, "INBOX", 1, 1), "old body")
        { Composition = new() { From = ["to@example.test"], MessageId = "parent@example.test", References = ["root@example.test"] } }, CompositionKind.Reply);
        model.Edit("to@example.test", "cc@example.test", "hidden@example.test", "Grüße", "Body Grüße\nsecond line");
        return model;
    }
    private static ParsedMimeMessage Parse(string text) => MimeParser.Parse(System.Text.Encoding.UTF8.GetBytes(text));
    private static MailServerSettings Smtp() => new() { Host = "smtp.example.test", Port = 465, UserName = "test" };
    private static AccountProfile Profile(LocalImapServer server, TransportSecurity security = TransportSecurity.Tls) => TestDirectory.Profile() with
    {
        IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = security },
        OutgoingServer = Smtp(), SentCopyMode = SentCopyMode.AppendToFolder, SentFolder = "Sent",
    };
    private static async Task<TestCredentialStore> Credentials(AccountProfile profile)
    {
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), LocalImapServer.Password);
        return credentials;
    }
    private static ImapSentCopyWriter Writer(ICredentialStore credentials, LocalImapServer server, double seconds = 5) =>
        new(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(seconds));
    private sealed class Sender(SubmissionStatus result) : IMailSender
    {
        public int Calls { get; private set; }
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new SendResult(result)); }
    }
    private sealed class Copy(Func<AccountProfile, MailDraft, Task<SentCopyState>> action) : ISentCopyWriter
    {
        public int Calls { get; private set; }
        public Task<SentCopyState> AppendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
        { Calls++; return action(account, draft); }
    }
    private sealed class FailingStore(SentCopyState failOn) : IDraftStore
    {
        private readonly MemoryDraftStore _inner = new();
        public bool IsPersistent => true;
        public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default) => _inner.LoadAsync(cancellationToken);
        public Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
        {
            if (draft?.SentCopy == failOn) throw new IOException("Test storage failure.");
            return _inner.SaveAsync(expectedRevision, draft, cancellationToken);
        }
    }
}
