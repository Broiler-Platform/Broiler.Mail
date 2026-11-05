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
        Assert.False(All(library).OfType<StandardButton>().Single(button=>button.Text=="Verify selected original").IsDefault);
        await library.VerifyAsync(library.Exports[0]);dispatcher.Drain();session.RenderFrame();Assert.Same(picker,session.FocusedElement);
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
        backend.Verify.SetResult(new(chosen.Export.Key,[1]));await task;if(mode=="queued")library.SetActive(false);dispatcher.Drain();Assert.Empty(All(library).OfType<MessageSourceInspection>());
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
        using var library=new SavedOriginalsView(null);library.SetAccount(TestDirectory.Profile());library.SetActive(true);await library.ListAsync();Assert.Empty(library.Exports);Assert.Contains("unavailable",library.Status);Assert.All(All(library).OfType<StandardButton>(),button=>Assert.False(button.IsEnabled));
    }
    private static SavedMessageSourceExport Row(AccountId account,uint uid=1)=>new(new(new(account,"INBOX",7,uid),"fixture"+uid+".eml",1,new string('a',64)),DateTimeOffset.UtcNow);
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class HeldExports:IMessageSourceExports,IAccountSourceExportCatalog,IVerifiedMessageSourceExports
    {
        public string ExportDirectory=>"unused";public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),VerifyEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<IReadOnlyList<SavedMessageSourceExport>> Rows=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<MailMessageSource>? Verify;
        public Task<IReadOnlyList<SavedMessageSourceExport>> ListAccountExportsAsync(AccountId account,CancellationToken token=default){Entered.TrySetResult();return Rows.Task;}
        public Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport source,CancellationToken token=default){VerifyEntered.TrySetResult();return Verify?.Task??Task.FromResult(new MailMessageSource(source.Key,[1]));}
        public Task<MessageSourceExport> SaveAsync(MailMessageSource source,CancellationToken token=default)=>throw new InvalidOperationException("Library must not export/download.");
    }
    private sealed class Sender:IMailSender{public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken token=default)=>throw new InvalidOperationException("Library must not send.");}
    private sealed class Host:IUiHost{public BSize ViewportSize=>new(940,680);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
