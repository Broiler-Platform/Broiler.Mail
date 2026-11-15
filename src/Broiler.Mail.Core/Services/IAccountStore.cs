// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
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

// Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D42B1A
// Hairs-Falsified-If: an implementation writes a password or other secret into the saved account profile data
// Hairs-Human:        PENDING
public interface IAccountStore
{
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D78755
    // Hairs-Falsified-If: a saved accounts file that fails ConfigurationValidator.Validate is returned as profiles instead of raising InvalidDataException
    // Hairs-Human:        PENDING
    Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default);
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=4728EA
    // Hairs-Falsified-If: a save interrupted before the replacement completes leaves the existing accounts file truncated or partly written
    // Hairs-Human:        PENDING
    Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default);
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=F15C59
    // Hairs-Falsified-If: removing an account from a corrupt accounts file replaces that file with an empty list instead of refusing
    // Hairs-Human:        PENDING
    Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default);
}
