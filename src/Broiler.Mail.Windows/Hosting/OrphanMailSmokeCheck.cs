using Broiler.Mail.Application;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Forms.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class OrphanMailSmokeCheck
{
    internal static async Task RunAsync(string directory,AccountProfile account)
    {
        using var profile=MailProfile.Open(directory);var cache=new SqliteMailCache(profile.CachePath);var storage=new ProfileStorageService(profile,cache);var store=new JsonAccountCollectionStore(directory);await store.SaveAndSelectAsync(account);
        var orphan=account with{Id=AccountId.New(),EmailAddress="removed@example.test"};
        foreach(var owner in new[]{account,orphan})
        {var box=new MailboxKey(owner.Id,"INBOX",7);var key=new MailMessageKey(owner.Id,"INBOX",7,1);await cache.SaveSummariesAsync(box,[new(){Key=key,Subject="Orphan fixture",Sender="fixture@example.test"}],DateTimeOffset.UtcNow);await cache.SaveBodyAsync(box,new(key,owner.EmailAddress));}
        await cache.RebuildSearchIndexAsync();var credentials=new Credentials();var credential=CredentialKey.For(orphan,MailProtocol.Imap);await credentials.WriteAsync(credential,"retained synthetic credential");
        using var application=new MailApplication(store,new JsonSettingsStore(profile.SettingsPath),new Receiver(),new Sender(),credentials,drafts:new JsonDraftStore(profile.DraftsPath),cache:cache,storage:storage);await application.InitializeAsync();var model=application.CreateViewModel();
        try
        {
            await model.Inbox.CacheLoadTask;var accounts=model.Accounts!;
            using(var view=new AccountSelectionView(accounts,()=>{}).CreateContent())
                if(!All(view).OfType<FormSection>().Any(section=>section.Content.AccessibleName=="Removed-account mail controls" && !section.IsExpanded))throw new InvalidOperationException("Published orphan controls missing.");
            if(accounts.OrphanMail.Count!=0)throw new InvalidOperationException("Published orphan discovery was automatic.");
            await accounts.FindOrphanMailAsync();if(accounts.OrphanMail.Count!=1 || accounts.SelectedOrphanMail is not null)throw new InvalidOperationException("Published orphan discovery mismatch.");
            if(!model.Composer.StartNew())throw new InvalidOperationException("Published orphan draft fixture could not start.");model.Composer.Edit("recipient@example.test","","","Retained draft","Keep local draft");var draft=model.Composer.DraftId;
            accounts.SelectOrphanMail(orphan.Id);if(!accounts.RequestOrphanCleanup())throw new InvalidOperationException("Published orphan confirmation missing.");await accounts.ConfirmOrphanCleanupAsync();await model.Inbox.CacheLoadTask;
            if(accounts.LastOrphanCleanupBackup is not {} backup || accounts.PendingOrphanCleanup is not null || accounts.OrphanMail.Count!=0 ||
                await cache.ReadBodyAsync(new(orphan.Id,"INBOX",7),1) is not null || (await cache.ReadBodyAsync(new(account.Id,"INBOX",7),1))?.PlainText!=account.EmailAddress ||
                model.Composer.DraftId!=draft || model.Composer.PlainText!="Keep local draft" || await credentials.ReadAsync(credential)!="retained synthetic credential" ||
                (await cache.GetSearchIndexInfoAsync()).Documents!=1 || !(await cache.GetSearchIndexInfoAsync()).Ready)
                throw new InvalidOperationException("Published orphan cleanup crossed retained-state boundaries.");
            string restored=await storage.RestoreBackupAsync(backup,directory+"-restored");
            if((await new SqliteMailCache(Path.Combine(restored,"mail-cache.db")).ReadBodyAsync(new(orphan.Id,"INBOX",7),1))?.PlainText!=orphan.EmailAddress ||
                (await new JsonDraftStore(Path.Combine(restored,"drafts.json")).LoadAsync()).Draft?.Draft.PlainText!="Keep local draft")throw new InvalidOperationException("Published orphan restore lost local state.");
        }
        finally{model.Inbox.Dispose();model.Composer.Dispose();}
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Receiver:IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account,CancellationToken token=default)=>throw new InvalidOperationException("No network in orphan fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No receive in orphan fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>throw new InvalidOperationException("No body fetch in orphan fixture.");
    }
    private sealed class Sender:IMailSender{public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken token=default)=>throw new InvalidOperationException("No send in orphan fixture.");}
    private sealed class Credentials:ICredentialStore
    {
        private readonly Dictionary<CredentialKey,string> _values=[];
        public Task<string?> ReadAsync(CredentialKey key,CancellationToken token=default)=>Task.FromResult(_values.GetValueOrDefault(key));
        public Task WriteAsync(CredentialKey key,string value,CancellationToken token=default){_values[key]=value;return Task.CompletedTask;}
        public Task DeleteAsync(CredentialKey key,CancellationToken token=default)=>throw new InvalidOperationException("Orphan cleanup must keep credentials.");
    }
}
