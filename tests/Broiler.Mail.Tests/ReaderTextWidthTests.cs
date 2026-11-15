using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// UI-12: the reader's text keeps one width whether its scroll bar is shown or not, so a message is
/// wrapped once per width rather than again beside the bar on every selection and resize frame.
/// </summary>
[Collection("UI theme")]
public sealed class ReaderTextWidthTests
{
    private static readonly string Long = string.Join("\n\n", Enumerable.Range(1, 80).Select(index => $"Paragraph {index}: a line of plain text that wraps in the reader."));

    [Theory]
    [InlineData(600)]
    [InlineData(900)]
    [InlineData(1400)]
    public void TheTextKeepsItsWidthAndMarginWhenTheScrollBarAppears(int width)
    {
        var (shortText, shortScroll) = Layout("A short message.", width);
        var (longText, longScroll) = Layout(Long, width);
        Assert.Equal(shortScroll.ViewportSize.Width, width);
        Assert.True(longScroll.ViewportSize.Width < width, "The long message scrolls, beside its bar.");

        Assert.Equal(shortText.Editor.Bounds.Width, longText.Editor.Bounds.Width);
        Assert.Equal(shortText.Editor.Bounds.X, longText.Editor.Bounds.X);
        Assert.Equal(ReadingColumn.MarginFor(width), longText.Editor.Bounds.X);
        // The bar sits in the right margin and leaves the text a gap.
        Assert.True(longText.Editor.Bounds.Right <= longScroll.ViewportSize.Width - ReadingColumn.ScrollBarGap,
            $"Text ends at {longText.Editor.Bounds.Right}, the bar starts at {longScroll.ViewportSize.Width}.");
    }

    [Fact]
    public void WhereTheMarginCannotHoldTheBarTheTextNarrowsBesideIt()
    {
        // Compact margins are as wide as the bar, so the text gives way instead of touching it.
        const int width = 400;
        var (text, scroll) = Layout(Long, width);
        Assert.True(scroll.ViewportSize.Width < width);
        Assert.Equal(ReadingColumn.MarginFor(scroll.ViewportSize.Width), text.Editor.Bounds.X);
        Assert.Equal(scroll.ViewportSize.Width - (2 * ReadingColumn.MarginFor(scroll.ViewportSize.Width)), text.Editor.Bounds.Width);
    }

    private static (ScrollableMessageText Text, StandardScrollView Scroll) Layout(string value, int width)
    {
        var text = new ScrollableMessageText { Text = value };
        var session = new StandardUiSessionBuilder().WithDispatcher(new ImmediateUiDispatcher()).Build(new Host(width));
        session.AddRoot(text);
        session.RenderFrame();
        return (text, Assert.IsType<StandardScrollView>(text.Children[0]));
    }

    private sealed class Host(int width) : IUiHost
    {
        public BSize ViewportSize => new(width, 400);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
