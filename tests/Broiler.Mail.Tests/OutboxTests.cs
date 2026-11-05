using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class OutboxTests
{
    [Theory]
    [InlineData(OutboxFilter.All,7)]
    [InlineData(OutboxFilter.Unsent,2)]
    [InlineData(OutboxFilter.InProgress,0)]
    [InlineData(OutboxFilter.NeedsReview,4)]
    [InlineData(OutboxFilter.Accepted,3)]
    public void ProjectionFiltersAccountAndOutcomesWithoutWritingOrSending(OutboxFilter filter,int count)
    {
        var one=Account();var two=Account();var active=Snapshot(one,DraftSubmissionState.Editing);var interrupted=Snapshot(one,DraftSubmissionState.Sending);
        var pending=Snapshot(one,DraftSubmissionState.Accepted,SentCopyState.Pending);var other=Snapshot(two,DraftSubmissionState.Editing);
        var state=new DraftStoreState(0,active){SavedDrafts=[Snapshot(one,DraftSubmissionState.Failed),Snapshot(one,DraftSubmissionState.Unknown),Snapshot(one,DraftSubmissionState.Accepted,SentCopyState.Saved),Snapshot(one,DraftSubmissionState.Accepted,SentCopyState.Failed),pending,interrupted,other]};
        var store=new ReadOnlyStore(state);var sender=new Sender();using var model=new ComposerViewModel(store,state,sender:sender);model.SetAccount(one);model.SetOutboxFilter(filter);
        Assert.Equal(count,model.OutboxRecords.Count);Assert.All(model.OutboxRecords,row=>Assert.Equal(one.Id,row.AccountId));Assert.Equal(0,store.Writes);Assert.Equal(0,sender.Calls);
        model.SetOutboxFilter(OutboxFilter.All);var recovered=model.OutboxRecords.Single(row=>row.Id==interrupted.Draft.Id);Assert.Equal(DraftSubmissionState.Unknown,recovered.Submission);Assert.True(recovered.InterruptedSend);
        Assert.Equal(SentCopyState.Unknown,model.OutboxRecords.Single(row=>row.Id==pending.Draft.Id).SentCopy);Assert.Equal(DraftSubmissionState.Sending,interrupted.State);Assert.Equal(SentCopyState.Pending,pending.SentCopy);
        model.SetAccount(two with{IsEnabled=false});Assert.Single(model.OutboxRecords);Assert.Equal(other.Draft.Id,model.OutboxRecords.Single().Id);Assert.Equal(OutboxFilter.All,model.SelectedOutboxFilter);
    }
    [Fact]
    public void DetailsAttributeAccountAndPinnedIdentityAndKeepSmtpSeparateFromCopyFailure()
    {
        var account=Account();var row=Snapshot(account,DraftSubmissionState.Accepted,SentCopyState.Failed) with{Draft=Snapshot(account,DraftSubmissionState.Editing).Draft with{FromAddress="retired-alias@example.test",FromDisplayName="Pinned alias",SubmissionDate=DateTimeOffset.Parse("2026-11-03T12:00:00Z")},BccText="hidden@example.test"};
        using var model=new ComposerViewModel(initial:new(0,row));model.SetAccount(account);
        string details=model.OutboxDetails(row.Draft.Id);Assert.Contains(account.Id.ToString(),details);Assert.Contains("retired-alias@example.test",details);Assert.Contains("Pinned alias",details);
        Assert.Contains("SMTP: Accepted",details);Assert.Contains("Sent copy: Failed",details);Assert.Contains("Do not resend",details);Assert.Contains("Bcc (raw edit): hidden@example.test",details);Assert.DoesNotContain("Private body",details);
        Assert.Contains("2026-11-03 12:00:00Z",details);Assert.Contains("SMTP accepted",model.OutboxNotice);
        string safe=ComposerViewModel.OutboxDisplay("a\n\u202e\ud800😀z",5);Assert.DoesNotContain('\n',safe);Assert.DoesNotContain('\u202e',safe);Assert.DoesNotContain('\ud800',safe);
    }
    [Fact]
    public async Task LiveSendingBecomesUnknownWithoutAutomaticResubmission()
    {
        var sender=new Sender{Hold=true};using var model=new ComposerViewModel(sender:sender);var account=Account();model.SetAccount(account);Assert.True(model.StartNew());model.Edit("to@example.test","","","Live","Body");
        var sending=model.SendAsync();await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(DraftSubmissionState.Sending,Assert.Single(model.OutboxRecords).Submission);Assert.True(model.OutboxRecords.Single().IsInProgress);
        Assert.False(model.CanOpenOutboxRecord(model.DraftId!.Value));sender.Release.SetResult(new(SubmissionStatus.Unknown));await sending;
        Assert.Equal(DraftSubmissionState.Unknown,Assert.Single(model.OutboxRecords).Submission);Assert.False(model.CanSend);Assert.True(model.OutboxRecords.Single().NeedsReview);
        Assert.True(await model.OpenOutboxRecordAsync(model.DraftId.Value));Assert.Equal(1,sender.Calls);Assert.False(model.CanSend);
    }
    [Theory]
    [InlineData(DraftSubmissionState.Accepted)]
    [InlineData(DraftSubmissionState.Unknown)]
    [InlineData(DraftSubmissionState.Sending)]
    public async Task OpeningParkedOutcomePreservesCurrentRawDraftAndNeverResends(DraftSubmissionState outcome)
    {
        using var directory=new TestDirectory();var store=new JsonDraftStore(directory.File("drafts.json"));var account=Account();var saved=Snapshot(account,outcome);
        var state=await store.SaveAsync(0,saved);state=await store.ParkAsync(state.Revision);var current=Snapshot(account,DraftSubmissionState.Editing) with{ToText="unfinished recipient",CcText="raw cc",BccText="raw bcc"};state=await store.SaveAsync(state.Revision,current);
        var sender=new Sender();using var model=new ComposerViewModel(store,state,sender:sender);model.SetAccount(account);
        Assert.True(await model.OpenOutboxRecordAsync(saved.Draft.Id));await model.SaveAsync();Assert.Equal(saved.Draft.Id,model.DraftId);Assert.False(model.CanSend);Assert.Equal(0,sender.Calls);
        var disk=await store.LoadAsync();var retained=Assert.Single(disk.SavedDrafts!);Assert.Equal(current.Draft.Id,retained.Draft.Id);Assert.Equal("unfinished recipient",retained.ToText);Assert.Equal("raw bcc",retained.BccText);
        using var reopened=new ComposerViewModel(store,disk,sender:sender);reopened.SetAccount(account);Assert.Equal(outcome==DraftSubmissionState.Sending?DraftSubmissionState.Unknown:outcome,reopened.OutboxRecords.Single(row=>row.IsActive).Submission);Assert.False(reopened.CanSend);
    }
    [Fact]
    public async Task FailedSaveOrUnreadableStateCannotOpenOrSilentlyOverwriteRecords()
    {
        var account=Account();var row=Snapshot(account,DraftSubmissionState.Editing);var state=new DraftStoreState(0,row);var store=new ReadOnlyStore(state);
        using var model=new ComposerViewModel(store,state);model.SetAccount(account);model.Edit("changed","","","Changed","Body");Assert.False(await model.OpenOutboxRecordAsync(row.Draft.Id));Assert.False(model.IsBusy);Assert.Equal(row.Draft.Id,model.DraftId);
        using var unreadable=new ComposerViewModel(store,state,loadError:"synthetic corrupt file");unreadable.SetAccount(account);Assert.Empty(unreadable.OutboxRecords);Assert.Contains("unavailable",unreadable.OutboxNotice);Assert.False(await unreadable.OpenOutboxRecordAsync(row.Draft.Id));
    }
    [Fact]
    public async Task LiveSentCopyPendingDoesNotUndoSmtpAcceptanceOrAppearAsUnsent()
    {
        var copies=new HoldingCopy();var sender=new Sender();using var model=new ComposerViewModel(sender:sender,sentCopies:copies);
        model.SetAccount(Account() with{SentCopyMode=SentCopyMode.AppendToFolder,SentFolder="Sent"});model.StartNew();model.Edit("to@example.test","","","Accepted","Body");
        var sending=model.SendAsync();await copies.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var row=Assert.Single(model.OutboxRecords);
        Assert.Equal(DraftSubmissionState.Accepted,row.Submission);Assert.Equal(SentCopyState.Pending,row.SentCopy);Assert.True(row.IsInProgress);Assert.False(row.IsUnsent);Assert.False(row.NeedsReview);
        copies.Release.SetResult(SentCopyState.Unknown);await sending;row=Assert.Single(model.OutboxRecords);Assert.Equal(DraftSubmissionState.Accepted,row.Submission);Assert.True(row.NeedsReview);Assert.False(model.CanSend);Assert.Equal(1,sender.Calls);
    }
    private sealed class HoldingCopy:ISentCopyWriter
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<SentCopyState> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SentCopyState> AppendAsync(AccountProfile account,MailDraft draft,CancellationToken cancellationToken=default){Entered.TrySetResult();return Release.Task;}
    }
    private static AccountProfile Account()=>TestDirectory.Profile() with{OutgoingServer=new(){Host="smtp.example.test",Port=587,UserName="test@example.test",Security=TransportSecurity.StartTls}};
    private static DraftSnapshot Snapshot(AccountProfile account,DraftSubmissionState state,SentCopyState copy=SentCopyState.NotRequested)=>new(){Draft=MailComposition.Create(account) with{Subject="Fixture",PlainText="Private body"},ToText="to@example.test",CcText="",BccText="",State=state,SentCopy=copy,SentCopyFolder=copy==SentCopyState.NotRequested?null:"Sent"};
    private sealed class ReadOnlyStore(DraftStoreState initial):IDraftStore
    {
        public int Writes;public bool IsPersistent=>true;public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken=default)=>Task.FromResult(initial);
        public Task<DraftStoreState> SaveAsync(long revision,DraftSnapshot? draft,CancellationToken cancellationToken=default){Writes++;throw new IOException("synthetic save failure");}
    }
    private sealed class Sender:IMailSender
    {
        public int Calls;public bool Hold;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<SendResult> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken cancellationToken=default){Calls++;Entered.TrySetResult();return Hold?Release.Task:Task.FromResult(new SendResult(SubmissionStatus.Accepted));}
    }
}
