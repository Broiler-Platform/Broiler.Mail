using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class LocalOriginalManagementTests
{
    [Fact]
    public async Task DecodeReadsMimeEncodingsAndExcludesAttachmentTextWithoutInventingServerIdentity()
    {
        using var directory = new TestDirectory(); var store = new JsonLocalMessageOriginals(directory.Root);
        var decoded = await store.DecodeAsync(new("fixture.eml", Encoding.ASCII.GetBytes("Subject: =?utf-8?Q?Gr=C3=BC=C3=9Fe?=\r\nFrom: sender@example.test\r\nContent-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\nSGVsbG8g8J+YgA==\r\n--x\r\nContent-Type: text/plain\r\nContent-Disposition: attachment; filename=hidden.txt\r\n\r\nNot reader text\r\n--x--\r\n")));
        Assert.Equal("Grüße", decoded.Subject); Assert.Equal("sender@example.test", decoded.Sender); Assert.Contains("Hello 😀", decoded.PlainText); Assert.DoesNotContain("Not reader text", decoded.PlainText);
        Assert.False(decoded.IsHtmlFallback); Assert.False(decoded.IsTruncated); Assert.False(Directory.Exists(directory.Root));
    }
    [Fact]
    public async Task HtmlOnlyDecodeUsesBoundedTextAlternativeAndNeverLoadsResources()
    {
        using var directory = new TestDirectory(); var store = new JsonLocalMessageOriginals(directory.Root);
        var result = await store.DecodeAsync(new("html.eml", Encoding.UTF8.GetBytes("Content-Type: text/html; charset=utf-8\r\n\r\n<html><body><script>private script text</script><style>hidden style</style><p>Readable &amp; safe</p><img src='https://never-contact.example.test/x'></body></html>")));
        Assert.True(result.IsHtmlFallback); Assert.Contains("Readable & safe", result.PlainText); Assert.DoesNotContain("private script", result.PlainText); Assert.DoesNotContain("hidden style", result.PlainText); Assert.False(Directory.Exists(directory.Root));
        var longText = await store.DecodeAsync(new("long.eml", Encoding.ASCII.GetBytes("Content-Type: text/plain\r\n\r\n" + new string('x', 40000))));
        Assert.True(longText.IsTruncated); Assert.Equal(32000, longText.PlainText.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DecodeAsync(new("cancel.eml", [65]), new(true)));
    }
    [Fact]
    public async Task ExactExportSurvivesSourceRemovalAndNeverOverwritesOrLeavesPartialFiles()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        byte[] bytes = Encoding.ASCII.GetBytes("Subject: Exact\r\n\r\nOriginal").Concat(new byte[] { 0, 255 }).ToArray(); var receipt = await store.ImportAsync(account, new("original.eml", bytes));
        string destination = directory.File("copy.eml"); var result = await store.ExportAsync(receipt, destination); Assert.Equal(bytes, await File.ReadAllBytesAsync(destination)); Assert.Equal(receipt.Sha256, result.Sha256);
        await Assert.ThrowsAsync<IOException>(() => store.ExportAsync(receipt, destination)); Assert.Equal(bytes, await File.ReadAllBytesAsync(destination)); Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ExportAsync(receipt, "relative.eml"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ExportAsync(receipt, directory.File("bad.exe")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ExportAsync(receipt, directory.File("canceled.eml"), new(true))); Assert.False(File.Exists(directory.File("canceled.eml")));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ExportAsync(receipt with { AccountId = AccountId.New() }, directory.File("foreign.eml"))); Assert.False(File.Exists(directory.File("foreign.eml")));
    }
    [Fact]
    public async Task RemovalBacksUpExactTargetPreservesOtherAccountAndFreesQuota()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.File("profile")); var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache);
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath + Path.DirectorySeparatorChar, storage); Assert.True(store.CanRemove);
        var own = AccountId.New(); var other = AccountId.New(); var target = await store.ImportAsync(own, new("own.eml", [65, 0, 255])); var foreign = await store.ImportAsync(other, new("other.eml", [66]));
        var removed = await store.RemoveAsync(target); Assert.Equal(target, removed.Removed); Assert.Empty(await store.ListImportsAsync(own)); Assert.Equal(foreign, Assert.Single(await store.ListImportsAsync(other)));
        Assert.Equal(new byte[] { 65, 0, 255 }, (await new JsonLocalMessageOriginals(removed.BackupDirectory).ReadImportAsync(target)).Bytes);
        string restored = await storage.RestoreBackupAsync(removed.BackupDirectory, directory.File("restored")); Assert.Equal(target, Assert.Single(await new JsonLocalMessageOriginals(restored).ListImportsAsync(own)));
        Assert.Equal(foreign, Assert.Single(await new JsonLocalMessageOriginals(restored).ListImportsAsync(other)));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RemoveAsync(target));
    }
    [Fact]
    public async Task FailedOrCanceledBackupCannotRemoveAnImport()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root); var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath, storage); var account = AccountId.New(); var target = await store.ImportAsync(account, new("retained.eml", [65]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RemoveAsync(target, new(true))); Assert.Single(await store.ListImportsAsync(account));
        await File.WriteAllTextAsync(profile.SettingsPath, "invalid"); await Assert.ThrowsAsync<InvalidDataException>(() => store.RemoveAsync(target)); Assert.Single(await store.ListImportsAsync(account));
        Assert.False(new JsonLocalMessageOriginals(profile.DirectoryPath).CanRemove);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new JsonLocalMessageOriginals(profile.DirectoryPath).RemoveAsync(target));
    }
    [Fact]
    public async Task BackupThatDoesNotContainTheTargetCannotAuthorizeDeletion()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root); var real = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        string path = Path.Combine(profile.DirectoryPath, "local-originals.json");
        var proxy = new BackupStorage(real, async (destination, token) =>
        {
            byte[] current = await File.ReadAllBytesAsync(path, token);
            await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"data\":{\"entries\":[]}}", token);
            try { return await real.CreateBackupAsync(destination, token); }
            finally { await File.WriteAllBytesAsync(path, current, CancellationToken.None); }
        });
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath, proxy); var account = AccountId.New(); var target = await store.ImportAsync(account, new("keep.eml", [65]));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RemoveAsync(target)); Assert.Equal(target, Assert.Single(await store.ListImportsAsync(account)));
    }
    [Fact]
    public async Task CancellationAfterBackupKeepsTheTargetAndCompletedRecoveryCopy()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root); var real = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        using var cancellation = new CancellationTokenSource(); string? backup = null;
        var proxy = new BackupStorage(real, async (destination, token) => { backup = await real.CreateBackupAsync(destination, token); cancellation.Cancel(); return backup; });
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath, proxy); var account = AccountId.New(); var target = await store.ImportAsync(account, new("keep.eml", [65]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RemoveAsync(target, cancellation.Token)); Assert.Single(await store.ListImportsAsync(account)); Assert.NotNull(backup);
        Assert.Equal(target, Assert.Single(await new JsonLocalMessageOriginals(backup).ListImportsAsync(account)));
    }
    [Fact]
    public async Task ExplicitRemovalReleasesAFullCollectionSlotWithoutEvictingOthers()
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root); var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath, storage); var account = AccountId.New(); var rows = new List<LocalOriginalReceipt>();
        for (int i = 0; i < 16; i++) rows.Add(await store.ImportAsync(account, new("original.eml", [65])));
        await store.RemoveAsync(rows[0]); var replacement = await store.ImportAsync(account, new("new.eml", [66]));
        var current = await store.ListImportsAsync(account); Assert.Equal(16, current.Count); Assert.Contains(replacement, current); Assert.All(rows.Skip(1), row => Assert.Contains(row, current));
    }
    private sealed class BackupStorage(IProfileStorage inner, Func<string, CancellationToken, Task<string>> backup) : IProfileStorage
    {
        public string ProfileDirectory => inner.ProfileDirectory; public ProfileStoragePolicy Policy => inner.Policy;
        public Task<ProfileStoragePolicy> SetPolicyAsync(ProfileStoragePolicy policy, CancellationToken token = default) => inner.SetPolicyAsync(policy, token);
        public Task<ProfileStorageInfo> GetInfoAsync(CancellationToken token = default) => inner.GetInfoAsync(token);
        public Task<string> CreateBackupAsync(string destination, CancellationToken token = default) => backup(destination, token);
        public Task<string> RestoreBackupAsync(string source, string destination, CancellationToken token = default) => inner.RestoreBackupAsync(source, destination, token);
        public Task<ProfileCleanupResult> ClearDownloadedMailAsync(string destination, CancellationToken token = default) => inner.ClearDownloadedMailAsync(destination, token);
    }

}
