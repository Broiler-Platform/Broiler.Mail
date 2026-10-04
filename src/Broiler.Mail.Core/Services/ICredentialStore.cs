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
// Resource impact:  2/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>OS-protected secrets bound to account, protocol and server identity. Never log values.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=ACF4A7
// Broiler-Falsified-If: a secret saved for one host, port, user name, security mode or authentication method is returned after any of them changes
// Broiler-Human:        PENDING
public interface ICredentialStore
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=9746DD
    // Broiler-Falsified-If: ReadAsync returns the secret held in the account and protocol slot when the stored binding differs from the key's Binding
    // Broiler-Human:        PENDING
    Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=A1C5B7
    // Broiler-Falsified-If: after WriteAsync, ReadAsync with the key of the previous connection details still returns the earlier secret
    // Broiler-Human:        PENDING
    Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default);
    // Removes this account/protocol slot, including a credential bound to older connection details.
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=1D0FA6
    // Broiler-Falsified-If: DeleteAsync leaves readable a secret for the same account and protocol that was saved under older connection details
    // Broiler-Human:        PENDING
    Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default);
    /// <summary>
    /// Whether a secret is stored under exactly this key; only its presence is reported. The default reads the
    /// secret and drops it at once. A store that can answer without one, such as demo mode, says so directly.
    /// </summary>
    async Task<bool> ContainsAsync(CredentialKey key, CancellationToken cancellationToken = default) =>
        await ReadAsync(key, cancellationToken).ConfigureAwait(false) is not null;
}
