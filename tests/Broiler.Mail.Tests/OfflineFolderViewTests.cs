using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OfflineFolderViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ExplicitOfflineControlsExposeCoverageAndMoveBatchFocusToVisibleCancel(double scale)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(new(account.Id, "INBOX", 7), [new() { Key = new(account.Id, "INBOX", 7, 1), Subject = "Fixture", Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
        var store = new JsonOfflineFolderStore(directory.File("offline-folders.json")); await store.SaveAsync(account.Id, ["INBOX"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestMailReceiver { Body = (_, token) => { entered.SetResult(); return release.Task.WaitAsync(token); } };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, offlineFolders: store);
        model.SetAccount(account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        var group = All(root).OfType<FormSection>().Single(item => item.Content.AccessibleName == "Offline-folder batch controls"); Assert.False(group.IsExpanded); group.Toggle!.Click(); session.RenderFrame();
        Assert.Same(group.Content, group.Toggle.Controls); Assert.Contains("500", model.OfflineFolderNotice); Assert.Contains("INBOX", model.OfflineFolderNotice);
        var download = All(root).OfType<StandardButton>().Single(item => item.Text == "Download selected offline folders");
        var cancel = All(root).OfType<StandardButton>().Single(item => item.Text == "Cancel");
        Assert.True(download.IsEnabled); session.SetFocus(download); var downloading = model.DownloadOfflineFoldersAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); session.RenderFrame();
        Assert.Same(cancel, session.FocusedElement); Assert.True(cancel.IsEnabled); Assert.InRange(cancel.Bounds.Bottom, 1, 480);
        cancel.Click(); await downloading; Assert.Null(await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1)); Assert.True(model.ProblemIsSelectedFolderDownload);
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ContinuationAndRestartControlsExposeDurableStateWithoutDeletingBodies(double scale)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db")); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var rows = Enumerable.Range(1, 501).Select(i => new MailMessageSummary { Key = new(account.Id, "INBOX", 7, (uint)i), Subject = "Paging fixture", Sender = "sender@example.test" }).ToArray();
        await cache.SaveSummariesAsync(mailbox, rows.Take(500).ToArray(), DateTimeOffset.UtcNow); await cache.SaveSummariesAsync(mailbox, rows.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        var store = new JsonOfflineFolderStore(directory.File("offline-folders.json")); await store.SaveAsync(account.Id, ["INBOX"]);
        var receiver = new TestMailReceiver { Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Saved fixture")) };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, offlineFolders: store); model.SetAccount(account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        var group = All(root).OfType<FormSection>().Single(item => item.Content.AccessibleName == "Offline-folder batch controls"); group.Toggle!.Click(); session.RenderFrame();
        var next = All(root).OfType<StandardButton>().Single(item => item.Text == "Download next offline batch"); Assert.True(next.IsEnabled);
        var restart = All(root).OfType<StandardButton>().Single(item => item.Text == "Restart offline batches from newest"); Assert.True(restart.IsEnabled); Assert.False(restart.IsDefault);
        var reset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.Changed += (_, _) => { if (!model.IsSavingOfflineFolders && !model.HasCompletedOfflineFolderBatch) reset.TrySetResult(); };
        restart.Click(); await reset.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(model.HasCompletedOfflineFolderBatch); Assert.Equal("Download selected offline folders", next.Text); Assert.NotNull(await cache.ReadBodyAsync(mailbox, 2)); Assert.Null(await cache.ReadBodyAsync(mailbox, 1));
    }

    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var row in All(child)) yield return row; }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
