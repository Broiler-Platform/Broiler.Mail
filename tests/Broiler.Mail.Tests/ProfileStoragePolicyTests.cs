using System.Text.Json.Nodes;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class ProfileStoragePolicyTests
{
    [Fact]
    public async Task PolicyAndQuotaPersistAcrossReopenWithoutDeletingBodies()
    {
        using var directory = new TestDirectory(); var account = AccountId.New(); var mailbox = new MailboxKey(account, "INBOX", 7);
        using (var profile = MailProfile.Open(directory.Root))
        {
            var cache = new SqliteMailCache(profile.CachePath); var service = new ProfileStorageService(profile, cache);
            await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
            await cache.SaveBodyAsync(mailbox, new(SqliteMailCacheTests.Key(mailbox, 1), "Keep me"));
            await service.SetPolicyAsync(new(8 * 1024 * 1024, OfflineDownloadPolicy.HeadersOnly));
            Assert.Equal(8 * 1024 * 1024, (await service.GetInfoAsync()).LimitBytes);
            Assert.Equal("Keep me", (await cache.ReadBodyAsync(mailbox, 1))!.PlainText);
        }
        using var reopened = MailProfile.Open(directory.Root);
        Assert.Equal(8 * 1024 * 1024, reopened.Manifest.CacheMaximumBytes);
        Assert.Equal(OfflineDownloadPolicy.HeadersOnly, reopened.Manifest.DownloadPolicy);
    }

    [Fact]
    public async Task ShrinkBelowExistingAllocationRefusesWithoutChangingManifestOrBody()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root);
        var cache = new SqliteMailCache(profile.CachePath); var service = new ProfileStorageService(profile, cache);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, LargeBody(mailbox));
        byte[] before = File.ReadAllBytes(Path.Combine(directory.Root, "profile.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPolicyAsync(new(1024 * 1024, OfflineDownloadPolicy.HeadersOnly)));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(directory.Root, "profile.json")));
        Assert.Equal(OfflineDownloadPolicy.OnDemand, service.Policy.Downloads); Assert.NotNull(await cache.ReadBodyAsync(mailbox, 1));
    }

    [Fact]
    public async Task FailedManifestCommitAndCancellationKeepTheOldEffectiveQuota()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root);
        var cache = new SqliteMailCache(profile.CachePath); var service = new ProfileStorageService(profile, cache);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetPolicyAsync(new(1024 * 1024, OfflineDownloadPolicy.HeadersOnly), canceled.Token));
        string manifestPath = Path.Combine(directory.Root, "profile.json"); var future = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        future["SchemaVersion"] = 999; File.WriteAllText(manifestPath, future.ToJsonString());
        string original = File.ReadAllText(manifestPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SetPolicyAsync(new(1024 * 1024, OfflineDownloadPolicy.HeadersOnly)));
        Assert.Equal(original, File.ReadAllText(manifestPath)); Assert.Equal(OfflineDownloadPolicy.OnDemand, service.Policy.Downloads);
        // This body is larger than the rejected 1 MiB quota, so a stale in-memory limit would fail here.
        await cache.SaveBodyAsync(mailbox, LargeBody(mailbox)); Assert.NotNull(await cache.ReadBodyAsync(mailbox, 1));
    }

    private static MailMessageBody LargeBody(MailboxKey mailbox) => new(SqliteMailCacheTests.Key(mailbox, 1), "Large bounded body")
    { EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["large"] = new("large", "image/png", new byte[900 * 1024]) } };

    [Fact]
    public void OlderManifestDefaultsToSaveOnOpen()
    {
        using var directory = new TestDirectory();
        using (var profile = MailProfile.Open(directory.Root)) { }
        string path = Path.Combine(directory.Root, "profile.json"); var document = JsonNode.Parse(File.ReadAllText(path))!;
        document.AsObject().Remove("DownloadPolicy"); File.WriteAllText(path, document.ToJsonString());
        using var reopened = MailProfile.Open(directory.Root);
        Assert.Equal(OfflineDownloadPolicy.OnDemand, reopened.Manifest.DownloadPolicy);
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("99")]
    public void UnsupportedPolicyIsRefusedWithoutResettingProfile(string value)
    {
        using var directory = new TestDirectory();
        using (var profile = MailProfile.Open(directory.Root)) { }
        string path = Path.Combine(directory.Root, "profile.json"); var document = JsonNode.Parse(File.ReadAllText(path))!;
        document["DownloadPolicy"] = JsonNode.Parse(value); string contents = document.ToJsonString(); File.WriteAllText(path, contents);
        Assert.Throws<InvalidDataException>(() => MailProfile.Open(directory.Root)); Assert.Equal(contents, File.ReadAllText(path));
    }
}
