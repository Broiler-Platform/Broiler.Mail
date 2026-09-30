using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using MailKit.Net.Imap;

namespace Broiler.Mail.Tests;

public sealed class ImapReceivingTests
{
    private const string Plain = "From: sender@example.test\r\nSubject: Test\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\nSGVsbG8gJiBncmVldGluZ3Mh\r\n";

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task ReceivesBoundedPagesNewestFirstWithoutBodiesOrFlagChanges(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security);
        for (uint uid = 10; uid <= 50; uid += 10) server.Messages.Add(new(uid, Plain, uid == 50));
        var (receiver, account) = await Setup(server, security);
        var first = await receiver.GetInboxAsync(account, 2);
        Assert.Equal(new uint[] { 50, 40 }, first.Messages.Select(message => message.Key.Uid));
        Assert.True(first.Messages[0].IsRead);
        Assert.False(first.Messages[1].IsRead);
        Assert.Equal("Grüße", first.Messages[0].Subject);
        Assert.Contains("sender@example.test", first.Messages[0].Sender);
        Assert.NotNull(first.Messages[0].ReceivedAt);
        var second = await receiver.GetInboxAsync(account, 2, first.Older);
        var last = await receiver.GetInboxAsync(account, 2, second.Older);
        Assert.Equal(new uint[] { 30, 20 }, second.Messages.Select(message => message.Key.Uid));
        Assert.Equal(10u, Assert.Single(last.Messages).Key.Uid);
        Assert.Null(last.Older);
        Assert.Contains(server.MailCommands, command => command.StartsWith("FETCH 4:5 ", StringComparison.Ordinal));
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("BODY", StringComparison.Ordinal));
        AssertReadOnly(server);
    }

    [Fact]
    public async Task EmptyInboxDoesNotIssueFetch()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        var (receiver, account) = await Setup(server);
        var page = await receiver.GetInboxAsync(account, 50);
        Assert.Empty(page.Messages);
        Assert.Null(page.Older);
        Assert.DoesNotContain("FETCH", server.Commands);
    }

    [Fact]
    public async Task ChangedMailboxMembershipRequiresRefreshBeforeContinuing()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.AddRange([new(1, Plain), new(2, Plain)]);
        var (receiver, account) = await Setup(server);
        var page = await receiver.GetInboxAsync(account, 1);
        // Same count, but one expunge and one delivery. UIDNEXT must also be checked.
        server.Messages.RemoveAt(0);
        server.Messages.Add(new(3, Plain));
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetInboxAsync(account, 1, page.Older));
        Assert.Contains("inbox changed", error.Message);
        Assert.Single(server.MailCommands, command => command.StartsWith("FETCH", StringComparison.Ordinal));
        Assert.Equal(3u, Assert.Single((await receiver.GetInboxAsync(account, 1)).Messages).Key.Uid);
    }

    [Fact]
    public async Task ReadsBodyUsingBoundedPeekAndPreservesUnreadFlag()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(42, Plain));
        var (receiver, account) = await Setup(server);
        var body = await receiver.GetBodyAsync(account, Key(account, 42));
        Assert.Equal("Hello & greetings!", body.PlainText);
        Assert.False(body.IsHtmlFallback);
        Assert.Null(body.HtmlText);
        Assert.Contains(server.MailCommands, command => command.Contains("BODY.PEEK[]<0.2097153>", StringComparison.Ordinal));
        Assert.False(Assert.Single((await receiver.GetInboxAsync(account, 50)).Messages).IsRead);
        AssertReadOnly(server);
    }

    [Fact]
    public async Task ChangedUidValidityNeverFetchesAReusedUid()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { UidValidity = 8 };
        server.Messages.Add(new(42, Plain));
        var (receiver, account) = await Setup(server);
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetBodyAsync(account, Key(account, 42)));
        Assert.DoesNotContain("UID", server.Commands);
    }

    [Fact]
    public async Task RemovedMessageAndOversizedBodyProduceUsefulErrors()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(2, Plain + new string('x', ImapMailReceiver.MaximumMessageBytes)));
        var (receiver, account) = await Setup(server);
        var removed = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetBodyAsync(account, Key(account, 1)));
        Assert.Contains("no longer available", removed.Message);
        var large = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetBodyAsync(account, Key(account, 2)));
        Assert.Contains("2 MiB", large.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectAndMalformedResponsesBecomeSafeErrors(bool malformed)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { DisconnectOnFetch = !malformed, MalformedFetch = malformed };
        server.Messages.Add(new(1, Plain));
        var (receiver, account) = await Setup(server);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetInboxAsync(account, 50));
        Assert.DoesNotContain("NOT-AN-IMAP", error.ToString());
        Assert.DoesNotContain(LocalImapServer.Password, error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowBodyCanBeCanceledOrTimesOut(bool cancel)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { StallOnFetch = true };
        server.Messages.Add(new(1, Plain));
        var (receiver, account) = await Setup(server, timeout: TimeSpan.FromMilliseconds(800));
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.CancelAfter(200);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiver.GetBodyAsync(account, Key(account, 1), cancellation.Token));
        else
            Assert.Contains("timed out", (await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetBodyAsync(account, Key(account, 1)))).Message);
    }

    [Fact]
    public async Task InvalidAccountOrPageCannotStartNetworkActivity()
    {
        var account = TestDirectory.Profile();
        var receiver = new ImapMailReceiver(new TestCredentialStore(), () => throw new InvalidOperationException("Network should not start"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => receiver.GetInboxAsync(account, 51));
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetBodyAsync(account, Key(account, 1) with { AccountId = AccountId.New() }));
    }

    [Fact]
    public async Task MultipartPrefersPlainTextAndDecodesCharsetAndTransferEncoding()
    {
        const string raw = "MIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=parts\r\n\r\n--parts\r\nContent-Type: text/plain; charset=iso-8859-1\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\nGr=FC=DFe & friends\r\n--parts\r\nContent-Type: text/html\r\n\r\n<p>HTML alternative</p>\r\n--parts--\r\n";
        var body = await Decode(raw);
        Assert.Contains("Grüße & friends", body.PlainText);
        Assert.DoesNotContain("HTML alternative", body.PlainText);
        Assert.False(body.IsHtmlFallback);
    }

    [Fact]
    public async Task HtmlOnlyProducesLabeledTextWithoutMarkupOrActiveContent()
    {
        const string raw = "MIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<html><head><style>hidden-style</style><title>hidden-title</title></head><body><p>Hello &amp; welcome</p><script>hidden-script</script><p>Second<br>line <a href='https://example.test'>link</a><img src='https://example.test/tracker'></p><!--hidden-comment--></body></html>";
        var body = await Decode(raw);
        Assert.True(body.IsHtmlFallback);
        Assert.Contains("Hello & welcome", body.PlainText);
        Assert.Contains("\nSecond\nline link", body.PlainText);
        Assert.DoesNotContain("hidden", body.PlainText);
        Assert.DoesNotContain("example.test", body.PlainText);
        Assert.DoesNotContain("<", body.PlainText);
        Assert.Contains("<script>", body.HtmlText); // Untrusted source retained only for the isolated preview boundary.
    }

    [Fact]
    public async Task LongAndAttachmentOnlyMessagesHaveExplicitDisplayStates()
    {
        var longBody = await Decode("Content-Type: text/plain\r\n\r\n" + new string('x', 40_000));
        Assert.True(longBody.IsTruncated);
        Assert.Equal(32_000, longBody.PlainText.Length);
        var attachment = await Decode("Content-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=data.bin\r\n\r\nabc");
        Assert.Contains("No readable text", attachment.PlainText);
    }

    private static async Task<MailMessageBody> Decode(string raw)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));
        return await MessageTextDecoder.DecodeAsync(Key(TestDirectory.Profile(), 1), stream, CancellationToken.None);
    }

    private static MailMessageKey Key(AccountProfile account, uint uid) => new(account.Id, "INBOX", 7, uid);
    private static void AssertReadOnly(LocalImapServer server)
    {
        Assert.Contains("EXAMINE", server.Commands);
        Assert.DoesNotContain(server.Commands, command => command is "SELECT" or "STORE" or "EXPUNGE" or "CLOSE");
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("STORE", StringComparison.Ordinal));
    }

    private static async Task<(ImapMailReceiver Receiver, AccountProfile Account)> Setup(LocalImapServer server,
        TransportSecurity security = TransportSecurity.Tls, TimeSpan? timeout = null)
    {
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = security } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var receiver = new ImapMailReceiver(credentials, () => new ImapClient
        { ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString() }, timeout ?? TimeSpan.FromSeconds(5));
        return (receiver, account);
    }
}
