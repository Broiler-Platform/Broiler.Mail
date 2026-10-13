using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Authenticated encryption backed by an operating-system protected key.</summary>
public interface ICredentialCipher
{
    byte[] Encrypt(byte[] plaintext, byte[] associatedData);
    byte[] Decrypt(byte[] ciphertext, byte[] associatedData);
}

/// <summary>One encrypted file per account/protocol; a changed server never receives an old secret.</summary>
public sealed class EncryptedCredentialStore(string directory, ICredentialCipher cipher) : ICredentialStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = FilePath(key);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 8192)
                throw new InvalidDataException("The saved password is invalid. Save it again.");
            byte[] record = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (record.Length < 65 || record[64] != '\n')
                throw new InvalidDataException("The saved password is invalid. Save it again.");
            if (Encoding.ASCII.GetString(record, 0, 64) != key.Binding) return null;
            byte[] plaintext = cipher.Decrypt(record[65..], AssociatedData(key));
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length is 0 or > 1280 || secret.Contains('\0'))
            throw new ArgumentException("Enter a password of 1–1280 characters without null characters.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(secret);
            byte[] encrypted;
            try { encrypted = cipher.Encrypt(plaintext, AssociatedData(key)); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            byte[] record = [.. Encoding.ASCII.GetBytes(key.Binding + "\n"), .. encrypted];
            Directory.CreateDirectory(directory);
            string path = FilePath(key);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, record, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }

    public async Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { File.Delete(FilePath(key)); }
        finally { _gate.Release(); }
    }

    private static string Slot(CredentialKey key) => $"Broiler.Mail/{key.AccountId}/{key.Protocol}";
    private string FilePath(CredentialKey key) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Slot(key)))) + ".secret");
    private static byte[] AssociatedData(CredentialKey key) => Encoding.UTF8.GetBytes(Slot(key) + "/" + key.Binding);
}
