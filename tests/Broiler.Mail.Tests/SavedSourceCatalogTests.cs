using System.Text;
using System.Text.Json.Nodes;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class SavedSourceCatalogTests
{
    [Fact]
    public async Task ReopenedCatalogScopesFullMessageIdentityAndPreservesOriginalBytes()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();
        var first=await store.SaveAsync(new(key,Bytes));var second=await store.SaveAsync(new(key,Bytes));
        foreach(var other in new[]{key with{AccountId=AccountId.New()},key with{MailboxId="Other"},key with{UidValidity=8},key with{Uid=2}})await store.SaveAsync(new(other,Bytes));
        var reopened=new FileMessageSourceExports(directory.Root);var rows=await reopened.ListSavedExportsAsync(key);Assert.Equal(2,rows.Count);Assert.Contains(rows,row=>row.Export==first);Assert.Contains(rows,row=>row.Export==second);Assert.True(rows[0].SavedAt>=rows[1].SavedAt);
        foreach(var row in rows)Assert.Equal(Bytes,(await reopened.ReadVerifiedAsync(row.Export)).Bytes);
    }
    [Fact]
    public async Task OldUncatalogedFilesAreKeptWithoutInventingIdentity()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);Directory.CreateDirectory(store.ExportDirectory);string path=Path.Combine(store.ExportDirectory,"message-"+Guid.NewGuid().ToString("N")+".eml");File.WriteAllBytes(path,Bytes);
        Assert.Empty(await store.ListSavedExportsAsync(Key()));Assert.Equal(Bytes,File.ReadAllBytes(path));Assert.False(File.Exists(directory.File("source-export-catalog.json")));
    }
    [Theory]
    [InlineData("corrupt")]
    [InlineData("future")]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("duplicate")]
    [InlineData("size")]
    [InlineData("hash")]
    public async Task InvalidCatalogFailsWithoutResettingReceiptsOrExportingMoreBytes(string mode)
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();await store.SaveAsync(new(key,Bytes));string path=directory.File("source-export-catalog.json");var json=JsonNode.Parse(File.ReadAllText(path))!;var entries=json["data"]!["entries"]!.AsArray();var row=entries[0]!;
        switch(mode)
        {
            case "future":json["schemaVersion"]=9;break;
            case "path":row["fileName"]="../../outside.eml";break;
            case "identity":row["key"]!["uid"]=0;break;
            case "duplicate":entries.Add(row.DeepClone());break;
            case "size":row["bytes"]=int.MaxValue;break;
            case "hash":row["sha256"]="bad";break;
        }
        File.WriteAllText(path,mode=="corrupt"?"broken":json.ToJsonString());byte[] before=File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.ListSavedExportsAsync(key));await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAsync(new(key,Bytes)));
        Assert.Equal(before,File.ReadAllBytes(path));Assert.Single(Directory.GetFiles(store.ExportDirectory,"*.eml"));
    }
    [Fact]
    public async Task MissingAndTamperedFilesStayListedUntilSelectedAndCannotVerify()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();var one=await store.SaveAsync(new(key,Bytes));var two=await store.SaveAsync(new(key,Bytes));
        File.Delete(one.Path);byte[] changed=Bytes.ToArray();changed[0]^=1;File.WriteAllBytes(two.Path,changed);var rows=await new FileMessageSourceExports(directory.Root).ListSavedExportsAsync(key);Assert.Equal(2,rows.Count);
        await Assert.ThrowsAsync<FileNotFoundException>(()=>store.ReadVerifiedAsync(one));await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadVerifiedAsync(two));
        await store.SaveAsync(new(key,Bytes));Assert.Equal(2,(await store.ListSavedExportsAsync(key)).Count);Assert.Equal(changed,File.ReadAllBytes(two.Path));
    }
    [Fact]
    public async Task ConcurrentStoresKeepEveryReceiptAndRespectTheExisting64FileQuota()
    {
        using var directory=new TestDirectory();directory.Create();var key=Key();await Task.WhenAll(Enumerable.Range(0,16).Select(_=>new FileMessageSourceExports(directory.Root).SaveAsync(new(key,Bytes))));
        var store=new FileMessageSourceExports(directory.Root);Assert.Equal(16,(await store.ListSavedExportsAsync(key)).Count);
        for(int index=16;index<64;index++)await store.SaveAsync(new(key,Bytes));Assert.Equal(64,(await store.ListSavedExportsAsync(key)).Count);
        await Assert.ThrowsAsync<IOException>(()=>store.SaveAsync(new(key,Bytes)));Assert.Equal(64,Directory.GetFiles(store.ExportDirectory).Length);
        File.Delete((await store.ListSavedExportsAsync(key))[0].Export.Path);await store.SaveAsync(new(key,Bytes));Assert.Equal(64,(await store.ListSavedExportsAsync(key)).Count);
    }
    [Fact]
    public async Task CancelingAQueuedWriterDoesNotLeakItsGateOrPublishAReceipt()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new FileMessageSourceExports(directory.Root); var key = Key(); Task<MessageSourceExport> first;
        using (var external = new FileStream(Path.Combine(directory.Root, "source-exports.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            first = store.SaveAsync(new(key, Bytes)); using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileMessageSourceExports(directory.Root + Path.DirectorySeparatorChar).SaveAsync(new(key, Bytes), cancellation.Token));
        }
        await first; await new FileMessageSourceExports(directory.Root).SaveAsync(new(key, Bytes)); Assert.Equal(2, (await store.ListSavedExportsAsync(key)).Count); Assert.Equal(2, Directory.GetFiles(store.ExportDirectory, "*.eml").Length);
    }
    [Fact]
    public async Task CacheCleanupKeepsCatalogButVerifiedProfileBackupsExcludeIt()
    {
        using var directory=new TestDirectory();using var profile=MailProfile.Open(directory.Root);var store=new FileMessageSourceExports(directory.Root);var key=Key();var export=await store.SaveAsync(new(key,Bytes));
        var cleanup=await new ProfileStorageService(profile,new SqliteMailCache(profile.CachePath)).ClearDownloadedMailAsync(directory.File("backup"));
        Assert.Equal(export,Assert.Single(await new FileMessageSourceExports(directory.Root).ListSavedExportsAsync(key)).Export);Assert.False(File.Exists(Path.Combine(cleanup.BackupDirectory,"source-export-catalog.json")));Assert.False(Directory.Exists(Path.Combine(cleanup.BackupDirectory,"source-exports")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.ListSavedExportsAsync(key,new(true)));
    }
    [Fact]
    public async Task SeparateCopyOfExportsAndCatalogResolvesOnlyInsideTheNewProfile()
    {
        using var directory=new TestDirectory();directory.Create();var key=Key();var store=new FileMessageSourceExports(directory.Root);var receipt=await store.SaveAsync(new(key,Bytes));
        string copy=directory.File("separate-copy");Directory.CreateDirectory(Path.Combine(copy,"source-exports"));File.Copy(receipt.Path,Path.Combine(copy,"source-exports",Path.GetFileName(receipt.Path)));File.Copy(directory.File("source-export-catalog.json"),Path.Combine(copy,"source-export-catalog.json"));
        var copied=new FileMessageSourceExports(copy);var row=Assert.Single(await copied.ListSavedExportsAsync(key));Assert.NotEqual(receipt.Path,row.Export.Path);Assert.Equal(Bytes,(await copied.ReadVerifiedAsync(row.Export)).Bytes);
        Assert.DoesNotContain(receipt.Path,File.ReadAllText(directory.File("source-export-catalog.json")));
    }
    internal static readonly byte[] Bytes=Encoding.ASCII.GetBytes("Subject: Saved source\r\nContent-Type: text/plain\r\n\r\nSaved original bytes");
    internal static MailMessageKey Key()=>new(AccountId.New(),"Archive/台北",7,1);
}
