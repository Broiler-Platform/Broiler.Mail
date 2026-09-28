using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Windows.Services;

namespace Broiler.Mail.Windows.Tests;

/// <summary>Uses only random test account IDs and deletes its own synthetic credentials in finally blocks.</summary>
public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public async Task ProtectedCredentialRoundTripsUpdatesAndDeletes()
    {
        var key = CredentialKey.For(Profile(), MailProtocol.Imap);
        var store = new WindowsCredentialStore();
        try
        {
            Assert.Null(await store.ReadAsync(key));
            await store.WriteAsync(key, "fixture-only-ä秘密-123");
            Assert.True(await new WindowsCredentialStore().ReadAsync(key) == "fixture-only-ä秘密-123");
            await store.WriteAsync(key, "fixture-only-replacement");
            Assert.True(await store.ReadAsync(key) == "fixture-only-replacement");
            await store.DeleteAsync(key);
            Assert.Null(await store.ReadAsync(key));
            await store.DeleteAsync(key); // Already absent is success.
        }
        finally { await store.DeleteAsync(key); }
    }

    [Fact]
    public async Task AccountProtocolAndConnectionBindingPreventCredentialReuse()
    {
        var account = Profile();
        var key = CredentialKey.For(account, MailProtocol.Imap);
        var smtp = CredentialKey.For(account, MailProtocol.Smtp);
        var other = CredentialKey.For(Profile(), MailProtocol.Imap);
        var changed = CredentialKey.For(account with { IncomingServer = account.IncomingServer with { UserName = "different" } }, MailProtocol.Imap);
        var store = new WindowsCredentialStore();
        try
        {
            await store.WriteAsync(key, "fixture-only-first");
            await store.WriteAsync(smtp, "fixture-only-smtp");
            Assert.Null(await store.ReadAsync(other));
            Assert.Null(await store.ReadAsync(changed));
            Assert.True(await store.ReadAsync(smtp) == "fixture-only-smtp");
            await store.WriteAsync(changed, "fixture-only-second");
            Assert.Null(await store.ReadAsync(key));
            Assert.True(await store.ReadAsync(changed) == "fixture-only-second");
        }
        finally
        {
            await store.DeleteAsync(key);
            await store.DeleteAsync(smtp);
        }
    }

    [Fact]
    public async Task InvalidOrCanceledWritesPreserveExistingCredential()
    {
        var key = CredentialKey.For(Profile(), MailProtocol.Imap);
        var store = new WindowsCredentialStore();
        try
        {
            await store.WriteAsync(key, "fixture-only-original");
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(key, new string('x', 1281)));
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(key, "contains\0null"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(key, "fixture-only-new", cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DeleteAsync(key, cancellation.Token));
            Assert.True(await store.ReadAsync(key) == "fixture-only-original");
        }
        finally { await store.DeleteAsync(key); }
    }

    private static AccountProfile Profile() => new()
    {
        Id = AccountId.New(), DisplayName = "Synthetic credential test", EmailAddress = "fixture@example.test",
        IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = "fixture" },
        OutgoingServer = new() { Host = "smtp.example.test", Port = 465, UserName = "fixture" },
    };
}
