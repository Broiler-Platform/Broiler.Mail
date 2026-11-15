// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   7
// Annotated:        0/7
// Exempt:           0
// Human-reviewed:   0/7
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Ordered, non-secret account configuration with a durable selected identity.</summary>
public sealed record AccountCollectionState(AccountProfile[] Accounts, AccountId? SelectedAccountId);

/// <summary>Storage only. UI removal must separately resolve drafts, secrets and local mail.</summary>
public interface IAccountCollectionStore : IAccountStore
{
    Task<AccountCollectionState> LoadStateAsync(CancellationToken cancellationToken = default);
    Task SelectAsync(AccountId accountId, CancellationToken cancellationToken = default);
    Task SaveAndSelectAsync(AccountProfile profile, CancellationToken cancellationToken = default);
    Task RemoveIfUnchangedAsync(AccountProfile expected, CancellationToken cancellationToken = default);
    Task ReorderAsync(IReadOnlyList<AccountId> accountIds, CancellationToken cancellationToken = default);
}
