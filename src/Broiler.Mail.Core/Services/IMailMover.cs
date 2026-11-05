using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public enum MailMoveStatus { Confirmed, NotApplied, Unknown }

/// <summary>A confirmed move includes the verified new mailbox/epoch/UID identity.</summary>
public sealed record MailMoveResult(MailMessageKey Source, string DestinationMailboxId,
    MailMoveStatus Status, MailMessageKey? Destination, string Explanation);

/// <summary>Explicit single-message moves. Unknown outcomes must never be queued for automatic retry.</summary>
public interface IMailMover
{
    Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId,
        CancellationToken cancellationToken = default);
}

/// <summary>Atomically rekey cached mail only after a verified server move. Local retries never contact IMAP.</summary>
public interface IMailMoveCache
{
    Task ReconcileMoveAsync(MailMessageKey source, MailMessageKey destination, CancellationToken cancellationToken = default);
}
