using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class AccountSearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllFolderPagesFollowSqliteBinaryOrderAcrossEpochsWithoutDuplicates(bool indexed)
    {
        using var f=await Fixture.Create();if(indexed)await f.Cache.RebuildSearchIndexAsync();
        var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=2,Field=indexed?MailHeaderSearchField.IndexedSubject:MailHeaderSearchField.Subject};var all=new List<MailMessageKey>();
        while(true)
        {
            var page=await new SqliteMailCache(f.Path).SearchHeadersAsync(query);Assert.NotNull(page.AccountMailboxSnapshot);all.AddRange(page.Messages.Select(row=>row.Key));
            if(!page.HasMore)break;query=query with{BeforeMessage=page.Messages[^1].Key,AccountMailboxSnapshot=page.AccountMailboxSnapshot};
        }
        var expected=new[]{"\U00010000","\uE000","台北","Z","INBOX","Archive"};Assert.Equal(expected.SelectMany(folder=>new[]{3u,2u,1u}.Select(uid=>(folder,uid))),all.Select(key=>(key.MailboxId,key.Uid)));
        Assert.Equal(18,all.Distinct().Count());Assert.All(all,key=>Assert.Equal(f.Account.Id,key.AccountId));Assert.All(all.Where(key=>key.MailboxId=="Archive"),key=>Assert.Equal(9u,key.UidValidity));
    }
    [Theory]
    [InlineData("add")]
    [InlineData("epoch")]
    [InlineData("delete")]
    public async Task AnyAccountHeadChangeInvalidatesContinuationEvenOutsideCursorFolder(string mode)
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var page=await f.Cache.SearchHeadersAsync(query);
        if(mode=="add")await f.Cache.SaveSummariesAsync(new(f.Account.Id,"New",7),[],DateTimeOffset.UtcNow);
        else if(mode=="epoch")await f.Cache.SaveSummariesAsync(new(f.Account.Id,"Archive",10),[],DateTimeOffset.UtcNow);
        else
        {using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.Path+";Pooling=False");await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="DELETE FROM mailbox_heads WHERE mailbox='Archive'";await command.ExecuteNonQueryAsync();}
        await Assert.ThrowsAsync<MailSearchScopeChangedException>(()=>f.Cache.SearchHeadersAsync(query with{BeforeMessage=page.Messages[^1].Key,AccountMailboxSnapshot=page.AccountMailboxSnapshot}));
    }
    [Fact]
    public async Task SnapshotIsAccountBoundAndInvalidOrUnboundContinuationsAreRefused()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var page=await f.Cache.SearchHeadersAsync(query);var key=page.Messages[^1].Key;
        foreach(var bad in new[]{query with{BeforeMessage=key},query with{AccountMailboxSnapshot="bad"},query with{BeforeMessage=key with{AccountId=AccountId.New()},AccountMailboxSnapshot=page.AccountMailboxSnapshot},query with{MailboxId=key.MailboxId,AccountMailboxSnapshot=page.AccountMailboxSnapshot}})
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Cache.SearchHeadersAsync(bad));
        await Assert.ThrowsAsync<MailSearchScopeChangedException>(()=>f.Cache.SearchHeadersAsync(query with{AccountId=AccountId.New(),AccountMailboxSnapshot=page.AccountMailboxSnapshot}));
    }
    [Fact]
    public async Task ForeignHeadAndSameEpochContentUpdatesDoNotPretendToBeEpochChanges()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var page=await f.Cache.SearchHeadersAsync(query);
        await f.Cache.SaveSummariesAsync(new(AccountId.New(),"Foreign",99),[],DateTimeOffset.UtcNow);
        await f.Cache.SaveSummariesAsync(new(f.Account.Id,"INBOX",7),[Row(f.Account.Id,"INBOX",7,8)],DateTimeOffset.UtcNow);
        var next=await f.Cache.SearchHeadersAsync(query with{BeforeMessage=page.Messages[^1].Key,AccountMailboxSnapshot=page.AccountMailboxSnapshot});Assert.Equal(page.AccountMailboxSnapshot,next.AccountMailboxSnapshot);Assert.Single(next.Messages);
    }
    [Theory]
    [InlineData(MailHeaderSearchField.Subject)]
    [InlineData(MailHeaderSearchField.BodyPreview)]
    [InlineData(MailHeaderSearchField.IndexedSubject)]
    [InlineData(MailHeaderSearchField.IndexedBodyPreview)]
    public async Task AllFolderScopeRetainsFlagDateAndBodyFilters(MailHeaderSearchField field)
    {
        using var f=await Fixture.Create();await f.Cache.RebuildSearchIndexAsync();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){Field=field,IsRead=false,IsStarred=true,IsBodyCached=true,ReceivedFrom=DateTimeOffset.UtcNow.AddDays(-2),ReceivedBefore=DateTimeOffset.UtcNow,MaximumCount=2};
        var all=new List<MailMessageSummary>();while(true){var page=await f.Cache.SearchHeadersAsync(query);all.AddRange(page.Messages);if(!page.HasMore)break;query=query with{BeforeMessage=page.Messages[^1].Key,AccountMailboxSnapshot=page.AccountMailboxSnapshot};}
        Assert.Equal(6,all.Count);Assert.All(all,row=>{Assert.Equal(1u,row.Key.Uid);Assert.True(row.IsBodyCached);});
    }
    [Fact]
    public async Task SubmittedAccountScopeAndHistoryStayPinnedWhileEditsChange()
    {
        using var f=await Fixture.Create();for(int start=4;start<=510;start+=100){var rows=Enumerable.Range(start,Math.Min(100,511-start)).Select(uid=>Row(f.Account.Id,"Z",7,(uint)uid)).ToArray();await f.Cache.SaveSummariesAsync(new(f.Account.Id,"Z",7),rows,DateTimeOffset.UtcNow);}
        using var model=await f.Model();model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();Assert.True(model.IsAccountSearchActive);Assert.Equal(500,model.Messages.Count);Assert.True(model.CanLoadNextSearchPage);
        model.SetSearchAllSavedFolders(false);model.SetSearchText("edited");await model.LoadNextSearchPageAsync();Assert.True(model.IsAccountSearchActive);Assert.Contains("all saved folders",model.SearchNotice);Assert.DoesNotContain("edited",model.SearchNotice);Assert.NotNull(model.Messages.FirstOrDefault(row=>row.Key.MailboxId=="Archive"));
        await model.LoadPreviousSearchPageAsync();Assert.Equal(500,model.Messages.Count);Assert.Equal(1,model.SearchPageNumber);
        model.SetSearch("needle");await model.SearchSavedHeadersAsync();Assert.False(model.IsAccountSearchActive);Assert.All(model.Messages,row=>Assert.Equal("INBOX",row.Key.MailboxId));
        model.SetAccount(f.Account with{IsEnabled=false});await model.CacheLoadTask;Assert.False(model.SearchAllSavedFolders);model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();Assert.True(model.IsAccountSearchActive);
    }
    [Fact]
    public async Task ResultSelectionReadsExactFolderBodyWithoutNetworkingOrChangingBaseFolder()
    {
        using var f=await Fixture.Create();using var model=await f.Model();model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();var key=model.Messages.Single(row=>row.Key.MailboxId=="Archive" && row.Key.Uid==1).Key;
        await model.SelectAsync(key);Assert.Equal("needle Archive",model.Body!.PlainText);Assert.Equal("INBOX",model.SelectedMailboxId);Assert.False(model.CanReceive);Assert.False(model.CanDownloadLoaded);
        await model.SelectAsync(model.Messages.First(row=>row.Key.Uid==2).Key);Assert.Null(model.Body);Assert.Contains("not saved",model.MessageProblem!.Text);
        await model.ClearSearchAsync();Assert.False(model.IsAccountSearchActive);Assert.Equal("INBOX",model.SelectedMailboxId);
    }
    [Fact]
    public async Task ChangedScopeExplainsFindRatherThanSuggestingCacheCorruption()
    {
        using var f=await Fixture.Create();using var model=await f.Model();model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();
        // Bind the same first-page query on Previous/Retry too, even when fewer than one page matches.
        var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var first=await f.Cache.SearchHeadersAsync(query);
        await f.Cache.SaveSummariesAsync(new(f.Account.Id,"New",7),[],DateTimeOffset.UtcNow);
        var error=await Assert.ThrowsAsync<MailSearchScopeChangedException>(()=>f.Cache.SearchHeadersAsync(query with{AccountMailboxSnapshot=first.AccountMailboxSnapshot}));Assert.Contains("Start a new search",error.Message);
        await model.SearchSavedHeadersAsync();Assert.Null(model.ListProblem);Assert.Contains("all saved folders",model.SearchNotice);
    }
    [Fact]
    public async Task AllFolderSnapshotHasABoundedHeadCountWhileFolderSearchStillWorks()
    {
        using var f=await Fixture.Create();using(var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.Path+";Pooling=False"))
        {
            await connection.OpenAsync();using var transaction=connection.BeginTransaction();using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText="INSERT INTO mailbox_heads(account,mailbox,epoch,received) VALUES($a,$m,7,$d)";command.Parameters.AddWithValue("$a",f.Account.Id.ToString());var name=command.Parameters.Add("$m",Microsoft.Data.Sqlite.SqliteType.Text);command.Parameters.AddWithValue("$d",DateTimeOffset.UtcNow.ToString("O"));
            for(int i=0;i<507;i++){name.Value="Extra"+i;command.ExecuteNonQuery();}transaction.Commit();
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Cache.SearchHeadersAsync(new(f.Account.Id,"needle")));
        Assert.Equal(3,(await f.Cache.SearchHeadersAsync(new(f.Account.Id,"needle"){MailboxId="INBOX"})).Messages.Count);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("dispose")]
    [InlineData("queued")]
    public async Task LateAccountSearchCannotReplaceAnotherContext(string mode)
    {
        using var f=await Fixture.Create();var cache=new HeldSearch(f.Cache);var dispatcher=new TestQueueDispatcher();using var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),dispatcher,cache);
        model.SetAccount(f.Account);await model.CacheLoadTask;dispatcher.Drain();model.SetSearch("needle");model.SetSearchAllSavedFolders(true);var task=model.SearchSavedHeadersAsync();await cache.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(cache.Query!.MailboxId);
        if(mode=="cancel")model.Cancel();else if(mode=="account"){model.SetAccount(TestDirectory.Profile());await model.CacheLoadTask;dispatcher.Drain();}else if(mode=="dispose")model.Dispose();
        cache.Release.SetResult(new([Row(f.Account.Id,"Z",7,1)],false){AccountMailboxSnapshot=new string('a',64)});await task;
        if(mode=="queued"){model.SetAccount(TestDirectory.Profile());await model.CacheLoadTask;}dispatcher.Drain();Assert.DoesNotContain(model.Messages,row=>row.Key.MailboxId=="Z");
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("order")]
    [InlineData("duplicate")]
    [InlineData("snapshot")]
    public async Task InvalidAccountSearchResultsCannotEnterTheReader(string mode)
    {
        using var f=await Fixture.Create();var cache=new HeldSearch(f.Cache);using var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),new ImmediateUiDispatcher(),cache);model.SetAccount(f.Account);await model.CacheLoadTask;
        var rows=new[]{Row(f.Account.Id,"Z",7,1),Row(f.Account.Id,"INBOX",7,1)};
        if(mode=="foreign")rows[0]=rows[0] with{Key=rows[0].Key with{AccountId=AccountId.New()}};else if(mode=="order")Array.Reverse(rows);else if(mode=="duplicate")rows[1]=rows[0];
        cache.Release.SetResult(new(rows,false){AccountMailboxSnapshot=mode=="snapshot"?"bad":new string('a',64)});
        model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();Assert.NotNull(model.ListProblem);Assert.Empty(model.Messages);Assert.Null(model.Body);
    }
    private sealed class HeldSearch(IMailCache inner):IMailCache,IMailLocalSearch
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<MailHeaderSearchResult> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);public MailHeaderSearchQuery? Query;
        public Task<MailHeaderSearchResult> SearchHeadersAsync(MailHeaderSearchQuery query,CancellationToken cancellationToken=default){Query=query;Entered.TrySetResult();return Release.Task;}
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account,string mailbox,CancellationToken cancellationToken=default)=>inner.GetLatestMailboxAsync(account,mailbox,cancellationToken);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox,int count,uint? afterUid=null,CancellationToken cancellationToken=default)=>inner.ReadSummariesAsync(mailbox,count,afterUid,cancellationToken);
        public Task SaveSummariesAsync(MailboxKey mailbox,IReadOnlyList<MailMessageSummary> rows,DateTimeOffset date,CancellationToken cancellationToken=default)=>inner.SaveSummariesAsync(mailbox,rows,date,cancellationToken);
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox,uint uid,CancellationToken cancellationToken=default)=>inner.ReadBodyAsync(mailbox,uid,cancellationToken);
        public Task SaveBodyAsync(MailboxKey mailbox,MailMessageBody body,CancellationToken cancellationToken=default)=>inner.SaveBodyAsync(mailbox,body,cancellationToken);
    }
    internal static MailMessageSummary Row(AccountId account,string folder,uint epoch,uint uid)=>new(){Key=new(account,folder,epoch,uid),Sender="sender@example.test",Subject="needle",IsRead=uid==2,IsStarred=uid==1,ReceivedAt=DateTimeOffset.UtcNow.AddDays(-1)};
    internal sealed class Fixture:IDisposable
    {
        public TestDirectory Directory=new();public string Path=>Directory.File("mail.db");public AccountProfile Account=TestDirectory.Profile();public SqliteMailCache Cache=null!;
        public static async Task<Fixture> Create()
        {
            var f=new Fixture();f.Cache=new(f.Path);await f.Cache.SaveSummariesAsync(new(f.Account.Id,"Archive",6),[Row(f.Account.Id,"Archive",6,99)],DateTimeOffset.UtcNow);
            foreach(string folder in new[]{"INBOX","Archive","Z","台北","\uE000","\U00010000"})
            {uint epoch=folder=="Archive"?9u:7u;await f.Cache.SaveSummariesAsync(new(f.Account.Id,folder,epoch),Enumerable.Range(1,3).Select(uid=>Row(f.Account.Id,folder,epoch,(uint)uid)).ToArray(),DateTimeOffset.UtcNow);await f.Cache.SaveBodyAsync(new(f.Account.Id,folder,epoch),new(new(f.Account.Id,folder,epoch,1),"needle "+folder));}
            var other=AccountId.New();await f.Cache.SaveSummariesAsync(new(other,"INBOX",7),[Row(other,"INBOX",7,200)],DateTimeOffset.UtcNow);return f;
        }
        public async Task<InboxViewModel> Model(){var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),new ImmediateUiDispatcher(),Cache);model.SetAccount(Account);await model.CacheLoadTask;return model;}
        public void Dispose()=>Directory.Dispose();
    }
}
