// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        5/5
// Exempt:           1
// Human-reviewed:   0/5
// IP risk:          Low
// Security risk:    High
// Criteria:         5/5
// Resource impact:  4/10 max
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Version 1 single-account persistence; profiles never contain credentials.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=DFAA50
// Broiler-Falsified-If: a load or save leaves the accounts file holding more than one profile, or a profile that ConfigurationValidator rejects
// Broiler-Human:        PENDING
public sealed class JsonAccountStore(string path) : IAccountStore
{
    private readonly JsonConfigurationFile<AccountProfile[]> _file = new(path, () => [], ValidateAccounts, ConfigurationJsonContext.Storage.Accounts);

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=None; Security=High; Resources=4; Fingerprint=13CB12
    // Broiler-Falsified-If: a corrupt or oversized accounts file loads as an empty account list instead of throwing InvalidDataException
    // Broiler-Human:        PENDING
    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
        await _file.ReadAsync(cancellationToken).ConfigureAwait(false);

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=446616
    // Broiler-Falsified-If: saving a profile whose Id differs from the stored account replaces that account instead of throwing
    // Broiler-Human:        PENDING
    public Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(profile);
        return _file.UpdateAsync(existing =>
        {
            if (existing.Length > 0 && existing[0].Id != profile.Id)
                throw new InvalidOperationException("Version 1 supports one account. Reload the saved account before editing it.");
            return [profile];
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=4E1969
    // Broiler-Falsified-If: removing an AccountId that is not stored changes or deletes the stored profile
    // Broiler-Human:        PENDING
    public Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
        _file.UpdateAsync(existing => existing.Where(profile => profile.Id != accountId).ToArray(), cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=2; Fingerprint=795E38
    // Broiler-Falsified-If: an accounts file with two profiles, or with a profile that ConfigurationValidator rejects, passes without an exception
    // Broiler-Human:        PENDING
    private static void ValidateAccounts(AccountProfile[] profiles)
    {
        if (profiles.Length > 1)
            throw new InvalidDataException("This version supports only one saved account. The file has not been changed.");
        foreach (var profile in profiles)
            ConfigurationValidator.Validate(profile);
    }
}
