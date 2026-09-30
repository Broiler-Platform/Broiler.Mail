// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  3/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D42B1A
// Broiler-Falsified-If: an implementation writes a password or other secret into the saved account profile data
// Broiler-Human:        PENDING
public interface IAccountStore
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D78755
    // Broiler-Falsified-If: a saved accounts file that fails ConfigurationValidator.Validate is returned as profiles instead of raising InvalidDataException
    // Broiler-Human:        PENDING
    Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=4728EA
    // Broiler-Falsified-If: a save interrupted before the replacement completes leaves the existing accounts file truncated or partly written
    // Broiler-Human:        PENDING
    Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=F15C59
    // Broiler-Falsified-If: removing an account from a corrupt accounts file replaces that file with an empty list instead of refusing
    // Broiler-Human:        PENDING
    Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default);
}
