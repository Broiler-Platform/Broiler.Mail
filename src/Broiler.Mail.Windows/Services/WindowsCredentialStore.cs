// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
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
using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Services;

/// <summary>Generic credentials protected by Windows for the current user; no plaintext fallback.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=Critical; Resources=2; Fingerprint=1D3EF1
// Broiler-Falsified-If: ReadAsync returns the password of a stored credential whose UserName differs from the requested key's Binding
// Broiler-Human:        PENDING
public sealed class WindowsCredentialStore : ICredentialStore
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=3019E7
    // Broiler-Falsified-If: the value is not CRED_TYPE_GENERIC (1), so the password is saved as a domain credential that Windows itself offers for network sign-in to the target
    // Broiler-Human:        PENDING
    private const uint Generic = 1;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=2D868A
    // Broiler-Falsified-If: the value is CRED_PERSIST_ENTERPRISE (3) rather than CRED_PERSIST_LOCAL_MACHINE (2), so the saved password roams to other machines with the user's profile
    // Broiler-Human:        PENDING
    private const uint PersistLocalMachine = 2; // Current user, persistent on this machine; not a machine-wide secret.
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=E37452
    // Broiler-Falsified-If: the value is not ERROR_NOT_FOUND (1168), so a Win32 failure such as ERROR_ACCESS_DENIED is swallowed as an absent credential by ReadAsync and DeleteAsync
    // Broiler-Human:        PENDING
    private const int NotFound = 1168;
    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=0; Fingerprint=FC2A69
    // Broiler-Falsified-If: a password longer than 1,280 characters passes the WriteAsync length check because the value exceeds CRED_MAX_CREDENTIAL_BLOB_SIZE (2,560 bytes)
    // Broiler-Human:        PENDING
    private const int MaximumBlobBytes = 2560;

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=Critical; Resources=2; Fingerprint=85421B
    // Broiler-Falsified-If: a stored credential whose BlobSize is odd or larger than 2,560 bytes is decoded with PtrToStringUni instead of raising InvalidDataException
    // Broiler-Human:        PENDING
    public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CredRead(Target(key), Generic, 0, out var pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NotFound) return null;
                throw Failure("read", error);
            }
            NativeCredential credential = default;
            try
            {
                credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.UserName != key.Binding) return null;
                if (credential.BlobSize > MaximumBlobBytes || credential.BlobSize % 2 != 0 || credential.Blob == IntPtr.Zero)
                    throw new InvalidDataException("The saved credential is invalid. Save the password again.");
                return Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
            }
            finally
            {
                if (credential.Blob != IntPtr.Zero && credential.BlobSize <= MaximumBlobBytes)
                    Marshal.Copy(new byte[credential.BlobSize], 0, credential.Blob, (int)credential.BlobSize);
                CredFree(pointer);
            }
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=Critical; Resources=2; Fingerprint=E38D58
    // Broiler-Falsified-If: the BlobSize handed to CredWrite is larger than the byte length of the unmanaged copy of the secret, so Credential Manager reads past its end
    // Broiler-Human:        PENDING
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
                var credential = new NativeCredential
                {
                    Type = Generic, TargetName = Target(key), UserName = key.Binding,
                    Blob = blob, BlobSize = checked((uint)secret.Length * 2), Persist = PersistLocalMachine,
                };
                if (!CredWrite(ref credential, 0)) throw Failure("save", Marshal.GetLastWin32Error());
            }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=1; Fingerprint=3F4BA5
    // Broiler-Falsified-If: a CredDelete failure other than ERROR_NOT_FOUND returns normally, so the caller treats a password that is still stored as forgotten
    // Broiler-Human:        PENDING
    public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CredDelete(Target(key), Generic, 0)) return;
            int error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw Failure("remove", error);
        }, cancellationToken);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=1; Fingerprint=12FF9B
    // Broiler-Falsified-If: two different account and protocol pairs format to the same target name, so saving one slot's password replaces another's
    // Broiler-Human:        PENDING
    private static string Target(CredentialKey key) => $"Broiler.Mail/{key.AccountId}/{key.Protocol}";
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=5E80AC
    // Broiler-Falsified-If: the exception message carries the password or the credential target name rather than only the action and the Win32 error code
    // Broiler-Human:        PENDING
    private static IOException Failure(string action, int code) =>
        new($"Windows Credential Manager could not {action} the password (error {code}).");

    // Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=0; Fingerprint=FEEA36
    // Broiler-Falsified-If: a field's order or width differs from Win32 CREDENTIALW, so BlobSize and Blob are read from the wrong offsets on x64
    // Broiler-Human:        PENDING
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=6B9172
    // Broiler-Falsified-If: the declaration omits SetLastError, so ReadAsync compares a stale Win32 error with ERROR_NOT_FOUND
    // Broiler-Human:        PENDING
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=D111B8
    // Broiler-Falsified-If: the credential's strings are marshalled as ANSI, so CredWriteW stores a TargetName and UserName that CredReadW never finds
    // Broiler-Human:        PENDING
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=7C4E10
    // Broiler-Falsified-If: the declaration omits SetLastError, so DeleteAsync compares a stale Win32 error with ERROR_NOT_FOUND and can report a failed delete as done
    // Broiler-Human:        PENDING
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=B6E4D2
    // Broiler-Falsified-If: the declaration binds an export other than advapi32 CredFree, so the buffer CredRead returns is released by the wrong allocator
    // Broiler-Human:        PENDING
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr pointer);
}
