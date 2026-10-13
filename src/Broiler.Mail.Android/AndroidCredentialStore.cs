using Android.Security.Keystore;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace Broiler.Mail.Android;

/// <summary>Passwords encrypted with a non-exportable Android Keystore AES-GCM key.</summary>
internal sealed class AndroidCredentialStore(string directory) : ICredentialStore
{
    private readonly EncryptedCredentialStore _store = new(directory, new KeystoreCipher());

    public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default) =>
        Task.Run(() => _store.ReadAsync(key, cancellationToken), cancellationToken);
    public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default) =>
        Task.Run(() => _store.WriteAsync(key, secret, cancellationToken), cancellationToken);
    public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default) =>
        Task.Run(() => _store.DeleteAsync(key, cancellationToken), cancellationToken);

    private sealed class KeystoreCipher : ICredentialCipher
    {
        private const string Alias = "org.broiler.mail.credentials.v1";
        private static readonly object KeyLock = new();

        private static IKey GetKey(bool create)
        {
            lock (KeyLock)
            {
                using var store = KeyStore.GetInstance("AndroidKeyStore")!;
                store.Load(null);
                if (store.ContainsAlias(Alias))
                    return store.GetKey(Alias, null) ?? throw new IOException("The password key is unavailable.");
                if (!create) throw new IOException("The password key is unavailable. Save the password again.");
                using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
                using var builder = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
                using var specification = builder.SetBlockModes(KeyProperties.BlockModeGcm)!
                    .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
                    .SetKeySize(256)!.Build()!;
                generator.Init(specification);
                return generator.GenerateKey()!;
            }
        }

        public byte[] Encrypt(byte[] plaintext, byte[] associatedData)
        {
            try
            {
                using var key = GetKey(create: true);
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(CipherMode.EncryptMode, key);
                cipher.UpdateAAD(associatedData);
                return [.. cipher.GetIV()!, .. cipher.DoFinal(plaintext)!];
            }
            catch (GeneralSecurityException) { throw new IOException("Android could not protect the password. Please try again."); }
        }

        public byte[] Decrypt(byte[] ciphertext, byte[] associatedData)
        {
            if (ciphertext.Length < 28) throw new InvalidDataException("The saved password is invalid. Save it again.");
            try
            {
                using var key = GetKey(create: false);
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                using var parameters = new GCMParameterSpec(128, ciphertext[..12]);
                cipher.Init(CipherMode.DecryptMode, key, parameters);
                cipher.UpdateAAD(associatedData);
                return cipher.DoFinal(ciphertext[12..])!;
            }
            catch (GeneralSecurityException) { throw new IOException("Android could not unlock the password. Save it again."); }
        }
    }
}
