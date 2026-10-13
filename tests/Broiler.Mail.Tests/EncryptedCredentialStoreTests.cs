using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class EncryptedCredentialStoreTests
{
    [Fact]
    public async Task PasswordSurvivesReopeningWithoutPlaintextOnDisk()
    {
        using var directory = new TestDirectory();
        var cipher = new TestCipher();
        var key = CredentialKey.For(TestDirectory.Profile(), MailProtocol.Imap);
        var store = new EncryptedCredentialStore(directory.Root, cipher);
        Assert.Null(await store.ReadAsync(key));
        Assert.False(Directory.Exists(directory.Root));
        await store.WriteAsync(key, "secret-ä🔐");
        Assert.Equal("secret-ä🔐", await new EncryptedCredentialStore(directory.Root, cipher).ReadAsync(key));
        Assert.DoesNotContain("secret", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(directory.Root)))));
    }

    [Fact]
    public async Task ChangedServerCannotReadOldPasswordAndForgetRemovesWholeSlot()
    {
        using var directory = new TestDirectory();
        var profile = TestDirectory.Profile();
        var original = CredentialKey.For(profile, MailProtocol.Imap);
        var changed = CredentialKey.For(profile with { IncomingServer = profile.IncomingServer with { Host = "new.example.test" } }, MailProtocol.Imap);
        var store = new EncryptedCredentialStore(directory.Root, new TestCipher());
        await store.WriteAsync(original, "first");
        Assert.Null(await store.ReadAsync(changed));
        await store.WriteAsync(changed, "second");
        Assert.Null(await store.ReadAsync(original));
        Assert.Equal("second", await store.ReadAsync(changed));
        await store.DeleteAsync(original);
        Assert.Null(await store.ReadAsync(changed));
        Assert.Empty(Directory.GetFiles(directory.Root));
    }

    [Fact]
    public async Task CiphertextCannotBeSwappedBetweenAccountsOrModified()
    {
        using var directory = new TestDirectory();
        var profile = TestDirectory.Profile();
        var first = CredentialKey.For(profile, MailProtocol.Imap);
        var second = CredentialKey.For(profile with { Id = AccountId.New() }, MailProtocol.Imap);
        var store = new EncryptedCredentialStore(directory.Root, new TestCipher());
        await store.WriteAsync(first, "first");
        var firstPath = Assert.Single(Directory.GetFiles(directory.Root));
        var bytes = await File.ReadAllBytesAsync(firstPath);
        await store.WriteAsync(second, "second");
        var secondPath = Assert.Single(Directory.GetFiles(directory.Root).Except([firstPath]));
        await File.WriteAllBytesAsync(secondPath, bytes);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => store.ReadAsync(second));
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(firstPath, bytes);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => store.ReadAsync(first));
    }

    [Fact]
    public async Task FailedOrCanceledReplacementKeepsPreviousPassword()
    {
        using var directory = new TestDirectory();
        var cipher = new TestCipher();
        var key = CredentialKey.For(TestDirectory.Profile(), MailProtocol.Imap);
        var store = new EncryptedCredentialStore(directory.Root, cipher);
        await store.WriteAsync(key, "original");
        cipher.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(key, "replacement"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(key, "replacement", new CancellationToken(true)));
        Assert.Equal("original", await store.ReadAsync(key));
        Assert.Single(Directory.GetFiles(directory.Root));
    }

    // Real authenticated encryption exercises the storage envelope's identity binding;
    // Android Keystore itself is exercised by the device acceptance checklist.
    private sealed class TestCipher : ICredentialCipher
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public bool FailWrites { get; set; }
        public byte[] Encrypt(byte[] plaintext, byte[] associatedData)
        {
            if (FailWrites) throw new IOException("Key unavailable.");
            using var aes = new AesGcm(_key, 16);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return [.. nonce, .. tag, .. ciphertext];
        }
        public byte[] Decrypt(byte[] ciphertext, byte[] associatedData)
        {
            using var aes = new AesGcm(_key, 16);
            var plaintext = new byte[ciphertext.Length - 28];
            aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), plaintext, associatedData);
            return plaintext;
        }
    }
}
