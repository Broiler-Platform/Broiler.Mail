using Broiler.Mail.Application.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
public sealed class ReaderViewportTests
{
    private static string Html => string.Concat(Enumerable.Range(1, 100).Select(i => $"<p>Paragraph {i}: readable HTML text that wraps across the page.</p>"));
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.6, 0.6)]
    [InlineData(2, 1)]
    public void ActualHtmlAdapterRestoresFractionAfterLayout(double requested, double expected)
    {
        using var view = new ScrollableHtmlView(Html, () => null, _ => throw new InvalidOperationException("No links in fixture."));
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(view); view.RestoreReadingPosition(requested); session.RenderFrame();
        Assert.IsAssignableFrom<IReaderViewport>(view); Assert.InRange(view.ReadingPosition, Math.Max(0, expected - 0.002), Math.Min(1, expected + 0.002));
        Assert.InRange(view.Scroll.VerticalOffset / (view.Scroll.ExtentSize.Height - view.Scroll.ViewportSize.Height), Math.Max(0, expected - 0.002), Math.Min(1, expected + 0.002));
    }
    [Fact]
    public void HiddenRestoreSurvivesZoomThemeAndImageReflowWhileNewDocumentResets()
    {
        using var view = new ScrollableHtmlView(Html, () => null, _ => { }); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(view); session.RenderFrame();
        view.Visibility = UiVisibility.Collapsed; session.RenderFrame(); view.RestoreReadingPosition(0.65); view.ZoomBy(1); view.ApplyReaderTheme(StandardThemeTokens.Dark);
        view.UpdateHtml(Html + "<p>Additional image layout placeholder</p>", preserveReadingPosition: true); view.Visibility = UiVisibility.Visible; session.RenderFrame();
        Assert.InRange(view.ReadingPosition, 0.64, 0.66); view.RestoreReadingPosition(0.8); view.UpdateHtml(Html); session.RenderFrame(); Assert.Equal(0, view.ReadingPosition);
        view.RestoreReadingPosition(1); session.RenderFrame(); view.ScrollToStart(); session.RenderFrame(); Assert.Equal(0, view.ReadingPosition);
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void HtmlRejectsNonfinitePosition(double value)
    { using var view = new ScrollableHtmlView(Html, () => null, _ => { }); Assert.Throws<ArgumentOutOfRangeException>(() => view.RestoreReadingPosition(value)); }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
