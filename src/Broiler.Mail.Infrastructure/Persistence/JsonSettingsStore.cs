// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           1
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  4/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Infrastructure.Persistence;

// Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=801CCD
// Broiler-Falsified-If: settings that ConfigurationValidator rejects are written to or returned from the settings file
// Broiler-Human:        PENDING
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private readonly JsonConfigurationFile<ApplicationSettings> _file = new(path, () => new(), ConfigurationValidator.Validate, ConfigurationJsonContext.Storage.Settings);

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=None; Security=High; Resources=4; Fingerprint=C2AA06
    // Broiler-Falsified-If: a corrupt or oversized settings file loads as default settings instead of throwing InvalidDataException
    // Broiler-Human:        PENDING
    public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync(cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=7B7A4B
    // Broiler-Falsified-If: settings with an undefined theme or a window size outside the validator's range are written to the settings file
    // Broiler-Human:        PENDING
    public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(settings);
        return _file.UpdateAsync(_ => settings, cancellationToken);
    }
}
