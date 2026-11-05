using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OriginalSourceInboxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderExportIsExplicitAndMaintenanceOrAccountSwitchCancelsLateSource(bool switchAccount)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db")); var key = new MailMessageKey(account.Id,"INBOX",7,1);
        await cache.SaveSummariesAsync(new(account.Id,"INBOX",7), [new() { Key = key, Subject = "Source fixture", Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(new(account.Id,"INBOX",7),new(key,"Cached preview")); var receiver = new Receiver(); var exports = new FileMessageSourceExports(directory.Root);
        using var model = new InboxViewModel(receiver,new ImmediateUiDispatcher(),cache,sourceExports:exports); model.SetAccount(account); await model.CacheLoadTask; await model.SelectAsync(key);
        using var root = new InboxView(model).CreateContent(); Assert.Equal(0,receiver.Calls); var actions = All(root).OfType<MessageSourceExportActions>().Single();
        var exporting = actions.ExportAsync(); await receiver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (switchAccount) { model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; Assert.True(actions.IsDisposed); }
        else { model.BeginStorageMaintenance(); Assert.False(model.CanDownloadOriginalSource); }
        receiver.Release.SetResult(new(key,Encoding.ASCII.GetBytes("Subject: Original\r\n\r\nUnchanged bytes\r\n"))); await exporting;
        Assert.False(Directory.Exists(exports.ExportDirectory)); Assert.Equal(1,receiver.Calls);
        if (!switchAccount) { model.EndStorageMaintenance(false); Assert.Equal("Cached preview",model.Body!.PlainText); Assert.True(model.CanDownloadOriginalSource); }
        else { Assert.Null(model.Body); Assert.Empty(All(root).OfType<MessageSourceExportActions>()); }
    }
    [Fact]
    public async Task SourcePreviewFocusReturnsToReaderAfterAccountSwitch()
    {
        using var directory=new TestDirectory();var account=TestDirectory.Profile();var cache=new SqliteMailCache(directory.File("mail.db"));var key=new MailMessageKey(account.Id,"INBOX",7,1);
        await cache.SaveSummariesAsync(new(account.Id,"INBOX",7),[new(){Key=key,Subject="Source fixture",Sender="sender@example.test"}],DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(new(account.Id,"INBOX",7),new(key,"Cached body"));var receiver=new Receiver();receiver.Release.SetResult(new(key,Encoding.ASCII.GetBytes("Subject: source\r\n\r\nBody")));
        using var model=new InboxViewModel(receiver,new ImmediateUiDispatcher(),cache,sourceExports:new FileMessageSourceExports(directory.Root));model.SetAccount(account);await model.CacheLoadTask;await model.SelectAsync(key);
        using var root=new InboxView(model).CreateContent();var dispatcher=new TestQueueDispatcher();using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());session.AddRoot(root);
        var actions=All(root).OfType<MessageSourceExportActions>().Single();await actions.ExportAsync();dispatcher.Drain();
        var inspection=All(root).OfType<MessageSourceInspection>().Single();await inspection.ReadPreviewAsync();dispatcher.Drain();Assert.True(inspection.IsPreviewVisible);
        var oldEditor=All(inspection).OfType<ScrollableMessageText>().Single().Editor;Assert.Same(oldEditor,session.FocusedElement);
        model.SetAccount(TestDirectory.Profile());await model.CacheLoadTask;dispatcher.Drain();Assert.True(oldEditor.IsDisposed);
        Assert.Same(All(root).OfType<ScrollableMessageText>().Single().Editor,session.FocusedElement);Assert.Empty(All(root).OfType<MessageSourceInspection>());
    }
    private sealed class Host:IUiHost
    {
        public BSize ViewportSize=>new(1100,720);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);
        public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }
    private sealed class Receiver : IMailReceiver, IMailSourceReceiver
    {
        public int Calls; public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource<MailMessageSource> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MailMessageSource> GetSourceAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default) { Calls++; Entered.TrySetResult(); return Release.Task; }
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default) => throw new InvalidOperationException("Cached body must be used.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default) => throw new InvalidOperationException("No receive.");
        public Task TestConnectionAsync(AccountProfile account,CancellationToken cancellationToken=default) => throw new InvalidOperationException("No connection test.");
    }
}
