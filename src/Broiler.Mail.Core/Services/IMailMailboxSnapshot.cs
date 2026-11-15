// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   9
// Annotated:        0/9
// Exempt:           0
// Human-reviewed:   0/9
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

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
