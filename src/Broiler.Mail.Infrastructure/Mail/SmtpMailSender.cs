using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Mail;

public sealed class SmtpMailSender(ICredentialStore credentials) : IMailSender
{
    public ICredentialStore Credentials { get; } = credentials ?? throw new ArgumentNullException(nameof(credentials));

    // TODO(v2): Validate recipients/account, serialize MIME, and retain uncertain submission outcomes.
    public Task<SendResult> SendAsync(
        AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Sending mail is not implemented.");
}
