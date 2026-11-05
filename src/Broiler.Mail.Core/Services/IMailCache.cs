using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>
/// Durable, read-only-server mail cache contract for M1.
/// Every call is scoped to an account/mailbox epoch and must honor cancellation.
/// </summary>
public interface IMailCache
{
    Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account, string mailboxId,
        CancellationToken cancellationToken = default);
    /// <summary>Read at most maximumCount summaries after afterUid (exclusive), ordered by descending UID.
    /// Null afterUid starts at the newest cached message. Reject nonpositive bounds and zero cursors.</summary>
    Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox, int maximumCount,
        uint? afterUid = null, CancellationToken cancellationToken = default);

    /// <summary>Atomically upsert a bounded batch and its successful receive timestamp.
    /// Reject any key outside mailbox before writing; do not infer deletions from a partial batch.
    /// A failed/cancelled transaction must not advance the timestamp. Epoch changes require a new scope.</summary>
    Task SaveSummariesAsync(MailboxKey mailbox, IReadOnlyList<MailMessageSummary> messages,
        DateTimeOffset receivedAt, CancellationToken cancellationToken = default);

    /// <summary>Return null for an uncached body. Reject zero UIDs and mismatched scope.
    /// Never fetch from the network; completeness/truncation is retained in the returned body.</summary>
    Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox, uint uid,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically save a decoded body and its bounded embedded resources.
    /// Reject a body key outside mailbox. Completion means durable local availability;
    /// retained source MIME and attachment streaming require separate contracts in M4.</summary>
    Task SaveBodyAsync(MailboxKey mailbox, MailMessageBody body,
        CancellationToken cancellationToken = default);
}

public sealed record CachedMailbox(MailboxKey Key, DateTimeOffset ReceivedAt);
