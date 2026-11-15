// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Recovery-backed configuration removal. Cached/server mail is never deleted.</summary>
public interface IAccountRemovalService
{
    Task RemoveAsync(AccountProfile expected, bool forgetPasswords, Action<string> backupCreated,
        CancellationToken cancellationToken = default);
}
