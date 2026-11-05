using System.Text;
using System.Text.Json.Nodes;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class LocalOriginalTests
{
    private static byte[] Bytes => Encoding.ASCII.GetBytes("Subject: Local\r\n\r\n<script>source only</script>").Concat(new byte[] { 0, 128, 255 }).ToArray();
    [Fact]
    public async Task OpenIsReadOnlyAndImportSnapshotsExactBytesWithIndependentLocalIdentity()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        string path = directory.File("local.EML"); await File.WriteAllBytesAsync(path, Bytes);
        var opened = await store.OpenFileAsync(path); Assert.Equal(Bytes, opened.Bytes); Assert.False(File.Exists(directory.File("local-originals.json")));
        var receipt = await store.ImportAsync(account, opened); opened.Bytes[0] ^= 1;
        Assert.Equal(Bytes, (await store.ReadImportAsync(receipt)).Bytes); Assert.Equal(Bytes, await File.ReadAllBytesAsync(path)); Assert.NotEqual(Guid.Empty, receipt.Id);
        Assert.Equal(receipt, Assert.Single(await new JsonLocalMessageOriginals(directory.Root).ListImportsAsync(account))); Assert.Empty(await store.ListImportsAsync(AccountId.New()));
        var read = await store.ReadImportAsync(receipt); read.Bytes[0] ^= 1; Assert.Equal(Bytes, (await store.ReadImportAsync(receipt)).Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadImportAsync(receipt with { AccountId = AccountId.New() }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadImportAsync(receipt with { Sha256 = new string('0', 64) }));
        Assert.DoesNotContain(directory.Root.Replace("\\", "\\\\"), await File.ReadAllTextAsync(directory.File("local-originals.json")));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(MailMessageSource.MaximumBytes + 1)]
    public async Task OversizedOrEmptyFilesAreRejectedBeforeReading(int length)
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("fixture.eml");
        using (var file = File.Create(path)) file.SetLength(length);
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonLocalMessageOriginals(directory.Root).OpenFileAsync(path));
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task InvalidInputsAndCancellationDoNotWrite()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenFileAsync("relative.eml"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenFileAsync(directory.File("file.exe")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(default, new("file.eml", Bytes)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("../file.eml", Bytes)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("file.eml", [])));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("file.eml", new byte[MailMessageSource.MaximumBytes + 1])));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(account, new("file.eml", Bytes), new(true)));
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task QuotasNeverEvictOrPartiallyReplaceTheExistingCollection()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        for (int i = 0; i < JsonLocalMessageOriginals.MaximumFiles; i++) await store.ImportAsync(account, new("file.eml", Bytes));
        byte[] before = await File.ReadAllBytesAsync(directory.File("local-originals.json"));
        await Assert.ThrowsAsync<IOException>(() => store.ImportAsync(account, new("file.eml", Bytes)));
        Assert.Equal(before, await File.ReadAllBytesAsync(directory.File("local-originals.json")));
        using var second = new TestDirectory(); second.Create(); var large = new JsonLocalMessageOriginals(second.Root);
        await large.ImportAsync(account, new("first.eml", new byte[MailMessageSource.MaximumBytes])); await large.ImportAsync(account, new("second.eml", new byte[MailMessageSource.MaximumBytes]));
        await Assert.ThrowsAsync<IOException>(() => large.ImportAsync(account, new("third.eml", Bytes))); Assert.Equal(2, (await large.ListImportsAsync(account)).Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrFutureStorageFailsClosedWithoutOverwriting(bool future)
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        var receipt = await store.ImportAsync(account, new("file.eml", Bytes)); string path = directory.File("local-originals.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (future) json["schemaVersion"] = 99; else json["data"]!["entries"]![0]!["bytes"] = Convert.ToBase64String(new byte[Bytes.Length]);
        await File.WriteAllTextAsync(path, json.ToJsonString()); byte[] before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListImportsAsync(account));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadImportAsync(receipt));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(account, new("file.eml", Bytes))); Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task ConcurrentImportsSerializeWithoutLosingAnotherAccount()
    {
        using var directory = new TestDirectory(); directory.Create(); var first = AccountId.New(); var second = AccountId.New();
        var store = new JsonLocalMessageOriginals(directory.Root);
        await Task.WhenAll(store.ImportAsync(first, new("one.eml", Bytes)), new JsonLocalMessageOriginals(directory.Root).ImportAsync(second, new("two.eml", Bytes)));
        Assert.Single(await store.ListImportsAsync(first)); Assert.Single(await store.ListImportsAsync(second));
    }
}
