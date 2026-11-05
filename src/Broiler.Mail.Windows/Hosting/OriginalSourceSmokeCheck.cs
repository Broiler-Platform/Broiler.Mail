using System.Text;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class OriginalSourceSmokeCheck
{
    internal static async Task RunAsync(string directory,AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath); var key = new MailMessageKey(account.Id,"INBOX",7,1);
        await cache.SaveSummariesAsync(new(account.Id,"INBOX",7),[new(){Key=key,Subject="Original-source fixture",Sender="sender@example.test"}],DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(new(account.Id,"INBOX",7),new(key,"Cached preview")); var receiver = new Receiver(); var exports = new FileMessageSourceExports(directory);
        using var model = new InboxViewModel(receiver,new ImmediateUiDispatcher(),cache,sourceExports:exports); model.SetAccount(account); await model.CacheLoadTask; await model.SelectAsync(key);
        using var view = new InboxView(model).CreateContent(); var actions = All(view).OfType<MessageSourceExportActions>().Single();
        if(receiver.Calls!=0) throw new InvalidOperationException("Published source export fetched automatically.");
        await actions.ExportAsync();
        string path = Directory.GetFiles(exports.ExportDirectory,"*.eml").Single();
        if(receiver.Calls!=1 || !File.ReadAllBytes(path).AsSpan().SequenceEqual(Receiver.Original) ||
            !All(actions).OfType<StandardRichEdit>().Single(editor => editor.AccessibleName == "Saved original source path and checksum").GetPlainText().Contains("SHA-256") || model.Body?.PlainText!="Cached preview")
            throw new InvalidOperationException("Published original-source export changed bytes or reader state.");
        var inspection=All(actions).OfType<MessageSourceInspection>().Single();
        if(inspection.IsPreviewVisible)throw new InvalidOperationException("Published source inspection opened automatically.");
        await inspection.ReadPreviewAsync();
        if(!inspection.IsPreviewVisible || !inspection.PreviewText.Contains("\\x00\\xFF\\x80") || receiver.Calls!=1 || model.Body?.PlainText!="Cached preview")
            throw new InvalidOperationException("Published local source inspection changed bytes, contacted the server or replaced the reader.");
        inspection.HidePreview();
        if(inspection.IsPreviewVisible || inspection.PreviewText.Length!=0)throw new InvalidOperationException("Published source inspection did not clear on Hide.");
        var capped=OriginalSourceText.Format(new byte[OriginalSourceText.MaximumPreviewBytes+1]);
        if(!capped.IsTruncated || capped.BytesShown!=OriginalSourceText.MaximumPreviewBytes || capped.Text.Length!=4*OriginalSourceText.MaximumPreviewBytes)
            throw new InvalidOperationException("Published source preview bounds failed.");
        var cleared=await new ProfileStorageService(profile,cache).ClearDownloadedMailAsync(directory+"-backup");
        if(!File.Exists(path) || Directory.Exists(Path.Combine(cleared.BackupDirectory,"source-exports"))) throw new InvalidOperationException("Published export retention/backup scope failed.");
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Receiver:IMailReceiver,IMailSourceReceiver
    {
        internal static readonly byte[] Original=Encoding.ASCII.GetBytes("Subject: Original fixture\r\nContent-Type: application/octet-stream\r\n\r\n").Concat(new byte[]{0,255,128,13,10}).ToArray();
        internal int Calls;
        public Task<MailMessageSource> GetSourceAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default){Calls++;return Task.FromResult(new MailMessageSource(key,Original));}
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>throw new InvalidOperationException("Use cached preview.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No automatic receive.");
        public Task TestConnectionAsync(AccountProfile account,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No connection test.");
    }
}
