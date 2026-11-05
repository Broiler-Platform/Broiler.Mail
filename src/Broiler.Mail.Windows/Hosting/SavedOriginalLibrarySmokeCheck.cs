using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class SavedOriginalLibrarySmokeCheck
{
    internal static async Task RunAsync(string directory,AccountProfile account)
    {
        using var profile=MailProfile.Open(directory);var cache=new SqliteMailCache(profile.CachePath);var storage=new ProfileStorageService(profile,cache);var exports=new FileMessageSourceExports(directory,storage);var key=new MailMessageKey(account.Id,"Archive",9,1);
        byte[] original=Encoding.ASCII.GetBytes("Subject: Native library\r\nContent-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/plain\r\n\r\nSaved body\r\n--x\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=fixture.bin\r\nContent-Transfer-Encoding: base64\r\n\r\nAP+ADQoq\r\n--x--\r\n");
        await exports.SaveAsync(new(key,original));await exports.SaveAsync(new(key with{AccountId=AccountId.New()},original));
        var cleaned=await new ProfileStorageService(profile,cache).ClearDownloadedMailAsync(directory+"-backup");
        if(File.Exists(Path.Combine(cleaned.BackupDirectory,"source-export-catalog.json")) || (await new FileMessageSourceExports(directory).ListAccountExportsAsync(account.Id)).Count!=1)throw new InvalidOperationException("Published account catalog cleanup/backup boundary failed.");
        var accounts=new JsonAccountCollectionStore(directory);await accounts.SaveAndSelectAsync(account with{IsEnabled=false});
        using var application=new MailApplication(accounts,new JsonSettingsStore(profile.SettingsPath),new Receiver(),new Sender(),new Credentials(),sourceExports:new FileMessageSourceExports(directory,storage));await application.InitializeAsync();var model=application.CreateViewModel();
        // Shell disposal owns these models, so this is the final use of them.
        using var shell=new MailShellView(model);using var session=new StandardUiSessionBuilder().WithDispatcher(new ImmediateUiDispatcher()).Build(new Host());shell.Attach(session);
        if(!shell.CreateKeyboardNavigation(session).Execute(MailCommand.SavedOriginals) || shell.ActiveViewId!="originals")throw new InvalidOperationException("Published Saved originals navigation failed.");
        var library=shell.SavedOriginals;if(library.Exports.Count!=0 || model.Inbox.Body is not null || model.Inbox.Messages.Count!=0)throw new InvalidOperationException("Published library loaded inbox or receipts automatically.");
        await library.ListAsync();if(library.Exports.Count!=1 || library.Exports[0].Export.Key!=key)throw new InvalidOperationException("Published library mixed account receipts.");
        await library.VerifyAsync(library.Exports[0]);var inspection=All(library).OfType<MessageSourceInspection>().Single();if(inspection.IsPreviewVisible)throw new InvalidOperationException("Published library preview opened automatically.");await inspection.ReadPreviewAsync();
        var attachments=All(library).OfType<MessageAttachmentActions>().Single();await attachments.ListAsync();if(attachments.Attachments.Count!=1)throw new InvalidOperationException("Published library attachment inventory failed.");await attachments.SaveAsync(attachments.Attachments[0]);session.RenderFrame();
        if(!inspection.IsPreviewVisible || !File.ReadAllBytes(Directory.GetFiles(exports.AttachmentDirectory).Single()).AsSpan().SequenceEqual(new byte[]{0,255,128,13,10,42}) || model.Inbox.Body is not null)throw new InvalidOperationException("Published library changed source/attachment bytes or required an inbox body.");
        string input = directory + "-local.eml"; await File.WriteAllBytesAsync(input, original);
        var local = library.LocalImports;
        if (local.Imports.Count != 0 || local.PreviewText.Length != 0) throw new InvalidOperationException("Local imports opened automatically.");
        await local.OpenFileAsync(input);
        if (!local.PreviewText.Contains("Saved body")) throw new InvalidOperationException("Published local .eml opening failed.");
        await local.DecodeAsync();
        if (!local.PreviewText.Contains("Subject: Native library") || !local.PreviewText.Contains("Saved body")) throw new InvalidOperationException("Published standalone MIME decoding failed.");
        local.ShowSource(); local.ShowSource();
        if (All(local).OfType<Broiler.Mail.Application.Preview.ScrollableMessageText>().Count() != 1 || !local.PreviewText.Contains("Content-Type:")) throw new InvalidOperationException("Published source/text switching retained duplicate previews.");
        await local.ImportOpenedAsync(); await local.ListAsync();
        var imported = local.Imports.Single(); await local.ReadAsync(imported); session.RenderFrame();
        if (imported.AccountId != account.Id || !(await exports.ReadImportAsync(imported)).Bytes.AsSpan().SequenceEqual(original))
            throw new InvalidOperationException("Published local-original identity or byte fidelity failed.");
        var importBackup = await new ProfileStorageService(profile, cache).ClearDownloadedMailAsync(directory + "-import-backup");
        string restored = await new ProfileStorageService(profile, cache).RestoreBackupAsync(importBackup.BackupDirectory, directory + "-import-restored");
        if (!(await new JsonLocalMessageOriginals(restored).ReadImportAsync(imported)).Bytes.AsSpan().SequenceEqual(original))
            throw new InvalidOperationException("Published local-original backup/restore failed.");
        string copy = directory + "-local-copy.eml";
        await local.ExportAsync(imported, copy);
        if (!File.ReadAllBytes(copy).AsSpan().SequenceEqual(original)) throw new InvalidOperationException("Published local original export changed bytes.");
        local.RequestRemoval(imported); local.Keep();
        if ((await exports.ListImportsAsync(account.Id)).Count != 1) throw new InvalidOperationException("Published Keep deleted an import.");
        local.RequestRemoval(imported); await local.ConfirmRemovalAsync(); session.RenderFrame();
        if ((await exports.ListImportsAsync(account.Id)).Count != 0 || !local.PreviewText.Contains("Verified recovery backup") || !File.ReadAllBytes(input).AsSpan().SequenceEqual(original))
            throw new InvalidOperationException("Published recovery-backed import removal failed.");
        string removalBackup = Directory.GetDirectories(Path.Combine(directory,"backups"),"before-local-original-removal-*").Single();
        string removalRestored = await storage.RestoreBackupAsync(removalBackup, directory + "-removal-restored");
        if (!(await new JsonLocalMessageOriginals(removalRestored).ReadImportAsync(imported)).Bytes.AsSpan().SequenceEqual(original))
            throw new InvalidOperationException("Published removed import did not restore exactly.");
        shell.CloseDialog();
        if (local.Imports.Count != 0 || local.PreviewText.Length != 0) throw new InvalidOperationException("Published local-original close guard failed.");
        if(library.Exports.Count!=0 || All(library).OfType<MessageSourceInspection>().Any())throw new InvalidOperationException("Published library retained details after closing.");
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Receiver:IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account,CancellationToken token=default)=>throw new InvalidOperationException("No network in library fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No receive in library fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>throw new InvalidOperationException("No body fetch in library fixture.");
    }
    private sealed class Sender:IMailSender{public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken token=default)=>throw new InvalidOperationException("No send in library fixture.");}
    private sealed class Credentials:ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key,CancellationToken token=default)=>Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key,string value,CancellationToken token=default)=>throw new InvalidOperationException("No credential write in library fixture.");
        public Task DeleteAsync(CredentialKey key,CancellationToken token=default)=>throw new InvalidOperationException("No credential deletion in library fixture.");
    }
    private sealed class Host:IUiHost{public BSize ViewportSize=>new(1100,720);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
