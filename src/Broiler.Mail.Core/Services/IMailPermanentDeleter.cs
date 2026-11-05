using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public enum MailDeleteStatus { Confirmed, NotApplied, Unknown }
public sealed record MailDeleteResult(MailMessageKey Key, MailDeleteStatus Status, string Explanation);

/// <summary>Call only for an explicitly confirmed single-message permanent deletion. Never replay Unknown automatically.</summary>
public interface IMailPermanentDeleter
{
    Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default);
}

/// <summary>Remove only the confirmed deleted identity from the active cache, preserving mailbox timestamps and all other rows.</summary>
public interface IMailDeleteCache
{
    Task ReconcilePermanentDeleteAsync(MailMessageKey key, CancellationToken cancellationToken = default);
}
