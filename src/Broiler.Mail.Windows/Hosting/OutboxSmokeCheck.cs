using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Windows.Hosting;

internal static class OutboxSmokeCheck
{
    internal static async Task RunAsync(string directory,AccountProfile account)
    {
        using var profile=MailProfile.Open(directory);var store=new JsonDraftStore(profile.DraftsPath);var accounts=new JsonAccountCollectionStore(directory);await accounts.SaveAsync(account);
        DraftSnapshot Row(DraftSubmissionState state,SentCopyState copy=SentCopyState.NotRequested)=>new(){Draft=MailComposition.Create(account) with{Subject="Outbox fixture",PlainText="Retained outbox body"},ToText="recipient@example.test",CcText="",BccText="hidden@example.test",State=state,SentCopy=copy,SentCopyFolder=copy==SentCopyState.NotRequested?null:"Sent"};
        var accepted=Row(DraftSubmissionState.Accepted,SentCopyState.Failed);var unknown=Row(DraftSubmissionState.Unknown);
        var state=await store.SaveAsync(0,accepted);state=await store.ParkAsync(state.Revision);state=await store.SaveAsync(state.Revision,unknown);state=await store.ParkAsync(state.Revision);var active=Row(DraftSubmissionState.Editing);state=await store.SaveAsync(state.Revision,active);
        var sender=new Sender();var cache=new SqliteMailCache(profile.CachePath);var storage=new ProfileStorageService(profile,cache);
        using var app=new MailApplication(accounts,new JsonSettingsStore(profile.SettingsPath),new Receiver(),sender,new Credentials(),drafts:store,cache:cache,storage:storage);
        await app.InitializeAsync();var model=app.CreateViewModel();
        try
        {
            await model.Inbox.CacheLoadTask;
            if(model.Composer.OutboxRecords.Count!=3)throw new InvalidOperationException("Published outbox lost retained records.");
            model.Composer.SetOutboxFilter(OutboxFilter.NeedsReview);
            if(model.Composer.OutboxRecords.Count!=2 || !model.Composer.OutboxDetails(accepted.Draft.Id).Contains("do not resend",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Published outbox conflated SMTP and Sent-copy outcomes.");
            model.Composer.SetOutboxFilter(OutboxFilter.All);
            if(!await model.Composer.OpenOutboxRecordAsync(unknown.Draft.Id))throw new InvalidOperationException("Published retained-record handoff failed.");
            await model.Composer.SaveAsync();
            if(model.Composer.DraftId!=unknown.Draft.Id || model.Composer.SubmissionState!=DraftSubmissionState.Unknown || model.Composer.CanSend || sender.Calls!=0)throw new InvalidOperationException("Published outbox replayed a submission or changed its identity.");
            var disk=await store.LoadAsync();if(!(disk.SavedDrafts??[]).Any(row=>row.Draft.Id==active.Draft.Id))throw new InvalidOperationException("Published outbox handoff discarded the prior composer.");
            var cleared=await storage.ClearDownloadedMailAsync(directory+"-backup");var restored=await storage.RestoreBackupAsync(cleared.BackupDirectory,directory+"-restored");
            var recovered=await new JsonDraftStore(Path.Combine(restored,"drafts.json")).LoadAsync();
            using var reopened=new ComposerViewModel(store,await store.LoadAsync(),sender:sender);reopened.SetAccount(account);
            if(recovered.Draft?.Draft.Id!=unknown.Draft.Id || reopened.OutboxRecords.Count!=3 || sender.Calls!=0)throw new InvalidOperationException("Published outbox recovery lost records or started network work.");
            using(var shell=new MailShellView(model))ShellSmokeCheck.Run(shell);
        }
        finally{model.Inbox.Dispose();await model.Inbox.CacheLoadTask;model.Composer.Dispose();}
    }
    private sealed class Sender:IMailSender
    {public int Calls;public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken cancellationToken=default){Calls++;throw new InvalidOperationException("Outbox must not submit.");}}
    private sealed class Receiver:IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No connection test.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No receive.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>throw new InvalidOperationException("No body fetch.");
    }
    private sealed class Credentials:ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key,CancellationToken token=default)=>Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key,string secret,CancellationToken token=default)=>throw new InvalidOperationException("No credential writes.");
        public Task DeleteAsync(CredentialKey key,CancellationToken token=default)=>throw new InvalidOperationException("No credential deletion.");
    }
}
