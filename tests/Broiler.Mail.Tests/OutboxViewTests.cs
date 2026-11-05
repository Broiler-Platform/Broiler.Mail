using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OutboxViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task SelectableDetailsFiltersAndComposeHandoffPreserveUnknownAndDoNotSend(double scale)
    {
        using var directory=new TestDirectory();var account=TestDirectory.Profile();var store=new JsonDraftStore(directory.File("drafts.json"));
        var unknown=new DraftSnapshot{Draft=MailComposition.Create(account) with{Subject="Unknown fixture",PlainText="Retained"},ToText="to@example.test",CcText="",BccText="hidden@example.test",State=DraftSubmissionState.Unknown};
        var state=await store.SaveAsync(0,unknown);state=await store.ParkAsync(state.Revision);var sender=new Sender();using var model=new ComposerViewModel(store,state,sender:sender);model.SetAccount(account);
        int opened=0;using var root=new OutboxView(model,()=>opened++).CreateContent();var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(root);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(root,theme);session.RenderFrame();
        var picker=All(root).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Outbox records for selected account");picker.SelectedIndex=0;
        var details=All(root).OfType<ScrollableMessageText>().Single();Assert.Contains("SMTP: Unknown",details.Text);Assert.Contains("hidden@example.test",details.Text);Assert.True(details.Editor.IsReadOnly);
        var filter=All(root).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Outbox record filter");filter.SelectedIndex=(int)OutboxFilter.Accepted;Assert.Equal(-1,picker.SelectedIndex);
        Assert.DoesNotContain("hidden@example.test",details.Text);filter.SelectedIndex=(int)OutboxFilter.All;picker.SelectedIndex=0;
        var open=All(root).OfType<StandardButton>().Single();Assert.True(open.IsEnabled);Assert.False(open.IsDefault);open.Click();dispatcher.DrainUntil(()=>opened==1);
        Assert.Equal(unknown.Draft.Id,model.DraftId);Assert.False(model.CanSend);Assert.Equal(0,sender.Calls);Assert.True(root.Bounds.Height<=480);
        model.SetAccount(TestDirectory.Profile());Assert.Equal(-1,picker.SelectedIndex);Assert.DoesNotContain("hidden@example.test",details.Text);Assert.False(open.IsEnabled);
    }
    [Fact]
    public async Task Ctrl5OpensOutboxAndClosingDuringPendingHandoffDoesNotReopenCompose()
    {
        using var directory=new TestDirectory();var account=TestDirectory.Profile();var store=new HeldStore();var sender=new Sender();
        using var app=new MailApplication(new JsonAccountStore(directory.File("accounts.json")),new JsonSettingsStore(directory.File("settings.json")),new TestMailReceiver(),sender,new TestCredentialStore(),drafts:store);
        await app.Accounts.SaveAsync(account);await app.InitializeAsync();var model=app.CreateViewModel();model.Composer.StartNew();model.Composer.Edit("to@example.test","","","Held current","Body");await model.Composer.SaveAsync();
        using var shell=new MailShellView(model);var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());shell.Attach(session);
        Assert.Equal("Ctrl+5",MailShortcuts.For(MailCommand.Outbox).Gesture);Assert.True(shell.CreateKeyboardNavigation(session).Execute(MailCommand.Outbox));Assert.Equal("outbox",shell.ActiveViewId);
        var root=shell.GetContent("outbox");var picker=All(root).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Outbox records for selected account");picker.SelectedIndex=0;
        store.Hold=true;model.Composer.Edit("to@example.test","","","New unsaved edit","Body");All(root).OfType<StandardButton>().Single().Click();await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shell.CloseDialog();store.Release.SetResult();await shell.Outbox.OpenTask;dispatcher.Drain();Assert.Equal("inbox",shell.ActiveViewId);Assert.Null(shell.ActiveDialog);Assert.Equal(0,sender.Calls);
        model.Inbox.Dispose();model.Composer.Dispose();
    }
    [Fact]
    public async Task OutboxCanBeBrowsedWhileSendingButCannotStartAHandoffOrAnotherSend()
    {
        var sender=new HoldingSender();using var model=new ComposerViewModel(sender:sender);var account=TestDirectory.Profile() with{OutgoingServer=new(){Host="smtp.example.test",Port=587,UserName="test@example.test",Security=TransportSecurity.StartTls}};
        model.SetAccount(account);model.StartNew();model.Edit("to@example.test","","","In progress","Body");var sending=model.SendAsync();await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var root=new OutboxView(model,()=>throw new InvalidOperationException("Must not open while sending.")).CreateContent();
        var picker=All(root).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Outbox records for selected account");Assert.True(picker.IsEnabled);picker.SelectedIndex=0;
        Assert.Contains("SMTP: Sending",All(root).OfType<ScrollableMessageText>().Single().Text);Assert.Contains("1 in progress",model.OutboxNotice);Assert.False(All(root).OfType<StandardButton>().Single().IsEnabled);
        sender.Release.SetResult(new(SubmissionStatus.Unknown));await sending;Assert.Contains("SMTP: Unknown",All(root).OfType<ScrollableMessageText>().Single().Text);Assert.Equal(1,sender.Calls);
    }
    private sealed class HoldingSender:IMailSender
    {
        public int Calls;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<SendResult> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken cancellationToken=default){Calls++;Entered.TrySetResult();return Release.Task;}
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Sender:IMailSender
    {public int Calls;public Task<SendResult> SendAsync(AccountProfile account,MailDraft draft,CancellationToken cancellationToken=default){Calls++;throw new InvalidOperationException("Outbox must not send.");}}
    private sealed class HeldStore:IDraftStore
    {
        private DraftStoreState state=new(0,null);public bool Hold;public bool IsPersistent=>true;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken=default)=>Task.FromResult(state);
        public async Task<DraftStoreState> SaveAsync(long revision,DraftSnapshot? draft,CancellationToken cancellationToken=default)
        {if(Hold){Entered.TrySetResult();await Release.Task;}if(state.Revision!=revision)throw new DraftConflictException();return state=new(revision+1,draft);}
    }
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
