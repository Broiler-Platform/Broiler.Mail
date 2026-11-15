// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        0/5
// Exempt:           0
// Human-reviewed:   0/5
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit local navigation preferences; never starts server synchronization.</summary>
public interface IAccountNavigationStore
{
    Task<AccountNavigation> LoadAsync(AccountId account, CancellationToken token = default);
    Task SaveAsync(AccountNavigation navigation, CancellationToken token = default);
}
public sealed record AccountNavigation(AccountId AccountId, string StartMailboxId, IReadOnlyList<string> PinnedMailboxIds);
public sealed record AccountNavigationState(IReadOnlyList<AccountNavigation> Accounts);
