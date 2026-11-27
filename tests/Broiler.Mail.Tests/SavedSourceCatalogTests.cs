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
    [Fact]
    public async Task RemoveExportDeletesVerifiedFileThenReceiptAndFreesQuota()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();
        for(int index=0;index<64;index++)await store.SaveAsync(new(key,Bytes));
        await Assert.ThrowsAsync<IOException>(()=>store.SaveStreamAsync(key,CopyBytes));
        var chosen=(await store.ListSavedExportsAsync(key))[5];ISourceExportCatalogMaintenance maintenance=new FileMessageSourceExports(directory.Root);
        var removed=await maintenance.RemoveExportAsync(chosen);
        Assert.Equal(chosen,removed.Removed);Assert.False(removed.FileWasMissing);Assert.False(File.Exists(chosen.Export.Path));
        var after=await store.ListSavedExportsAsync(key);Assert.Equal(63,after.Count);Assert.DoesNotContain(chosen,after);Assert.Equal(63,Directory.GetFiles(store.ExportDirectory,"*.eml").Length);
        var again=await store.SaveStreamAsync(key,CopyBytes);Assert.Equal(64,(await store.ListSavedExportsAsync(key)).Count);Assert.Equal(Bytes,(await store.ReadVerifiedAsync(again)).Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>maintenance.RemoveExportAsync(chosen));Assert.Equal(64,Directory.GetFiles(store.ExportDirectory,"*.eml").Length);
    }
    [Fact]
    public async Task RemoveExportDeletesTheFileBeforeItsReceipt()
    {
        if(!OperatingSystem.IsWindows())return; // A read-only catalog refuses replacement only on Windows.
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();var receipt=await store.SaveAsync(new(key,Bytes));var chosen=Assert.Single(await store.ListSavedExportsAsync(key));
        string catalog=directory.File("source-export-catalog.json");File.SetAttributes(catalog,FileAttributes.ReadOnly);
        try{await Assert.ThrowsAnyAsync<Exception>(()=>store.RemoveExportAsync(chosen));}finally{File.SetAttributes(catalog,FileAttributes.Normal);}
        Assert.False(File.Exists(receipt.Path));Assert.Equal(chosen,Assert.Single(await store.ListSavedExportsAsync(key)));
        Assert.True((await store.RemoveExportAsync(chosen)).FileWasMissing);Assert.Empty(await store.ListSavedExportsAsync(key));
    }
    [Theory]
    [InlineData("changed")]
    [InlineData("shorter")]
    [InlineData("open")]
    [InlineData("canceled")]
    public async Task RemoveExportRefusesChangedFileAndKeepsEverything(string mode)
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();var receipt=await store.SaveAsync(new(key,Bytes));await store.SaveAsync(new(key,Bytes));
        var chosen=(await store.ListSavedExportsAsync(key)).Single(row=>row.Export==receipt);byte[] content=mode=="changed"?Bytes.Select((value,index)=>index==0?(byte)(value^1):value).ToArray():mode=="shorter"?Bytes[..^1]:Bytes;File.WriteAllBytes(receipt.Path,content);
        if(mode=="open"&&!OperatingSystem.IsWindows())return; // Only Windows refuses a file another handle holds.
        if(mode=="open"){using var held=new FileStream(receipt.Path,FileMode.Open,FileAccess.Read,FileShare.Read);await Assert.ThrowsAsync<IOException>(()=>store.RemoveExportAsync(chosen));}
        else if(mode=="canceled")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.RemoveExportAsync(chosen,new(true)));
        else Assert.Equal("The saved file no longer matches its receipt. Nothing was deleted.",(await Assert.ThrowsAsync<InvalidDataException>(()=>store.RemoveExportAsync(chosen))).Message);
        Assert.Equal(content,File.ReadAllBytes(receipt.Path));Assert.Equal(2,(await store.ListSavedExportsAsync(key)).Count);Assert.Equal(2,Directory.GetFiles(store.ExportDirectory,"*.eml").Length);
    }
    [Fact]
    public async Task RemoveExportOfMissingFileRemovesReceiptOnly()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();var gone=await store.SaveAsync(new(key,Bytes));var kept=await store.SaveAsync(new(key,Bytes));
        File.Delete(gone.Path);var removed=await store.RemoveExportAsync((await store.ListSavedExportsAsync(key)).Single(row=>row.Export==gone));
        Assert.True(removed.FileWasMissing);Assert.Equal(kept,Assert.Single(await store.ListSavedExportsAsync(key)).Export);Assert.Equal(Bytes,File.ReadAllBytes(kept.Path));
    }
    [Theory]
    [InlineData("savedAt")]
    [InlineData("hash")]
    [InlineData("bytes")]
    [InlineData("account")]
    [InlineData("uid")]
    [InlineData("outside")]
    [InlineData("otherProfile")]
    [InlineData("name")]
    public async Task RemoveExportRejectsStaleOrForeignReceipt(string mode)
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();await store.SaveAsync(new(key,Bytes));var chosen=Assert.Single(await store.ListSavedExportsAsync(key));
        var other=new FileMessageSourceExports(directory.File("other"));Directory.CreateDirectory(directory.File("other"));var foreign=await other.SaveAsync(new(key,Bytes));var export=chosen.Export;
        SavedMessageSourceExport stale=mode switch
        {
            "savedAt"=>chosen with{SavedAt=chosen.SavedAt.AddSeconds(-1)},
            "hash"=>chosen with{Export=export with{Sha256=new string('0',64)}},
            "bytes"=>chosen with{Export=export with{Bytes=export.Bytes+1}},
            "account"=>chosen with{Export=export with{Key=key with{AccountId=AccountId.New()}}},
            "uid"=>chosen with{Export=export with{Key=key with{Uid=2}}},
            "outside"=>chosen with{Export=export with{Path=directory.File(Path.GetFileName(export.Path))}},
            "otherProfile"=>chosen with{Export=foreign},
            _=>chosen with{Export=export with{Path=Path.Combine(store.ExportDirectory,"message-"+Guid.NewGuid().ToString("N")+".eml")}},
        };
        var failure=await Assert.ThrowsAnyAsync<Exception>(()=>store.RemoveExportAsync(stale));
        if(mode is "outside" or "otherProfile")Assert.IsAssignableFrom<ArgumentException>(failure);
        else Assert.Equal("This saved original changed or was already removed. List again; nothing was deleted.",Assert.IsType<InvalidDataException>(failure).Message);
        Assert.Equal(chosen,Assert.Single(await store.ListSavedExportsAsync(key)));Assert.Equal(Bytes,File.ReadAllBytes(export.Path));Assert.Equal(Bytes,File.ReadAllBytes(foreign.Path));
    }
    private static async Task<int> CopyBytes(Stream destination,int maximum,CancellationToken token){await destination.WriteAsync(Bytes,token);return Bytes.Length;}
    internal static readonly byte[] Bytes=Encoding.ASCII.GetBytes("Subject: Saved source\r\nContent-Type: text/plain\r\n\r\nSaved original bytes");
    internal static MailMessageKey Key()=>new(AccountId.New(),"Archive/台北",7,1);
}
