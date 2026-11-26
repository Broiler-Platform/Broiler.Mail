// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   15
// Annotated:        15/15
// Exempt:           12
// Human-reviewed:   0/15
// IP risk:          Low
// Security risk:    Critical
// Criteria:         15/14
// Resource impact:  2/10 max
// Unverified:       15
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Native.Windows.Credentials;
using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Services;

/// <summary>Generic credentials protected by Windows for the current user; no plaintext fallback.</summary>
// Hairs-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=Critical; Resources=2; Fingerprint=1D3EF1
// Hairs-Falsified-If: ReadAsync returns the password of a stored credential whose UserName differs from the requested key's Binding
// Hairs-Human:        PENDING
public sealed class WindowsCredentialStore : ICredentialStore
{
    // Hairs-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=3019E7
    // Hairs-Falsified-If: the value is not CRED_TYPE_GENERIC (1), so the password is saved as a domain credential that Windows itself offers for network sign-in to the target
    // Hairs-Human:        PENDING
    private const uint Generic = 1;
    // Hairs-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=2D868A
    // Hairs-Falsified-If: the value is CRED_PERSIST_ENTERPRISE (3) rather than CRED_PERSIST_LOCAL_MACHINE (2), so the saved password roams to other machines with the user's profile
    // Hairs-Human:        PENDING
    private const uint PersistLocalMachine = 2; // Current user, persistent on this machine; not a machine-wide secret.
    // Hairs-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=E37452
    // Hairs-Falsified-If: the value is not ERROR_NOT_FOUND (1168), so a Win32 failure such as ERROR_ACCESS_DENIED is swallowed as an absent credential by ReadAsync and DeleteAsync
    // Hairs-Human:        PENDING
    private const int NotFound = 1168;
    // Hairs-AI:           Origin=AI; IP=None; Security=Critical; Resources=0; Fingerprint=FC2A69
    // Hairs-Falsified-If: a password longer than 1,280 characters passes the WriteAsync length check because the value exceeds CRED_MAX_CREDENTIAL_BLOB_SIZE (2,560 bytes)
    // Hairs-Human:        PENDING
    private const int MaximumBlobBytes = 2560;

    // Hairs-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=Critical; Resources=2; Fingerprint=85421B
    // Hairs-Falsified-If: a stored credential whose BlobSize is odd or larger than 2,560 bytes is decoded with PtrToStringUni instead of raising InvalidDataException
    // Hairs-Human:        PENDING
    public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CredentialNative.CredRead(Target(key), Generic, 0, out var pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NotFound) return null;
                throw Failure("read", error);
            }
            CredentialNative.CREDENTIAL credential = default;
            try
            {
                credential = Marshal.PtrToStructure<CredentialNative.CREDENTIAL>(pointer);
                if (credential.UserName != key.Binding) return null;
                if (credential.CredentialBlobSize > MaximumBlobBytes || credential.CredentialBlobSize % 2 != 0 || credential.CredentialBlob == IntPtr.Zero)
                    throw new InvalidDataException("The saved credential is invalid. Save the password again.");
                return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
            }
            finally
            {
                if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize <= MaximumBlobBytes)
                    Marshal.Copy(new byte[credential.CredentialBlobSize], 0, credential.CredentialBlob, (int)credential.CredentialBlobSize);
                CredentialNative.CredFree(pointer);
            }
        }, cancellationToken);
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=Critical; Resources=2; Fingerprint=E38D58
    // Hairs-Falsified-If: the BlobSize handed to CredWrite is larger than the byte length of the unmanaged copy of the secret, so Credential Manager reads past its end
    // Hairs-Human:        PENDING
    public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length is 0 or > MaximumBlobBytes / 2 || secret.Contains('\0'))
            throw new ArgumentException("Enter a password of 1–1280 characters without null characters.");
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            IntPtr blob = Marshal.StringToCoTaskMemUni(secret);
            try
            {
                var credential = new CredentialNative.CREDENTIAL
                {
                    Type = Generic, TargetName = Target(key), UserName = key.Binding,
                    CredentialBlob = blob, CredentialBlobSize = checked((uint)secret.Length * 2), Persist = PersistLocalMachine,
                };
                if (!CredentialNative.CredWrite(ref credential, 0)) throw Failure("save", Marshal.GetLastWin32Error());
            }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
        }, cancellationToken);
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=1; Fingerprint=3F4BA5
    // Hairs-Falsified-If: a CredDelete failure other than ERROR_NOT_FOUND returns normally, so the caller treats a password that is still stored as forgotten
    // Hairs-Human:        PENDING
    public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CredentialNative.CredDelete(Target(key), Generic, 0)) return;
            int error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw Failure("remove", error);
        }, cancellationToken);
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=1; Fingerprint=12FF9B
    // Hairs-Falsified-If: two different account and protocol pairs format to the same target name, so saving one slot's password replaces another's
    // Hairs-Human:        PENDING
    private static string Target(CredentialKey key) => $"Broiler.Mail/{key.AccountId}/{key.Protocol}";
    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=5E80AC
    // Hairs-Falsified-If: the exception message carries the password or the credential target name rather than only the action and the Win32 error code
    // Hairs-Human:        PENDING
    private static IOException Failure(string action, int code) =>
        new($"Windows Credential Manager could not {action} the password (error {code}).");
}
