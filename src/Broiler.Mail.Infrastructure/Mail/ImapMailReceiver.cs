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
public sealed class ImapMailReceiver : IMailReceiver
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
    public const int MaximumMessageBytes = 2 * 1024 * 1024;
    private readonly ICredentialStore _credentials;
    private readonly Func<ImapProtocolClient> _clientFactory;
    private readonly TimeSpan _timeout;

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=1; Fingerprint=8F6932
    // Broiler-Falsified-If: a client made by the factory this constructor installs accepts a server certificate that fails platform chain validation
    // Broiler-Human:        PENDING
    public ImapMailReceiver(ICredentialStore credentials) : this(credentials, (RemoteCertificateValidationCallback?)null, TimeSpan.FromSeconds(20)) { }

    // Test-only seam for a fixture certificate and a short deadline; production uses platform certificate validation.
    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=1; Fingerprint=0F261A
    // Broiler-Falsified-If: code outside the test assembly reaches this constructor with a client factory that replaces certificate validation
    // Broiler-Human:        PENDING
    internal ImapMailReceiver(ICredentialStore credentials, RemoteCertificateValidationCallback? certValidator, TimeSpan timeout)
        : this(credentials, () => new ImapProtocolClient(certValidator), timeout) { }

    internal ImapMailReceiver(ICredentialStore credentials, Func<ImapProtocolClient> clientFactory, TimeSpan timeout)
    {
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
    public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount,
        MailInboxCursor? older = null, CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (older is not null && (older.AccountId != account.Id || older.NextIndex < 0 || older.NextIndex >= older.MessageCount))
            throw new ArgumentException("The inbox continuation does not belong to this request.", nameof(older));
        return WithConnectionAsync(account, async (client, token) =>
        {
            var inbox = await client.ExamineInboxAsync(token).ConfigureAwait(false);
            int count = inbox.Count;
            uint validity = inbox.UidValidity;
            uint? nextUid = inbox.UidNext;
            if (older is not null && (older.UidValidity != validity || older.MessageCount != count || older.UidNext != nextUid))
                throw InboxChanged();
            if (count == 0) return new MailInboxPage([], null);
            int end = older?.NextIndex ?? count - 1;
            int start = Math.Max(0, end - maximumCount + 1);
            var summaries = await client.FetchSummariesAsync(start + 1, end + 1, token).ConfigureAwait(false);

            var requested = summaries.Where(item => (item.SequenceNumber - 1) >= start && (item.SequenceNumber - 1) <= end).ToArray();
            if (requested.Length != end - start + 1 || requested.Any(item => item.Uid == 0 || item.Subject is null && item.From is null))
                throw InboxChanged();
            var messages = requested.OrderByDescending(item => item.Uid).Select(item => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", validity, item.Uid),
                Sender = MessageTextDecoder.Header(item.Sender ?? item.From, "(Unknown sender)"),
                Subject = MessageTextDecoder.Header(item.Subject, "(No subject)"),
                ReceivedAt = item.InternalDate ?? item.EnvelopeDate,
                IsRead = item.IsSeen,
            }).ToArray();
            return new MailInboxPage(messages, start == 0 ? null : new(account.Id, validity, nextUid, count, start - 1));
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=High; Resources=7; Fingerprint=AA9481
    // Broiler-Falsified-If: a message larger than 2 MiB reaches MessageTextDecoder.DecodeAsync instead of being refused with the reading-limit error
    // Broiler-Human:        PENDING
    public Task<MailMessageBody> GetBodyAsync(
        AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default)
    {
        if (message.AccountId != account.Id || message.MailboxId != "INBOX" || message.Uid == 0 || message.UidValidity == 0)
            throw new ArgumentException("The message does not belong to this account's inbox.", nameof(message));
        return WithConnectionAsync(account, async (client, token) =>
        {
            var inbox = await client.ExamineInboxAsync(token).ConfigureAwait(false);
            if (inbox.UidValidity != message.UidValidity) throw InboxChanged();

            // Partial BODY.PEEK bounds the transfer and leaves server flags unchanged. One extra byte detects overflow.
            using var stream = await client.GetBodyStreamAsync(message.Uid, 0, MaximumMessageBytes + 1, token, (transferred, total) =>
            {
                if (transferred > MaximumMessageBytes || total > MaximumMessageBytes + 1) throw MessageTooLarge();
            }).ConfigureAwait(false);

            if (stream.Length > MaximumMessageBytes) throw MessageTooLarge();
            return await MessageTextDecoder.DecodeAsync(message, stream, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=90B19F
    // Broiler-Human:        PENDING
    private static MailConnectionException InboxChanged() => new("The inbox changed. Receive mail again before loading more messages.");
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=7B28BA
    // Broiler-Falsified-If: the reading limit stated in the message differs from MaximumMessageBytes
    // Broiler-Human:        PENDING
    private static MailConnectionException MessageTooLarge() => new("This message exceeds the version 1 reading limit of 2 MiB (including attachments).");
}
