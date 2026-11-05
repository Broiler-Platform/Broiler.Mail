using System.Text.Json.Nodes;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class OrphanMailTests
{
    [Fact]
    public async Task DiscoveryIsExplicitBoundedAndIncludesOldEpochsAndEmptyHeads()
    {
        using var f=await Fixture.Create();byte[] before=File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath,"account-collection.json"));
        var page=await f.Storage.FindOrphanDownloadedMailAsync();Assert.False(page.HasMore);Assert.Equal(2,page.Accounts.Count);
        var row=page.Accounts.Single(item=>item.AccountId==f.Orphan.Id);Assert.Equal(2,row.HeaderCount);Assert.Equal(2,row.DownloadedBodyCount);Assert.Equal(1,row.MailboxCount);
        var empty=page.Accounts.Single(item=>item.AccountId==f.Empty);Assert.Equal(0,empty.HeaderCount);Assert.Equal(1,empty.MailboxCount);
        Assert.DoesNotContain(page.Accounts,item=>item.AccountId==f.Active.Id);Assert.Equal(before,File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath,"account-collection.json")));
    }
    [Fact]
    public async Task DiscoveryStopsAt64AndReportsMoreWithoutDiscardingMail()
    {
        using var f=await Fixture.Create();for(int i=0;i<64;i++)await f.Cache.SaveSummariesAsync(new(AccountId.New(),"INBOX",7),[],DateTimeOffset.UtcNow);
        var page=await f.Storage.FindOrphanDownloadedMailAsync();Assert.Equal(64,page.Accounts.Count);Assert.True(page.HasMore);
        Assert.Equal(page.Accounts.Select(row=>row.AccountId.ToString()).Order(StringComparer.Ordinal),page.Accounts.Select(row=>row.AccountId.ToString()));
    }
    [Fact]
    public async Task LegacyAccountIsExcludedWithoutImportingOrRewritingItsConfiguration()
    {
        using var directory=new TestDirectory();using var profile=MailProfile.Open(directory.Root);var account=TestDirectory.Profile();await new JsonAccountStore(profile.AccountsPath).SaveAsync(account);
        var cache=new SqliteMailCache(profile.CachePath);await cache.SaveSummariesAsync(new(account.Id,"INBOX",7),[],DateTimeOffset.UtcNow);
        var storage=new ProfileStorageService(profile,cache);byte[] bytes=File.ReadAllBytes(profile.AccountsPath);Assert.Empty((await storage.FindOrphanDownloadedMailAsync()).Accounts);
        Assert.False(File.Exists(Path.Combine(profile.DirectoryPath,"account-collection.json")));Assert.Equal(bytes,File.ReadAllBytes(profile.AccountsPath));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrFutureAccountsNeverBecomeAnEmptyConfiguredSet(bool future)
    {
        using var f=await Fixture.Create();string path=Path.Combine(f.Profile.DirectoryPath,"account-collection.json");
        if(future){var json=JsonNode.Parse(File.ReadAllText(path))!;json["schemaVersion"]=999;File.WriteAllText(path,json.ToJsonString());}else File.WriteAllText(path,"broken");
        byte[] bytes=File.ReadAllBytes(path);await Assert.ThrowsAsync<InvalidDataException>(()=>f.Storage.FindOrphanDownloadedMailAsync());
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Storage.ClearOrphanDownloadedMailAsync(f.Orphan.Id,f.Directory.File("refused")));
        Assert.Equal(bytes,File.ReadAllBytes(path));Assert.False(Directory.Exists(f.Directory.File("refused")));Assert.Equal(2,(await f.Storage.GetAccountDownloadedMailInfoAsync(f.Orphan.Id)).HeaderCount);
    }
    [Fact]
    public async Task ReAddedAccountIsRecheckedBeforeAnyBackupOrDeletion()
    {
        using var f=await Fixture.Create();Assert.Contains((await f.Storage.FindOrphanDownloadedMailAsync()).Accounts,row=>row.AccountId==f.Orphan.Id);
        await f.Accounts.SaveAsync(f.Orphan);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Storage.ClearOrphanDownloadedMailAsync(f.Orphan.Id,f.Directory.File("refused")));
        Assert.False(Directory.Exists(f.Directory.File("refused")));Assert.Equal(2,(await f.Storage.GetAccountDownloadedMailInfoAsync(f.Orphan.Id)).HeaderCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryBackedOrphanClearKeepsConfiguredMailDraftsPreferencesAndIndex(bool indexed)
    {
        using var f=await Fixture.Create();if(indexed)await f.Cache.RebuildSearchIndexAsync();await f.Model.Composer.SaveAsync();
        byte[] config=File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath,"account-collection.json"));
        var result=await f.Storage.ClearOrphanDownloadedMailAsync(f.Orphan.Id,f.Directory.File("backup"));Assert.Equal(2,result.RemovedHeaderCount);
        Assert.Null(await f.Cache.ReadBodyAsync(new(f.Orphan.Id,"INBOX",7),1));Assert.Null(await f.Cache.ReadBodyAsync(new(f.Orphan.Id,"INBOX",8),1));
        Assert.Equal("Active body",(await f.Cache.ReadBodyAsync(new(f.Active.Id,"INBOX",7),1))!.PlainText);Assert.Equal(config,File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath,"account-collection.json")));
        Assert.Equal("INBOX",Assert.Single(await f.Folders.LoadAsync(f.Orphan.Id)));Assert.Null(await f.Folders.LoadProgressAsync(f.Orphan.Id,["INBOX"]));
        Assert.Empty((await f.Folders.LoadProgressAsync(f.Active.Id,["INBOX"]))!);if(indexed){Assert.True((await f.Cache.GetSearchIndexInfoAsync()).Ready);Assert.Equal(1,(await f.Cache.GetSearchIndexInfoAsync()).Documents);}
        string restored=await f.Storage.RestoreBackupAsync(result.BackupDirectory,f.Directory.File("restored"));var cache=new SqliteMailCache(Path.Combine(restored,"mail-cache.db"));Assert.Equal("Orphan body",(await cache.ReadBodyAsync(new(f.Orphan.Id,"INBOX",8),1))!.PlainText);
        Assert.DoesNotContain((await f.Storage.FindOrphanDownloadedMailAsync()).Accounts,row=>row.AccountId==f.Orphan.Id);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("pending")]
    [InlineData("backup-failure")]
    public async Task CanceledOrBlockedCleanupPreservesMail(string mode)
    {
        using var f=await Fixture.Create();string path=f.Directory.File("recovery");
        if(mode=="pending")await new MutationIntentJournal(f.Profile.CachePath).BeginAsync(new(f.Active.Id,"INBOX",7,1),"Archive");
        if(mode=="backup-failure")Directory.CreateDirectory(path);
        using var cancellation=new CancellationTokenSource();string? backup=null;
        if(mode=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Storage.ClearOrphanDownloadedMailAsync(f.Orphan.Id,path,p=>{backup=p;cancellation.Cancel();},cancellation.Token));
        else if(mode=="pending")await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Storage.ClearOrphanDownloadedMailAsync(f.Orphan.Id,path));
        else await Assert.ThrowsAsync<IOException>(()=>f.Storage.ClearOrphanDownloadedMailAsync(f.Orphan.Id,path));
        Assert.Equal(2,(await f.Storage.GetAccountDownloadedMailInfoAsync(f.Orphan.Id)).HeaderCount);if(mode=="cancel")Assert.True(Directory.Exists(backup));
    }
    [Fact]
    public async Task ViewModelRequiresExplicitSelectionAndKeepAndFailureReleaseMaintenance()
    {
        using var f=await Fixture.Create();var model=f.Model.Accounts!;Assert.Empty(model.OrphanMail);await model.FindOrphanMailAsync();Assert.Null(model.SelectedOrphanMail);Assert.False(model.RequestOrphanCleanup());
        model.SelectOrphanMail(f.Orphan.Id);Assert.True(model.RequestOrphanCleanup());Assert.False(model.CanSwitch);model.CancelOrphanCleanup();Assert.Null(model.PendingOrphanCleanup);
        Assert.True(f.Model.Composer.StartNew());f.Model.Composer.Edit("recipient@example.test","","","Unsent","Retained draft");var draft=f.Model.Composer.DraftId;
        model.SelectOrphanMail(f.Orphan.Id);Assert.True(model.RequestOrphanCleanup());await model.ConfirmOrphanCleanupAsync(new(true));Assert.Null(model.PendingOrphanCleanup);Assert.True(f.Model.Inbox.CanReceive);Assert.Equal(2,(await f.Storage.GetAccountDownloadedMailInfoAsync(f.Orphan.Id)).HeaderCount);
        await model.FindOrphanMailAsync();model.SelectOrphanMail(f.Orphan.Id);Assert.True(model.RequestOrphanCleanup());await model.ConfirmOrphanCleanupAsync();await f.Model.Inbox.CacheLoadTask;
        Assert.NotNull(model.LastOrphanCleanupBackup);Assert.Empty(model.OrphanMail);Assert.Equal(draft,f.Model.Composer.DraftId);Assert.Equal("Retained draft",f.Model.Composer.PlainText);Assert.True(f.Model.Inbox.CanReceive);
    }
    [Fact]
    public async Task AllAccountsRemovedStillAllowsExplicitDiscoveryAndCleanup()
    {
        using var f=await Fixture.Create(noAccounts:true);var model=f.Model.Accounts!;Assert.Empty(model.Profiles);Assert.True(model.CanFindOrphanMail);await model.FindOrphanMailAsync();model.SelectOrphanMail(f.Orphan.Id);
        Assert.True(model.RequestOrphanCleanup());await model.ConfirmOrphanCleanupAsync();Assert.NotNull(model.LastOrphanCleanupBackup);Assert.Equal(0,(await f.Storage.GetAccountDownloadedMailInfoAsync(f.Orphan.Id)).HeaderCount);
    }
    internal sealed class Fixture:IDisposable
    {
        public TestDirectory Directory=new();public MailProfile Profile=null!;public SqliteMailCache Cache=null!;public ProfileStorageService Storage=null!;public JsonAccountCollectionStore Accounts=null!;public JsonOfflineFolderStore Folders=null!;
        public AccountProfile Active=TestDirectory.Profile(),Orphan=TestDirectory.Profile();public AccountId Empty=AccountId.New();public MailApplication Application=null!;public MailShellViewModel Model=null!;
        public static async Task<Fixture> Create(bool noAccounts=false)
        {
            var f=new Fixture();f.Profile=MailProfile.Open(f.Directory.Root);f.Cache=new(f.Profile.CachePath);f.Storage=new(f.Profile,f.Cache);f.Accounts=new(f.Directory.Root);
            await f.Accounts.SaveAndSelectAsync(f.Active);if(noAccounts)await f.Accounts.RemoveAsync(f.Active.Id);
            f.Folders=new(Path.Combine(f.Directory.Root,"offline-folders.json"));foreach(var account in new[]{f.Active,f.Orphan}){await f.Folders.SaveAsync(account.Id,["INBOX"]);await f.Folders.SaveProgressAsync(account.Id,["INBOX"],[]);}
            foreach(var (account,epoch,text) in new[]{(f.Active,7u,"Active body"),(f.Orphan,7u,"Orphan body"),(f.Orphan,8u,"Orphan body")})
            {var box=new MailboxKey(account.Id,"INBOX",epoch);var key=new MailMessageKey(account.Id,"INBOX",epoch,1);await f.Cache.SaveSummariesAsync(box,[new(){Key=key,Subject="Fixture",Sender="fixture@example.test"}],DateTimeOffset.UtcNow);await f.Cache.SaveBodyAsync(box,new(key,text));}
            await f.Cache.SaveSummariesAsync(new(f.Empty,"Empty",7),[],DateTimeOffset.UtcNow);
            f.Application=new(f.Accounts,new JsonSettingsStore(f.Profile.SettingsPath),new TestMailReceiver(),new Sender(),new TestCredentialStore(),drafts:new JsonDraftStore(f.Profile.DraftsPath),cache:f.Cache,storage:f.Storage);
            await f.Application.InitializeAsync();f.Model=f.Application.CreateViewModel();await f.Model.Inbox.CacheLoadTask;return f;
        }
        public void Dispose(){Model.Inbox.Dispose();Model.Composer.Dispose();Application.Dispose();Profile.Dispose();Directory.Dispose();}
    }
    private sealed class Sender:IMailSender{public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken token=default)=>throw new InvalidOperationException("No transport in fixture.");}
}
