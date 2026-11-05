// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   15
// Annotated:        13/15
// Exempt:           4
// Human-reviewed:   0/15
// IP risk:          Low
// Security risk:    High
// Criteria:         12/11
// Resource impact:  7/10 max
// Unverified:       15
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Net.Security;
using Broiler.Mail.Infrastructure.Persistence;
using System.Net.Sockets;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Protocols.Imap;
using Broiler.Mail.Infrastructure.Protocols.Smtp;

namespace Broiler.Mail.Infrastructure.Mail;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=C55898
// Broiler-Falsified-If: the IMAP password is sent over a connection whose server certificate failed platform validation or that never upgraded to TLS
// Broiler-Human:        PENDING
public sealed partial class ImapMailReceiver : IMailFolderReceiver, IMailReadStateWriter, IMailStarStateWriter, IMailMover, IMailPermanentDeleter, IMailSourceReceiver, IStreamingMailSourceReceiver, IMailMailboxSnapshotReceiver
{
    static ImapMailReceiver()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=High; Resources=0; Fingerprint=431894
    // Broiler-Falsified-If: GetInboxAsync accepts a maximumCount of 51 and fetches more than 50 envelopes in one request
    // Broiler-Human:        PENDING
    public const int MaximumPageSize = 50;
    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=High; Resources=0; Fingerprint=3C945E
    // Broiler-Falsified-If: a message body of more than 2 MiB is transferred in full and passed to the MIME decoder
    // Broiler-Human:        PENDING
    public const int MaximumMessageBytes = IStreamingMessageSourceExports.MaximumBytes;
    private readonly ICredentialStore _credentials;
    private readonly Func<ImapProtocolClient> _clientFactory;
    private readonly TimeSpan _timeout;
    private readonly MutationIntentJournal? _intents;

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=1; Fingerprint=8F6932
    // Broiler-Falsified-If: a client made by the factory this constructor installs accepts a server certificate that fails platform chain validation
    // Broiler-Human:        PENDING
    public ImapMailReceiver(ICredentialStore credentials, MutationIntentJournal? intents = null) : this(credentials, (RemoteCertificateValidationCallback?)null, TimeSpan.FromSeconds(20), intents) { }

    // Test-only seam for a fixture certificate and a short deadline; production uses platform certificate validation.
    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=1; Fingerprint=0F261A
    // Broiler-Falsified-If: code outside the test assembly reaches this constructor with a client factory that replaces certificate validation
    // Broiler-Human:        PENDING
    internal ImapMailReceiver(ICredentialStore credentials, RemoteCertificateValidationCallback? certValidator, TimeSpan timeout, MutationIntentJournal? intents = null)
        : this(credentials, () => new ImapProtocolClient(certValidator), timeout, intents) { }

    internal ImapMailReceiver(ICredentialStore credentials, Func<ImapProtocolClient> clientFactory, TimeSpan timeout, MutationIntentJournal? intents = null)
    {
        _intents = intents;
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _timeout = timeout;
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=705856
    // Broiler-Falsified-If: a connection test selects or examines a mailbox, or fetches a message, after authenticating
    // Broiler-Human:        PENDING
    public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
        WithConnectionAsync(account, (_, _) => Task.FromResult(true), cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=41FD65
    // Broiler-Falsified-If: an account configured for STARTTLS authenticates in plaintext when the server does not advertise STARTTLS
    // Broiler-Human:        PENDING
    private async Task<T> WithConnectionAsync<T>(AccountProfile account,
        Func<ImapProtocolClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ConfigurationValidator.Validate(account);
        cancellationToken.ThrowIfCancellationRequested();
        if (!account.IsEnabled) throw new MailConnectionException("Enable the account before connecting.");
        var server = account.IncomingServer;
        if (server.Authentication != AuthenticationMethod.Password)
            throw new MailConnectionException("OAuth sign-in is not implemented. Use an account that supports password or app-password authentication.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            string? secret = await _credentials.ReadAsync(CredentialKey.For(account, MailProtocol.Imap), deadline.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(secret))
                throw new MailConnectionException("No password is saved for these connection details. Save a password in the Account tab.");
            await using var client = _clientFactory();
            await client.ConnectAsync(server.Host, server.Port, server.Security, deadline.Token).ConfigureAwait(false);
            await client.AuthenticateAsync(server.UserName, secret, deadline.Token).ConfigureAwait(false);
            return await operation(client, deadline.Token).ConfigureAwait(false);
        }
        // TLS stream reads can surface deadline cancellation as an I/O error instead of OCE.
        catch (Exception error) when (error is IOException or SocketException && (deadline.IsCancellationRequested || IsTimeout(error)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new MailConnectionException("The mail operation timed out. Check the server address, port, and network, then retry.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new MailConnectionException("The mail operation timed out. Check the server address, port, and network, then retry."); }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new MailConnectionException("The mail operation timed out. Check the server address, port, and network, then retry.");
        }
        catch (TlsHandshakeException)
        { throw new MailConnectionException("TLS verification failed. Check the server name, certificate, and TLS port. Certificate errors cannot be bypassed."); }
        catch (ImapAuthenticationException)
        { throw new MailConnectionException("Authentication was rejected. Check the username and password, or the provider's app-password requirements."); }
        catch (SocketException)
        { throw new MailConnectionException("The mail server could not be reached. Check its address, port, and your network."); }
        catch (NotSupportedException)
        { throw new MailConnectionException("The server does not support the required TLS or authentication mode. An encrypted connection is required."); }
        catch (ImapCommandException)
        { throw new MailConnectionException("The server rejected an IMAP command. Try receiving mail again."); }
        catch (ImapProtocolException)
        { throw new MailConnectionException("The server returned an invalid IMAP response."); }
        catch (ImapMessageNotFoundException)
        { throw new MailConnectionException("This message is no longer available. Receive mail again to update the inbox."); }
        catch (FormatException)
        { throw new MailConnectionException("The message could not be decoded. It may contain malformed mail data."); }
        catch (IOException)
        { throw new MailConnectionException("The connection or protected credential could not be read. Check the network and try saving the password again."); }
    }

    private static bool IsTimeout(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is TimeoutException or SocketException { SocketErrorCode: SocketError.TimedOut })
                return true;
        return false;
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=High; Resources=7; Fingerprint=53B5C5
    // Broiler-Falsified-If: a FETCH reply that omits the envelope or UID of a message in the requested range yields a page instead of the inbox-changed error
    // Broiler-Human:        PENDING
    public async Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead,
        CancellationToken cancellationToken = default)
    {
        var result = await SetFlagStateAsync(account, key, ImapMutableFlag.Seen, isRead, cancellationToken).ConfigureAwait(false);
        return new(result.Key, result.IsSet, result.Status, result.Explanation);
    }

    public Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred,
        CancellationToken cancellationToken = default) => SetFlagStateAsync(account, key, ImapMutableFlag.Flagged, isStarred, cancellationToken);

    private async Task<MailFlagChangeResult> SetFlagStateAsync(AccountProfile account, MailMessageKey key, ImapMutableFlag flag, bool isSet, CancellationToken cancellationToken)
    {
        if (_intents is null) return await SetFlagCoreAsync(account, key, flag, isSet, cancellationToken).ConfigureAwait(false);
        string name = flag switch { ImapMutableFlag.Seen => "seen", ImapMutableFlag.Flagged => "flagged", _ => throw new ArgumentOutOfRangeException(nameof(flag)) };
        using var active = _intents.Enter(key);
        var id = await _intents.BeginAsync(key, null, cancellationToken, name, isSet).ConfigureAwait(false);
        var result = await SetFlagCoreAsync(account, key, flag, isSet, cancellationToken).ConfigureAwait(false);
        if (result.Status == MailFlagChangeStatus.Confirmed)
            await _intents.Receipts.RecordFlagAsync(key, name, isSet, id, CancellationToken.None).ConfigureAwait(false);
        if (result.Status != MailFlagChangeStatus.Unknown)
            await _intents.CompleteAsync(id, CancellationToken.None, keepPending: result.Status == MailFlagChangeStatus.Confirmed).ConfigureAwait(false);
        return result;
    }

    private async Task<MailFlagChangeResult> SetFlagCoreAsync(AccountProfile account, MailMessageKey key, ImapMutableFlag flag, bool isSet,
        CancellationToken cancellationToken = default)
    {
        if (key.AccountId != account.Id || key.Uid == 0 || key.UidValidity == 0 || ImapMailboxName.Validate(key.MailboxId) != key.MailboxId)
            throw new ArgumentException("The message identity must belong to this account and canonical mailbox.", nameof(key));
        string stateName = flag == ImapMutableFlag.Seen ? "read state" : "starred state";
        bool attempted = false;
        MailFlagChangeResult Outcome(MailFlagChangeStatus status, string explanation) => new(key, isSet, status, explanation);
        try
        {
            return await WithConnectionAsync(account, async (client, token) =>
            {
                var selected = await client.SelectForFlagChangeAsync(key.MailboxId, flag, token).ConfigureAwait(false);
                if (selected.UidValidity != key.UidValidity)
                    return Outcome(MailFlagChangeStatus.NotApplied, $"Mailbox identity changed. Receive mail before changing {stateName}.");
                if (!selected.AllowsFlag)
                    return Outcome(MailFlagChangeStatus.NotApplied, $"The server does not allow permanent {stateName} changes in this mailbox.");
                bool? before = await client.GetFlagStateAsync(key.Uid, flag, token).ConfigureAwait(false);
                if (before is null) return Outcome(MailFlagChangeStatus.NotApplied, "The message is no longer available. Receive mail to refresh.");
                if (before == isSet) return Outcome(MailFlagChangeStatus.Confirmed, $"The server already confirms the requested {stateName}.");
                token.ThrowIfCancellationRequested();
                attempted = true;
                try { await client.StoreFlagStateAsync(key.Uid, flag, isSet, token).ConfigureAwait(false); }
                catch (ImapCommandException)
                { return Outcome(MailFlagChangeStatus.NotApplied, $"The server rejected the {stateName} change. Receive mail to refresh."); }
                bool? after = await client.GetFlagStateAsync(key.Uid, flag, token).ConfigureAwait(false);
                return after == isSet ? Outcome(MailFlagChangeStatus.Confirmed, $"{stateName} confirmed by the server.") :
                    Outcome(MailFlagChangeStatus.Unknown, $"The requested {stateName} could not be verified. Receive mail to check the server; no change is queued for retry.");
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is MailConnectionException or OperationCanceledException or IOException or ArgumentException)
        {
            return attempted ? Outcome(MailFlagChangeStatus.Unknown,
                $"The {stateName} acknowledgement was lost or canceled. The server may have applied it. Receive mail to check; no change is queued for retry.") :
                Outcome(MailFlagChangeStatus.NotApplied, $"{stateName} was not changed. Check the account and connection, then receive mail before trying again.");
        }
    }

    public async Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId, CancellationToken cancellationToken = default)
    {
        if (_intents is null) return await MoveCoreAsync(account, source, destinationMailboxId, cancellationToken).ConfigureAwait(false);
        using var active = _intents.Enter(source);
        var id = await _intents.BeginAsync(source, ImapMailboxName.Validate(destinationMailboxId), cancellationToken).ConfigureAwait(false);
        var result = await MoveCoreAsync(account, source, destinationMailboxId, cancellationToken).ConfigureAwait(false);
        if (result.Status == MailMoveStatus.Confirmed && (result.Destination is not { } target || target.AccountId != source.AccountId || target.MailboxId != ImapMailboxName.Validate(destinationMailboxId) || target.Uid == 0 || target.UidValidity == 0))
            throw new InvalidDataException("The confirmed move mapping is invalid; the intent remains unresolved.");
        if (result.Status == MailMoveStatus.Confirmed)
            await _intents.Receipts.ConfirmIntentAsync(id, source, result.Destination, CancellationToken.None).ConfigureAwait(false);
        if (result.Status != MailMoveStatus.Unknown) await _intents.CompleteAsync(id, CancellationToken.None, keepPending: result.Status == MailMoveStatus.Confirmed).ConfigureAwait(false);
        return result;
    }
    public async Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default)
    {
        if (_intents is null) return await DeleteCoreAsync(account, key, cancellationToken).ConfigureAwait(false);
        using var active = _intents.Enter(key);
        var id = await _intents.BeginAsync(key, null, cancellationToken).ConfigureAwait(false);
        var result = await DeleteCoreAsync(account, key, cancellationToken).ConfigureAwait(false);
        if (result.Status == MailDeleteStatus.Confirmed)
            await _intents.Receipts.ConfirmIntentAsync(id, key, null, CancellationToken.None).ConfigureAwait(false);
        if (result.Status != MailDeleteStatus.Unknown) await _intents.CompleteAsync(id, CancellationToken.None, keepPending: result.Status == MailDeleteStatus.Confirmed).ConfigureAwait(false);
        return result;
    }

    private async Task<MailMoveResult> MoveCoreAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId,
        CancellationToken cancellationToken = default)
    {
        if (source.AccountId != account.Id || source.Uid == 0 || source.UidValidity == 0 ||
            ImapMailboxName.Validate(source.MailboxId) != source.MailboxId)
            throw new ArgumentException("The source identity must belong to the account and canonical mailbox.", nameof(source));
        destinationMailboxId = ImapMailboxName.Validate(destinationMailboxId);
        bool attempted = false;
        MailMoveResult Outcome(MailMoveStatus status, string explanation, MailMessageKey? destination = null) =>
            new(source, destinationMailboxId, status, destination, explanation);
        if (source.MailboxId == destinationMailboxId)
            return Outcome(MailMoveStatus.NotApplied, "Choose a different destination folder.");
        try
        {
            return await WithConnectionAsync(account, async (client, token) =>
            {
                // LIST refreshes post-authentication capabilities and verifies exact destination identity.
                var folders = await client.ListFoldersAsync(token).ConfigureAwait(false);
                if (!client.Capabilities.Contains("MOVE", StringComparer.OrdinalIgnoreCase))
                    return Outcome(MailMoveStatus.NotApplied, "The server does not advertise MOVE. No copy/delete fallback was attempted.");
                if (!folders.Any(folder => folder.MailboxId == destinationMailboxId && folder.IsSelectable))
                    return Outcome(MailMoveStatus.NotApplied, "The destination was not found as a selectable server folder. Refresh folders and check its exact name.");
                var selected = await client.SelectForMoveAsync(source.MailboxId, token).ConfigureAwait(false);
                if (selected.UidValidity != source.UidValidity || !selected.AllowsFlag)
                    return Outcome(MailMoveStatus.NotApplied, "The source mailbox identity changed or is not writable. Receive mail before moving it.");
                if (await client.GetFlagStateAsync(source.Uid, ImapMutableFlag.Seen, token).ConfigureAwait(false) is null)
                    return Outcome(MailMoveStatus.NotApplied, "The source message is no longer available. Receive mail to refresh.");
                token.ThrowIfCancellationRequested();
                attempted = true;
                var receipt = await client.MoveUidAsync(source.Uid, destinationMailboxId, token).ConfigureAwait(false);
                if (!receipt.IsOk || receipt.DestinationValidity is not { } epoch || receipt.DestinationUid is not { } uid)
                    return Outcome(MailMoveStatus.Unknown, "The move was not fully confirmed with a destination UID. Check both folders; no move is queued for retry.");
                if (await client.GetFlagStateAsync(source.Uid, ImapMutableFlag.Seen, token).ConfigureAwait(false) is not null)
                    return Outcome(MailMoveStatus.Unknown, "The source message remains after the move acknowledgement. Check both folders; no move is queued for retry.");
                var destination = await client.ExamineMailboxAsync(destinationMailboxId, token).ConfigureAwait(false);
                if (destination.UidValidity != epoch ||
                    await client.GetFlagStateAsync(uid, ImapMutableFlag.Seen, token).ConfigureAwait(false) is null)
                    return Outcome(MailMoveStatus.Unknown, "The destination identity could not be verified. Check both folders; no move is queued for retry.");
                return Outcome(MailMoveStatus.Confirmed, "Move confirmed in both source and destination mailboxes.",
                    new(account.Id, destinationMailboxId, epoch, uid));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is MailConnectionException or OperationCanceledException or IOException or ArgumentException)
        {
            return attempted ? Outcome(MailMoveStatus.Unknown,
                "The move acknowledgement or verification was lost or canceled. The server may have moved the message. Check both folders; no move is queued for retry.") :
                Outcome(MailMoveStatus.NotApplied, "No move was sent. Check the account, connection and folder identities before trying again.");
        }
    }

    private async Task<MailDeleteResult> DeleteCoreAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default)
    {
        if (key.AccountId != account.Id || key.Uid == 0 || key.UidValidity == 0 || ImapMailboxName.Validate(key.MailboxId) != key.MailboxId)
            throw new ArgumentException("The confirmed message must belong to this account and canonical mailbox.", nameof(key));
        bool attempted = false;
        MailDeleteResult Outcome(MailDeleteStatus status, string explanation) => new(key, status, explanation);
        try
        {
            return await WithConnectionAsync(account, async (client, token) =>
            {
                await client.RefreshMutationCapabilitiesAsync(token).ConfigureAwait(false);
                if (!client.Capabilities.Contains("UIDPLUS", StringComparer.OrdinalIgnoreCase))
                    return Outcome(MailDeleteStatus.NotApplied, "The server does not advertise UIDPLUS. Permanent deletion was not sent; no mailbox-wide fallback is used.");
                var selected = await client.SelectForFlagChangeAsync(key.MailboxId, ImapMutableFlag.Deleted, token).ConfigureAwait(false);
                if (selected.UidValidity != key.UidValidity || !selected.AllowsFlag)
                    return Outcome(MailDeleteStatus.NotApplied, "The mailbox identity changed or deletion is not permitted. Receive mail before trying again.");
                bool? deleted = await client.GetFlagStateAsync(key.Uid, ImapMutableFlag.Deleted, token).ConfigureAwait(false);
                if (deleted is null) return Outcome(MailDeleteStatus.NotApplied, "The message is no longer present. No deletion was sent; refresh the folder.");
                token.ThrowIfCancellationRequested(); attempted = true;
                if (!deleted.Value)
                {
                    await client.StoreFlagStateAsync(key.Uid, ImapMutableFlag.Deleted, true, token).ConfigureAwait(false);
                    if (await client.GetFlagStateAsync(key.Uid, ImapMutableFlag.Deleted, token).ConfigureAwait(false) != true)
                        return Outcome(MailDeleteStatus.Unknown, "Deletion marking could not be verified. Refresh the folder; no deletion is queued for retry.");
                }
                await client.ExpungeUidAsync(key.Uid, token).ConfigureAwait(false);
                return await client.GetFlagStateAsync(key.Uid, ImapMutableFlag.Deleted, token).ConfigureAwait(false) is null
                    ? Outcome(MailDeleteStatus.Confirmed, "Permanent deletion of the confirmed message was verified on the server.")
                    : Outcome(MailDeleteStatus.Unknown, "The message remains after the deletion acknowledgement. Refresh the folder; no deletion is queued for retry.");
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is MailConnectionException or OperationCanceledException or IOException or ArgumentException)
        {
            return attempted ? Outcome(MailDeleteStatus.Unknown,
                "Deletion was interrupted or not fully acknowledged. The message may be gone or marked for deletion. Refresh the folder; no deletion is queued for retry.") :
                Outcome(MailDeleteStatus.NotApplied, "No deletion was sent. Check the account, connection and message identity before trying again.");
        }
    }

    public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
        WithConnectionAsync(account, (client, token) => client.ListFoldersAsync(token), cancellationToken);

    public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount,
        MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        => GetMailboxAsync(account, "INBOX", maximumCount, older, cancellationToken);

    public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount,
        MailInboxCursor? older = null, CancellationToken cancellationToken = default)
    {
        mailboxId = ImapMailboxName.Validate(mailboxId);
        if (maximumCount is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (older is not null && (older.AccountId != account.Id || older.MailboxId != mailboxId || older.NextIndex < 0 || older.NextIndex >= older.MessageCount))
            throw new ArgumentException("The inbox continuation does not belong to this request.", nameof(older));
        return WithConnectionAsync(account, async (client, token) =>
        {
            var inbox = await client.ExamineMailboxAsync(mailboxId, token).ConfigureAwait(false);
            int count = inbox.Count;
            uint validity = inbox.UidValidity;
            uint? nextUid = inbox.UidNext;
            if (older is not null && (older.UidValidity != validity || older.MessageCount != count || older.UidNext != nextUid))
                throw InboxChanged();
            if (count == 0) return new MailInboxPage([], null) { Mailbox = new(account.Id, mailboxId, validity) };
            int end = older?.NextIndex ?? count - 1;
            int start = Math.Max(0, end - maximumCount + 1);
            var summaries = await client.FetchSummariesAsync(start + 1, end + 1, token, boundedSnapshot: true).ConfigureAwait(false);

            var requested = summaries.Where(item => (item.SequenceNumber - 1) >= start && (item.SequenceNumber - 1) <= end).ToArray();
            if (requested.Length != end - start + 1 || requested.Any(item => item.Uid == 0 || item.Subject is null && item.From is null))
                throw InboxChanged();
            var messages = requested.OrderByDescending(item => item.Uid).Select(item => new MailMessageSummary
            {
                Key = new(account.Id, mailboxId, validity, item.Uid),
                Sender = MessageTextDecoder.Header(item.Sender ?? item.From, "(Unknown sender)"),
                Subject = MessageTextDecoder.Header(item.Subject, "(No subject)"),
                ReceivedAt = item.InternalDate ?? item.EnvelopeDate,
                IsRead = item.IsSeen, IsStarred = item.IsFlagged,
            }).ToArray();
            return new MailInboxPage(messages, start == 0 ? null : new(account.Id, validity, nextUid, count, start - 1) { MailboxId = mailboxId })
            { Mailbox = new(account.Id, mailboxId, validity) };
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=High; Resources=7; Fingerprint=AA9481
    // Broiler-Falsified-If: a message larger than 2 MiB reaches MessageTextDecoder.DecodeAsync instead of being refused with the reading-limit error
    // Broiler-Human:        PENDING
    public async Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default)
    {
        var source = await ReadSourceAsync(account, message, false, cancellationToken).ConfigureAwait(false);
        return MessageTextDecoder.DecodeReceived(message, source.Bytes, cancellationToken);
    }

    public Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey message, CancellationToken token = default) => ReadSourceAsync(account, message, true, token);

    public Task<int> CopySourceAsync(AccountProfile account, MailMessageKey message, Stream destination, int maximumBytes,
        Action<int, int>? progress = null, CancellationToken token = default)
    {
        string mailboxId = SourceMailbox(account, message);
        if (maximumBytes is < 1 or > IStreamingMessageSourceExports.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        return WithConnectionAsync(account, async (client, cancellation) =>
        {
            var mailbox = await client.ExamineMailboxAsync(mailboxId, cancellation).ConfigureAwait(false);
            if (mailbox.UidValidity != message.UidValidity) throw InboxChanged();
            long expected = await client.GetMessageSizeAsync(message.Uid, cancellation).ConfigureAwait(false);
            if (expected < 1 || expected > maximumBytes)
                throw new MailConnectionException("The original is empty or exceeds the available export limit.");
            int lastReport = 0;
            return await client.CopyBodyAsync(message.Uid, 0, (int)expected, destination, cancellation, (transferred, total) =>
            {
                if (transferred != total && transferred - lastReport < 256 * 1024) return;
                lastReport = (int)transferred;
                progress?.Invoke(lastReport, (int)total);
            }, expectedLength: (int)expected).ConfigureAwait(false);
        }, token);
    }

    private static string SourceMailbox(AccountProfile account, MailMessageKey message)
    {
        if (message.AccountId != account.Id || message.Uid == 0 || message.UidValidity == 0)
            throw new ArgumentException("The message does not belong to this account's mailbox.", nameof(message));
        var mailboxId = ImapMailboxName.Validate(message.MailboxId);
        if (mailboxId != message.MailboxId) throw new ArgumentException("Use the canonical INBOX identity.", nameof(message));
        return mailboxId;
    }

    private Task<MailMessageSource> ReadSourceAsync(AccountProfile account, MailMessageKey message, bool verifyComplete, CancellationToken token)
    {
        string mailboxId = SourceMailbox(account, message);
        int maximum = verifyComplete ? MailMessageSource.MaximumBytes : MaximumMessageBytes;
        return WithConnectionAsync(account, async (client, cancellation) =>
        {
            var mailbox = await client.ExamineMailboxAsync(mailboxId, cancellation).ConfigureAwait(false);
            if (mailbox.UidValidity != message.UidValidity) throw InboxChanged();
            long? expected = verifyComplete ? await client.GetMessageSizeAsync(message.Uid, cancellation).ConfigureAwait(false) : null;
            if (expected > maximum) throw MessageTooLarge(maximum);
            using var stream = new MemoryStream(expected is { } length ? (int)length : 0);
            await client.CopyBodyAsync(message.Uid, 0, maximum + 1, stream, cancellation, (transferred, total) =>
            {
                if (transferred > maximum || total > maximum) throw MessageTooLarge(maximum);
            }).ConfigureAwait(false);
            if (expected is not null && stream.Length != expected) throw new MailConnectionException("Original message source was incomplete or changed size; no complete source was accepted. Retry explicitly.");
            cancellation.ThrowIfCancellationRequested(); return new MailMessageSource(message, stream.ToArray());
        }, token);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=90B19F
    // Broiler-Human:        PENDING
    private static MailConnectionException InboxChanged() => new("The inbox changed. Receive mail again before loading more messages.");
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=7B28BA
    // Broiler-Falsified-If: the reading limit stated in the message differs from MaximumMessageBytes
    // Broiler-Human:        PENDING
    private static MailConnectionException MessageTooLarge(int maximum) => new($"This message exceeds the {maximum / (1024 * 1024)} MiB limit (including attachments).");
}
