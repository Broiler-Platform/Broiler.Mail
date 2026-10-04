using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-11: the HTML preview's zoom also enlarges its plain text.</summary>
[Collection("UI theme")]
public sealed class ScrollableMessageTextZoomTests
{
    [Fact]
    public void ZoomEnlargesTheScrollableTextAndKeepsTheReadingPlace()
    {
        var text = new ScrollableMessageText
        {
            Text = string.Join("\n\n", Enumerable.Range(1, 80).Select(index => $"Paragraph {index}: a line of plain text that wraps in a narrow window.")),
        };
        using var session = new StandardUiSessionBuilder().WithDispatcher(new ImmediateUiDispatcher()).Build(new Host());
        session.AddRoot(text);
        session.RenderFrame();
        var scroll = Assert.IsType<StandardScrollView>(text.Children[0]);
        double Range() => scroll.ExtentSize.Height - scroll.ViewportSize.Height;
        double extent = scroll.ExtentSize.Height;
        scroll.SetOffset(new BPoint(0, Math.Round(Range() / 2)));
        session.RenderFrame();

        text.Zoom = 2;
        session.RenderFrame();
        Assert.Equal(2, text.Editor.Zoom);
        // The text is taller (and wraps more), so the scroll view reaches its end, and the reader is still halfway.
        Assert.True(scroll.ExtentSize.Height > extent * 1.9, $"Extent {scroll.ExtentSize.Height}, before {extent}.");
        Assert.InRange(scroll.VerticalOffset / Range(), 0.45, 0.55);
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(600, 400);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
