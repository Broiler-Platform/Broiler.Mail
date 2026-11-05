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
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class MessageAttachmentViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ListAndSaveAreExplicitLocalActionsWithSelectableResultAndSafeFocus(double scale)
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var key=Key();var source=await store.SaveAsync(new(key,ReceivedAttachmentTests.Original()));
        using var actions=new MessageAttachmentActions(key,store,()=>true);actions.SetExport(source);var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(actions);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(actions,theme);session.RenderFrame();
        Assert.Empty(actions.Attachments);Assert.False(Directory.Exists(store.AttachmentDirectory));await actions.ListAsync();dispatcher.Drain();Assert.Equal(2,actions.Attachments.Count);
        var picker=All(actions).OfType<StandardComboBox>().Single();Assert.Equal(-1,picker.SelectedIndex);picker.SelectedIndex=0;
        var save=All(actions).OfType<StandardButton>().Single(button=>button.Text=="Save selected attachment");Assert.True(save.IsEnabled);Assert.False(save.IsDefault);
        await actions.SaveAsync(actions.Attachments[0]);dispatcher.Drain();Assert.Contains("not opened",actions.Status);Assert.Contains("SHA-256",All(actions).OfType<StandardRichEdit>().Single().GetPlainText());
        Assert.Equal(ReceivedAttachmentTests.Binary,File.ReadAllBytes(Directory.GetFiles(store.AttachmentDirectory).Single()));Assert.Same(picker,session.FocusedElement);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("replace")]
    [InlineData("stale")]
    [InlineData("dispose")]
    public async Task LateOrWrongContextInventoryNeverAppearsInAnotherReader(string mode)
    {
        var key=Key();var fake=new HeldExports();bool current=true;using var actions=new MessageAttachmentActions(key,fake,()=>current);actions.SetExport(Source(key));
        var task=actions.ListAsync();await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")actions.Cancel();else if(mode=="replace")actions.SetExport(null);else if(mode=="stale"){current=false;actions.RefreshContext();}else actions.Dispose();
        fake.Release.SetResult([Row(key)]);await task;Assert.Empty(actions.Attachments);
    }
    [Fact]
    public async Task WrongSourceAndFailureClearOldInventoryAndDoNotAllowSavingStaleSelection()
    {
        var key=Key();var fake=new HeldExports();fake.Release.SetResult([Row(key)]);using var actions=new MessageAttachmentActions(key,fake,()=>true);actions.SetExport(Source(key));await actions.ListAsync();var prior=Assert.Single(actions.Attachments);
        fake.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);fake.Release.SetResult([Row(Key())]);await actions.ListAsync();Assert.Empty(actions.Attachments);Assert.Contains("another source",actions.Status);
        await actions.SaveAsync(prior);Assert.Equal(0,fake.Saves);
    }
    private static MailMessageKey Key()=>new(AccountId.New(),"INBOX",7,1);
    private static MessageSourceExport Source(MailMessageKey key)=>new(key,"fixture.eml",1,new string('a',64));
    private static ReceivedAttachment Row(MailMessageKey key)=>new(key,new string('a',64),1,"file.bin","application/octet-stream",1,new string('b',64));
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class HeldExports:IMessageAttachmentExports
    {
        public string AttachmentDirectory=>"unused";public int Saves;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<IReadOnlyList<ReceivedAttachment>> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<ReceivedAttachment>> ListAttachmentsAsync(MessageSourceExport source,CancellationToken token=default){Entered.TrySetResult();return Release.Task;}
        public Task<SavedAttachment> SaveAttachmentAsync(MessageSourceExport source,ReceivedAttachment attachment,CancellationToken token=default){Saves++;throw new InvalidOperationException("No stale save permitted.");}
    }
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
