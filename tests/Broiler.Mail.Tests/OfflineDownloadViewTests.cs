using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OfflineDownloadViewTests
{
    [Fact]
    public async Task PersistentInboxToolbarWrapsAndHandsDownloadFocusToVisibleCancel()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestMailReceiver { Body = (_, token) => { entered.SetResult(); return held.Task.WaitAsync(token); } };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask;
        using var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(content); session.RenderFrame();
        var toolbar = content.Children.OfType<StandardToolbar>().First();
        var download = toolbar.Children.OfType<StandardButton>().Single(button => button.Text == "Download loaded messages");
        var cancel = toolbar.Children.OfType<StandardButton>().Single(button => button.Text == "Cancel");
        Assert.True(download.IsEnabled);
        foreach (var child in toolbar.Children)
        {
            Assert.InRange(child.Bounds.Left, toolbar.Bounds.Left - 1, toolbar.Bounds.Right + 1);
            Assert.InRange(child.Bounds.Right, toolbar.Bounds.Left - 1, toolbar.Bounds.Right + 1);
            Assert.InRange(child.Bounds.Bottom, toolbar.Bounds.Top - 1, toolbar.Bounds.Bottom + 1);
        }
        session.SetFocus(download); var downloading = model.DownloadLoadedBodiesAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RenderFrame(); Assert.Same(cancel, session.FocusedElement); Assert.True(cancel.IsEnabled); Assert.False(download.IsEnabled);
        Assert.InRange(cancel.Bounds.Bottom, 1, 480);
        cancel.Click(); await downloading; session.RenderFrame();
        Assert.False(model.IsBusy); Assert.Equal("Receive mail", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        Assert.Null(await cache.ReadBodyAsync(mailbox, 1));
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
