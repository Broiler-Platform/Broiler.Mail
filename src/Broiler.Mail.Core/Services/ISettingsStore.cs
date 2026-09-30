// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           0
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  3/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Core.Services;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=DBC5DD
// Broiler-Falsified-If: a corrupt or future-version settings file is replaced by a save instead of being preserved and reported
// Broiler-Human:        PENDING
public interface ISettingsStore
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=45B6DC
    // Broiler-Falsified-If: a settings file with an unknown member or an integer theme value is returned instead of reported invalid
    // Broiler-Human:        PENDING
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=DED35E
    // Broiler-Falsified-If: a save interrupted before the replacement completes leaves the existing settings file truncated or partly written
    // Broiler-Human:        PENDING
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
