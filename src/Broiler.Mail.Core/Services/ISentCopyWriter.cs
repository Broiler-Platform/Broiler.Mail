using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>One append attempt after durable SMTP acceptance. Never sends or retries mail.</summary>
public interface ISentCopyWriter
{
    Task<SentCopyState> AppendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default);
}
