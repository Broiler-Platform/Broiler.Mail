using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class AccountReceiveQueueViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ExplicitControlsKeepSelectionLocalExposeCancelAndRetainReadOnlyDetails(double scale)
    {
        var dispatcher = new TestQueueDispatcher(); using var f = await AccountReceiveQueueTests.Fixture.Create(2, dispatcher); var release = AccountReceiveQueueTests.NewSource();
        f.Receiver.Get = async (profile, token) => { await release.Task.WaitAsync(token); return AccountReceiveQueueTests.Page(profile); };
        using var activity = new AccountReceiveQueueView(f.Queue, () => f.Profiles[0].Id).CreateContent(); using var frame = new BoundedScrollArea(activity, 0.4, "Account receive activity");
        using var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock }; root.AddChild(frame); root.SetDock(frame, UiDock.Top);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root); var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); dispatcher.Drain(); session.RenderFrame();
        Assert.Empty(f.Receiver.Started); Button(activity, "Show account receives").Click(); session.RenderFrame(); Assert.False(Button(activity, "Queue enabled accounts").IsDefault);
        Button(activity, "Queue enabled accounts").Click(); dispatcher.Drain(); session.RenderFrame(); Assert.Equal("Cancel selected account receive", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        var details = All(activity).OfType<StandardRichEdit>().Single(); Assert.True(details.IsReadOnly); Assert.Equal("Account receive job details", details.AccessibleName);
        var picker = All(activity).OfType<StandardComboBox>().Single(); picker.SelectedIndex = 1; Assert.Equal(f.Profiles[0].Id, (await f.Store.LoadStateAsync()).SelectedAccountId);
        Button(activity, "Cancel selected account receive").Click(); release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); dispatcher.Drain(); session.RenderFrame();
        Assert.Equal(AccountReceiveState.Completed, f.Queue.Jobs[0].State); Assert.Equal(AccountReceiveState.Canceled, f.Queue.Jobs[1].State); Assert.True(session.FocusedElement?.CanFocus); Assert.InRange(frame.Bounds.Height, 1, 192);
    }
    private static StandardButton Button(UiElement root, string text) => All(root).OfType<StandardButton>().Single(button => button.Text == text);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class Host : IUiHost { public BSize ViewportSize => new(640, 480); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { } }
}
