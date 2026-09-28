using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Version 2 seam. Implementations must check draft/account identity before submission.</summary>
public interface IMailSender
{
    Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default);
}
