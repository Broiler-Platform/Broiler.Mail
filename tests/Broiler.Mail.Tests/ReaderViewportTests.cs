using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class ReaderViewportTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.45, 0.45)]
    [InlineData(2, 1)]
    public void TextViewportRestoresClampedFractionWithoutChangingSelection(double requested, double expected)
    {
        using var text = Text(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(text); session.RenderFrame();
        text.Editor.SetEditorSelection(2, 15); var selection = text.Editor.Selection; text.RestoreReadingPosition(requested); session.RenderFrame();
        Assert.InRange(text.ReadingPosition, Math.Max(0, expected - 0.002), Math.Min(1, expected + 0.002)); Assert.Equal(selection, text.Editor.Selection);
        var scroll = Assert.IsType<StandardScrollView>(text.Children[0]); Assert.InRange(scroll.VerticalOffset / (scroll.ExtentSize.Height - scroll.ViewportSize.Height), Math.Max(0, expected - 0.002), Math.Min(1, expected + 0.002));
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TextViewportRejectsNonfinitePosition(double value)
    { using var text = Text(); Assert.Throws<ArgumentOutOfRangeException>(() => text.RestoreReadingPosition(value)); }
    [Fact]
    public void HiddenRestoreSurvivesZoomAndResetStartsNewTextAtTop()
    {
        using var text = Text(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(text); session.RenderFrame();
        text.Visibility = UiVisibility.Collapsed; session.RenderFrame(); text.RestoreReadingPosition(0.6); text.Zoom = 1.5;
        text.Visibility = UiVisibility.Visible; session.RenderFrame(); Assert.InRange(text.ReadingPosition, 0.595, 0.605);
        text.RestoreReadingPosition(0.8); text.ScrollToStart(); session.RenderFrame(); Assert.Equal(0, text.ReadingPosition);
    }
    [Fact]
    public void ShortDocumentHasZeroScrollPosition()
    {
        using var text = new ScrollableMessageText { Text = "Short" }; using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(text);
        text.RestoreReadingPosition(1); session.RenderFrame(); Assert.Equal(0, text.ReadingPosition);
    }
    private static ScrollableMessageText Text() => new() { Text = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Paragraph {i}: some readable plain text that wraps.")) };
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(600, 400); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
