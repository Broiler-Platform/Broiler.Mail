using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public sealed record MailFlagChangeResult(MailMessageKey Key, bool IsSet, MailFlagChangeStatus Status, string Explanation);

public interface IMailStarStateWriter
{
    Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred,
        CancellationToken cancellationToken = default);
}

public interface IMailStarStateCache
{
    Task UpdateStarStateAsync(MailMessageKey key, bool isStarred, CancellationToken cancellationToken = default);
}
