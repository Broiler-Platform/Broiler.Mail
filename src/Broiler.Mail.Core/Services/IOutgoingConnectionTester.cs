using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>
/// Checks that the saved outgoing server can be signed in to, without sending anything. Kept apart from
/// <see cref="IMailSender"/>, so code that tests a sign-in cannot reach submission at all.
/// </summary>
/// <remarks>
/// An implementation connects to the saved <see cref="AccountProfile.OutgoingServer"/> with its required
/// transport security (implicit TLS, or mandatory STARTTLS before AUTH), authenticates with the SMTP password
/// bound to exactly those server details, and then sends a best-effort QUIT. It never issues MAIL, RCPT, DATA,
/// BDAT, VRFY, EXPN, ETRN, RSET or NOOP, never builds a message, and never retries.
/// Success means the returned task completes; a pass proves TLS and sign-in only, not that the server will accept
/// a sender or recipients, or deliver anything. A failure is a <see cref="MailConnectionException"/> with its
/// <see cref="MailConnectionException.Failure"/> set. Cancellation by the caller at any point before the task
/// completes is an <see cref="OperationCanceledException"/>, even after the sign-in succeeded.
/// </remarks>
public interface IOutgoingConnectionTester
{
    Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default);
}
