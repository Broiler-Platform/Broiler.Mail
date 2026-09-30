// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           4
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  7/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;

namespace Broiler.Mail.Infrastructure.Mail;

// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=7; Fingerprint=6C5483
// Broiler-Falsified-If: the IMAP password is sent to the Sent-copy server over a connection whose certificate failed platform validation or that never upgraded to TLS
// Broiler-Human:        PENDING
public sealed class ImapSentCopyWriter : ISentCopyWriter
{
    private readonly ICredentialStore _credentials;
    private readonly Func<ImapClient> _createClient;
    private readonly TimeSpan _timeout;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=25C087
    // Broiler-Falsified-If: a client made by the factory this constructor installs accepts a server certificate that fails platform chain validation
    // Broiler-Human:        PENDING
    public ImapSentCopyWriter(ICredentialStore credentials) : this(credentials, () => new ImapClient(), TimeSpan.FromSeconds(20)) { }
    internal ImapSentCopyWriter(ICredentialStore credentials, Func<ImapClient> createClient, TimeSpan timeout)
    { _credentials = credentials; _createClient = createClient; _timeout = timeout; }

    // Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=7; Fingerprint=4B2DD6
    // Broiler-Falsified-If: an APPEND interrupted after it was sent is reported as Failed rather than Unknown
    // Broiler-Human:        PENDING
    public async Task<SentCopyState> AppendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
    {
        bool appending = false, saved = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            ConfigurationValidator.Validate(account);
            MailComposition.ValidateDraft(account, draft);
            if (account.SentCopyMode != SentCopyMode.AppendToFolder || account.IncomingServer.Authentication != AuthenticationMethod.Password)
                return SentCopyState.Failed;
            using var message = OutgoingMessageFactory.Create(account, draft, includeBcc: true);
            string? secret = await _credentials.ReadAsync(CredentialKey.For(account, MailProtocol.Imap), deadline.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(secret)) return SentCopyState.Failed;
            using var client = _createClient();
            client.Timeout = (int)_timeout.TotalMilliseconds;
            var server = account.IncomingServer;
            await client.ConnectAsync(server.Host, server.Port,
                server.Security == TransportSecurity.Tls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, deadline.Token).ConfigureAwait(false);
            await client.AuthenticateAsync(server.UserName, secret, deadline.Token).ConfigureAwait(false);
            var folder = await client.GetFolderAsync(account.SentFolder!, deadline.Token).ConfigureAwait(false);
            if (!folder.Exists || (folder.Attributes & FolderAttributes.NoSelect) != 0) return SentCopyState.Failed;
            deadline.Token.ThrowIfCancellationRequested();
            appending = true;
            // APPEND needs no SELECT or CREATE. Preserve Bcc in this private copy only.
            var options = FormatOptions.Default.Clone();
            options.HiddenHeaders.Remove(HeaderId.Bcc);
            await folder.AppendAsync(options, message, MessageFlags.Seen, message.Date, deadline.Token).ConfigureAwait(false);
            saved = true;
            return SentCopyState.Saved; // A missing APPENDUID is still success after tagged OK.
        }
        catch (ImapCommandException) when (!saved) { return SentCopyState.Failed; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (saved) return SentCopyState.Saved;
            // A lost APPEND acknowledgement can mean the copy exists. Never retry blindly.
            return appending ? SentCopyState.Unknown : SentCopyState.Failed;
        }
    }
}
