using Broiler.Mail.Application.Preview;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
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
        var load = Find(actions, "Load images"); session.SetFocus(load); load.Click();
        Assert.Equal(1, images.Loads); Assert.Same(Find(actions, "Cancel loading"), session.FocusedElement);
        Assert.Equal(UiVisibility.Collapsed, load.Visibility);
        images.Complete(partial ? 1 : 2); session.RenderFrame();
        Assert.Equal(partial, load.IsEnabled);
        if (!partial) { Assert.Equal(1, fallback); Assert.Equal(UiVisibility.Collapsed, load.Visibility); }
        else
        {
            // A partial failure offers Retry failed on the same button, which keeps focus.
            Assert.Equal("Retry failed", load.Text); Assert.Equal(UiVisibility.Visible, load.Visibility);
            Assert.Same(load, session.FocusedElement); load.Click(); Assert.Equal(2, images.Loads);
        }
    }

    [Fact]
    public void PlainTextCancelsAndDisposalStopsListening()
    {
        var images = new Images(); var actions = new RemoteImageActions(images, () => { });
        Assert.Equal(UiVisibility.Visible, actions.Visibility);
        Find(actions, "Load images").Click();
        actions.SetHtmlVisible(false); Assert.Equal(1, images.Cancellations);
        Assert.Equal(UiVisibility.Collapsed, actions.Visibility);
        Assert.False(Find(actions, "Retry").IsEnabled);
        actions.SetHtmlVisible(true); Assert.True(Find(actions, "Retry").IsEnabled);
        Assert.Equal(UiVisibility.Visible, actions.Visibility);
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
        var block = Find(actions, "Block again"); Assert.False(block.IsEnabled); Assert.False(actions.CanBlockAgain);
        Assert.Equal(UiVisibility.Collapsed, block.Visibility);
        Find(actions, "Load images").Click();
        if (!pending)
        {
            images.Complete(2);
            // Once loaded, Block again is the banner's right-edge button.
            Assert.True(block.IsEnabled); Assert.Equal(UiVisibility.Visible, block.Visibility);
            session.SetFocus(block); block.Click();
        }
        else
        {
            // While loading, Cancel is inline and Block again is offered by More message actions.
            Assert.Equal(UiVisibility.Collapsed, block.Visibility); Assert.True(actions.CanBlockAgain);
            session.SetFocus(Find(actions, "Cancel loading")); actions.BlockAgain();
        }
        Assert.Equal(1, images.Resets); Assert.Equal(1, images.Loads);
        Assert.Equal(0, images.RemoteImages.Loaded); Assert.False(images.RemoteImages.IsLoading);
        Assert.False(block.IsEnabled); Assert.False(actions.CanBlockAgain);
        Assert.Same(Find(actions, "Load images"), session.FocusedElement);
        Find(actions, "Load images").Click(); Assert.Equal(2, images.Loads);
    }

    [Fact]
    public void BlockCapabilityIsOptionalAndUnavailableOutsideHtml()
    {
        var plain = new Images(); using var legacy = new RemoteImageActions(plain, () => { });
        plain.Complete(2);
        Assert.Equal(UiVisibility.Collapsed, Find(legacy, "Block again").Visibility); Assert.False(legacy.CanBlockAgain);
        var images = new ResetImages(); using var actions = new RemoteImageActions(images, () => { });
        images.Complete(2); actions.SetHtmlVisible(false);
        Assert.False(Find(actions, "Block again").IsEnabled); Assert.False(actions.CanBlockAgain); Assert.Equal(0, images.Resets);
        Assert.Equal(UiVisibility.Collapsed, actions.Visibility);
        actions.BlockAgain(); Assert.Equal(0, images.Resets);
        actions.SetHtmlVisible(true); Assert.True(Find(actions, "Block again").IsEnabled); Assert.True(actions.CanBlockAgain);
        Assert.Equal(UiVisibility.Visible, Find(actions, "Block again").Visibility);
    }

    [Fact]
    public void SummaryIsOneSentenceWithCountAndLearnMoreDisclosesDetails()
    {
        var images = new Images();
        using var actions = new RemoteImageActions(images, () => { });
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(actions); session.RenderFrame();
        var summary = Labels(actions).Single(label => label.Text.StartsWith("Remote images (", StringComparison.Ordinal));
        Assert.Equal("Remote images (2) are blocked to protect your privacy.", summary.Text);
        Assert.Equal(RemoteImageActions.PrivacyDetails, summary.ToolTipText);
        var details = Labels(actions).Single(label => label.Text == RemoteImageActions.PrivacyDetails);
        Assert.False(Shown(details));
        var learnMore = Find(actions, "Learn more"); session.SetFocus(learnMore); learnMore.Click(); session.RenderFrame();
        Assert.True(Shown(details));
        // The banner is one row: the sentence and its buttons, until Learn more opens the details below it.
        var load = Find(actions, "Load images");
        Assert.InRange(load.Bounds.Top, summary.Bounds.Top - 8, summary.Bounds.Bottom);
        Assert.True(details.Bounds.Top >= load.Bounds.Bottom - 1);
        learnMore.Click(); Assert.False(Shown(details));
    }

    [Fact]
    public void OverLimitShowsStatusWithoutLoad()
    {
        var images = new Images(new RemoteImageState(17, 0, false, false, "This message exceeds the 16 remote-image limit."));
        using var actions = new RemoteImageActions(images, () => { });
        Assert.Equal(UiVisibility.Visible, actions.Visibility);
        Assert.Contains(Labels(actions), label => label.Text == "This message exceeds the 16 remote-image limit.");
        var load = Find(actions, "Load images");
        Assert.Equal(UiVisibility.Collapsed, load.Visibility); Assert.False(load.IsEnabled); Assert.Equal(0, images.Loads);
    }

    [Fact]
    public void StatusAfterBlockAgainKeepsCannotBeUndoneDisclosure()
    {
        var images = new ResetImages { BlockedStatus = "Remote images are blocked again. Previously sent requests cannot be undone." };
        using var actions = new RemoteImageActions(images, () => { });
        Find(actions, "Load images").Click(); images.Complete(2);
        actions.BlockAgain();
        Assert.Contains(Labels(actions), label => label.Text == images.BlockedStatus);
        Assert.Equal("Load images", Find(actions, "Load images").Text);
    }

    [Fact]
    public void BannerCollapsingWithFocusInsideCallsFallback()
    {
        var images = new Images(); int fallback = 0;
        using var actions = new RemoteImageActions(images, () => fallback++);
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(actions); session.RenderFrame();
        Find(actions, "Load images").Click();
        session.SetFocus(Find(actions, "Cancel loading"));
        actions.SetHtmlVisible(false);
        Assert.Equal(UiVisibility.Collapsed, actions.Visibility); Assert.Equal(1, fallback);
    }

    private class ResetImages : Images, IInlineRemoteImageReset
    {
        public int Resets;
        public string BlockedStatus = "Complete";
        public void BlockRemoteImages() { Resets++; CancelRemoteImages(); Complete(0, BlockedStatus); }
    }

    private static StandardButton Find(UiElement root, string text) => Descendants(root).OfType<StandardButton>().Single(b => b.Text == text);
    private static IEnumerable<StandardLabel> Labels(UiElement root) => Descendants(root).OfType<StandardLabel>();
    private static bool Shown(UiElement element) { for (UiElement? at = element; at is not null; at = at.Parent) if (at.Visibility != UiVisibility.Visible) return false; return true; }
    private static IEnumerable<UiElement> Descendants(UiElement root)
    { foreach (var child in root.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private class Images : IInlineRemoteImages
    {
        private EventHandler? _changed;
        public int Loads, Cancellations, Subscribers;
        public Images() : this(new(2, 0, false, true, "Blocked")) { }
        public Images(RemoteImageState initial) => RemoteImages = initial;
        public event EventHandler? RemoteImagesChanged { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public RemoteImageState RemoteImages { get; private set; }
        public Task LoadRemoteImagesAsync() { Loads++; RemoteImages = RemoteImages with { IsLoading = true, CanLoad = false }; _changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public void CancelRemoteImages() { if (!RemoteImages.IsLoading) return; Cancellations++; RemoteImages = RemoteImages with { IsLoading = false, CanLoad = true }; _changed?.Invoke(this, EventArgs.Empty); }
        public void Complete(int loaded, string status = "Complete") { RemoteImages = new(2, loaded, false, loaded < 2, status); _changed?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class Host : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(640, 480); public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(Broiler.Graphics.RenderList.BRenderList list) { }
    }
}
