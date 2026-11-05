using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Tests;

// This standalone probe is not shipped with Mail. It exercises the production
// adapters with existing loopback TLS fixtures, without weakening the public app.
try
{
    using (var files = new IntentProbeDirectory())
    {
        var key = new MailMessageKey(AccountId.New(), "INBOX", 1, 1);
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Content-Type: text/plain; charset=utf-8\r\n\r\n" + new string('x', 34000) + " retained tail phrase"));
        var body = await MessageTextDecoder.DecodeAsync(key, source, default);
        Require(body.IsTruncated && !body.PlainText.Contains("retained tail phrase") && body.RetainedBodyText!.Contains("retained tail phrase"), "Complete retained MIME text did not remain separate from preview.");
        string path = Path.Combine(files.Path, "mail.db"); var cache = new SqliteMailCache(path); var box = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        await cache.SaveSummariesAsync(box, [new() { Key = key, Sender = "fixture@example.test", Subject = "Retained text" }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, body); await cache.RebuildSearchIndexAsync();
        var reopened = new SqliteMailCache(path);
        Require((await reopened.SearchHeadersAsync(new(key.AccountId, "retained tail phrase") { Field = MailHeaderSearchField.IndexedRetainedBody, SenderContains = "FIXTURE@" })).Messages.Single().Key == key && (await reopened.GetSearchIndexInfoAsync()).RetainedBodyDocuments == 1, "Native retained-body search/reopen/coverage failed.");
    }
    foreach (var security in new[] { TransportSecurity.Tls, TransportSecurity.StartTls })
    {
        using var files = new IntentProbeDirectory();
        var intents = new MutationIntentJournal(Path.Combine(files.Path, "mail.db"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var imap = new LocalImapServer(security);
        imap.Messages.Add(new(1, "From: sender@example.test\r\nTo: fixture@example.test\r\nSubject: Fixture\r\nContent-Type: multipart/mixed; boundary=fixture\r\n\r\n--fixture\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nDownloaded fixture body\r\n--fixture\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=fallback.bin; filename*=iso-8859-1''caf%E9.bin\r\nContent-Transfer-Encoding: base64\r\n\r\nAP+ADQoq\r\n--fixture--\r\n"));
        await using var smtp = new LocalSmtpServer(security);
        var account = new AccountProfile
        {
            Id = AccountId.New(), DisplayName = "Native protocol fixture", EmailAddress = "fixture@example.test",
            IncomingServer = new() { Host = "127.0.0.1", Port = imap.Port, UserName = "test", Security = security },
            OutgoingServer = new() { Host = "127.0.0.1", Port = smtp.Port, UserName = "test", Security = security },
        };
        var credentials = new FixtureCredentials();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);
        var receiver = new ImapMailReceiver(credentials, (_, certificate, _, _) =>
            certificate?.GetCertHashString() == imap.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5), intents);
        imap.AdvertiseSpecialUse = true;
        imap.FolderListResponse = "* LIST () \"/\" inbox\r\n" +
            "* LIST (\\Noselect) \"/\" Projects\r\n* LIST (\\Archive) \"/\" {18}\r\nArchive/&U,BTFw-&-";
        var folders = await receiver.GetFoldersAsync(account, deadline.Token);
        Require(folders.Count == 3 && folders[0].MailboxId == "INBOX" && !folders[1].IsSelectable &&
            folders[2].MailboxId == "Archive/台北&" && folders[2].Attributes.Contains("\\Archive"),
            "Folder discovery metadata/encoding failed.");
        Require(imap.MailCommands.Contains("LIST \"\" \"*\" RETURN (SPECIAL-USE)") &&
            MailFolderRoles.Resolve(new(), folders).Single(role => role.Role == MailFolderRole.Archive) is
                { MailboxId: "Archive/台北&", IsConfirmed: true, IsOverride: false },
            "Special-use negotiation/role mapping failed.");
        var page = await receiver.GetInboxAsync(account, 50, cancellationToken: deadline.Token);
        Require(page.Messages.Count == 1, "IMAP fixture page mismatch.");
        var snapshot = await receiver.GetMailboxSnapshotAsync(account, "INBOX", deadline.Token);
        var snapshotCache = new SqliteMailCache(Path.Combine(files.Path, "snapshot.db"));
        var checkpoint = await snapshotCache.ApplyMailboxSnapshotAsync(snapshot, deadline.Token);
        Require(snapshot.Messages.Count == page.Messages.Count && checkpoint.ServerMessages == 1 && checkpoint.RetainedAbsentMessages == 0 && await new SqliteMailCache(Path.Combine(files.Path, "snapshot.db")).ReadMailboxCheckpointAsync(account.Id, "INBOX", deadline.Token) == checkpoint, "Native full-header census/checkpoint/reopen failed.");
        var body = await receiver.GetBodyAsync(account, page.Messages[0].Key, deadline.Token);
        Require(body.PlainText.Contains("Downloaded fixture body"), "IMAP body fixture mismatch.");
        var original = await receiver.GetSourceAsync(account, page.Messages[0].Key, deadline.Token);
        Require(original.Bytes.AsSpan().SequenceEqual(System.Text.Encoding.ASCII.GetBytes(imap.Messages[0].Raw)), "Original source octets were changed.");
        var exportedSource = await new FileMessageSourceExports(files.Path).SaveStreamAsync(original.Key, (destination, maximum, token) =>
            receiver.CopySourceAsync(account, original.Key, destination, maximum, token: token), deadline.Token);
        Require(File.ReadAllBytes(exportedSource.Path).AsSpan().SequenceEqual(original.Bytes), "Original .eml export changed bytes.");
        var savedReceipts = await new FileMessageSourceExports(files.Path).ListSavedExportsAsync(original.Key, deadline.Token);
        Require(savedReceipts.Count == 1 && savedReceipts[0].Export == exportedSource, "Original source receipt catalog did not survive service recreation.");
        var inspectedSource = await new FileMessageSourceExports(files.Path).ReadVerifiedAsync(exportedSource, deadline.Token);
        Require(inspectedSource.Key == original.Key && inspectedSource.Bytes.AsSpan().SequenceEqual(original.Bytes), "Verified local source inspection changed identity or bytes.");
        var attachmentExports = new FileMessageSourceExports(files.Path);
        int commandCount = imap.MailCommands.Count;
        var attachments = await attachmentExports.ListAttachmentsAsync(exportedSource, deadline.Token);
        Require(attachments.Count == 1 && attachments[0].FileName == "café.bin", "Received attachment inventory mismatch.");
        var savedAttachment = await attachmentExports.SaveAttachmentAsync(exportedSource, attachments[0], deadline.Token);
        Require(File.ReadAllBytes(savedAttachment.Path).AsSpan().SequenceEqual(new byte[] { 0, 255, 128, 13, 10, 42 }) &&
            imap.MailCommands.Count == commandCount, "Received attachment bytes changed or extraction performed a server request.");
        var archive = await receiver.GetMailboxAsync(account, "Archive/台北&", 50, cancellationToken: deadline.Token);
        Require(archive.Mailbox?.MailboxId == "Archive/台北&" && archive.Messages[0].Key != page.Messages[0].Key,
            "Folder identity overlaps with INBOX.");
        var archivedBody = await receiver.GetBodyAsync(account, archive.Messages[0].Key, deadline.Token);
        Require(archivedBody.PlainText == body.PlainText &&
            imap.MailCommands.Contains("EXAMINE \"Archive/&U,BTFw-&-\""), "Named mailbox read/encoding failed.");
        Require(imap.AuthenticatedOverTls && !imap.Commands.Any(command => command is "SELECT" or "STORE" or "EXPUNGE"), "IMAP TLS/read-only invariant failed.");
        var flagCache = new SqliteMailCache(Path.Combine(files.Path, "mail.db"));
        await flagCache.SaveSummariesAsync(archive.Mailbox!, archive.Messages, DateTimeOffset.UtcNow, deadline.Token);
        var readChange = await receiver.SetReadStateAsync(account, archive.Messages[0].Key, true, deadline.Token);
        Require(readChange.Status == MailFlagChangeStatus.Confirmed && imap.ReadStateStoreCount == 1,
            "Explicit read-state mutation was not confirmed.");
        await flagCache.UpdateReadStateAsync(readChange.Key, true, deadline.Token);
        var unreadChange = await receiver.SetReadStateAsync(account, archive.Messages[0].Key, false, deadline.Token);
        Require(unreadChange.Status == MailFlagChangeStatus.Confirmed && imap.ReadStateStoreCount == 2 &&
            !imap.Messages[0].IsRead && !imap.Commands.Any(command => command is "EXPUNGE" or "CLOSE"),
            "Explicit unread-state mutation/scope invariant failed.");
        await flagCache.UpdateReadStateAsync(unreadChange.Key, false, deadline.Token);
        var star = await receiver.SetStarStateAsync(account, archive.Messages[0].Key, true, deadline.Token);
        Require(star.Status == MailFlagChangeStatus.Confirmed && !imap.Messages[0].IsRead && imap.Messages[0].IsStarred,
            "Explicit star mutation did not preserve read state.");
        await flagCache.UpdateStarStateAsync(star.Key, true, deadline.Token);
        Require((await receiver.SetStarStateAsync(account, archive.Messages[0].Key, false, deadline.Token)).Status == MailFlagChangeStatus.Confirmed &&
            !imap.Messages[0].IsStarred, "Explicit unstar mutation failed.");
        await flagCache.UpdateStarStateAsync(star.Key, false, deadline.Token);
        imap.AdvertiseMove = true; imap.MoveDestinationWireName = "Moved/&U,BTFw-&-";
        imap.FolderListResponse = "* LIST () \"/\" \"Moved/&U,BTFw-&-\"";
        var moved = await receiver.MoveAsync(account, archive.Messages[0].Key, "Moved/台北&", deadline.Token);
        Require(moved.Status == MailMoveStatus.Confirmed && moved.Destination == new MailMessageKey(account.Id, "Moved/台北&", 9, 900) &&
            imap.MoveCount == 1 && imap.Messages.Count == 0 && imap.MovedMessages.Count == 1 &&
            !imap.Commands.Any(command => command is "EXPUNGE" or "CLOSE" or "COPY"), "Verified UID MOVE fixture failed.");
        imap.MoveDestinationWireName = "Archive/&U,BTFw-&-"; imap.MoveDestinationUid = 1000;
        imap.FolderListResponse = "* LIST () NIL \"Moved/&U,BTFw-&-\"\r\n* LIST () NIL \"Archive/&U,BTFw-&-\"";
        var reversed = await receiver.MoveAsync(account, moved.Destination!.Value, "Archive/台北&", deadline.Token);
        Require(reversed.Status == MailMoveStatus.Confirmed && reversed.Destination == new MailMessageKey(account.Id, "Archive/台北&", 7, 1000) &&
            imap.MoveCount == 2 && imap.MovedMessages.Count == 0 && imap.Messages.Single().Uid == 1000,
            "Reverse UID MOVE did not verify a new returned source identity.");
        Require((await receiver.GetBodyAsync(account, reversed.Destination!.Value, deadline.Token)).PlainText == body.PlainText,
            "Reverse UID MOVE did not preserve message content.");
        imap.AdvertiseUidPlus = true;
        imap.Messages.Add(new(1001, "Unrelated pre-deleted fixture", IsDeleted: true));
        var deleted = await receiver.PermanentlyDeleteAsync(account, reversed.Destination!.Value, deadline.Token);
        Require(deleted.Status == MailDeleteStatus.Confirmed && imap.UidExpungeCount == 1 && imap.Messages.Single().Uid == 1001 &&
            !imap.Commands.Any(command => command is "EXPUNGE" or "CLOSE"), "UID-scoped permanent deletion affected unrelated mail.");
        Require((await intents.ReadAsync()).Count == 0 && (await intents.Receipts.ReadAsync()).Count == 3, "Confirmed mutation receipts were not durable before adapter return.");
        var sender = new SmtpMailSender(credentials, (_, certificate, _, _) =>
            certificate?.GetCertHashString() == smtp.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5));
        account = account with { SenderAliases = "alias@example.test", DefaultSenderAddress = "alias@example.test", SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Alias, Native", Signature = "Alias fixture signature", ReplySignature = "" }] };
        byte[] attachmentBytes = new byte[300 * 1024];
        new Random(41).NextBytes(attachmentBytes);
        var draft = MailComposition.Create(account) with
        { To = ["to@example.test"], Bcc = ["private@example.test"], Subject = "Native fixture", PlainText = "Synthetic body", Attachments = [new("Grüße.bin", attachmentBytes)] };
        long uploaded = 0;
        var sent = await sender.SendWithProgressAsync(account with { DisplayName = "Renamed account", SenderIdentities = null }, draft, bytes => uploaded = bytes, deadline.Token);
        Require(sent.Status == SubmissionStatus.Accepted && smtp.AuthenticatedOverTls && smtp.DataCount == 1 && uploaded >= 256 * 1024, "SMTP acceptance/TLS/progress fixture failed.");
        Require(smtp.Sender?.Contains("alias@example.test", StringComparison.Ordinal) == true && smtp.RawMessage?.Contains("alias@example.test", StringComparison.Ordinal) == true, "SMTP alias envelope/header fixture failed.");
        Require(smtp.RawMessage?.Contains("From: \"Alias, Native\" <alias@example.test>", StringComparison.Ordinal) == true &&
            !smtp.RawMessage.Contains("Renamed account", StringComparison.Ordinal), "SMTP pinned sender display name fixture failed.");
        Require(smtp.Recipients.Count == 2 && smtp.RawMessage is not null &&
            !smtp.RawMessage.Contains("Bcc:", StringComparison.OrdinalIgnoreCase), "SMTP private Bcc fixture failed.");
        var submitted = Broiler.Mail.Infrastructure.Mime.MimeParser.Parse(System.Text.Encoding.UTF8.GetBytes(smtp.RawMessage!));
        var part = submitted.Parts.Single(item => item.IsAttachment);
        Require(part.FileName == "Grüße.bin" && part.Data.SequenceEqual(attachmentBytes), "SMTP multipart filename or attachment bytes changed.");
        var copyWriter = new ImapSentCopyWriter(credentials, (_, certificate, _, _) =>
            certificate?.GetCertHashString() == imap.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5));
        var copyStatus = await copyWriter.AppendAsync(account with { SentCopyMode = SentCopyMode.AppendToFolder, SentFolder = "Sent" }, draft, deadline.Token);
        Require(copyStatus == SentCopyState.Saved && imap.AppendCount == 1 && imap.AppendedMessage is not null, "IMAP streamed Sent copy failed.");
        var copied = Broiler.Mail.Infrastructure.Mime.MimeParser.Parse(System.Text.Encoding.UTF8.GetBytes(imap.AppendedMessage!));
        Require(copied.Bcc.SequenceEqual(draft.Bcc) && copied.Parts.Single(item => item.IsAttachment).Data.SequenceEqual(attachmentBytes), "Private Sent copy changed attachment bytes or Bcc.");
        var aliasRefused = await sender.SendAsync(account with { SenderAliases = "" }, draft, deadline.Token);
        Require(aliasRefused.Status == SubmissionStatus.Rejected && smtp.DataCount == 1, "Removed alias reached SMTP submission.");
        var reply = MailComposition.Create(account, new(new(account.Id, "INBOX", 7, 1), "quoted")
            { Composition = new() { From = ["other@example.test"], To = [account.EmailAddress, "ALIAS@example.test", "friend@example.test"] } }, CompositionKind.ReplyAll, "alias@example.test");
        Require(reply.To.SequenceEqual(new[] { "other@example.test", "friend@example.test" }), "Reply-all retained own aliases.");
        await using var lost = new LocalImapServer(security) { DropAfterReadStateStore = true };
        lost.Messages.Add(new(1, "Synthetic body"));
        var lostAccount = account with { IncomingServer = account.IncomingServer with { Port = lost.Port } };
        await credentials.WriteAsync(CredentialKey.For(lostAccount, MailProtocol.Imap), LocalImapServer.Password);
        var lostFlagIntents = new MutationIntentJournal(Path.Combine(files.Path, "lost-flag.db"));
        var lostReceiver = new ImapMailReceiver(credentials, (_, certificate, _, _) =>
            certificate?.GetCertHashString() == lost.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5), lostFlagIntents);
        var unknown = await lostReceiver.SetStarStateAsync(lostAccount, new(account.Id, "INBOX", 7, 1), true, deadline.Token);
        Require(unknown.Status == MailFlagChangeStatus.Unknown && lost.ReadStateStoreCount == 1,
            "Lost read-state acknowledgement was retried or misreported.");
        Require((await lostFlagIntents.ReadAsync()).Single().Flag == "flagged" && (await new SqliteMailCache(Path.Combine(files.Path, "lost-flag.db")).RepairAsync()).Pending == 1 && lost.ReadStateStoreCount == 1,
            "Lost star intent was not retained without replay.");
        await using var lostMove = new LocalImapServer(security)
        { AdvertiseMove = true, DropAfterMove = true, FolderListResponse = "* LIST () NIL Archive" };
        lostMove.Messages.Add(new(1, "Synthetic move body"));
        var moveAccount = account with { IncomingServer = account.IncomingServer with { Port = lostMove.Port } };
        await credentials.WriteAsync(CredentialKey.For(moveAccount, MailProtocol.Imap), LocalImapServer.Password);
        var lostIntents = new MutationIntentJournal(Path.Combine(files.Path, "lost.db"));
        var moveReceiver = new ImapMailReceiver(credentials, (_, certificate, _, _) =>
            certificate?.GetCertHashString() == lostMove.Certificate.GetCertHashString(), TimeSpan.FromSeconds(5), lostIntents);
        var unknownMove = await moveReceiver.MoveAsync(moveAccount, new(account.Id, "INBOX", 7, 1), "Archive", deadline.Token);
        Require(unknownMove.Status == MailMoveStatus.Unknown && lostMove.MoveCount == 1 && lostMove.MovedMessages.Count == 1,
            "Lost UID MOVE acknowledgement was retried or misreported.");
        Require((await lostIntents.ReadAsync()).Count == 1 && (await new SqliteMailCache(Path.Combine(files.Path, "lost.db")).RepairAsync()).Pending == 1 && lostMove.MoveCount == 1,
            "Uncertain MOVE intent did not survive reopening without replay.");
        Console.WriteLine($"PASS {security}: IMAP discovery/read + original .eml/extended attachment filename + flags + verified UID MOVE/reverse/delete/unknown + SMTP accepted/private-Bcc over pinned loopback TLS.");
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

internal sealed class FixtureCredentials : ICredentialStore
{
    private readonly Dictionary<CredentialKey, string> _values = [];
    public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); _values[key] = secret; return Task.CompletedTask; }
    public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(_values.GetValueOrDefault(key)); }
    public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); _values.Remove(key); return Task.CompletedTask; }
}

internal sealed class IntentProbeDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("Broiler-Mail-IntentProbe-").FullName;
    public void Dispose()
    {
        string full = System.IO.Path.GetFullPath(Path);
        string temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(full).StartsWith("Broiler-Mail-IntentProbe-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture cleanup path.");
        Directory.Delete(full, recursive: true);
    }
}
