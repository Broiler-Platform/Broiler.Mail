// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit maintenance of downloaded mail whose account is no longer configured.</summary>
public interface IOrphanDownloadedMailStorage
{
    Task<OrphanDownloadedMailPage> FindOrphanDownloadedMailAsync(CancellationToken token = default);
    Task<AccountMailCleanupResult> ClearOrphanDownloadedMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated = null, CancellationToken token = default);
}
public sealed record OrphanDownloadedMailPage(IReadOnlyList<AccountDownloadedMailInfo> Accounts, bool HasMore);
