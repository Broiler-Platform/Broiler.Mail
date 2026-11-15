// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   7
// Annotated:        0/7
// Exempt:           1
// Human-reviewed:   0/7
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Services;

/// <summary>Local repair of already-confirmed server changes. Never repeats a network mutation.</summary>
public interface IMailCacheRecovery
{
    int PendingRecoveryCount { get; }
    Task<int> GetPendingRecoveryCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MailUncertainAction>> ReadUncertainActionsAsync(CancellationToken cancellationToken = default);
    Task DismissUncertainActionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MailCacheRecoveryReport> RepairAsync(CancellationToken cancellationToken = default);
}
public sealed record MailCacheRecoveryReport(int Repaired, int Pending);

public sealed record MailUncertainAction(Guid Id, Broiler.Mail.Core.Messages.MailMessageKey Source, string? DestinationMailbox, string Description);
