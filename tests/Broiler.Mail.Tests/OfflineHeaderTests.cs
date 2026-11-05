using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class OfflineHeaderTests
{
    [Fact]
    public async Task ExplicitPagesSaveAcrossFoldersWithoutBodiesAndReopenReadsLocally()
    {
        using var f = await Fixture.Create(); using var model = await f.Model();
        Assert.Empty(f.Receiver.Calls); Assert.True(model.CanReceiveOfflineHeaders);
        await model.ReceiveOfflineHeadersAsync(); Assert.Null(model.ListProblem);
        Assert.Equal(52, model.OfflineHeadersSaved); Assert.Equal(2, model.OfflineHeaderPagesSaved); Assert.True(model.HasOfflineHeaderContinuation);
        Assert.Empty(model.Messages); Assert.Null(model.Body); Assert.Equal("INBOX", model.SelectedMailboxId);
        Assert.Equal(50, (await f.Cache.ReadSummariesAsync(new(f.Account.Id,"INBOX",7),100)).Count);
        Assert.Equal(2, (await f.Cache.ReadSummariesAsync(new(f.Account.Id,"Archive",7),100)).Count);
        await model.ReceiveOfflineHeadersAsync(); Assert.Equal(102, model.OfflineHeadersSaved);
        await model.ReceiveOfflineHeadersAsync(); Assert.Equal(103, model.OfflineHeadersSaved); Assert.False(model.HasOfflineHeaderContinuation); Assert.False(model.CanReceiveOfflineHeaders);
        Assert.Equal(4, f.Receiver.Calls.Count); Assert.Equal(0, f.Receiver.Bodies);
        await model.ShowSavedHeadersAsync(); Assert.Equal(50,model.Messages.Count); Assert.True(model.IsShowingCachedMail); Assert.Equal(4,f.Receiver.Calls.Count);
        using var reopened = await f.Model(); Assert.Equal(50,reopened.Messages.Count); Assert.False(reopened.HasOfflineHeaderWalk); Assert.True(reopened.CanReceiveOfflineHeaders); Assert.Equal(4,f.Receiver.Calls.Count);
    }

    [Fact]
    public async Task FailedLaterFolderRetriesOnlyUncommittedPageBeforeAdvancingAnotherFolder()
    {
        using var f = await Fixture.Create(); bool fail = true;
        f.Receiver.Override = (account,folder,cursor) => folder=="Archive" && fail ? throw new MailConnectionException("Fixture stop") : Task.FromResult(Page(account,folder,folder=="INBOX"?101:2,cursor));
        using var model = await f.Model(); await model.ReceiveOfflineHeadersAsync();
        Assert.True(model.ProblemIsOfflineHeaderReceive); Assert.True(model.CanRetry); Assert.Equal(50,model.OfflineHeadersSaved);
        fail=false; await model.RetryAsync(InboxProblemScope.List); Assert.Null(model.ListProblem); Assert.Equal(52,model.OfflineHeadersSaved);
        Assert.Equal(new[]{"INBOX","Archive","Archive"},f.Receiver.Calls.Select(call=>call.Folder));
        await model.ReceiveOfflineHeadersAsync(); Assert.Equal(102,model.OfflineHeadersSaved); Assert.NotNull(f.Receiver.Calls.Last().Cursor);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("folder")]
    [InlineData("maintenance")]
    [InlineData("dispose")]
    public async Task LateUncancellablePageNeverSavesOrRepaintsSupersededReader(string mode)
    {
        using var f = await Fixture.Create(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<MailInboxPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Receiver.Override = (_,_,_)=>{entered.TrySetResult();return release.Task;};
        using var model = await f.Model(); var task=model.ReceiveOfflineHeadersAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")model.Cancel();else if(mode=="account"){model.SetAccount(TestDirectory.Profile());await model.CacheLoadTask;await model.OfflineFolderLoadTask;}
        else if(mode=="folder"){Assert.True(model.SelectFolder("Archive"));await model.CacheLoadTask;}else if(mode=="maintenance")model.BeginStorageMaintenance();else model.Dispose();
        release.SetResult(Page(f.Account,"INBOX",101,null));await task;
        Assert.Null(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"INBOX")); Assert.Empty(model.Messages); Assert.Null(model.Body);
        if(mode=="cancel"){Assert.True(model.ProblemIsOfflineHeaderReceive);Assert.Contains("confirmed saved",model.Status);}
    }

    [Theory]
    [InlineData("wrong-account")]
    [InlineData("wrong-folder")]
    [InlineData("wrong-epoch")]
    [InlineData("duplicate")]
    [InlineData("ascending")]
    [InlineData("oversized")]
    [InlineData("missing-mailbox")]
    [InlineData("cursor-account")]
    [InlineData("cursor-epoch")]
    [InlineData("cursor-index")]
    [InlineData("cursor-uidnext")]
    public async Task InvalidPageOrContinuationCannotBecomeDurableMail(string mode)
    {
        using var f = await Fixture.Create();
        f.Receiver.Override=(account,folder,cursor)=>
        {
            var page=Page(account,folder,101,cursor);var rows=page.Messages.ToArray();var next=page.Older!;
            switch(mode)
            {
                case "wrong-account": rows[0]=rows[0] with{Key=new(AccountId.New(),folder,7,101)};break;
                case "wrong-folder": rows[0]=rows[0] with{Key=new(account.Id,"Other",7,101)};break;
                case "wrong-epoch": rows[0]=rows[0] with{Key=new(account.Id,folder,8,101)};break;
                case "duplicate": rows[1]=rows[0];break;
                case "ascending": Array.Reverse(rows);break;
                case "oversized": rows=rows.Append(rows[^1] with{Key=new(account.Id,folder,7,1)}).ToArray();break;
                case "missing-mailbox": return Task.FromResult(page with{Mailbox=null});
                case "cursor-account": next=next with{AccountId=AccountId.New()};break;
                case "cursor-epoch": next=next with{UidValidity=8};break;
                case "cursor-index": next=next with{NextIndex=next.NextIndex+1};break;
                case "cursor-uidnext": next=next with{UidNext=50};break;
            }
            return Task.FromResult(page with{Messages=rows,Older=next});
        };
        using var model=await f.Model();await model.ReceiveOfflineHeadersAsync();Assert.True(model.ProblemIsOfflineHeaderReceive);Assert.Equal(0,model.OfflineHeaderPagesSaved);
        Assert.Null(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"INBOX"));Assert.Single(f.Receiver.Calls);
    }

    [Fact]
    public async Task EpochChangeDuringReceiveCannotRollCacheBackAndRestartAcceptsNewEpoch()
    {
        using var f=await Fixture.Create();f.Receiver.Override=async(account,folder,cursor)=>
        {
            await f.Cache.SaveSummariesAsync(new(account.Id,folder,8),[new(){Key=new(account.Id,folder,8,1),Subject="New epoch",Sender="fixture@example.test"}],DateTimeOffset.UtcNow);
            return Page(account,folder,101,cursor);
        };
        using var model=await f.Model();await model.ReceiveOfflineHeadersAsync();Assert.True(model.ProblemIsOfflineHeaderReceive);Assert.Equal(8u,(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"INBOX"))!.Key.UidValidity);Assert.Equal(0,model.OfflineHeadersSaved);
        Assert.True(model.CanRestartOfflineHeaders);model.RestartOfflineHeaders();Assert.Null(model.ListProblem);Assert.False(model.HasOfflineHeaderWalk);
    }

    [Fact]
    public async Task RestartReloadSelectionAndDisabledAccountNeverAutomaticallyReceiveOrDelete()
    {
        using var f=await Fixture.Create();using var model=await f.Model();await model.ReceiveOfflineHeadersAsync();int calls=f.Receiver.Calls.Count;
        model.RestartOfflineHeaders();Assert.False(model.HasOfflineHeaderWalk);Assert.Equal(calls,f.Receiver.Calls.Count);Assert.NotNull(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"INBOX"));
        await model.ReceiveOfflineHeadersAsync();Assert.Null(f.Receiver.Calls[calls].Cursor);
        await model.ReloadOfflineFolderSelectionAsync();Assert.False(model.HasOfflineHeaderWalk);
        await model.ReceiveOfflineHeadersAsync();await model.ToggleOfflineFolderAsync();Assert.False(model.HasOfflineHeaderWalk);Assert.Equal("Archive",Assert.Single(model.OfflineFolders));
        model.SetAccount(f.Account with{IsEnabled=false});await model.CacheLoadTask;await model.OfflineFolderLoadTask;calls=f.Receiver.Calls.Count;
        Assert.False(model.CanReceiveOfflineHeaders);await model.ReceiveOfflineHeadersAsync();Assert.Equal(calls,f.Receiver.Calls.Count);Assert.NotEmpty(model.Messages);
    }

    [Fact]
    public async Task EmptyFolderStillSavesItsEpochAndDoesNotNeedContinuation()
    {
        using var f=await Fixture.Create();await f.Store.SaveAsync(f.Account.Id,["Empty"]);using var model=await f.Model();await model.ReceiveOfflineHeadersAsync();
        Assert.Null(model.ListProblem);Assert.Equal(0,model.OfflineHeadersSaved);Assert.Equal(1,model.OfflineHeaderPagesSaved);Assert.False(model.CanReceiveOfflineHeaders);
        Assert.Equal(7u,(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"Empty"))!.Key.UidValidity);
    }

    [Fact]
    public async Task HeaderAcquisitionCanContinueBeyondThe500VisibleRowLimit()
    {
        using var f=await Fixture.Create();await f.Store.SaveAsync(f.Account.Id,["INBOX"]);
        f.Receiver.Override=(account,folder,cursor)=>Task.FromResult(Page(account,folder,1101,cursor));
        using var model=await f.Model();for(int i=0;i<23;i++)await model.ReceiveOfflineHeadersAsync();
        Assert.Null(model.ListProblem);Assert.Equal(1101,model.OfflineHeadersSaved);Assert.False(model.CanReceiveOfflineHeaders);
        Assert.Equal(500,(await f.Cache.ReadSummariesAsync(new(f.Account.Id,"INBOX",7),500)).Count);
        Assert.Single(await f.Cache.ReadSummariesAsync(new(f.Account.Id,"INBOX",7),500,2));
        await model.ShowSavedHeadersAsync();Assert.Equal(50,model.Messages.Count);Assert.Equal(0,f.Receiver.Bodies);
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("membership")]
    [InlineData("truncated")]
    public async Task InvalidOlderPageKeepsPriorCheckpointAndRestartIsExplicit(string mode)
    {
        using var f=await Fixture.Create();await f.Store.SaveAsync(f.Account.Id,["INBOX"]);using var model=await f.Model();await model.ReceiveOfflineHeadersAsync();
        f.Receiver.Override=(account,folder,cursor)=>
        {
            var page=Page(account,folder,101,cursor);
            return Task.FromResult(mode=="overlap"?Page(account,folder,101,null):mode=="membership"?page with{Older=page.Older! with{MessageCount=102}}:page with{Older=null});
        };
        await model.ReceiveOfflineHeadersAsync();Assert.True(model.ProblemIsOfflineHeaderReceive);Assert.Equal(50,model.OfflineHeadersSaved);
        Assert.Null(await f.Cache.ReadBodyAsync(new(f.Account.Id,"INBOX",7),101));Assert.True(model.CanRestartOfflineHeaders);
        f.Receiver.Override=null;await model.RetryAsync(InboxProblemScope.List);Assert.Equal(100,model.OfflineHeadersSaved);Assert.Null(model.ListProblem);
    }

    [Fact]
    public async Task FailedCacheCommitDoesNotAdvanceAndRetryRepeatsTheSamePage()
    {
        using var f=await Fixture.Create();var cache=new FailingCache(f.Cache);
        using var model=new InboxViewModel(f.Receiver,new ImmediateUiDispatcher(),cache,offlineFolders:f.Store);model.SetAccount(f.Account);await model.CacheLoadTask;await model.OfflineFolderLoadTask;
        await model.ReceiveOfflineHeadersAsync();Assert.True(model.ProblemIsOfflineHeaderReceive);Assert.Equal(0,model.OfflineHeaderPagesSaved);Assert.Null(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"INBOX"));
        cache.Fail=false;await model.RetryAsync(InboxProblemScope.List);Assert.Null(model.ListProblem);Assert.Equal(52,model.OfflineHeadersSaved);
        Assert.Null(f.Receiver.Calls[0].Cursor);Assert.Null(f.Receiver.Calls[1].Cursor);
    }

    [Fact]
    public async Task QueuedProgressAndCompletionCannotPaintAfterAccountChange()
    {
        using var f=await Fixture.Create();var dispatcher=new TestQueueDispatcher();
        using var model=new InboxViewModel(f.Receiver,dispatcher,f.Cache,offlineFolders:f.Store);model.SetAccount(f.Account);await model.CacheLoadTask;await model.OfflineFolderLoadTask;dispatcher.Drain();
        await model.ReceiveOfflineHeadersAsync();Assert.True(model.IsBusy);model.SetAccount(TestDirectory.Profile());await model.CacheLoadTask;await model.OfflineFolderLoadTask;dispatcher.Drain();
        Assert.Empty(model.Messages);Assert.False(model.HasOfflineHeaderWalk);Assert.Equal(0,model.OfflineHeadersSaved);Assert.DoesNotContain("header batch complete",model.Status);
        Assert.Equal(50,(await f.Cache.ReadSummariesAsync(new(f.Account.Id,"INBOX",7),100)).Count);
    }

    [Fact]
    public async Task InboxOnlyReceiverSupportsSelectedInboxWithoutFolderCapability()
    {
        using var f=await Fixture.Create();await f.Store.SaveAsync(f.Account.Id,["INBOX"]);int calls=0;
        var receiver=new TestMailReceiver{Inbox=(cursor,_)=>{calls++;return Task.FromResult(Page(f.Account,"INBOX",1,cursor));}};
        using var model=new InboxViewModel(receiver,new ImmediateUiDispatcher(),f.Cache,offlineFolders:f.Store);model.SetAccount(f.Account);await model.CacheLoadTask;await model.OfflineFolderLoadTask;
        Assert.True(model.CanReceiveOfflineHeaders);await model.ReceiveOfflineHeadersAsync();Assert.Equal(1,calls);Assert.Equal(1,model.OfflineHeadersSaved);
        await f.Store.SaveAsync(f.Account.Id,["Archive"]);await model.ReloadOfflineFolderSelectionAsync();Assert.False(model.CanReceiveOfflineHeaders);
    }

    [Fact]
    public async Task CanceledCommittedSaveCannotChangeTheNewAttemptsCheckpoints()
    {
        using var f=await Fixture.Create();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int saves=0;
        var cache=new FailingCache(f.Cache){Fail=false,AfterSave=async()=>{if(Interlocked.Increment(ref saves)==1){entered.SetResult();await release.Task;}}};
        using var model=new InboxViewModel(f.Receiver,new ImmediateUiDispatcher(),cache,offlineFolders:f.Store);model.SetAccount(f.Account);await model.CacheLoadTask;await model.OfflineFolderLoadTask;
        var old=model.ReceiveOfflineHeadersAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));model.Cancel();
        await model.RetryAsync(InboxProblemScope.List);Assert.Null(model.ListProblem);Assert.Equal(52,model.OfflineHeadersSaved);
        release.SetResult();await old;Assert.Equal(52,model.OfflineHeadersSaved);Assert.Equal(2,model.OfflineHeaderPagesSaved);Assert.Null(model.ListProblem);
        await model.ReceiveOfflineHeadersAsync();Assert.Equal(102,model.OfflineHeadersSaved);
    }

    private sealed class FailingCache(IMailCache inner):IMailCache
    {
        internal bool Fail=true; internal Func<Task>? AfterSave;
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account,string mailbox,CancellationToken cancellationToken=default)=>inner.GetLatestMailboxAsync(account,mailbox,cancellationToken);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox,int maximumCount,uint? afterUid=null,CancellationToken cancellationToken=default)=>inner.ReadSummariesAsync(mailbox,maximumCount,afterUid,cancellationToken);
        public async Task SaveSummariesAsync(MailboxKey mailbox,IReadOnlyList<MailMessageSummary> rows,DateTimeOffset receivedAt,CancellationToken cancellationToken=default){if(Fail)throw new IOException("Fixture disk full");await inner.SaveSummariesAsync(mailbox,rows,receivedAt,cancellationToken);if(AfterSave is {} action)await action();}
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox,uint uid,CancellationToken cancellationToken=default)=>inner.ReadBodyAsync(mailbox,uid,cancellationToken);
        public Task SaveBodyAsync(MailboxKey mailbox,MailMessageBody body,CancellationToken cancellationToken=default)=>inner.SaveBodyAsync(mailbox,body,cancellationToken);
    }

    internal static MailInboxPage Page(AccountProfile account,string folder,int count,MailInboxCursor? cursor)
    {
        int end=cursor?.NextIndex??count-1,start=Math.Max(0,end-InboxViewModel.PageSize+1);
        var rows=count==0?[]:Enumerable.Range(start,end-start+1).Reverse().Select(index=>new MailMessageSummary{Key=new(account.Id,folder,7,(uint)index+1),Subject="Header "+index,Sender="fixture@example.test"}).ToArray();
        return new(rows,start==0?null:new(account.Id,7,(uint)count+1,count,start-1){MailboxId=folder}){Mailbox=new(account.Id,folder,7)};
    }
    internal sealed class Receiver:IMailFolderReceiver
    {
        public List<(string Folder,MailInboxCursor? Cursor)> Calls=[];public int Bodies;
        public Func<AccountProfile,string,MailInboxCursor?,Task<MailInboxPage>>? Override;
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account,string folder,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)
        {Assert.Equal(50,count);Calls.Add((folder,older));return Override?.Invoke(account,folder,older)??Task.FromResult(Page(account,folder,folder=="INBOX"?101:folder=="Empty"?0:2,older));}
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>GetMailboxAsync(account,"INBOX",count,older,cancellationToken);
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken cancellationToken=default){Bodies++;throw new InvalidOperationException("No body fetch expected.");}
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No discovery expected.");
        public Task TestConnectionAsync(AccountProfile account,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No connection test expected.");
    }
    internal sealed class Fixture:IDisposable
    {
        public TestDirectory Directory=new();public AccountProfile Account=TestDirectory.Profile();public Receiver Receiver=new();public SqliteMailCache Cache=null!;public JsonOfflineFolderStore Store=null!;
        public static async Task<Fixture> Create(){var f=new Fixture();f.Cache=new(f.Directory.File("mail.db"));f.Store=new(f.Directory.File("offline-folders.json"));await f.Store.SaveAsync(f.Account.Id,["INBOX","Archive"]);return f;}
        public async Task<InboxViewModel> Model(){var model=new InboxViewModel(Receiver,new ImmediateUiDispatcher(),Cache,offlineFolders:Store);model.SetAccount(Account);await model.CacheLoadTask;await model.OfflineFolderLoadTask;return model;}
        public void Dispose()=>Directory.Dispose();
    }
}
