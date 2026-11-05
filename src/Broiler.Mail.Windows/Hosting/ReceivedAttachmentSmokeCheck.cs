using System.Text;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class ReceivedAttachmentSmokeCheck
{
    internal static async Task RunAsync(string directory,AccountProfile account)
    {
        using var profile=MailProfile.Open(directory);var cache=new SqliteMailCache(profile.CachePath);var key=new MailMessageKey(account.Id,"INBOX",7,1);
        await cache.SaveSummariesAsync(new(account.Id,"INBOX",7),[new(){Key=key,Subject="Attachment fixture",Sender="sender@example.test"}],DateTimeOffset.UtcNow);await cache.SaveBodyAsync(new(account.Id,"INBOX",7),new(key,"Cached body"));
        var receiver=new Receiver();var exports=new FileMessageSourceExports(directory);using var model=new InboxViewModel(receiver,new ImmediateUiDispatcher(),cache,sourceExports:exports);model.SetAccount(account);await model.CacheLoadTask;await model.SelectAsync(key);
        using var view=new InboxView(model).CreateContent();var source=All(view).OfType<MessageSourceExportActions>().Single();await source.ExportAsync();var attachments=All(source).OfType<MessageAttachmentActions>().Single();
        if(attachments.Attachments.Count!=0 || receiver.Calls!=1)throw new InvalidOperationException("Published attachment inventory loaded automatically.");
        await attachments.ListAsync();if(attachments.Attachments.Count!=1 || attachments.Attachments[0].FileName!="café.bin")throw new InvalidOperationException("Published attachment inventory mismatch.");
        await attachments.SaveAsync(attachments.Attachments[0]);string saved=Directory.GetFiles(exports.AttachmentDirectory).Single();
        if(!File.ReadAllBytes(saved).AsSpan().SequenceEqual(Receiver.Payload) || receiver.Calls!=1 || model.Body?.PlainText!="Cached body")throw new InvalidOperationException("Published attachment save changed bytes or performed another download.");
        using(var reopened=new MessageSourceExportActions(account,key,receiver,new FileMessageSourceExports(directory),()=>true,()=>false))
        {
            var picker=All(reopened).OfType<SavedSourceExportPicker>().Single();
            if(picker.Exports.Count!=0)throw new InvalidOperationException("Published saved-original list loaded automatically.");
            await picker.ListAsync();if(picker.Exports.Count!=1)throw new InvalidOperationException("Published saved-original catalog did not reopen.");
            await picker.UseAsync(picker.Exports[0]);var local=All(reopened).OfType<MessageAttachmentActions>().Single();await local.ListAsync();
            if(local.Attachments.Count!=1)throw new InvalidOperationException("Published saved-original activation did not enable attachments.");
            await local.SaveAsync(local.Attachments[0]);
            var preview=All(reopened).OfType<MessageSourceInspection>().Single();if(preview.IsPreviewVisible)throw new InvalidOperationException("Published saved-original preview opened automatically.");
            await preview.ReadPreviewAsync();if(!preview.IsPreviewVisible || receiver.Calls!=1 || Directory.GetFiles(exports.AttachmentDirectory).Any(path=>!File.ReadAllBytes(path).AsSpan().SequenceEqual(Receiver.Payload)))
                throw new InvalidOperationException("Published saved-original reuse changed bytes or fetched again.");
        }
        var cleared=await new ProfileStorageService(profile,cache).ClearDownloadedMailAsync(directory+"-backup");
        if(!File.Exists(saved) || (await new FileMessageSourceExports(directory).ListSavedExportsAsync(key)).Count!=1 || File.Exists(Path.Combine(cleared.BackupDirectory,"source-export-catalog.json")) || Directory.Exists(Path.Combine(cleared.BackupDirectory,"attachment-exports")))throw new InvalidOperationException("Published attachment retention/backup boundary failed.");
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Receiver:IMailReceiver,IMailSourceReceiver
    {
        internal static readonly byte[] Payload=[0,255,128,13,10,42];internal int Calls;
        public Task<MailMessageSource> GetSourceAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)
        {
            Calls++;string raw="Content-Type: multipart/mixed; boundary=native\r\n\r\n--native\r\nContent-Type: text/plain\r\n\r\nBody\r\n--native\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=fallback.bin; filename*1*=%A9.bin; filename*0*=utf-8''caf%C3\r\nContent-Transfer-Encoding: base64\r\n\r\n"+Convert.ToBase64String(Payload)+"\r\n--native--\r\n";
            return Task.FromResult(new MailMessageSource(key,Encoding.ASCII.GetBytes(raw)));
        }
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>throw new InvalidOperationException("Use cached body.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No receive.");
        public Task TestConnectionAsync(AccountProfile account,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No connection test.");
    }
}
