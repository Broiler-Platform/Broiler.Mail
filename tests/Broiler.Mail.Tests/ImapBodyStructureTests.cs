using System.Globalization;
using System.Text;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Infrastructure.Protocols.Imap;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.ListView;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>Owner decision for UX-M2: a best-effort IMAP BODYSTRUCTURE hint marks rows that were never opened.</summary>
public sealed class ImapBodyStructureTests
{
    private const string Text = "(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"utf-8\") NIL NIL \"7BIT\" 12 1 NIL NIL NIL NIL)";
    private const string Html = "(\"TEXT\" \"HTML\" (\"CHARSET\" \"utf-8\") NIL NIL \"QUOTED-PRINTABLE\" 40 2 NIL NIL NIL NIL)";
    private const string Pdf = "(\"APPLICATION\" \"PDF\" (\"NAME\" \"report.pdf\") NIL NIL \"BASE64\" 1024 NIL (\"ATTACHMENT\" (\"FILENAME\" \"report.pdf\")) NIL NIL)";
    private const string InlineImage = "(\"IMAGE\" \"PNG\" (\"NAME\" \"logo.png\") \"<logo@example.test>\" NIL \"BASE64\" 512 NIL (\"INLINE\" (\"FILENAME\" \"logo.png\")) NIL NIL)";

    private static string Multipart(string subtype, params string[] parts) => "(" + string.Concat(parts) + $" \"{subtype}\" (\"BOUNDARY\" \"b\") NIL NIL NIL)";

    public static TheoryData<string, bool> Structures => new()
    {
        { Text, false },
        { "(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 12 1)", false },
        { Pdf, true },
        // Attachment-only message: the root part itself is the attachment.
        { "(\"APPLICATION\" \"OCTET-STREAM\" NIL NIL NIL \"BASE64\" 4 NIL (\"attachment\" NIL) NIL NIL)", true },
        { Multipart("ALTERNATIVE", Text, Html), false },
        { Multipart("MIXED", Multipart("ALTERNATIVE", Text, Html), Pdf), true },
        { Multipart("MIXED", Multipart("RELATED", Multipart("ALTERNATIVE", Text, Html), InlineImage)), false },
        // A file name without a disposition counts unless the part has a Content-ID or Content-Location.
        { "(\"IMAGE\" \"JPEG\" (\"NAME\" \"photo.jpg\") NIL NIL \"BASE64\" 10 NIL NIL NIL NIL)", true },
        { "(\"IMAGE\" \"JPEG\" (\"NAME\" \"photo.jpg\") \"<photo@x>\" NIL \"BASE64\" 10 NIL NIL NIL NIL)", false },
        { "(\"IMAGE\" \"JPEG\" (\"NAME\" \"photo.jpg\") NIL NIL \"BASE64\" 10 NIL NIL NIL \"http://example.test/photo.jpg\")", false },
        { "(\"IMAGE\" \"JPEG\" NIL NIL NIL \"BASE64\" 10 NIL (\"INLINE\" (\"FILENAME\" \"photo.jpg\")) NIL NIL)", false },
        // A disposition of attachment always counts, even with a Content-ID.
        { "(\"IMAGE\" \"PNG\" NIL \"<a@x>\" NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" NIL) NIL NIL)", true },
        // RFC 2231 parameter names.
        { "(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"FORM-DATA\" (\"FILENAME*\" \"utf-8''r%C3%A9sum%C3%A9.pdf\")) NIL NIL)", true },
        { "(\"APPLICATION\" \"PDF\" (\"NAME*0\" \"long\" \"NAME*1\" \".pdf\") NIL NIL \"BASE64\" 10 NIL NIL NIL NIL)", true },
        // An empty file name is no file name.
        { "(\"APPLICATION\" \"PDF\" (\"NAME\" \"\") NIL NIL \"BASE64\" 10 NIL NIL NIL NIL)", false },
        // Literal strings are read inline.
        { "(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" (\"FILENAME\" {7}\r\nrep.pdf)) NIL NIL)", true },
        // An embedded message is one leaf, as in the MIME parser: its own attachments do not mark the outer message.
        { Multipart("MIXED", Text, "(\"MESSAGE\" \"RFC822\" NIL NIL NIL \"7BIT\" 300 (NIL \"Inner\" NIL NIL NIL NIL NIL NIL NIL NIL) " + Multipart("MIXED", Text, Pdf) + " 12 NIL NIL NIL NIL)"), false },
        { Multipart("MIXED", Text, "(\"MESSAGE\" \"RFC822\" NIL NIL NIL \"7BIT\" 300 (NIL \"Inner\" NIL NIL NIL NIL NIL NIL NIL NIL) " + Text + " 12 NIL (\"ATTACHMENT\" NIL) NIL NIL)"), true },
        // A multipart part with an attachment disposition counts without descending into it.
        { "(" + Text + Text + " \"MIXED\" (\"BOUNDARY\" \"c\") (\"ATTACHMENT\" (\"FILENAME\" \"bundle\")) NIL NIL)", true },
        // Servers may omit trailing extension data.
        { "(" + Text + Pdf + " \"MIXED\")", true },
    };

    [Theory]
    [MemberData(nameof(Structures))]
    public void TheHintUsesTheMimeParserAttachmentRule(string structure, bool expected) =>
        Assert.Equal(expected, ImapBodyStructure.HasAttachments(structure));

    [Theory]
    [InlineData("")]
    [InlineData("NIL")]
    [InlineData("()")]
    [InlineData("(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 12 1")]                    // unbalanced
    [InlineData("(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" twelve 1)")]              // octets not a number
    [InlineData("(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 12)")]                    // text without line count
    [InlineData("(\"TEXT\" \"PLAIN\" (\"CHARSET\") NIL NIL \"7BIT\" 12 1)")]        // odd parameter list
    [InlineData("((\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 12 1))")]                // multipart without subtype
    [InlineData("(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL \"ATTACHMENT\" NIL NIL)")] // disposition not a list
    [InlineData("(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" (\"FILENAME\" {9}\r\nrep.pdf)) NIL NIL)")] // literal runs past the end
    [InlineData("(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" (\"FILENAME\" {7}rep.pdf)) NIL NIL)")]   // literal without CRLF
    [InlineData("(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 12 1) trailing")]
    [InlineData("(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7B\\IT\" 12 1)")]               // invalid quoted escape
    public void MalformedStructuresGiveNoHint(string structure) => Assert.Null(ImapBodyStructure.HasAttachments(structure));

    [Fact]
    public void StructuresOverTheBudgetGiveNoHint()
    {
        // Deeper than the MIME parser's nesting limit, even with an attachment at the bottom.
        string deep = Pdf;
        for (int level = 0; level < ImapBodyStructure.MaximumPartDepth + 1; level++) deep = "(" + deep + " \"MIXED\")";
        Assert.Null(ImapBodyStructure.HasAttachments(deep));
        string shallow = Pdf;
        for (int level = 0; level < 8; level++) shallow = "(" + shallow + " \"MIXED\")";
        Assert.True(ImapBodyStructure.HasAttachments(shallow));
        // More parts than the MIME parser reads.
        Assert.Null(ImapBodyStructure.HasAttachments("(" + string.Concat(Enumerable.Repeat(Text, ImapBodyStructure.MaximumParts)) + Pdf + " \"MIXED\")"));
        // Oversized literal and oversized text.
        string big = new('x', ImapBodyStructure.MaximumLiteralBytes + 1);
        Assert.Null(ImapBodyStructure.HasAttachments($"(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" (\"FILENAME\" {{{big.Length}}}\r\n{big})) NIL NIL)"));
        Assert.Null(ImapBodyStructure.HasAttachments(Pdf + new string(' ', ImapBodyStructure.MaximumResponseLength)));
        // Too many tokens.
        string parameters = string.Concat(Enumerable.Repeat("\"A\" \"B\" ", ImapBodyStructure.MaximumNodes / 2 + 1));
        Assert.Null(ImapBodyStructure.HasAttachments($"(\"TEXT\" \"PLAIN\" ({parameters}) NIL NIL \"7BIT\" 12 1)"));
        // Parenthesis nesting inside an extension.
        Assert.Null(ImapBodyStructure.HasAttachments("(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 12 1 NIL NIL NIL NIL " +
            new string('(', ImapBodyStructure.MaximumListDepth + 1) + new string(')', ImapBodyStructure.MaximumListDepth + 1) + ")"));
    }

    [Theory]
    [InlineData("attachment")]
    [InlineData("inline cid")]
    [InlineData("content location")]
    [InlineData("named")]
    [InlineData("plain")]
    public void TheHintAgreesWithTheReaderDecoder(string mode)
    {
        var (raw, structure) = mode switch
        {
            "attachment" => (Mime("Content-Type: application/pdf; name=\"a.pdf\"\r\nContent-Disposition: attachment; filename=\"a.pdf\"\r\nContent-Transfer-Encoding: base64\r\n\r\nAAEC\r\n"),
                "(\"APPLICATION\" \"PDF\" (\"NAME\" \"a.pdf\") NIL NIL \"BASE64\" 6 NIL (\"ATTACHMENT\" (\"FILENAME\" \"a.pdf\")) NIL NIL)"),
            "inline cid" => (Mime("Content-Type: image/png; name=\"logo.png\"\r\nContent-ID: <logo@x>\r\nContent-Transfer-Encoding: base64\r\n\r\nAAEC\r\n"),
                "(\"IMAGE\" \"PNG\" (\"NAME\" \"logo.png\") \"<logo@x>\" NIL \"BASE64\" 6 NIL NIL NIL NIL)"),
            "content location" => (Mime("Content-Type: image/png; name=\"logo.png\"\r\nContent-Location: logo.png\r\nContent-Transfer-Encoding: base64\r\n\r\nAAEC\r\n"),
                "(\"IMAGE\" \"PNG\" (\"NAME\" \"logo.png\") NIL NIL \"BASE64\" 6 NIL NIL NIL \"logo.png\")"),
            "named" => (Mime("Content-Type: image/png; name=\"photo.png\"\r\nContent-Transfer-Encoding: base64\r\n\r\nAAEC\r\n"),
                "(\"IMAGE\" \"PNG\" (\"NAME\" \"photo.png\") NIL NIL \"BASE64\" 6 NIL NIL NIL NIL)"),
            _ => (Mime("Content-Type: text/plain\r\n\r\nMore text\r\n"), "(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 11 1 NIL NIL NIL NIL)"),
        };
        string full = "(" + Text + structure + " \"MIXED\" (\"BOUNDARY\" \"b\") NIL NIL NIL)";
        var decoded = MessageTextDecoder.DecodeReceived(new MailMessageKey(AccountId.New(), "INBOX", 7, 1), raw, CancellationToken.None);
        Assert.Equal(decoded.Attachments is { Count: > 0 }, ImapBodyStructure.HasAttachments(full));
        Assert.Equal(MimeParser.ParseForReader(raw).Parts.Any(part => part.IsAttachment), ImapBodyStructure.HasAttachments(full));
    }

    private static byte[] Mime(string secondPart) => Encoding.ASCII.GetBytes(
        "From: a@example.test\r\nSubject: s\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"b\"\r\n\r\n" +
        "--b\r\nContent-Type: text/plain\r\n\r\nHello there\r\n--b\r\n" + secondPart + "--b--\r\n");

    [Theory]
    [InlineData("* 3 FETCH (UID 42 BODYSTRUCTURE " + Pdf + ")", 3, 42u, true)]
    [InlineData("* 3 FETCH (BODYSTRUCTURE " + Text + " UID 42)", 3, 42u, false)]
    [InlineData("* 3 fetch (uid 42 bodystructure " + Text + ")", 3, 42u, false)]
    [InlineData("* 3 FETCH (UID 42 FLAGS (\\Seen) BODYSTRUCTURE " + Pdf + ")", 3, 42u, true)]
    [InlineData("* 3 FETCH (UID 42)", 3, 42u, null)]
    [InlineData("* 3 FETCH (UID 42 BODYSTRUCTURE (\"TEXT\"))", 3, 42u, null)]
    [InlineData("* 3 FETCH (UID 42 BODYSTRUCTURE " + Pdf + " BODYSTRUCTURE " + Text + ")", 3, 42u, null)]
    public void FetchResponsesNameTheirMessage(string response, int sequence, uint uid, bool? expected) =>
        Assert.Equal(new ImapAttachmentHint(sequence, uid, expected), ImapBodyStructure.ParseFetchResponse(response));

    [Theory]
    [InlineData("* 3 FETCH (BODYSTRUCTURE " + Pdf + ")")]
    [InlineData("* 3 FETCH (UID 0 BODYSTRUCTURE " + Pdf + ")")]
    [InlineData("* 3 FETCH (UID 4 UID 5 BODYSTRUCTURE " + Pdf + ")")]
    [InlineData("* x FETCH (UID 42 BODYSTRUCTURE " + Pdf + ")")]
    [InlineData("* 3 EXISTS")]
    [InlineData("* 3 FETCH (UID 42 BODYSTRUCTURE " + Pdf + ") extra")]
    [InlineData("A0001 OK done")]
    public void ResponsesWithoutAClearMessageAreIgnored(string response) => Assert.Null(ImapBodyStructure.ParseFetchResponse(response));

    [Fact]
    public async Task ReceivingMarksNeverOpenedMessagesWithAttachments()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.AddRange([
            new(1, Plain),
            new(2, Plain, BodyStructure: Multipart("MIXED", Multipart("ALTERNATIVE", Text, Html), Pdf)),
            new(3, Plain, BodyStructure: Multipart("RELATED", Html, InlineImage)),
            new(4, Plain, BodyStructure: "(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" (\"FILENAME\" {7}\r\nrep.pdf)) NIL NIL)"),
            new(5, Plain, BodyStructure: "(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" twelve 1)"),
        ]);
        var (receiver, account) = await Setup(server);
        var page = await receiver.GetInboxAsync(account, 50);
        Assert.Equal(new uint[] { 2, 4 }, page.Messages.Where(row => row.HasAttachments).Select(row => row.Key.Uid).Order());
        Assert.Equal(1, server.BodyStructureFetchCount);
        Assert.Contains("FETCH 1:5 (UID BODYSTRUCTURE)", server.MailCommands);
        // Never a body.
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("BODY[", StringComparison.Ordinal) || command.Contains("BODY.PEEK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANeverOpenedMessageShowsTheBadgeAndKeepsItInTheCache()
    {
        using var directory = new TestDirectory();
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.AddRange([new(1, Plain), new(2, Plain, BodyStructure: Multipart("MIXED", Text, Pdf))]);
        var (receiver, account) = await Setup(server);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        using (var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache))
        {
            model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync();
            var row = model.Messages.Single(message => message.Key.Uid == 2);
            Assert.True(row.HasAttachments); Assert.False(row.IsBodyCached);
            Assert.False(model.Messages.Single(message => message.Key.Uid == 1).HasAttachments);
            Assert.Contains("Has attachments, ", AccessibleName(row), StringComparison.Ordinal);
            Assert.DoesNotContain("attachments", AccessibleName(model.Messages.Single(message => message.Key.Uid == 1)), StringComparison.Ordinal);
        }
        var saved = await cache.ReadSummariesAsync(new MailboxKey(account.Id, "INBOX", 7), 10);
        Assert.True(saved.Single(row => row.Key.Uid == 2).HasAttachments);
        Assert.False(saved.Single(row => row.Key.Uid == 1).HasAttachments);

        // Offline, the next session shows the badge from the cache.
        using var offline = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        offline.SetAccount(account); await offline.CacheLoadTask;
        Assert.True(offline.Messages.Single(row => row.Key.Uid == 2).HasAttachments);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("disconnect")]
    [InlineData("stall")]
    [InlineData("garbage")]
    [InlineData("oversized literal")]
    [InlineData("oversized line")]
    [InlineData("wrong uid")]
    public async Task AServerThatFailsTheHintStillDeliversHeaders(string mode)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        {
            RejectBodyStructure = mode == "rejected", DisconnectOnBodyStructure = mode == "disconnect", StallOnBodyStructure = mode == "stall",
            BodyStructureResponseOverride = mode switch
            {
                "garbage" => "NOT-AN-IMAP-RESPONSE",
                "oversized literal" => "* 1 FETCH (UID 1 BODYSTRUCTURE (\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" 10 NIL (\"ATTACHMENT\" (\"FILENAME\" {99999}",
                "oversized line" => "* 1 FETCH (UID 1 BODYSTRUCTURE (\"" + new string('x', ImapBodyStructure.MaximumResponseLength + 10) + "\"))",
                "wrong uid" => "* 1 FETCH (UID 9 BODYSTRUCTURE " + Pdf + ")\r\n* 2 FETCH (UID 2 BODYSTRUCTURE " + Pdf + ")\r\n* 2 FETCH (UID 2 BODYSTRUCTURE " + Text + ")",
                _ => null,
            },
        };
        server.Messages.AddRange([new(1, Plain, BodyStructure: Pdf), new(2, Plain, BodyStructure: Pdf)]);
        var (receiver, account) = await Setup(server, timeout: TimeSpan.FromSeconds(mode == "stall" ? 3 : 5));
        var started = DateTimeOffset.UtcNow;
        var page = await receiver.GetInboxAsync(account, 50);
        Assert.Equal(new uint[] { 2, 1 }, page.Messages.Select(row => row.Key.Uid));
        Assert.All(page.Messages, row => Assert.Equal("Grüße", row.Subject));
        Assert.All(page.Messages, row => Assert.False(row.HasAttachments));
        Assert.Equal(1, server.BodyStructureFetchCount);
        if (mode == "stall") Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(3), "The hint waits at most half the deadline.");
    }

    [Fact]
    public async Task CancelingDuringTheHintCancelsTheReceive()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { StallOnBodyStructure = true };
        server.Messages.Add(new(1, Plain, BodyStructure: Pdf));
        var (receiver, account) = await Setup(server, timeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var receive = receiver.GetInboxAsync(account, 50, cancellationToken: cancellation.Token);
        for (int wait = 0; wait < 100 && server.BodyStructureFetchCount == 0; wait++) await Task.Delay(50);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive);
    }

    [Fact]
    public async Task TheSavedHintNeverOverridesAMarkFromADecodedBody()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        MailMessageSummary Hinted(uint uid, bool hint) => SqliteMailCacheTests.Summary(mailbox, uid) with { HasAttachments = hint };
        await cache.SaveSummariesAsync(mailbox, [Hinted(1, true), Hinted(2, true), Hinted(3, false), Hinted(4, true)], DateTimeOffset.UtcNow);
        // Message 2's body says it has none, message 3's says it has some, message 4's predates attachment lists.
        await cache.SaveBodyAsync(mailbox, new MailMessageBody(SqliteMailCacheTests.Key(mailbox, 2), "None") { Attachments = [] });
        await cache.SaveBodyAsync(mailbox, new MailMessageBody(SqliteMailCacheTests.Key(mailbox, 3), "Some") { Attachments = [new(2, "a.pdf", "application/pdf", 3)] });
        await cache.SaveBodyAsync(mailbox, new MailMessageBody(SqliteMailCacheTests.Key(mailbox, 4), "Legacy"));
        // Receiving again with the opposite hints changes nothing.
        await cache.SaveSummariesAsync(mailbox, [Hinted(1, false), Hinted(2, true), Hinted(3, false), Hinted(4, false)], DateTimeOffset.UtcNow);
        var saved = (await cache.ReadSummariesAsync(mailbox, 10)).ToDictionary(row => row.Key.Uid, row => row.HasAttachments);
        Assert.Equal(new Dictionary<uint, bool> { [1] = true, [2] = false, [3] = true, [4] = true }, saved);
        var marks = await ((IMailAttachmentStateCache)cache).ReadAttachmentMarksAsync(mailbox, [1, 2, 3, 4, 5]);
        Assert.Equal(new Dictionary<uint, bool> { [1] = true, [2] = false, [3] = true, [4] = true }, marks);
        // A complete snapshot write follows the same rule.
        await cache.ApplyMailboxSnapshotAsync(new MailMailboxSnapshot(mailbox, [Hinted(5, true), Hinted(2, true), Hinted(1, false)], DateTimeOffset.UtcNow.AddMinutes(1)));
        saved = (await cache.ReadSummariesAsync(mailbox, 10)).ToDictionary(row => row.Key.Uid, row => row.HasAttachments);
        Assert.True(saved[5]); Assert.False(saved[2]); Assert.True(saved[1]);
        Assert.Empty(await ((IMailAttachmentStateCache)cache).ReadAttachmentMarksAsync(mailbox, []));
        await Assert.ThrowsAsync<ArgumentException>(() => ((IMailAttachmentStateCache)cache).ReadAttachmentMarksAsync(mailbox, [0]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ((IMailAttachmentStateCache)cache).ReadAttachmentMarksAsync(mailbox,
            Enumerable.Range(1, IMailAttachmentStateCache.MaximumRequestedUids + 1).Select(uid => (uint)uid).ToArray()));
    }

    [Fact]
    public async Task AVersionFiveCacheAcceptsHintedHeadersWithoutStoringThem()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        string path = directory.File("mail.db");
        await new SqliteMailCache(path).SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 9)], DateTimeOffset.UtcNow);
        CacheSchemaFixture.DowngradeTo(path, 5);
        var cache = new SqliteMailCache(path, CacheSchemaFixture.FillSoMigrationDefers(path));
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1) with { HasAttachments = true }], DateTimeOffset.UtcNow);
        Assert.Equal(5L, CacheSchemaFixture.Scalar(path, "PRAGMA user_version"));
        Assert.False((await cache.ReadSummariesAsync(mailbox, 10)).Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.Empty(await cache.ReadAttachmentMarksAsync(mailbox, [1]));
    }

    [Fact]
    public async Task ABodyThatListsNoAttachmentsOverridesTheHintInTheList()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new MailMessageBody(SqliteMailCacheTests.Key(mailbox, 1), "None") { Attachments = [] });
        var receiver = new TestMailReceiver
        {
            // The server's structure says both have attachments; the decoded body of message 1 says it has none.
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 2) with { HasAttachments = true },
                SqliteMailCacheTests.Summary(mailbox, 1) with { HasAttachments = true }], null) { Mailbox = mailbox }),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync();
        Assert.False(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.True(model.Messages.Single(row => row.Key.Uid == 2).HasAttachments);
        // Without a cache the hint is shown as received.
        using var uncached = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        uncached.SetAccount(account); await uncached.ReceiveAsync();
        Assert.All(uncached.Messages, row => Assert.True(row.HasAttachments));
    }

    [Fact]
    public async Task ReceivingAgainKeepsADownloadedMessageAvailableOffline()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        // Received headers never say whether a body is saved.
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 2),
                SqliteMailCacheTests.Summary(mailbox, 1) with { HasAttachments = true }], null) { Mailbox = mailbox }),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask; await model.ReceiveAsync();
        Assert.All(model.Messages, row => Assert.False(row.IsBodyCached));
        // Opening message 1 saves its body under the on-demand policy.
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        Assert.True(model.SelectedMessage!.IsBodyCached);
        await model.ReceiveAsync();
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).IsBodyCached);
        Assert.True(model.SelectedMessage!.IsBodyCached);
        Assert.True(model.Messages.Single(row => row.Key.Uid == 1).HasAttachments);
        Assert.False(model.Messages.Single(row => row.Key.Uid == 2).IsBodyCached);
    }

    [Fact]
    public async Task TheSavedRowStatesReportMarksAndSavedBodiesInOneRead()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        string path = directory.File("mail.db");
        var cache = new SqliteMailCache(path);
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1) with { HasAttachments = true },
            SqliteMailCacheTests.Summary(mailbox, 2), SqliteMailCacheTests.Summary(mailbox, 3)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new MailMessageBody(SqliteMailCacheTests.Key(mailbox, 2), "None") { Attachments = [] });
        await cache.SaveBodyAsync(mailbox, new MailMessageBody(SqliteMailCacheTests.Key(mailbox, 3), "Legacy"));
        var states = await cache.ReadSavedRowStatesAsync(mailbox, [1, 2, 3, 4]);
        Assert.Equal(new Dictionary<uint, MailSavedRowState>
        {
            [1] = new(true, false), [2] = new(false, true), [3] = new(null, true),
        }, states);
        // Another epoch's rows are not reported.
        Assert.Empty(await cache.ReadSavedRowStatesAsync(new MailboxKey(account.Id, "INBOX", 8), [1, 2, 3]));
        Assert.Empty(await cache.ReadSavedRowStatesAsync(mailbox, []));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.ReadSavedRowStatesAsync(mailbox, [0]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.ReadSavedRowStatesAsync(mailbox,
            Enumerable.Range(1, IMailAttachmentStateCache.MaximumRequestedUids + 1).Select(uid => (uint)uid).ToArray()));

        // A version five cache cannot record the mark but still reports the saved body.
        CacheSchemaFixture.DowngradeTo(path, 5);
        var legacy = new SqliteMailCache(path, CacheSchemaFixture.FillSoMigrationDefers(path));
        var legacyStates = await legacy.ReadSavedRowStatesAsync(mailbox, [1, 2]);
        Assert.Equal(new Dictionary<uint, MailSavedRowState> { [1] = new(null, false), [2] = new(null, true) }, legacyStates);
    }

    private const string Plain = "From: sender@example.test\r\nSubject: Test\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nHello\r\n";

    private static string AccessibleName(MailMessageSummary message) =>
        new MailMessageItemPresenter(new MessageDateFormatter(TimeProvider.System, CultureInfo.GetCultureInfo("en-US"))).CreateSemanticNode(new UiListItemSemanticContext
        {
            Item = new UiListItem(message.Key.ToString(), message.Sender, message.Subject, null, message.IsRead, message), Index = 0,
            Bounds = new BRect(0, 0, 280, 52), State = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: message.IsRead, Index: 0),
        }).Name;

    private static async Task<(ImapMailReceiver Receiver, AccountProfile Account)> Setup(LocalImapServer server, TimeSpan? timeout = null)
    {
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        return (new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), timeout ?? TimeSpan.FromSeconds(5)), account);
    }
}
