// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   7
// Annotated:        6/7
// Exempt:           4
// Human-reviewed:   0/7
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  3/10 max
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Net.Security;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Protocols.Smtp;

namespace Broiler.Mail.Infrastructure.Mail;

// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=3; Fingerprint=8FC6AB
// Broiler-Falsified-If: an SMTP server configured for STARTTLS that does not offer it receives the AUTH password over the unencrypted connection
// Broiler-Human:        PENDING
public sealed class SmtpMailSender : IMailSender
{
    static SmtpMailSender()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    private readonly ICredentialStore _credentials;
    private readonly Func<SmtpProtocolClient> _clientFactory;
    private readonly TimeSpan _timeout;
    public bool IsAvailable => true;

    // Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=None; Security=High; Resources=1; Fingerprint=B5D1E4
    // Broiler-Falsified-If: the client produced by the public constructor accepts a server certificate that system certificate validation rejects
    // Broiler-Human:        PENDING
    public SmtpMailSender(ICredentialStore credentials)
        : this(credentials, (RemoteCertificateValidationCallback?)null, TimeSpan.FromSeconds(20)) { }

    // Only tests replace certificate validation and shorten the deadline.
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=B7C6FC
    // Broiler-Falsified-If: an assembly other than the component's test project can construct the sender with its own client factory or deadline
    // Broiler-Human:        PENDING
    internal SmtpMailSender(ICredentialStore credentials, RemoteCertificateValidationCallback? certValidator, TimeSpan timeout)
        : this(credentials, () => new SmtpProtocolClient(certValidator), timeout) { }

    internal SmtpMailSender(ICredentialStore credentials, Func<SmtpProtocolClient> clientFactory, TimeSpan timeout)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _timeout = timeout;
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=3; Fingerprint=86DE37
    // Broiler-Falsified-If: a disconnect, timeout or cancellation after client.SendAsync has begun is reported as Rejected instead of Unknown
    // Broiler-Human:        PENDING
    public async Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
    {
        bool submissionStarted = false;
        bool accepted = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            ConfigurationValidator.Validate(account);
            MailComposition.ValidateDraft(account, draft);
            var server = account.OutgoingServer;
            if (server is null) return Rejected("Configure and save the SMTP server first.");
            if (server.Authentication != AuthenticationMethod.Password)
                return Rejected("SMTP OAuth sign-in is not implemented. Use password or app-password authentication.");
            byte[] messageBytes = MimeSerializer.Serialize(account, draft, includeBcc: false);
            var recipients = draft.To.Concat(draft.Cc).Concat(draft.Bcc).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string? secret = await _credentials.ReadAsync(CredentialKey.For(account, MailProtocol.Smtp), deadline.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(secret)) return Rejected("Save an SMTP password for the current outgoing server in Account first.");

            await using var client = _clientFactory();
            await client.ConnectAsync(server.Host, server.Port, server.Security, deadline.Token).ConfigureAwait(false);
            await client.AuthenticateAsync(server.UserName, secret, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            submissionStarted = true;

            // Bcc exists only in the explicit envelope. No Bcc header is ever constructed.
            await client.SendMailAsync(draft.FromAddress, recipients, messageBytes, deadline.Token).ConfigureAwait(false);
            accepted = true;
            // No QUIT round trip after acceptance: a later connection failure must not suggest a retry.
            return Accepted();
        }
        catch (SmtpCommandException error) when (!accepted && error.ErrorCode is SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.MessageNotAccepted)
        { return Rejected("The SMTP server rejected the sender, a recipient, or the message. Check the account and recipients before retrying."); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (accepted) return Accepted();
            // Never expose server responses or credential-store diagnostics. Once SendAsync starts,
            // a disconnect, timeout, or cancellation cannot prove that acceptance did not occur.
            if (submissionStarted) return new(SubmissionStatus.Unknown, "SMTP outcome unknown. Check with the recipient/provider before composing another copy. Do not automatically resend.");
            return Rejected(error is OperationCanceledException
                ? "SMTP submission was canceled or timed out before sending. Your draft is retained."
                : "SMTP submission did not start. Check the saved account, SMTP password, connection security, and draft fields.");
        }
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=4B9B2F
    // Broiler-Human:        PENDING
    private static SendResult Rejected(string message) => new(SubmissionStatus.Rejected, message);
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=D25E4C
    // Broiler-Human:        PENDING
    private static SendResult Accepted() => new(SubmissionStatus.Accepted,
        "Accepted by the SMTP server. Delivery is not guaranteed. See the separate Sent-copy status.");
}
