using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public enum MailFlagChangeStatus { Confirmed, NotApplied, Unknown }
public sealed record ReadStateChangeResult(MailMessageKey Key, bool IsRead, MailFlagChangeStatus Status, string Explanation);

/// <summary>Explicit per-message changes only. Unknown outcomes must never be replayed automatically.</summary>
public interface IMailReadStateWriter
{
    Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead,
        CancellationToken cancellationToken = default);
}

/// <summary>Update an existing cached row without changing receive timestamps or creating headers.</summary>
public interface IMailReadStateCache
{
    Task UpdateReadStateAsync(MailMessageKey key, bool isRead, CancellationToken cancellationToken = default);
}
