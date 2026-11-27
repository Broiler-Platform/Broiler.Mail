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
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class SavedOriginalLibraryTests
{
    [Fact]
    public async Task AccountCatalogListsFoldersEpochsAndUidsWithoutOpeningOrCreatingCache()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var account=AccountId.New();
        var receipts=new List<MessageSourceExport>();foreach(var key in new[]{new MailMessageKey(account,"INBOX",7,1),new(account,"Archive",9,1),new(account,"Archive",8,1),new(account,"INBOX",7,2)})receipts.Add(await store.SaveAsync(new(key,SavedSourceCatalogTests.Bytes)));
        await store.SaveAsync(new(new(AccountId.New(),"INBOX",7,1),SavedSourceCatalogTests.Bytes));var rows=await new FileMessageSourceExports(directory.Root).ListAccountExportsAsync(account);
        Assert.Equal(4,rows.Count);Assert.All(rows,row=>Assert.Contains(row.Export,receipts));Assert.Equal(rows.OrderByDescending(row=>row.SavedAt).Select(row=>row.SavedAt),rows.Select(row=>row.SavedAt));Assert.False(File.Exists(directory.File("mail-cache.db")));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.ListAccountExportsAsync(default));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.ListAccountExportsAsync(account,new(true)));
    }
    [Theory]
    [InlineData(1d,false)]
    [InlineData(2d,false)]
    [InlineData(1d,true)]
    [InlineData(2d,true)]
    public async Task LibraryExplicitlyVerifiesAndInspectsWithoutAnInboxBody(double scale,bool disabled)
    {
        using var directory=new TestDirectory();directory.Create();var account=TestDirectory.Profile() with{IsEnabled=!disabled};var store=new FileMessageSourceExports(directory.Root);var key=new MailMessageKey(account.Id,"Archive",9,1);
        await store.SaveAsync(new(key,ReceivedAttachmentTests.Original()));await store.SaveAsync(new(key with{Uid=2},ReceivedAttachmentTests.Original()));
        using var library=new SavedOriginalsView(store);library.SetAccount(account);library.SetActive(true);var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(library);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(library,theme);session.RenderFrame();Assert.Empty(library.Exports);
        await library.ListAsync();dispatcher.Drain();Assert.Equal(2,library.Exports.Count);var picker=All(library).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Saved originals for current account");Assert.Equal(-1,picker.SelectedIndex);
        Assert.False(All(library).OfType<StandardButton>().Single(button=>button.Text=="Verify selected original").IsDefault);var remove=Button(library,SavedOriginalsView.RemoveButtonText);var saveCopy=Button(library,SavedOriginalsView.SaveCopyButtonText);Assert.False(remove.IsEnabled);Assert.False(saveCopy.IsEnabled);
        await library.VerifyAsync(library.Exports[0]);dispatcher.Drain();session.RenderFrame();Assert.Same(picker,session.FocusedElement);Assert.True(remove.IsEnabled);Assert.False(saveCopy.IsEnabled);
        var identity=All(library).OfType<StandardRichEdit>().Single(edit=>edit.AccessibleName=="Saved-original account identity path and checksum");Assert.True(identity.IsReadOnly);Assert.Contains(account.Id.ToString(),identity.GetPlainText());Assert.Contains("Folder Archive",identity.GetPlainText());Assert.Contains("SHA-256",identity.GetPlainText());
        var inspect=All(library).OfType<MessageSourceInspection>().Single();var attachments=All(library).OfType<MessageAttachmentActions>().Single();Assert.False(inspect.IsPreviewVisible);Assert.Empty(attachments.Attachments);
        await inspect.ReadPreviewAsync();dispatcher.Drain();Assert.True(inspect.IsPreviewVisible);await attachments.ListAsync();dispatcher.Drain();Assert.Equal(2,attachments.Attachments.Count);await attachments.SaveAsync(attachments.Attachments[0]);dispatcher.Drain();Assert.Equal(ReceivedAttachmentTests.Binary,File.ReadAllBytes(Directory.GetFiles(store.AttachmentDirectory).Single()));
        picker.SelectedIndex=1;Assert.Empty(All(library).OfType<MessageSourceInspection>());Assert.Equal("",identity.GetPlainText());library.SetActive(false);Assert.Empty(library.Exports);Assert.False(File.Exists(directory.File("mail-cache.db")));
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("queued")]
    public async Task LateAccountInventoryCannotRepopulateClosedOrChangedLibrary(string mode)
    {
        var account=TestDirectory.Profile();var backend=new HeldExports();using var library=new SavedOriginalsView(backend);var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(library);library.SetAccount(account);library.SetActive(true);
        var task=library.ListAsync();await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")library.Cancel();else if(mode=="account")library.SetAccount(TestDirectory.Profile());else if(mode=="close")library.SetActive(false);else if(mode=="dispose")library.Dispose();
        backend.Rows.SetResult([Row(account.Id)]);await task;if(mode=="queued")library.SetActive(false);dispatcher.Drain();Assert.Empty(library.Exports);Assert.Empty(All(library).OfType<MessageSourceInspection>());
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("queued")]
    [InlineData("selection")]
    public async Task LateVerificationCannotCreateDetailsAfterContextLoss(string mode)
    {
        var account=TestDirectory.Profile();var backend=new HeldExports();backend.Rows.SetResult([Row(account.Id),Row(account.Id,2)]);backend.Verify=new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var library=new SavedOriginalsView(backend);var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(library);library.SetAccount(account);library.SetActive(true);await library.ListAsync();dispatcher.Drain();var chosen=library.Exports[0];
        var task=library.VerifyAsync(chosen);await backend.VerifyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")library.Cancel();else if(mode=="account")library.SetAccount(TestDirectory.Profile());else if(mode=="close")library.SetActive(false);else if(mode=="dispose")library.Dispose();else if(mode=="selection")All(library).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == "Saved originals for current account").SelectedIndex=1;
        backend.Verify.SetResult(new(chosen.Export.Key,[1]));await task;if(mode=="queued")library.SetActive(false);dispatcher.Drain();Assert.Empty(All(library).OfType<MessageSourceInspection>());if(mode!="dispose")Assert.All(new[]{SavedOriginalsView.SaveCopyButtonText,SavedOriginalsView.RemoveButtonText},text=>Assert.False(Button(library,text).IsEnabled));Assert.False(library.HasPendingRemoval);
    }
    [Fact]
    public async Task AnotherAccountOrChangedFileIsNeverActivated()
    {
        var account=TestDirectory.Profile();var bad=new HeldExports();bad.Rows.SetResult([Row(AccountId.New())]);using(var library=new SavedOriginalsView(bad)){library.SetAccount(account);library.SetActive(true);await library.ListAsync();Assert.Empty(library.Exports);Assert.Contains("mismatched",library.Status);}
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var receipt=await store.SaveAsync(new(new(account.Id,"INBOX",7,1),SavedSourceCatalogTests.Bytes));using var view=new SavedOriginalsView(store);view.SetAccount(account);view.SetActive(true);await view.ListAsync();File.Delete(receipt.Path);await view.VerifyAsync(view.Exports[0]);Assert.Empty(All(view).OfType<MessageSourceInspection>());Assert.Empty(view.Exports);Assert.Contains("did not finish",view.Status);
    }
    [Fact]
    public async Task NewAccountCanListWhileOldUncancellableLookupFinishes()
    {
        var account=TestDirectory.Profile();var other=TestDirectory.Profile();var backend=new HeldExports();using var library=new SavedOriginalsView(backend);library.SetAccount(account);library.SetActive(true);var old=library.ListAsync();await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldRows=backend.Rows;backend.Rows=new(TaskCreationOptions.RunContinuationsAsynchronously);backend.Rows.SetResult([Row(other.Id)]);library.SetAccount(other);await library.ListAsync();Assert.Equal(other.Id,Assert.Single(library.Exports).Export.Key.AccountId);
        oldRows.SetResult([Row(account.Id)]);await old;Assert.Equal(other.Id,Assert.Single(library.Exports).Export.Key.AccountId);
    }
    [Fact]
    public async Task Ctrl6OpensLibraryAndCloseRejectsPendingWorkWithoutAnInboxCache()
    {
        using var directory=new TestDirectory();var account=TestDirectory.Profile();var backend=new HeldExports();using var application=new MailApplication(new JsonAccountStore(directory.File("accounts.json")),new JsonSettingsStore(directory.File("settings.json")),new TestMailReceiver(),new Sender(),new TestCredentialStore(),sourceExports:backend);
        await application.Accounts.SaveAsync(account);await application.InitializeAsync();var model=application.CreateViewModel();using var shell=new MailShellView(model);var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());shell.Attach(session);
        Assert.Equal("Ctrl+6",MailShortcuts.For(MailCommand.SavedOriginals).Gesture);Assert.True(shell.CreateKeyboardNavigation(session).Execute(MailCommand.SavedOriginals));Assert.Equal("originals",shell.ActiveViewId);Assert.Equal("Saved originals",shell.ActiveDialog!.Title);
        var task=shell.SavedOriginals.ListAsync();await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));shell.CloseDialog();backend.Rows.SetResult([Row(account.Id)]);await task;dispatcher.Drain();Assert.Equal("inbox",shell.ActiveViewId);Assert.Empty(shell.SavedOriginals.Exports);Assert.Null(shell.ActiveDialog);
    }
    [Fact]
    public async Task UnsupportedHostExplainsDisabledLibraryWithoutStartingWork()
    {
        using var library=new SavedOriginalsView(null);library.SetAccount(TestDirectory.Profile());library.SetActive(true);await library.ListAsync();Assert.Empty(library.Exports);Assert.Contains("unavailable",library.Status);Assert.All(All(library).OfType<StandardButton>(),button=>Assert.False(button.IsEnabled));Assert.Equal(UiVisibility.Collapsed,Button(library,SavedOriginalsView.RemoveButtonText).Visibility);
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task RemoveIsKeepFirstAndUpdatesListWithoutRelisting(double scale)
    {
        using var directory=new TestDirectory();directory.Create();var account=TestDirectory.Profile();var store=new FileMessageSourceExports(directory.Root);
        for(uint uid=1;uid<=3;uid++)await store.SaveAsync(new(new(account.Id,"Archive",9,uid),SavedSourceCatalogTests.Bytes));
        var backend=new CountingExports(store);using var library=new SavedOriginalsView(backend,files:new FakeMailFilePicker());library.SetAccount(account);library.SetActive(true);
        var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(library);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(library,theme);session.RenderFrame();
        var remove=Button(library,SavedOriginalsView.RemoveButtonText);var keep=Button(library,SavedOriginalsView.KeepButtonText);var delete=Button(library,SavedOriginalsView.DeleteButtonText);var saveCopy=Button(library,SavedOriginalsView.SaveCopyButtonText);
        Assert.Equal(UiVisibility.Visible,remove.Visibility);Assert.False(remove.IsEnabled);Assert.False(saveCopy.IsEnabled);Assert.False(library.HasPendingRemoval);
        await library.ListAsync();dispatcher.Drain();Assert.Equal(3,library.Exports.Count);Assert.False(remove.IsEnabled);
        var picker=Picker(library);picker.SelectedIndex=1;var chosen=library.Exports[1];Assert.True(remove.IsEnabled);Assert.False(saveCopy.IsEnabled);
        remove.Click();session.RenderFrame();Assert.True(library.HasPendingRemoval);Assert.Same(keep,session.FocusedElement);Assert.False(keep.IsDefault);Assert.False(delete.IsDefault);
        Assert.Equal(SavedOriginalsView.RemovalQuestion(chosen),library.ConfirmationText);Assert.Contains($"({SavedSourceCatalogTests.Bytes.Length} bytes, Archive UID {chosen.Export.Key.Uid})?",library.ConfirmationText);
        Assert.Contains("frees a slot in the 64-file saved-originals folder",library.ConfirmationText);Assert.Contains("Profile backups do not include saved originals.",library.ConfirmationText);
        Assert.All(new[]{Button(library,"List account saved originals"),Button(library,"Verify selected original"),remove,saveCopy},button=>Assert.False(button.IsEnabled));Assert.False(picker.IsEnabled);Assert.True(keep.IsEnabled);Assert.True(delete.IsEnabled);
        keep.Click();Assert.False(library.HasPendingRemoval);Assert.Equal("",library.ConfirmationText);Assert.Contains("Nothing was deleted",library.Status);Assert.Same(remove,session.FocusedElement);Assert.True(File.Exists(chosen.Export.Path));Assert.Equal(0,backend.RemoveCalls);
        remove.Click();await library.ConfirmRemovalAsync();dispatcher.Drain();session.RenderFrame();
        Assert.False(File.Exists(chosen.Export.Path));Assert.Equal(1,backend.RemoveCalls);Assert.Equal(1,backend.ListCalls);Assert.Equal(2,library.Exports.Count);Assert.DoesNotContain(chosen,library.Exports);
        Assert.Equal("Saved original deleted. 2 saved originals remain for this account.",library.Status);Assert.Same(picker,session.FocusedElement);Assert.False(library.HasPendingRemoval);Assert.Equal(2,(await store.ListAccountExportsAsync(account.Id)).Count);
        var missing=library.Exports[0];File.Delete(missing.Export.Path);library.RequestRemoval(missing);await library.ConfirmRemovalAsync();dispatcher.Drain();
        Assert.Equal("Saved original deleted (its file was already gone; only the receipt was removed). 1 saved original remains for this account.",library.Status);Assert.Equal(1,backend.ListCalls);
        library.RequestRemoval(library.Exports[0]);await library.ConfirmRemovalAsync();dispatcher.Drain();Assert.Empty(library.Exports);Assert.Contains("0 saved originals remain",library.Status);Assert.Same(Button(library,"List account saved originals"),session.FocusedElement);
        Assert.Empty(await store.ListAccountExportsAsync(account.Id));Assert.Empty(Directory.GetFiles(store.ExportDirectory,"*.eml"));
    }
    [Theory]
    [InlineData("selection")]
    [InlineData("account")]
    [InlineData("close")]
    [InlineData("escape")]
    public async Task PendingRemovalIsClearedBySelectionAccountCloseAndEscape(string mode)
    {
        using var directory=new TestDirectory();directory.Create();var account=TestDirectory.Profile();var store=new FileMessageSourceExports(directory.Root);
        for(uint uid=1;uid<=2;uid++)await store.SaveAsync(new(new(account.Id,"INBOX",7,uid),SavedSourceCatalogTests.Bytes));var backend=new CountingExports(store);var dispatcher=new TestQueueDispatcher();
        using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());MailApplication? application=null;MailShellView? shell=null;SavedOriginalsView library;
        if(mode=="escape")
        {
            application=new MailApplication(new JsonAccountStore(directory.File("accounts.json")),new JsonSettingsStore(directory.File("settings.json")),new TestMailReceiver(),new Sender(),new TestCredentialStore(),sourceExports:backend);
            await application.Accounts.SaveAsync(account);await application.InitializeAsync();shell=new MailShellView(application.CreateViewModel());shell.Attach(session);
            Assert.True(shell.CreateKeyboardNavigation(session).Execute(MailCommand.SavedOriginals));library=shell.SavedOriginals;
        }
        else{library=new SavedOriginalsView(backend);session.AddRoot(library);library.SetAccount(account);library.SetActive(true);}
        try
        {
            await library.ListAsync();dispatcher.Drain();var chosen=library.Exports[0];library.RequestRemoval(chosen);Assert.True(library.HasPendingRemoval);
            if(mode=="selection")Picker(library).SelectedIndex=1;else if(mode=="account")library.SetAccount(TestDirectory.Profile());else if(mode=="close")library.SetActive(false);
            else{Assert.True(shell!.CreateKeyboardNavigation(session).Execute(MailCommand.BackOrCancel));Assert.Equal("originals",shell.ActiveViewId);Assert.Contains("Nothing was deleted",library.Status);}
            Assert.False(library.HasPendingRemoval);Assert.Equal("",library.ConfirmationText);await library.ConfirmRemovalAsync();dispatcher.Drain();
            Assert.Equal(0,backend.RemoveCalls);Assert.True(File.Exists(chosen.Export.Path));Assert.Equal(2,(await store.ListAccountExportsAsync(account.Id)).Count);
            if(mode=="escape"){Assert.True(shell!.CreateKeyboardNavigation(session).Execute(MailCommand.BackOrCancel));Assert.Equal("inbox",shell.ActiveViewId);}
        }
        finally{shell?.Dispose();if(mode!="escape")library.Dispose();application?.Dispose();}
    }
    [Fact]
    public async Task SaveCopyRequiresVerificationAndReverifiesBeforeWriting()
    {
        using var directory=new TestDirectory();directory.Create();var account=TestDirectory.Profile();var store=new FileMessageSourceExports(directory.Root);
        for(uint uid=1;uid<=2;uid++)await store.SaveAsync(new(new(account.Id,"INBOX",7,uid),SavedSourceCatalogTests.Bytes));
        var backend=new CountingExports(store);var files=new FakeMailFilePicker();using var library=new SavedOriginalsView(backend,files:files);library.SetAccount(account);library.SetActive(true);
        var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(library);var saveCopy=Button(library,SavedOriginalsView.SaveCopyButtonText);
        await library.ListAsync();dispatcher.Drain();var picker=Picker(library);picker.SelectedIndex=0;var chosen=library.Exports[0];Assert.False(saveCopy.IsEnabled);
        await library.SaveCopyAsync(chosen);Assert.Empty(files.Requests);
        await library.VerifyAsync(chosen);dispatcher.Drain();Assert.True(saveCopy.IsEnabled);int verified=backend.VerifyCalls;
        string target=directory.File("copy.eml");files.WillSave(target);await library.SaveCopyAsync(chosen);dispatcher.Drain();
        var request=Assert.Single(files.Requests).Request;Assert.Equal(MailFilePurpose.SaveMessage,request.Purpose);Assert.Equal(MailFileType.Email,Assert.Single(request.Types));Assert.EndsWith(".eml",request.SuggestedFileName);
        Assert.Equal(verified+1,backend.VerifyCalls);Assert.Equal(SavedSourceCatalogTests.Bytes,File.ReadAllBytes(target));Assert.StartsWith("Saved a copy as "+target,library.Status);Assert.True(File.Exists(chosen.Export.Path));Assert.Same(saveCopy,session.FocusedElement);
        await library.SaveCopyAsync(chosen);dispatcher.Drain();Assert.Equal("No file chosen. Nothing was saved.",library.Status);Assert.Same(saveCopy,session.FocusedElement);
        files.SaveResults.Enqueue(MailFilePick<IMailWritableFile>.Rejected("Broiler Mail never replaces files."));await library.SaveCopyAsync(chosen);dispatcher.Drain();Assert.Contains("never replaces files",library.Status);
        string swapped=directory.File("swapped.eml");files.WillSave(swapped);backend.AfterVerify=source=>source with{Bytes=source.Bytes.Select(value=>(byte)(value^1)).ToArray()};await library.SaveCopyAsync(chosen);dispatcher.Drain();
        Assert.False(File.Exists(swapped));Assert.Contains("no longer matches its checksum",library.Status);backend.AfterVerify=null;
        byte[] changed=SavedSourceCatalogTests.Bytes.ToArray();changed[0]^=1;File.WriteAllBytes(chosen.Export.Path,changed);string tampered=directory.File("tampered.eml");files.WillSave(tampered);await library.SaveCopyAsync(chosen);dispatcher.Drain();
        Assert.False(File.Exists(tampered));Assert.Contains("The copy was not saved.",library.Status);Assert.Empty(Directory.GetFiles(directory.Root,"*.part",SearchOption.AllDirectories));
        picker.SelectedIndex=1;Assert.False(saveCopy.IsEnabled);int requests=files.Requests.Count;await library.SaveCopyAsync(library.Exports[1]);Assert.Equal(requests,files.Requests.Count);
        using var phone=new SavedOriginalsView(backend);phone.SetAccount(account);phone.SetActive(true);await phone.ListAsync();await phone.VerifyAsync(phone.Exports[1]);var unavailable=Button(phone,SavedOriginalsView.SaveCopyButtonText);
        Assert.False(unavailable.IsEnabled);Assert.Equal(UnavailableMailFilePicker.NotOnThisDeviceYet.Reason,unavailable.ToolTipText);Assert.Contains("verified locally",phone.Status);
    }
    private static StandardButton Button(UiElement root,string text)=>All(root).OfType<StandardButton>().Single(button=>button.Text==text);
    private static StandardComboBox Picker(UiElement root)=>All(root).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Saved originals for current account");
    private static SavedMessageSourceExport Row(AccountId account,uint uid=1)=>new(new(new(account,"INBOX",7,uid),"fixture"+uid+".eml",1,new string('a',64)),DateTimeOffset.UtcNow);
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class HeldExports:IMessageSourceExports,IAccountSourceExportCatalog,IVerifiedMessageSourceExports
    {
        public string ExportDirectory=>"unused";public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),VerifyEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<IReadOnlyList<SavedMessageSourceExport>> Rows=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<MailMessageSource>? Verify;
        public Task<IReadOnlyList<SavedMessageSourceExport>> ListAccountExportsAsync(AccountId account,CancellationToken token=default){Entered.TrySetResult();return Rows.Task;}
        public Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport source,CancellationToken token=default){VerifyEntered.TrySetResult();return Verify?.Task??Task.FromResult(new MailMessageSource(source.Key,[1]));}
        public Task<MessageSourceExport> SaveAsync(MailMessageSource source,CancellationToken token=default)=>throw new InvalidOperationException("Library must not export/download.");
    }
    private sealed class CountingExports(FileMessageSourceExports store):IMessageSourceExports,IAccountSourceExportCatalog,IVerifiedMessageSourceExports,ISourceExportCatalogMaintenance
    {
        public int ListCalls,VerifyCalls,RemoveCalls;public Func<MailMessageSource,MailMessageSource>? AfterVerify;public string ExportDirectory=>store.ExportDirectory;
        public Task<IReadOnlyList<SavedMessageSourceExport>> ListAccountExportsAsync(AccountId account,CancellationToken token=default){ListCalls++;return store.ListAccountExportsAsync(account,token);}
        public async Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport source,CancellationToken token=default){VerifyCalls++;var read=await store.ReadVerifiedAsync(source,token);return AfterVerify?.Invoke(read)??read;}
        public Task<RemovedMessageSourceExport> RemoveExportAsync(SavedMessageSourceExport saved,CancellationToken token=default){RemoveCalls++;return store.RemoveExportAsync(saved,token);}
        public Task<MessageSourceExport> SaveAsync(MailMessageSource source,CancellationToken token=default)=>throw new InvalidOperationException("Library must not export/download.");
    }
    private sealed class Sender:IMailSender{public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken token=default)=>throw new InvalidOperationException("Library must not send.");}
    private sealed class Host:IUiHost{public BSize ViewportSize=>new(940,680);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
