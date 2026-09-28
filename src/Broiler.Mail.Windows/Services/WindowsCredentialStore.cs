using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Services;

/// <summary>Generic credentials protected by Windows for the current user; no plaintext fallback.</summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2; // Current user, persistent on this machine; not a machine-wide secret.
    private const int NotFound = 1168;
    private const int MaximumBlobBytes = 2560;

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

    private static string Target(CredentialKey key) => $"Broiler.Mail/{key.AccountId}/{key.Protocol}";
    private static IOException Failure(string action, int code) =>
        new($"Windows Credential Manager could not {action} the password (error {code}).");

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

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr pointer);
}
