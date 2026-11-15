// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   5
// Annotated:        4/5
// Exempt:           0
// Human-reviewed:   0/5
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  2/10 max
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>OS-protected secrets bound to account, protocol and server identity. Never log values.</summary>
// Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=ACF4A7
// Hairs-Falsified-If: a secret saved for one host, port, user name, security mode or authentication method is returned after any of them changes
// Hairs-Human:        PENDING
public interface ICredentialStore
{
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=9746DD
    // Hairs-Falsified-If: ReadAsync returns the secret held in the account and protocol slot when the stored binding differs from the key's Binding
    // Hairs-Human:        PENDING
    Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default);
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=A1C5B7
    // Hairs-Falsified-If: after WriteAsync, ReadAsync with the key of the previous connection details still returns the earlier secret
    // Hairs-Human:        PENDING
    Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default);
    // Removes this account/protocol slot, including a credential bound to older connection details.
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=1D0FA6
    // Hairs-Falsified-If: DeleteAsync leaves readable a secret for the same account and protocol that was saved under older connection details
    // Hairs-Human:        PENDING
    Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default);
    /// <summary>
    /// Whether a secret is stored under exactly this key; only its presence is reported. The default reads the
    /// secret and drops it at once. A store that can answer without one, such as demo mode, says so directly.
    /// </summary>
    async Task<bool> ContainsAsync(CredentialKey key, CancellationToken cancellationToken = default) =>
        await ReadAsync(key, cancellationToken).ConfigureAwait(false) is not null;
}
