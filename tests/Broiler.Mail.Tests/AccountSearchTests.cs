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
    public async Task AllFolderPagesFollowNewestFirstThenFolderThenUidAcrossEpochsWithoutDuplicates(bool indexed)
    {
        using var f=await Fixture.Create();if(indexed)await f.Cache.RebuildSearchIndexAsync();
        var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=2,Field=indexed?MailHeaderSearchField.IndexedSubject:MailHeaderSearchField.Subject};var all=new List<MailMessageKey>();
        while(true)
        {
            var page=await new SqliteMailCache(f.Path).SearchHeadersAsync(query);Assert.NotNull(page.AccountMailboxSnapshot);all.AddRange(page.Messages.Select(row=>row.Key));
            if(!page.HasMore)break;query=MailHeaderSearchOrder.Next(query,page);Assert.Equal(page.Messages[^1].ReceivedAt,query.BeforeReceivedAt);
        }
        // Uid n of every folder is received n minutes before the base, so each date ties across the six folders,
        // which then list ascending in SQLite BINARY (UTF-8 byte) order.
        var expected=new[]{"\U00010000","\uE000","台北","Z","INBOX","Archive"};Array.Reverse(expected);Assert.Equal(new[]{1u,2u,3u}.SelectMany(uid=>expected.Select(folder=>(folder,uid))),all.Select(key=>(key.MailboxId,key.Uid)));
        Assert.Equal(18,all.Distinct().Count());Assert.All(all,key=>Assert.Equal(f.Account.Id,key.AccountId));Assert.All(all.Where(key=>key.MailboxId=="Archive"),key=>Assert.Equal(9u,key.UidValidity));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChronologicalOrderPutsUnknownDatesLastAndBreaksTiesByFolderAscThenUidDesc(bool indexed)
    {
        using var directory=new TestDirectory();var cache=new SqliteMailCache(directory.File("mail.db"));var account=AccountId.New();
        var rows=new (string Folder,uint Uid,DateTimeOffset? Received)[]
        {
            ("INBOX",1,null),("INBOX",2,ReceivedAtBase.AddHours(1)),("INBOX",8,ReceivedAtBase),("INBOX",9,ReceivedAtBase),
            ("Archive",3,ReceivedAtBase.AddHours(-2)),("Archive",4,null),("Archive",5,ReceivedAtBase),("Sent",6,null),("Sent",7,ReceivedAtBase.AddHours(-1)),
        };
        foreach(var folder in rows.GroupBy(row=>row.Folder))
            await cache.SaveSummariesAsync(new(account,folder.Key,7),folder.Select(row=>Row(account,row.Folder,7,row.Uid) with{ReceivedAt=row.Received}).ToArray(),DateTimeOffset.UtcNow);
        if(indexed)await cache.RebuildSearchIndexAsync();
        var query=new MailHeaderSearchQuery(account,"needle"){MaximumCount=2,Field=indexed?MailHeaderSearchField.IndexedSubject:MailHeaderSearchField.Subject};var all=new List<MailMessageSummary>();int pages=0;
        while(true){var page=await cache.SearchHeadersAsync(query);pages++;all.AddRange(page.Messages);if(!page.HasMore)break;query=MailHeaderSearchOrder.Next(query,page);}
        Assert.Equal(new[]{("INBOX",2u),("Archive",5u),("INBOX",9u),("INBOX",8u),("Sent",7u),("Archive",3u),("Archive",4u),("INBOX",1u),("Sent",6u)},all.Select(row=>(row.Key.MailboxId,row.Key.Uid)));
        Assert.Equal(5,pages);Assert.Equal(9,all.Select(row=>row.Key).Distinct().Count());
        // The managed comparison the reader validates pages with agrees with SQLite's order.
        Assert.All(all.Zip(all.Skip(1)),pair=>Assert.True(MailHeaderSearchOrder.CompareChronological(pair.First.ReceivedAt,pair.First.Key,pair.Second.ReceivedAt,pair.Second.Key)>0));
    }
    [Fact]
    public async Task CompareChronologicalMatchesSqliteOrderForRandomDatesAndUnicodeFolders()
    {
        using var directory=new TestDirectory();var cache=new SqliteMailCache(directory.File("mail.db"));var account=AccountId.New();var random=new Random(20261126);
        var folders=new[]{"INBOX","Archive","archive","Z","台北","Ä",char.ConvertFromUtf32(0xE000),char.ConvertFromUtf32(0x10000)};
        var dates=Enumerable.Range(0,12).Select(i=>(DateTimeOffset?)ReceivedAtBase.AddMinutes(-7*i)).Append(null).ToArray();
        var saved=new List<MailMessageSummary>();
        foreach(string folder in folders)
        {
            var folderRows=Enumerable.Range(1,25).Select(uid=>Row(account,folder,7,(uint)uid) with{ReceivedAt=dates[random.Next(dates.Length)]}).ToArray();
            await cache.SaveSummariesAsync(new(account,folder,7),folderRows,DateTimeOffset.UtcNow);saved.AddRange(folderRows);
        }
        var result=await cache.SearchHeadersAsync(new(account,"needle"){MaximumCount=500});
        var expected=saved.ToList();expected.Sort((left,right)=>MailHeaderSearchOrder.CompareChronological(right.ReceivedAt,right.Key,left.ReceivedAt,left.Key));
        Assert.Equal(200,result.Messages.Count);Assert.Equal(expected.Select(row=>row.Key),result.Messages.Select(row=>row.Key));
    }
    [Fact]
    public async Task ChronologicalContinuationRejectsAMismatchedCursorDate()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var page=await f.Cache.SearchHeadersAsync(query);var next=MailHeaderSearchOrder.Next(query,page);
        Assert.Equal(ReceivedAtBase.AddMinutes(-1),next.BeforeReceivedAt);
        foreach(var bad in new[]{next with{BeforeReceivedAt=next.BeforeReceivedAt!.Value.AddSeconds(1)},next with{BeforeReceivedAt=null}})
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Cache.SearchHeadersAsync(bad));
        // The same instant in another offset is the same date.
        Assert.Single((await f.Cache.SearchHeadersAsync(next with{BeforeReceivedAt=next.BeforeReceivedAt!.Value.ToOffset(TimeSpan.FromHours(5))})).Messages);
    }
    [Fact]
    public async Task ChronologicalContinuationSurvivesCursorRowDeletion()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=2};var page=await f.Cache.SearchHeadersAsync(query);
        Assert.Equal(new[]{("Archive",1u),("INBOX",1u)},page.Messages.Select(row=>(row.Key.MailboxId,row.Key.Uid)));
        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.Path+";Pooling=False"))
        {await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="DELETE FROM messages WHERE mailbox='INBOX' AND uid=1";await command.ExecuteNonQueryAsync();}
        var all=page.Messages.Select(row=>row.Key).ToList();query=MailHeaderSearchOrder.Next(query,page);
        while(true){page=await f.Cache.SearchHeadersAsync(query);all.AddRange(page.Messages.Select(row=>row.Key));if(!page.HasMore)break;query=MailHeaderSearchOrder.Next(query,page);}
        Assert.Equal(("Z",1u),(all[2].MailboxId,all[2].Uid));Assert.Equal(18,all.Count);Assert.Equal(18,all.Distinct().Count());
    }
    [Fact]
    public async Task SameEpochNewerArrivalsDoNotShiftLaterPages()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=4};var page=await f.Cache.SearchHeadersAsync(query);var all=page.Messages.Select(row=>row.Key).ToList();
        // A newer message saved into a current epoch keeps the snapshot; it sorts before the cursor, so later pages never show it.
        await f.Cache.SaveSummariesAsync(new(f.Account.Id,"INBOX",7),[Row(f.Account.Id,"INBOX",7,8,ReceivedAtBase.AddHours(1))],DateTimeOffset.UtcNow);
        query=MailHeaderSearchOrder.Next(query,page);
        while(true){page=await f.Cache.SearchHeadersAsync(query);all.AddRange(page.Messages.Select(row=>row.Key));if(!page.HasMore)break;query=MailHeaderSearchOrder.Next(query,page);}
        Assert.Equal(18,all.Count);Assert.Equal(18,all.Distinct().Count());Assert.DoesNotContain(all,key=>key.Uid==8);
        Assert.Equal(8u,(await f.Cache.SearchHeadersAsync(new(f.Account.Id,"needle"){MaximumCount=1})).Messages.Single().Key.Uid);
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
        await Assert.ThrowsAsync<MailSearchScopeChangedException>(()=>f.Cache.SearchHeadersAsync(MailHeaderSearchOrder.Next(query,page)));
    }
    [Fact]
    public async Task SnapshotIsAccountBoundAndInvalidOrUnboundContinuationsAreRefused()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var page=await f.Cache.SearchHeadersAsync(query);var key=page.Messages[^1].Key;
        foreach(var bad in new[]{query with{BeforeMessage=key},query with{AccountMailboxSnapshot="bad"},query with{BeforeMessage=key with{AccountId=AccountId.New()},AccountMailboxSnapshot=page.AccountMailboxSnapshot},query with{MailboxId=key.MailboxId,AccountMailboxSnapshot=page.AccountMailboxSnapshot},
            // A continuation date belongs only to an all-folder continuation.
            query with{BeforeReceivedAt=page.Messages[^1].ReceivedAt},query with{MailboxId=key.MailboxId,BeforeMessage=key,BeforeReceivedAt=page.Messages[^1].ReceivedAt}})
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Cache.SearchHeadersAsync(bad));
        await Assert.ThrowsAsync<MailSearchScopeChangedException>(()=>f.Cache.SearchHeadersAsync(query with{AccountId=AccountId.New(),AccountMailboxSnapshot=page.AccountMailboxSnapshot}));
    }
    [Fact]
    public async Task ForeignHeadAndSameEpochContentUpdatesDoNotPretendToBeEpochChanges()
    {
        using var f=await Fixture.Create();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){MaximumCount=1};var page=await f.Cache.SearchHeadersAsync(query);
        await f.Cache.SaveSummariesAsync(new(AccountId.New(),"Foreign",99),[],DateTimeOffset.UtcNow);
        await f.Cache.SaveSummariesAsync(new(f.Account.Id,"INBOX",7),[Row(f.Account.Id,"INBOX",7,8)],DateTimeOffset.UtcNow);
        var next=await f.Cache.SearchHeadersAsync(MailHeaderSearchOrder.Next(query,page));Assert.Equal(page.AccountMailboxSnapshot,next.AccountMailboxSnapshot);Assert.Single(next.Messages);
        // INBOX uid 8 is newer than the cursor, so the continuation passes over it.
        Assert.Equal(("INBOX",1u),(next.Messages[0].Key.MailboxId,next.Messages[0].Key.Uid));
    }
    [Theory]
    [InlineData(MailHeaderSearchField.Subject)]
    [InlineData(MailHeaderSearchField.BodyPreview)]
    [InlineData(MailHeaderSearchField.IndexedSubject)]
    [InlineData(MailHeaderSearchField.IndexedBodyPreview)]
    public async Task AllFolderScopeRetainsFlagDateAndBodyFilters(MailHeaderSearchField field)
    {
        using var f=await Fixture.Create();await f.Cache.RebuildSearchIndexAsync();var query=new MailHeaderSearchQuery(f.Account.Id,"needle"){Field=field,IsRead=false,IsStarred=true,IsBodyCached=true,ReceivedFrom=ReceivedAtBase.AddDays(-1),ReceivedBefore=ReceivedAtBase.AddMinutes(1),MaximumCount=2};
        var all=new List<MailMessageSummary>();while(true){var page=await f.Cache.SearchHeadersAsync(query);all.AddRange(page.Messages);if(!page.HasMore)break;query=MailHeaderSearchOrder.Next(query,page);}
        Assert.Equal(6,all.Count);Assert.All(all,row=>{Assert.Equal(1u,row.Key.Uid);Assert.True(row.IsBodyCached);});
    }
    [Fact]
    public async Task SubmittedAccountScopeAndHistoryStayPinnedWhileEditsChange()
    {
        using var f=await Fixture.Create();for(int start=4;start<=510;start+=100){var rows=Enumerable.Range(start,Math.Min(100,511-start)).Select(uid=>Row(f.Account.Id,"Z",7,(uint)uid)).ToArray();await f.Cache.SaveSummariesAsync(new(f.Account.Id,"Z",7),rows,DateTimeOffset.UtcNow);}
        using var model=await f.Model();model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();Assert.True(model.IsAccountSearchActive);Assert.Equal(500,model.Messages.Count);Assert.True(model.CanLoadNextSearchPage);
        model.SetSearchAllSavedFolders(false);model.SetSearchText("edited");await model.LoadNextSearchPageAsync();Assert.True(model.IsAccountSearchActive);Assert.Contains("all saved folders",model.SearchNotice);Assert.DoesNotContain("edited",model.SearchNotice);
        // Newest first: page 1 held every folder's three rows and Z 4-485, so page 2 is the oldest Z rows.
        Assert.Equal(Enumerable.Range(486,25).Select(uid=>("Z",(uint)uid)),model.Messages.Select(row=>(row.Key.MailboxId,row.Key.Uid)));
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
        // Z is strictly newer than INBOX, so the given order is valid whether results sort by folder or by date.
        var rows=new[]{Row(f.Account.Id,"Z",7,1,ReceivedAtBase),Row(f.Account.Id,"INBOX",7,1,ReceivedAtBase.AddHours(-1))};
        if(mode=="foreign")rows[0]=rows[0] with{Key=rows[0].Key with{AccountId=AccountId.New()}};else if(mode=="order")Array.Reverse(rows);else if(mode=="duplicate")rows[1]=rows[0];
        cache.Release.SetResult(new(rows,false){AccountMailboxSnapshot=mode=="snapshot"?"bad":new string('a',64)});
        model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();Assert.NotNull(model.ListProblem);Assert.Empty(model.Messages);Assert.Null(model.Body);
    }
    [Theory]
    [InlineData("older",true)]
    [InlineData("tie-later-folder",true)]
    [InlineData("newer",false)]
    [InlineData("tie-earlier-folder",false)]
    [InlineData("cursor",false)]
    public async Task ViewModelNextAllFolderPagePassesTheCursorDateAndMustFollowIt(string mode,bool valid)
    {
        using var f=await Fixture.Create();var cache=new HeldSearch(f.Cache);using var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),new ImmediateUiDispatcher(),cache);model.SetAccount(f.Account);await model.CacheLoadTask;
        string snapshot=new('a',64);
        cache.Release.SetResult(new([Row(f.Account.Id,"Z",7,2,ReceivedAtBase),Row(f.Account.Id,"INBOX",7,1,ReceivedAtBase.AddHours(-1))],true){AccountMailboxSnapshot=snapshot});
        model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();Assert.Null(model.ListProblem);Assert.Contains("Sorted by newest received date",model.SearchNotice);
        cache.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);cache.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var next=model.LoadNextSearchPageAsync();await cache.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MailMessageKey(f.Account.Id,"INBOX",7,1),cache.Query!.BeforeMessage);Assert.Equal(ReceivedAtBase.AddHours(-1),cache.Query.BeforeReceivedAt);Assert.Equal(snapshot,cache.Query.AccountMailboxSnapshot);
        var row=mode switch
        {
            "older"=>Row(f.Account.Id,"Z",7,1,ReceivedAtBase.AddHours(-2)),
            // Equal dates list folders ascending: Z follows the INBOX cursor, Archive precedes it.
            "tie-later-folder"=>Row(f.Account.Id,"Z",7,1,ReceivedAtBase.AddHours(-1)),
            "tie-earlier-folder"=>Row(f.Account.Id,"Archive",9,1,ReceivedAtBase.AddHours(-1)),
            "cursor"=>Row(f.Account.Id,"INBOX",7,1,ReceivedAtBase.AddHours(-1)),
            _=>Row(f.Account.Id,"Z",7,1,ReceivedAtBase),
        };
        cache.Release.SetResult(new([row],false){AccountMailboxSnapshot=snapshot});await next;
        if(valid){Assert.Null(model.ListProblem);Assert.Equal(row.Key,Assert.Single(model.Messages).Key);Assert.Equal(2,model.SearchPageNumber);}
        else{Assert.NotNull(model.ListProblem);Assert.Empty(model.Messages);}
    }
    [Fact]
    public async Task ViewModelNextFolderPageContinuesByUidWithoutADate()
    {
        using var f=await Fixture.Create();var cache=new HeldSearch(f.Cache);using var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),new ImmediateUiDispatcher(),cache);model.SetAccount(f.Account);await model.CacheLoadTask;
        // Within one folder the order is the UID, whatever the dates say.
        cache.Release.SetResult(new([Row(f.Account.Id,"INBOX",7,5,ReceivedAtBase.AddHours(-3)),Row(f.Account.Id,"INBOX",7,3,ReceivedAtBase)],true));
        model.SetSearch("needle");await model.SearchSavedHeadersAsync();Assert.Null(model.ListProblem);Assert.False(model.IsAccountSearchActive);Assert.DoesNotContain("Sorted by newest",model.SearchNotice);
        cache.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);cache.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var next=model.LoadNextSearchPageAsync();await cache.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MailMessageKey(f.Account.Id,"INBOX",7,3),cache.Query!.BeforeMessage);Assert.Null(cache.Query.BeforeReceivedAt);Assert.Null(cache.Query.AccountMailboxSnapshot);
        cache.Release.SetResult(new([Row(f.Account.Id,"INBOX",7,2,ReceivedAtBase.AddHours(1))],false));await next;
        Assert.Null(model.ListProblem);Assert.Equal(2u,Assert.Single(model.Messages).Key.Uid);
    }
    [Fact]
    public async Task RefreshResultsRestartsFromTheNewestPageAfterTheFoldersChange()
    {
        using var f=await Fixture.Create();for(int start=4;start<=510;start+=100){var rows=Enumerable.Range(start,Math.Min(100,511-start)).Select(uid=>Row(f.Account.Id,"Z",7,(uint)uid)).ToArray();await f.Cache.SaveSummariesAsync(new(f.Account.Id,"Z",7),rows,DateTimeOffset.UtcNow);}
        var cache=new RecordingSearch(f.Cache);using var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),new ImmediateUiDispatcher(),cache);model.SetAccount(f.Account);await model.CacheLoadTask;
        model.SetSearch("needle");model.SetSearchScope(MailSearchScope.Everywhere);model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();
        Assert.Equal(500,model.Messages.Count);Assert.False(model.CanRefreshSearchResults);Assert.Null(cache.Queries[^1].Engine);
        await model.LoadNextSearchPageAsync();Assert.Null(model.ListProblem);Assert.Equal(25,model.Messages.Count);Assert.False(model.CanRefreshSearchResults);Assert.NotNull(cache.Queries[^1].Engine);
        // A new folder head changes the account snapshot, so Retry would only repeat the stale continuation.
        await f.Cache.SaveSummariesAsync(new(f.Account.Id,"New",7),[],DateTimeOffset.UtcNow);
        await model.LoadPreviousSearchPageAsync();await model.LoadNextSearchPageAsync();
        Assert.NotNull(model.ListProblem);Assert.Contains("Start a new search",model.ListProblem!.Text);Assert.Contains("Refresh results",model.ListProblem.Text);Assert.True(model.CanRefreshSearchResults);
        model.SetSearchText("edited");await model.RefreshSearchResultsAsync();
        var refreshed=cache.Queries[^1];Assert.Null(refreshed.BeforeMessage);Assert.Null(refreshed.BeforeReceivedAt);Assert.Null(refreshed.AccountMailboxSnapshot);Assert.Null(refreshed.Engine);Assert.Equal("needle",refreshed.Text);
        Assert.Null(model.ListProblem);Assert.False(model.CanRefreshSearchResults);Assert.False(model.CanLoadPreviousSearchPage);Assert.Equal(1,model.SearchPageNumber);Assert.Equal(500,model.Messages.Count);
        Assert.Equal(("Archive",1u),(model.Messages[0].Key.MailboxId,model.Messages[0].Key.Uid));
        await model.LoadNextSearchPageAsync();Assert.Null(model.ListProblem);Assert.Equal(25,model.Messages.Count);
    }
    [Fact]
    public async Task OtherSearchFailuresDoNotOfferRefreshResults()
    {
        using var f=await Fixture.Create();var cache=new HeldSearch(f.Cache);using var model=new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(),new ImmediateUiDispatcher(),cache);model.SetAccount(f.Account);await model.CacheLoadTask;
        cache.Release.SetException(new InvalidDataException("broken"));model.SetSearch("needle");model.SetSearchAllSavedFolders(true);await model.SearchSavedHeadersAsync();
        Assert.NotNull(model.ListProblem);Assert.False(model.CanRefreshSearchResults);Assert.True(model.CanRetry);
        cache.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);cache.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.Release.SetException(new MailSearchScopeChangedException("Saved account folders or UIDVALIDITY changed. Start a new search with Search, or use Refresh results."));await model.RetryAsync();
        Assert.True(model.CanRefreshSearchResults);
        await model.ClearSearchAsync();Assert.False(model.CanRefreshSearchResults);
    }
    private sealed class RecordingSearch(SqliteMailCache inner):IMailCache,IMailLocalSearch
    {
        public readonly List<MailHeaderSearchQuery> Queries=[];
        public Task<MailHeaderSearchResult> SearchHeadersAsync(MailHeaderSearchQuery query,CancellationToken cancellationToken=default){lock(Queries)Queries.Add(query);return inner.SearchHeadersAsync(query,cancellationToken);}
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account,string mailbox,CancellationToken cancellationToken=default)=>inner.GetLatestMailboxAsync(account,mailbox,cancellationToken);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox,int count,uint? afterUid=null,CancellationToken cancellationToken=default)=>inner.ReadSummariesAsync(mailbox,count,afterUid,cancellationToken);
        public Task SaveSummariesAsync(MailboxKey mailbox,IReadOnlyList<MailMessageSummary> rows,DateTimeOffset date,CancellationToken cancellationToken=default)=>inner.SaveSummariesAsync(mailbox,rows,date,cancellationToken);
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox,uint uid,CancellationToken cancellationToken=default)=>inner.ReadBodyAsync(mailbox,uid,cancellationToken);
        public Task SaveBodyAsync(MailboxKey mailbox,MailMessageBody body,CancellationToken cancellationToken=default)=>inner.SaveBodyAsync(mailbox,body,cancellationToken);
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
    // A fixed instant, so the fixture never depends on the clock. By default a row is received uid minutes
    // before it: dates fall with the uid inside one folder, and equal uids tie across folders.
    internal static readonly DateTimeOffset ReceivedAtBase=new(2026,3,14,9,0,0,TimeSpan.Zero);
    internal static MailMessageSummary Row(AccountId account,string folder,uint epoch,uint uid,DateTimeOffset? receivedAt=null)=>new(){Key=new(account,folder,epoch,uid),Sender="sender@example.test",Subject="needle",IsRead=uid==2,IsStarred=uid==1,ReceivedAt=receivedAt??ReceivedAtBase.AddMinutes(-uid)};
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
