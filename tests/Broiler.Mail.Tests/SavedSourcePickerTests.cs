using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class SavedSourcePickerTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ReopenedReaderListsAndUsesLocallyWithoutReceiverOrAutomaticPreview(double scale)
    {
        using var directory=new TestDirectory();directory.Create();var account=TestDirectory.Profile();var key=new MailMessageKey(account.Id,"INBOX",7,1);var store=new FileMessageSourceExports(directory.Root);await store.SaveAsync(new(key,ReceivedAttachmentTests.Original()));
        var receiver=new NoReceiver();using var actions=new MessageSourceExportActions(account,key,receiver,new FileMessageSourceExports(directory.Root),()=>true,()=>false);
        var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(actions);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(actions,theme);session.RenderFrame();All(actions).OfType<FormSection>().Single().Toggle!.Click();session.RenderFrame();
        var picker=All(actions).OfType<SavedSourceExportPicker>().Single();var inspect=All(actions).OfType<MessageSourceInspection>().Single();var attachments=All(actions).OfType<MessageAttachmentActions>().Single();Assert.Empty(picker.Exports);Assert.False(inspect.IsPreviewVisible);
        await picker.ListAsync();dispatcher.Drain();var row=Assert.Single(picker.Exports);var choices=All(picker).OfType<StandardComboBox>().Single();Assert.Equal(-1,choices.SelectedIndex);
        choices.SelectedIndex=0;Assert.False(All(picker).OfType<StandardButton>().Single(button=>button.Text=="Use selected saved original").IsDefault);
        await picker.UseAsync(row);dispatcher.Drain();Assert.Contains("SHA-256",All(actions).OfType<StandardRichEdit>().Single(edit=>edit.AccessibleName=="Saved original source path and checksum").GetPlainText());Assert.False(inspect.IsPreviewVisible);Assert.Empty(attachments.Attachments);Assert.Same(choices,session.FocusedElement);
        await inspect.ReadPreviewAsync();dispatcher.Drain();Assert.True(inspect.IsPreviewVisible);await attachments.ListAsync();dispatcher.Drain();Assert.Equal(2,attachments.Attachments.Count);Assert.Equal(0,receiver.Calls);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("reset")]
    [InlineData("dispose")]
    [InlineData("queued")]
    public async Task LateInventoryNeverActivatesOrAppearsAfterContextLoss(string mode)
    {
        var key=SavedSourceCatalogTests.Key();var fake=new HeldCatalog();bool current=true;MessageSourceExport? activated=null;var dispatcher=new TestQueueDispatcher();
        using var picker=new SavedSourceExportPicker(key,fake,fake,()=>current,()=>true,row=>activated=row);using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(picker);
        var task=picker.ListAsync();await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")picker.Cancel();else if(mode=="stale"){current=false;picker.RefreshContext();}else if(mode=="reset")picker.Reset();else if(mode=="dispose")picker.Dispose();
        fake.Release.SetResult([Row(key)]);await task;if(mode=="queued"){current=false;picker.RefreshContext();}dispatcher.Drain();Assert.Empty(picker.Exports);Assert.Null(activated);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("reset")]
    [InlineData("dispose")]
    [InlineData("queued")]
    public async Task LateFileVerificationNeverActivatesAChangedReader(string mode)
    {
        var key=SavedSourceCatalogTests.Key();var fake=new HeldCatalog();fake.Release.SetResult([Row(key)]);fake.Verify=new(TaskCreationOptions.RunContinuationsAsynchronously);bool current=true;MessageSourceExport? active=null;var dispatcher=new TestQueueDispatcher();
        using var picker=new SavedSourceExportPicker(key,fake,fake,()=>current,()=>true,row=>active=row);using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(picker);
        await picker.ListAsync();dispatcher.Drain();var task=picker.UseAsync(picker.Exports[0]);await fake.VerifyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")picker.Cancel();else if(mode=="stale"){current=false;picker.RefreshContext();}else if(mode=="reset")picker.Reset();else if(mode=="dispose")picker.Dispose();
        fake.Verify.SetResult(new(key,[1]));await task;if(mode=="queued"){current=false;picker.RefreshContext();}dispatcher.Drain();Assert.Null(active);
    }

    [Fact]
    public async Task TamperedSelectionClearsPriorActivationAndRequiresExplicitRelist()
    {
        using var directory=new TestDirectory();directory.Create();var key=SavedSourceCatalogTests.Key();var store=new FileMessageSourceExports(directory.Root);var receipt=await store.SaveAsync(new(key,SavedSourceCatalogTests.Bytes));MessageSourceExport? active=null;
        using var picker=new SavedSourceExportPicker(key,store,store,()=>true,()=>true,row=>active=row);await picker.ListAsync();var row=Assert.Single(picker.Exports);await picker.UseAsync(row);Assert.NotNull(active);
        byte[] bytes=File.ReadAllBytes(receipt.Path);bytes[0]^=1;File.WriteAllBytes(receipt.Path,bytes);await picker.UseAsync(row);Assert.Null(active);Assert.Empty(picker.Exports);Assert.Contains("did not finish",picker.Status);
        await picker.UseAsync(row);Assert.Null(active);
    }
    [Fact]
    public async Task AnotherMessagesCatalogRowsAreRejectedBeforeActivation()
    {
        var key=SavedSourceCatalogTests.Key();var fake=new HeldCatalog();fake.Release.SetResult([Row(key with{Uid=2})]);
        using var picker=new SavedSourceExportPicker(key,fake,fake,()=>true,()=>true,_=>{});await picker.ListAsync();Assert.Empty(picker.Exports);Assert.Contains("mismatched",picker.Status);
    }
    [Fact]
    public async Task CanceledAfterFileAndReceiptCommitDoesNotActivateSourceControls()
    {
        using var directory=new TestDirectory();directory.Create();var account=TestDirectory.Profile();var key=new MailMessageKey(account.Id,"INBOX",7,1);var inner=new FileMessageSourceExports(directory.Root);var held=new CommittedExport(inner);
        using var actions=new MessageSourceExportActions(account,key,new ReceiptReceiver(),held,()=>true,()=>true);
        var task=actions.ExportAsync();await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));actions.Cancel();held.Release.SetResult();await task;
        Assert.Contains("canceled",actions.Status);Assert.Equal("",All(actions).OfType<StandardRichEdit>().Single(edit=>edit.AccessibleName=="Saved original source path and checksum").GetPlainText());
        Assert.Single(await inner.ListSavedExportsAsync(key));
    }
    private sealed class CommittedExport(FileMessageSourceExports inner):IMessageSourceExports
    {
        public string ExportDirectory=>inner.ExportDirectory;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MessageSourceExport> SaveAsync(MailMessageSource source,CancellationToken token=default){var receipt=await inner.SaveAsync(source,token);Entered.SetResult();await Release.Task;return receipt;}
    }
    private sealed class ReceiptReceiver:IMailSourceReceiver
    {public Task<MailMessageSource> GetSourceAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>Task.FromResult(new MailMessageSource(key,SavedSourceCatalogTests.Bytes));}
    private static SavedMessageSourceExport Row(MailMessageKey key)=>new(new(key,"fixture.eml",1,new string('a',64)),DateTimeOffset.UtcNow);
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class HeldCatalog:IMessageSourceExportCatalog,IVerifiedMessageSourceExports
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<IReadOnlyList<SavedMessageSourceExport>> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<SavedMessageSourceExport>> ListSavedExportsAsync(MailMessageKey key,CancellationToken token=default){Entered.TrySetResult();return Release.Task;}
        public TaskCompletionSource<MailMessageSource>? Verify;public TaskCompletionSource VerifyEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport export,CancellationToken token=default){VerifyEntered.TrySetResult();return Verify?.Task??throw new InvalidOperationException("No automatic read.");}
    }
    private sealed class NoReceiver:IMailSourceReceiver
    {public int Calls;public Task<MailMessageSource> GetSourceAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default){Calls++;throw new InvalidOperationException("Local reuse must not fetch.");}}
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
