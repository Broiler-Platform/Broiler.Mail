using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class MailboxSnapshotViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task FullSnapshotControlsAreExplicitCurrentFolderActionsAndDisabledAccountCanInspect(double scale)
    {
        using var dir = new TestDirectory(); var cache = new SqliteMailCache(dir.File("mail.db")); var receiver = new MailboxSnapshotWorkflowTests.Receiver(); var account = TestDirectory.Profile();
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(account); await model.CacheLoadTask;
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root); var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        Assert.Equal(0, receiver.Calls); Button(root, "Show offline folders").Click(); session.RenderFrame();
        var capture = Button(root, "Snapshot all headers in current folder"); var inspect = Button(root, "Inspect saved header snapshot"); Assert.True(capture.IsEnabled); Assert.True(inspect.IsEnabled); Assert.False(capture.IsDefault);
        await model.CaptureMailboxSnapshotAsync(); session.RenderFrame(); Assert.Equal(1, receiver.Calls); Assert.Contains(All(root).OfType<StandardLabel>(), label => label.Text.Contains("Last complete header observation"));
        model.SetAccount(account with { IsEnabled = false }); await model.CacheLoadTask; session.RenderFrame(); Assert.False(capture.IsEnabled); Assert.True(inspect.IsEnabled); await model.InspectMailboxSnapshotAsync(); session.RenderFrame(); Assert.Equal(1, receiver.Calls); Assert.Contains("1 server headers", model.MailboxSnapshotNotice);
        var frame = All(root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Offline folder controls"); Assert.InRange(frame.Bounds.Height, 1, 168);
    }
    private static StandardButton Button(UiElement root, string text) => All(root).OfType<StandardButton>().Single(button => button.Text == text);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }
    private sealed class Host : IUiHost { public BSize ViewportSize => new(640, 480); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { } }
}
