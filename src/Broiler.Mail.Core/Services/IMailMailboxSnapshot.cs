using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit read-only full-folder header observation. No mutation or automatic synchronization.</summary>
public interface IMailMailboxSnapshotReceiver
{
    Task<MailMailboxSnapshot> GetMailboxSnapshotAsync(AccountProfile account, string mailboxId, CancellationToken token = default);
}
public sealed record MailMailboxSnapshot(MailboxKey Mailbox, IReadOnlyList<MailMessageSummary> Messages, DateTimeOffset ObservedAt)
{
    public const int MaximumMessages = 4096;
}
/// <summary>Atomic persistence of a verified full-header observation. Absent saved messages/bodies are retained.</summary>
public interface IMailMailboxSnapshotCache
{
    Task<MailMailboxCheckpoint> ApplyMailboxSnapshotAsync(MailMailboxSnapshot snapshot, CancellationToken token = default);
    Task<MailMailboxCheckpoint?> ReadMailboxCheckpointAsync(AccountId account, string mailboxId, CancellationToken token = default);
}
/// <summary>Membership as observed at a successful complete census, not a live server guarantee or a QRESYNC cursor.</summary>
public sealed record MailMailboxCheckpoint(MailboxKey Mailbox, DateTimeOffset ObservedAt, int ServerMessages, string UidSetSha256, long RetainedAbsentMessages);
public sealed class MailMailboxSnapshotUnavailableException(string message) : InvalidOperationException(message);
