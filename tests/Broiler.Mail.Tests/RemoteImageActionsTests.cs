using Broiler.Mail.Application.Preview;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class RemoteImageActionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingRequiresClickAndCompletionLeavesUsableFocus(bool partial)
    {
        var images = new Images(); int fallback = 0;
        using var actions = new RemoteImageActions(images, () => fallback++);
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(actions); session.RenderFrame();
        Assert.Equal(0, images.Loads);
        var load = Find(actions, "Load remote images"); session.SetFocus(load); load.Click();
        Assert.Equal(1, images.Loads); Assert.Equal("Cancel image loading", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        images.Complete(partial ? 1 : 2); session.RenderFrame();
        Assert.Equal(partial, load.IsEnabled);
        if (!partial) Assert.Equal(1, fallback);
        else { Assert.Same(load, session.FocusedElement); load.Click(); Assert.Equal(2, images.Loads); }
    }

    [Fact]
    public void PlainTextCancelsAndDisposalStopsListening()
    {
        var images = new Images(); var actions = new RemoteImageActions(images, () => { });
        Find(actions, "Load remote images").Click();
        actions.SetHtmlVisible(false); Assert.Equal(1, images.Cancellations);
        Assert.False(Find(actions, "Retry unavailable images").IsEnabled);
        actions.SetHtmlVisible(true); Assert.True(Find(actions, "Retry unavailable images").IsEnabled);
        actions.Dispose(); Assert.Equal(0, images.Subscribers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitBlockResetsLoadedOrPendingConsentAndRestoresLoadFocus(bool pending)
    {
        var images = new ResetImages();
        using var actions = new RemoteImageActions(images, () => { });
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(actions); session.RenderFrame();
        var block = Find(actions, "Block remote images again"); Assert.False(block.IsEnabled);
        Find(actions, "Load remote images").Click();
        if (!pending) images.Complete(2);
        Assert.True(block.IsEnabled); session.SetFocus(block); block.Click();
        Assert.Equal(1, images.Resets); Assert.Equal(1, images.Loads);
        Assert.Equal(0, images.RemoteImages.Loaded); Assert.False(images.RemoteImages.IsLoading);
        Assert.False(block.IsEnabled); Assert.Same(Find(actions, "Load remote images"), session.FocusedElement);
        Find(actions, "Load remote images").Click(); Assert.Equal(2, images.Loads);
    }

    [Fact]
    public void BlockCapabilityIsOptionalAndUnavailableOutsideHtml()
    {
        using var legacy = new RemoteImageActions(new Images(), () => { });
        Assert.Equal(UiVisibility.Collapsed, Find(legacy, "Block remote images again").Visibility);
        var images = new ResetImages(); using var actions = new RemoteImageActions(images, () => { });
        images.Complete(2); actions.SetHtmlVisible(false);
        Assert.False(Find(actions, "Block remote images again").IsEnabled); Assert.Equal(0, images.Resets);
        actions.SetHtmlVisible(true); Assert.True(Find(actions, "Block remote images again").IsEnabled);
    }

    private sealed class ResetImages : Images, IInlineRemoteImageReset
    {
        public int Resets;
        public void BlockRemoteImages() { Resets++; CancelRemoteImages(); Complete(0); }
    }

    private static StandardButton Find(UiElement root, string text) => Descendants(root).OfType<StandardButton>().Single(b => b.Text == text);
    private static IEnumerable<UiElement> Descendants(UiElement root)
    { foreach (var child in root.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private class Images : IInlineRemoteImages
    {
        private EventHandler? _changed;
        public int Loads, Cancellations, Subscribers;
        public event EventHandler? RemoteImagesChanged { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public RemoteImageState RemoteImages { get; private set; } = new(2, 0, false, true, "Blocked");
        public Task LoadRemoteImagesAsync() { Loads++; RemoteImages = RemoteImages with { IsLoading = true, CanLoad = false }; _changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public void CancelRemoteImages() { if (!RemoteImages.IsLoading) return; Cancellations++; RemoteImages = RemoteImages with { IsLoading = false, CanLoad = true }; _changed?.Invoke(this, EventArgs.Empty); }
        public void Complete(int loaded) { RemoteImages = new(2, loaded, false, loaded < 2, "Complete"); _changed?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class Host : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(640, 480); public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(Broiler.Graphics.RenderList.BRenderList list) { }
    }
}
