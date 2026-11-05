using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

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
        Assert.Contains(server.MailCommands, command => command.Contains($"BODY.PEEK[]<0.{ImapMailReceiver.MaximumMessageBytes + 1}>", StringComparison.Ordinal));
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
        Assert.Contains("12 MiB", large.Message);
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

    [Theory]
    [InlineData("Archive", "Archive")]
    [InlineData("A&B", "A&-B")]
    [InlineData("\u53f0\u5317", "&U,BTFw-")]
    [InlineData("inbox", "INBOX")]
    [InlineData("A\"B", "A\\\"B")]
    public async Task ReadsNamedMailboxUsingEncodedExamineAndPeek(string mailbox, string wire)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.AddRange([new(1, Plain), new(2, Plain)]);
        var (receiver, account) = await Setup(server);
        var page = await receiver.GetMailboxAsync(account, mailbox, 1);
        string canonical = mailbox == "inbox" ? "INBOX" : mailbox;
        Assert.Equal(canonical, page.Mailbox!.MailboxId);
        Assert.Equal(canonical, page.Older!.MailboxId);
        var key = Assert.Single(page.Messages).Key;
        Assert.Equal(canonical, key.MailboxId);
        Assert.Equal("Hello & greetings!", (await receiver.GetBodyAsync(account, key)).PlainText);
        Assert.Contains($"EXAMINE \"{wire}\"", server.MailCommands);
        AssertReadOnly(server);
    }

    [Fact]
    public async Task CursorCannotCrossFoldersEvenWithIdenticalUidEpochs()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.AddRange([new(1, Plain), new(2, Plain)]);
        var (receiver, account) = await Setup(server);
        var page = await receiver.GetMailboxAsync(account, "Archive", 1);
        int before = server.MailCommands.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetMailboxAsync(account, "archive", 1, page.Older));
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetInboxAsync(account, 1, page.Older));
        Assert.Equal(before, server.MailCommands.Count);
        var inbox = await receiver.GetInboxAsync(account, 1);
        Assert.NotEqual(Assert.Single(page.Messages).Key, Assert.Single(inbox.Messages).Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Archive\r\nA99 LOGOUT")]
    [InlineData("Bad\0Name")]
    [InlineData("Bad\u007fName")]
    public async Task RejectsUnsafeMailboxBeforeConnecting(string mailbox)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        var (receiver, account) = await Setup(server);
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetMailboxAsync(account, mailbox, 1));
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task NamedMailboxPagingAndEpochChecksRemainReadOnly()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.AddRange([new(1, Plain), new(2, Plain)]);
        var (receiver, account) = await Setup(server);
        var page = await receiver.GetMailboxAsync(account, "Archive", 1);
        Assert.Equal(1u, Assert.Single((await receiver.GetMailboxAsync(account, "Archive", 1, page.Older)).Messages).Key.Uid);
        server.UidValidity++;
        int before = server.MailCommands.Count(command => command.StartsWith("UID FETCH"));
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetBodyAsync(account, page.Messages[0].Key));
        Assert.Equal(before, server.MailCommands.Count(command => command.StartsWith("UID FETCH")));
        server.Messages.Clear();
        var empty = await receiver.GetMailboxAsync(account, "Archive", 1);
        Assert.Empty(empty.Messages);
        Assert.Equal("Archive", empty.Mailbox!.MailboxId);
        AssertReadOnly(server);
    }

    [Fact]
    public async Task RejectsInvalidUnicodeAndOversizedMailboxNames()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        var (receiver, account) = await Setup(server);
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetMailboxAsync(account, new string((char)0xd800, 1), 1));
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetMailboxAsync(account, new string('a', 1025), 1));
        Assert.Empty(server.Commands);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task DiscoversFolderNamesHierarchyAndAttributesWithoutSelecting(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security)
        {
            FolderListResponse = "* LIST (\\HasNoChildren) \"/\" inbox\r\n" +
                "* LIST (\\Noselect \\HasChildren) \"/\" \"Projects\"\r\n" +
                "* LIST (\\Sent) \"/\" \"Projects/&U,BTFw-&-\"\r\n" +
                "* LIST () NIL NIL\r\n" +
                "* LIST () \"/\" {6}\r\nA\\B\"CD\r\n" +
                "* LIST (\\NonExistent) \".\" Missing"
        };
        var (receiver, account) = await Setup(server, security);
        var folders = await receiver.GetFoldersAsync(account);
        Assert.Equal(6, folders.Count);
        Assert.Equal("INBOX", folders[0].MailboxId);
        Assert.False(folders[1].IsSelectable);
        Assert.Equal("Projects/\u53f0\u5317&", folders[2].MailboxId);
        Assert.Contains("\\Sent", folders[2].Attributes);
        Assert.Equal("/", folders[2].HierarchyDelimiter);
        Assert.Equal("NIL", folders[3].MailboxId);
        Assert.Null(folders[3].HierarchyDelimiter);
        Assert.Equal("A\\B\"CD", folders[4].MailboxId);
        Assert.False(folders[5].IsSelectable);
        Assert.Equal(".", folders[5].HierarchyDelimiter);
        Assert.Single(server.MailCommands);
        Assert.Contains("LIST \"\" \"*\"", server.MailCommands);
        Assert.DoesNotContain(server.Commands, command => command is "EXAMINE" or "SELECT" or "FETCH" or "STORE");
        Assert.True(server.AuthenticatedOverTls);
    }

    [Theory]
    [InlineData("* LIST () \"/\" \"&bad\"")]
    [InlineData("* LIST () \"/\" \"&AGE-\"")]
    [InlineData("* LIST () \"/\" \"&2AA-\"")]
    [InlineData("* LIST () \"/\" \"&AA-\"")]
    [InlineData("* LIST () \"/\" \"&A/-\"")]
    [InlineData("* LIST () \"//\" Name")]
    [InlineData("* LIST () \"/\" \"Bad\\q\"")]
    [InlineData("* LIST () \"/\" {4097}")]
    [InlineData("* LIST () \"/\" \"INBOX\"\r\n* LIST () \"/\" inbox")]
    [InlineData("* LIST () \"/\" \"A\" trailing")]
    public async Task MalformedDiscoveryNeverReturnsAPartialCatalog(string response)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { FolderListResponse = response };
        var (receiver, account) = await Setup(server);
        var error = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetFoldersAsync(account));
        Assert.DoesNotContain(response, error.Message);
        Assert.DoesNotContain(server.Commands, command => command is "EXAMINE" or "SELECT");
    }

    [Theory]
    [InlineData(513, false)]
    [InlineData(1, true)]
    public async Task DiscoveryRejectsOversizedCatalogsAndLines(int count, bool longLine)
    {
        string response = longLine ? "* LIST () \"/\" \"" + new string('a', 9000) + "\"" :
            string.Join("\r\n", Enumerable.Range(0, count).Select(index => $"* LIST () NIL Folder{index}"));
        await using var server = new LocalImapServer(TransportSecurity.Tls) { FolderListResponse = response };
        var (receiver, account) = await Setup(server);
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetFoldersAsync(account));
    }

    [Fact]
    public async Task DiscoveryDistinguishesEmptyCatalogFromServerRejection()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { FolderListResponse = "" };
        var (receiver, account) = await Setup(server);
        Assert.Empty(await receiver.GetFoldersAsync(account));
        server.RejectFolderList = true;
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetFoldersAsync(account));
    }

    [Fact]
    public async Task DiscoveryPreservesWildcardCharactersInReturnedAtomNames()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { FolderListResponse = "* LIST (\\X_Custom) NIL A*%]B" };
        var (receiver, account) = await Setup(server);
        var folder = Assert.Single(await receiver.GetFoldersAsync(account));
        Assert.Equal("A*%]B", folder.MailboxId);
        Assert.Contains("\\X_Custom", folder.Attributes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryRequestsSpecialUseOnlyWhenAdvertisedAfterAuthentication(bool advertised)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        {
            AdvertiseSpecialUse = advertised,
            FolderListResponse = "* LIST (\\Sent) \"/\" \"Envoyes\"",
        };
        var (receiver, account) = await Setup(server);
        var folders = await receiver.GetFoldersAsync(account);
        Assert.Equal("Envoyes", Assert.Single(folders).MailboxId);
        Assert.Contains("LIST \"\" \"*\"" + (advertised ? " RETURN (SPECIAL-USE)" : ""), server.MailCommands);
        Assert.True(server.Commands.Count(command => command == "CAPABILITY") >= 2);
        Assert.DoesNotContain(server.Commands, command => command is "SELECT" or "EXAMINE" or "APPEND");
    }

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task ExplicitReadChangesAreUidScopedIncrementalAndConfirmed(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security); server.Messages.Add(new(42, Plain));
        var (receiver, account) = await Setup(server, security);
        var key = new MailMessageKey(account.Id, "Archive/\u53f0\u5317&", 7, 42);
        var result = await receiver.SetReadStateAsync(account, key, true);
        Assert.Equal(MailFlagChangeStatus.Confirmed, result.Status); Assert.True(server.Messages[0].IsRead);
        Assert.Contains("SELECT \"Archive/&U,BTFw-&-\"", server.MailCommands);
        Assert.Contains("UID STORE 42 +FLAGS.SILENT (\\Seen)", server.MailCommands);
        Assert.Equal(MailFlagChangeStatus.Confirmed, (await receiver.SetReadStateAsync(account, key, true)).Status);
        Assert.Equal(1, server.ReadStateStoreCount);
        Assert.Equal(MailFlagChangeStatus.Confirmed, (await receiver.SetReadStateAsync(account, key, false)).Status);
        Assert.False(server.Messages[0].IsRead); Assert.Equal(2, server.ReadStateStoreCount);
        Assert.Contains("UID STORE 42 -FLAGS.SILENT (\\Seen)", server.MailCommands);
        Assert.DoesNotContain(server.Commands, command => command is "CLOSE" or "EXPUNGE" or "CREATE");
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("readonly")]
    [InlineData("permission")]
    [InlineData("missing")]
    public async Task ReadChangePreflightNeverStoresForInvalidMailboxOrMessage(string reason)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { UidValidity = reason == "epoch" ? 8u : 7u, ReadOnlySelection = reason == "readonly", AllowSeenChanges = reason != "permission" };
        if (reason != "missing") server.Messages.Add(new(1, Plain));
        var (receiver, account) = await Setup(server);
        Assert.Equal(MailFlagChangeStatus.NotApplied, (await receiver.SetReadStateAsync(account, Key(account, 1), true)).Status);
        Assert.Equal(0, server.ReadStateStoreCount);
    }

    [Theory]
    [InlineData("lost", MailFlagChangeStatus.Unknown)]
    [InlineData("removed", MailFlagChangeStatus.Unknown)]
    [InlineData("ignored", MailFlagChangeStatus.Unknown)]
    [InlineData("rejected", MailFlagChangeStatus.NotApplied)]
    public async Task ReadChangeDoesNotBlindlyRetryUnconfirmedStore(string outcome, MailFlagChangeStatus expected)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { DropAfterReadStateStore = outcome == "lost", RemoveAfterReadStateStore = outcome == "removed",
          IgnoreReadStateStore = outcome == "ignored", RejectReadStateStore = outcome == "rejected" };
        server.Messages.Add(new(1, Plain)); var (receiver, account) = await Setup(server);
        Assert.Equal(expected, (await receiver.SetReadStateAsync(account, Key(account, 1), true)).Status);
        Assert.Equal(1, server.ReadStateStoreCount);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task StarAndUnstarAreExplicitConfirmedAndPreserveSeen(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security); server.Messages.Add(new(42, Plain, true));
        var (receiver, account) = await Setup(server, security);
        var key = new MailMessageKey(account.Id, "Archive", 7, 42);
        Assert.Equal(MailFlagChangeStatus.Confirmed, (await receiver.SetStarStateAsync(account, key, true)).Status);
        Assert.True(server.Messages[0].IsStarred); Assert.True(server.Messages[0].IsRead);
        Assert.Contains("UID STORE 42 +FLAGS.SILENT (\\Flagged)", server.MailCommands);
        Assert.True(Assert.Single((await receiver.GetMailboxAsync(account, "Archive", 1)).Messages).IsStarred);
        Assert.Equal(MailFlagChangeStatus.Confirmed, (await receiver.SetStarStateAsync(account, key, true)).Status);
        Assert.Equal(1, server.ReadStateStoreCount);
        Assert.Equal(MailFlagChangeStatus.Confirmed, (await receiver.SetReadStateAsync(account, key, false)).Status);
        Assert.True(server.Messages[0].IsStarred); Assert.False(server.Messages[0].IsRead);
        Assert.Equal(MailFlagChangeStatus.Confirmed, (await receiver.SetStarStateAsync(account, key, false)).Status);
        Assert.False(server.Messages[0].IsStarred); Assert.False(server.Messages[0].IsRead);
        Assert.Contains("UID STORE 42 -FLAGS.SILENT (\\Flagged)", server.MailCommands);
        Assert.DoesNotContain(server.Commands, command => command is "EXPUNGE" or "CLOSE" or "CREATE");
    }

    [Theory]
    [InlineData("permission", MailFlagChangeStatus.NotApplied)]
    [InlineData("epoch", MailFlagChangeStatus.NotApplied)]
    [InlineData("readonly", MailFlagChangeStatus.NotApplied)]
    [InlineData("lost", MailFlagChangeStatus.Unknown)]
    [InlineData("ignored", MailFlagChangeStatus.Unknown)]
    [InlineData("removed", MailFlagChangeStatus.Unknown)]
    [InlineData("rejected", MailFlagChangeStatus.NotApplied)]
    public async Task StarRefusalAndUncertaintyNeverUpdateOrRetryBlindly(string outcome, MailFlagChangeStatus expected)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AllowStarChanges = outcome != "permission", UidValidity = outcome == "epoch" ? 8u : 7u,
          ReadOnlySelection = outcome == "readonly", DropAfterReadStateStore = outcome == "lost",
          IgnoreReadStateStore = outcome == "ignored", RemoveAfterReadStateStore = outcome == "removed", RejectReadStateStore = outcome == "rejected" };
        server.Messages.Add(new(1, Plain)); var (receiver, account) = await Setup(server);
        Assert.Equal(expected, (await receiver.SetStarStateAsync(account, Key(account, 1), true)).Status);
        Assert.Equal(outcome is "permission" or "epoch" or "readonly" ? 0 : 1, server.ReadStateStoreCount);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task UidMoveConfirmsBothMailboxIdentitiesWithoutCopyDeleteFallback(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security)
        { AdvertiseMove = true, MoveDestinationWireName = "Archive/&U,BTFw-&-",
          FolderListResponse = "* LIST () \"/\" INBOX\r\n* LIST () \"/\" \"Archive/&U,BTFw-&-\"" };
        server.Messages.Add(new(42, Plain, true, true)); var (receiver, account) = await Setup(server, security);
        var result = await receiver.MoveAsync(account, Key(account, 42), "Archive/\u53f0\u5317&");
        Assert.Equal(MailMoveStatus.Confirmed, result.Status);
        Assert.Equal(new MailMessageKey(account.Id, "Archive/\u53f0\u5317&", 9, 900), result.Destination);
        Assert.Equal(1, server.MoveCount); Assert.Empty(server.Messages);
        var moved = Assert.Single(server.MovedMessages); Assert.Equal(Plain, moved.Raw); Assert.True(moved.IsRead); Assert.True(moved.IsStarred);
        Assert.Contains("UID MOVE 42 \"Archive/&U,BTFw-&-\"", server.MailCommands);
        Assert.DoesNotContain(server.MailCommands, command => command.StartsWith("UID STORE") || command.StartsWith("UID COPY"));
        Assert.DoesNotContain(server.Commands, command => command is "EXPUNGE" or "CLOSE" or "CREATE");
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("missing destination")]
    [InlineData("nonselectable destination")]
    [InlineData("epoch")]
    [InlineData("readonly")]
    [InlineData("missing source")]
    public async Task MovePreflightNeverMutatesUnsupportedOrInvalidTargets(string reason)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AdvertiseMove = reason != "unsupported", UidValidity = reason == "epoch" ? 8u : 7u,
          ReadOnlySelection = reason == "readonly", FolderListResponse = reason == "missing destination"
            ? "* LIST () NIL INBOX" : $"* LIST ({(reason == "nonselectable destination" ? "\\Noselect" : "")}) NIL Archive" };
        if (reason != "missing source") server.Messages.Add(new(42, Plain));
        var (receiver, account) = await Setup(server);
        var result = await receiver.MoveAsync(account, Key(account, 42), "Archive");
        Assert.Equal(MailMoveStatus.NotApplied, result.Status); Assert.Null(result.Destination); Assert.Equal(0, server.MoveCount);
    }

    [Theory]
    [InlineData("lost")]
    [InlineData("rejected")]
    [InlineData("no receipt")]
    [InlineData("source remains")]
    [InlineData("destination missing")]
    [InlineData("bad source UID")]
    [InlineData("multiple UIDs")]
    [InlineData("wrong destination epoch")]
    public async Task UnverifiedMoveIsUnknownAndNeverRetried(string reason)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AdvertiseMove = true, FolderListResponse = "* LIST () NIL Archive", DropAfterMove = reason == "lost",
          RejectMove = reason == "rejected", MissingMoveReceipt = reason == "no receipt", KeepSourceAfterMove = reason == "source remains",
          MissingMoveDestination = reason == "destination missing", MoveReceiptOverride = reason switch
          { "bad source UID" => "9 43 900", "multiple UIDs" => "9 42:43 900:901", "wrong destination epoch" => "10 42 900", _ => null } };
        server.Messages.Add(new(42, Plain)); var (receiver, account) = await Setup(server);
        var result = await receiver.MoveAsync(account, Key(account, 42), "Archive");
        Assert.Equal(MailMoveStatus.Unknown, result.Status); Assert.Null(result.Destination); Assert.Equal(1, server.MoveCount);
    }

    [Fact]
    public async Task SameFolderAndInvalidMoveKeysNeverConnect()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); var (receiver, account) = await Setup(server);
        Assert.Equal(MailMoveStatus.NotApplied, (await receiver.MoveAsync(account, Key(account, 42), "inbox")).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.MoveAsync(account, Key(TestDirectory.Profile(), 42), "Archive"));
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.MoveAsync(account, Key(account, 42), "Archive\r\nA99 LOGOUT"));
        Assert.Empty(server.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveAcceptsSingleUidRangesAndDoesNotRequireSeenPermission(bool taggedReceipt)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AdvertiseMove = true, AllowSeenChanges = false, AllowStarChanges = false, TaggedMoveReceipt = taggedReceipt,
          FolderListResponse = "* LIST () NIL Archive", MoveReceiptOverride = "9 42:42 900:900" };
        server.Messages.Add(new(42, Plain)); var (receiver, account) = await Setup(server);
        var moved = await receiver.MoveAsync(account, Key(account, 42), "Archive");
        Assert.Equal(MailMoveStatus.Confirmed, moved.Status); Assert.Equal(900u, moved.Destination!.Value.Uid);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task ReverseMoveVerifiesNewSourceEpochAndNewAssignedUid(TransportSecurity security)
    {
        await using var server = new LocalImapServer(security) { AdvertiseMove = true,
            FolderListResponse = "* LIST () NIL INBOX\r\n* LIST () NIL Archive" };
        server.Messages.Add(new(42, Plain, true, true)); var (receiver, account) = await Setup(server, security);
        var moved = await receiver.MoveAsync(account, Key(account, 42), "Archive"); Assert.Equal(MailMoveStatus.Confirmed, moved.Status);
        server.MoveDestinationWireName = "INBOX"; server.MoveDestinationUid = 1000;
        var reversed = await receiver.MoveAsync(account, moved.Destination!.Value, "INBOX");
        Assert.Equal(MailMoveStatus.Confirmed, reversed.Status); Assert.Equal(new MailMessageKey(account.Id, "INBOX", 7, 1000), reversed.Destination);
        Assert.Equal(1000u, Assert.Single(server.Messages).Uid); Assert.Empty(server.MovedMessages); Assert.Equal(2, server.MoveCount);
        Assert.Contains("UID MOVE 900 \"INBOX\"", server.MailCommands);
    }

    [Fact]
    public async Task LostReverseMoveAcknowledgementIsUnknownAndNotReplayed()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { AdvertiseMove = true,
            FolderListResponse = "* LIST () NIL INBOX\r\n* LIST () NIL Archive" };
        server.Messages.Add(new(42, Plain)); var (receiver, account) = await Setup(server);
        var moved = await receiver.MoveAsync(account, Key(account, 42), "Archive"); Assert.Equal(MailMoveStatus.Confirmed, moved.Status);
        server.MoveDestinationWireName = "INBOX"; server.MoveDestinationUid = 1000; server.DropAfterMove = true;
        var reversed = await receiver.MoveAsync(account, moved.Destination!.Value, "INBOX");
        Assert.Equal(MailMoveStatus.Unknown, reversed.Status); Assert.Null(reversed.Destination); Assert.Equal(2, server.MoveCount);
        Assert.Equal(1000u, Assert.Single(server.Messages).Uid);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls, false)]
    [InlineData(TransportSecurity.StartTls, false)]
    [InlineData(TransportSecurity.Tls, true)]
    public async Task PermanentDeletionExpungesOnlyTheConfirmedUid(TransportSecurity security, bool alreadyDeleted)
    {
        await using var server = new LocalImapServer(security) { AdvertiseUidPlus = true };
        server.Messages.AddRange([new(1, Plain, true, true, alreadyDeleted), new(2, Plain, true, true, true)]);
        var (receiver, account) = await Setup(server, security);
        var result = await receiver.PermanentlyDeleteAsync(account, Key(account, 1));
        Assert.Equal(MailDeleteStatus.Confirmed, result.Status); Assert.Equal(2u, Assert.Single(server.Messages).Uid);
        Assert.Equal(alreadyDeleted ? 0 : 1, server.DeleteMarkCount); Assert.Equal(1, server.UidExpungeCount);
        Assert.Contains("UID EXPUNGE 1", server.MailCommands);
        Assert.DoesNotContain(server.Commands, command => command is "EXPUNGE" or "CLOSE");
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("epoch")]
    [InlineData("readonly")]
    [InlineData("permission")]
    [InlineData("missing")]
    public async Task PermanentDeletionRefusesUnsafePreflightWithoutMarkingOrExpunging(string reason)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AdvertiseUidPlus = reason != "unsupported", UidValidity = reason == "epoch" ? 8u : 7u,
          ReadOnlySelection = reason == "readonly", AllowDeletedChanges = reason != "permission" };
        if (reason != "missing") server.Messages.Add(new(1, Plain));
        var (receiver, account) = await Setup(server);
        Assert.Equal(MailDeleteStatus.NotApplied, (await receiver.PermanentlyDeleteAsync(account, Key(account, 1))).Status);
        Assert.Equal(0, server.DeleteMarkCount); Assert.Equal(0, server.UidExpungeCount);
    }

    [Theory]
    [InlineData("lost mark")]
    [InlineData("lost expunge")]
    [InlineData("rejected expunge")]
    [InlineData("ignored expunge")]
    [InlineData("ignored mark")]
    public async Task InterruptedDeletionIsUnknownAndNeverReplayed(string reason)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls)
        { AdvertiseUidPlus = true, DropAfterDeleteMark = reason == "lost mark", DropAfterUidExpunge = reason == "lost expunge",
          RejectUidExpunge = reason == "rejected expunge", IgnoreUidExpunge = reason == "ignored expunge", IgnoreReadStateStore = reason == "ignored mark" };
        server.Messages.Add(new(1, Plain)); var (receiver, account) = await Setup(server);
        Assert.Equal(MailDeleteStatus.Unknown, (await receiver.PermanentlyDeleteAsync(account, Key(account, 1))).Status);
        Assert.Equal(1, server.DeleteMarkCount); Assert.InRange(server.UidExpungeCount, 0, 1);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls, false)]
    [InlineData(TransportSecurity.StartTls, true)]
    public async Task OriginalSourcePreservesOctetsWithUidBeforeOrAfterLiteral(TransportSecurity security, bool uidAfter)
    {
        await using var server = new LocalImapServer(security) { BodyUidAfterLiteral = uidAfter };
        server.Messages.Add(new(1, Plain)); server.SourceOctets = Encoding.ASCII.GetBytes(Plain).Concat(new byte[] { 0, 255, 128, 13, 10 }).ToArray();
        var (receiver, account) = await Setup(server, security); var source = await receiver.GetSourceAsync(account, Key(account, 1));
        Assert.Equal(Key(account, 1), source.Key); Assert.Equal(server.SourceOctets, source.Bytes); AssertReadOnly(server);
        Assert.Contains(server.MailCommands, command => command.Contains("BODY.PEEK[]<0.2097153>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("uid")]
    [InlineData("offset")]
    [InlineData("oversized")]
    [InlineData("missing uid")]
    [InlineData("duplicate")]
    [InlineData("ending")]
    public async Task MalformedSourceResponsesAreRejectedRatherThanAssignedToRequestedMessage(string mode)
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, Plain));
        switch (mode)
        {
            case "uid": server.BodyResponseUid = 2; break; case "offset": server.BodyResponseOffset = 1; break;
            case "oversized": server.DeclaredBodyLiteralLength = MailMessageSource.MaximumBytes + 2; break;
            case "missing uid": server.OmitBodyUid = true; break; case "duplicate": server.DuplicateBodyLiteral = true; break;
            default: server.InvalidBodyEnding = true; break;
        }
        var (receiver, account) = await Setup(server);
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetSourceAsync(account, Key(account, 1))); AssertReadOnly(server);
    }

    [Fact]
    public async Task OriginalSourceRejectsChangedEpochAndDisabledAccountBeforeBodyFetch()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.Add(new(1, Plain)); var (receiver, account) = await Setup(server);
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetSourceAsync(account, Key(account, 1) with { UidValidity = 8 }));
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetSourceAsync(account with { IsEnabled = false }, Key(account, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => receiver.GetSourceAsync(account, Key(account, 1) with { AccountId = AccountId.New() }));
        Assert.DoesNotContain(server.MailCommands, line => line.Contains("BODY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LargeOriginalStreamsToVerifiedFileAndReaderSkipsAttachmentPayload()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { BodyUidAfterLiteral = true };
        server.Messages.Add(new(1, Plain));
        var (receiver, account) = await Setup(server, timeout: TimeSpan.FromSeconds(20));
        var key = Key(account, 1);
        byte[] payload = new byte[3 * 1024 * 1024];
        new Random(41).NextBytes(payload);
        server.SourceOctets = MimeSerializer.Serialize(account, MailComposition.Create(account) with
        {
            To = ["recipient@example.test"], PlainText = "Visible text with a large attachment.",
            Attachments = [new("report.bin", payload)],
        });
        using var directory = new TestDirectory();
        directory.Create();
        var exports = new FileMessageSourceExports(directory.Root);
        var progress = new List<int>();
        var receipt = await exports.SaveStreamAsync(key, (destination, maximum, ct) =>
            receiver.CopySourceAsync(account, key, destination, maximum, (bytes, _) => progress.Add(bytes), ct));
        Assert.True(progress.Count > 1);
        Assert.Equal(receipt.Bytes, progress[^1]);
        Assert.Equal(server.SourceOctets, await File.ReadAllBytesAsync(receipt.Path));
        var attachments = (IMessageAttachmentExports)exports;
        var part = Assert.Single(await attachments.ListAttachmentsAsync(receipt));
        var saved = await attachments.SaveAttachmentAsync(receipt, part);
        Assert.Equal(payload, await File.ReadAllBytesAsync(saved.Path));
        Assert.Equal("Visible text with a large attachment.", (await receiver.GetBodyAsync(account, key)).PlainText);
        AssertReadOnly(server);
    }

    [Fact]
    public async Task StreamedOriginalCancellationOrInvalidTrailingUidLeavesNoPublishedFile()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls) { BodyUidAfterLiteral = true, BodyResponseUid = 2 };
        server.Messages.Add(new(1, Plain));
        var (receiver, account) = await Setup(server);
        using var directory = new TestDirectory();
        directory.Create();
        var exports = new FileMessageSourceExports(directory.Root);
        var key = Key(account, 1);
        await Assert.ThrowsAsync<MailConnectionException>(() => exports.SaveStreamAsync(key, (destination, maximum, ct) =>
            receiver.CopySourceAsync(account, key, destination, maximum, token: ct)));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exports.SaveStreamAsync(key, async (destination, _, ct) =>
        {
            await destination.WriteAsync(new byte[] { 1, 2, 3 }, ct);
            cancellation.Cancel();
            return 3;
        }, cancellation.Token));
        Assert.Empty(Directory.GetFiles(exports.ExportDirectory));
        Assert.Empty(await exports.ListSavedExportsAsync(key));
    }

    [Fact]
    public async Task SourceExportRequiresCompleteReportedSizeAndRefusesOversizeBeforeBodyTransfer()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.Add(new(1, Plain)); var (receiver, account) = await Setup(server);
        server.SourceReportedSize = Encoding.ASCII.GetByteCount(Plain) + 10;
        var incomplete = await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetSourceAsync(account, Key(account, 1))); Assert.Contains("incomplete", incomplete.Message);
        int bodies = server.MailCommands.Count(line => line.Contains("BODY.PEEK", StringComparison.Ordinal)); server.SourceReportedSize = MailMessageSource.MaximumBytes + 1;
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetSourceAsync(account, Key(account, 1)));
        Assert.Equal(bodies, server.MailCommands.Count(line => line.Contains("BODY.PEEK", StringComparison.Ordinal)));
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
        var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), timeout ?? TimeSpan.FromSeconds(5));
        return (receiver, account);
    }
}
