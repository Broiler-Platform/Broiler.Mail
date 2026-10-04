using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>UI-11 and UI-07: the preview's zoom, which opens at the system text size.</summary>
public sealed class HtmlPreviewZoomTests
{
    private static readonly string TallDocument = string.Concat(Enumerable.Range(1, 60).Select(index => $"<p>Paragraph {index}: a longer line of text that wraps differently once the window becomes narrower than before, so a resize changes how tall the whole document is.</p>"))
        + "<p>Last: <a href='https://example.test/end'>the closing link</a></p>";

    // Long paragraphs: laid out six times narrower (300 % against 50 %), the document passes the render budget.
    private static readonly string BudgetDocument = string.Concat(Enumerable.Range(1, 70).Select(index =>
        $"<p>Paragraph {index}: {string.Join(" ", Enumerable.Repeat("readable", 100))}</p>"));

    [Fact]
    public void ZoomLaysTheDocumentOutAtTheViewportWidthOverTheZoomAndScalesItsHeight()
    {
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        using var fixture = new Fixture(view);
        float width = view.Snapshot!.Width;

        view.SetZoom(2);
        fixture.Session.RenderFrame();
        var snapshot = view.Snapshot!;
        Assert.Equal(width / 2, snapshot.Width, 1);
        Assert.Equal(snapshot.ContentHeight * 2, view.Content.DesiredSize.Height, 1);
        Assert.Equal(snapshot.ContentHeight * 2, view.Scroll.ExtentSize.Height, 1);
        // Ordinary text reflows to the narrower layout; nothing scrolls sideways.
        Assert.False(view.Scroll.HasHorizontalScrollbar);
    }

    [Fact]
    public void LinkTargetsAndClicksFollowTheZoom()
    {
        string html = "<p>Read <a href='https://example.test/a'>the agenda</a> today.</p><p>Then see <a href='https://example.test/b'>the minutes</a>.</p>";
        var opened = new List<string>();
        var view = new HtmlViewElement(html, () => null, opened.Add) { Zoom = 2 };
        using var fixture = new Fixture(view);

        var link = view.Snapshot!.Links.First(item => item.Href == "https://example.test/a").Bounds;
        var target = view.LinkTargets[0];
        var zoomed = new BRect(view.Bounds.X + (link.X * 2), view.Bounds.Y + (link.Y * 2), link.Width * 2, link.Height * 2);
        Assert.Equal(zoomed, target.Bounds);
        Assert.Equal(zoomed, target.GetSemanticNode().Bounds);

        // A click where the zoomed link is drawn opens it; one where it would be unzoomed does not.
        Assert.True(view.SendInput(Click(zoomed.Right - 2, zoomed.Bottom - 2)));
        Assert.False(view.SendInput(Click(view.Bounds.X + link.X + (link.Width / 2), view.Bounds.Y + link.Y + (link.Height / 2))));
        Assert.Equal(["https://example.test/a"], opened);

        // Focus stays on the same link through another zoom, and Enter opens it.
        fixture.Session.SetFocus(view.LinkTargets[1]);
        view.Zoom = 1.5;
        fixture.Session.RenderFrame();
        Assert.Same(view.LinkTargets[1], fixture.Session.FocusedElement);
        Assert.Equal(view.Snapshot!.Links[1].Bounds.Width * 1.5, view.LinkTargets[1].Bounds.Width, 3);
        Assert.True(fixture.Press(0x0D));
        Assert.Equal(["https://example.test/a", "https://example.test/b"], opened);
    }

    [Fact]
    public void ZoomKeepsTheTextAtTheTopOfTheViewport()
    {
        var status = new StandardLabel { Text = "Simplified HTML.", Wrapping = UiTextWrapping.Wrap };
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        root.AddChild(status);
        root.SetDock(status, UiDock.Top);
        root.AddChild(view);
        using var fixture = new Fixture(root);
        var scroll = view.Scroll;
        BRect Paragraph(int number) => view.Snapshot!.Diagnostics.Where(box => box.TagName == "p").ElementAt(number - 1).BorderBox;
        scroll.SetOffset(new BPoint(0, Paragraph(30).Y));
        fixture.Session.RenderFrame();

        foreach (double zoom in new[] { 2.0, 0.75, 1.5, 3.0 })
        {
            view.SetZoom(zoom);
            fixture.Session.RenderFrame();
            // Paragraph 30 is still at the top, within a line, and the content is already arranged there.
            double top = Paragraph(30).Y * zoom;
            Assert.InRange(scroll.VerticalOffset, top - (24 * zoom), top + (24 * zoom));
            Assert.Equal(scroll.ContentBounds.Top - scroll.VerticalOffset, view.Content.Bounds.Y, 3);
        }

        // A longer status line still leaves the zoomed reader where it was.
        double offset = scroll.VerticalOffset;
        status.Text = string.Join(" ", Enumerable.Repeat("Loaded 3 of 5 remote images. Some images failed or exceeded limits.", 4));
        fixture.Session.RenderFrame();
        Assert.Equal(offset, scroll.VerticalOffset, 3);

        // Another document starts at its top, at the same zoom.
        view.UpdateHtml("<p>Replacement document</p>" + TallDocument);
        fixture.Session.RenderFrame();
        Assert.Equal(0, scroll.VerticalOffset);
        Assert.Equal(3.0, view.Zoom);
        Assert.Equal(view.Content.ContentWidth / 3, view.Snapshot!.Width, 1);
    }

    [Fact]
    public void AScrollBeforeTheZoomedLayoutMovesTheKeptPlace()
    {
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        using var fixture = new Fixture(view);
        var scroll = view.Scroll;
        BRect Paragraph(int number) => view.Snapshot!.Diagnostics.Where(box => box.TagName == "p").ElementAt(number - 1).BorderBox;
        scroll.SetOffset(new BPoint(0, Paragraph(30).Y));
        fixture.Session.RenderFrame();

        // Zoomed, then scrolled 200 DIPs (at the old zoom) before the next frame lays the document out:
        // the kept place is 200 CSS pixels below paragraph 30.
        view.SetZoom(2);
        scroll.ScrollBy(0, 200);
        fixture.Session.RenderFrame();
        double expected = (Paragraph(30).Y + 200) * 2;
        Assert.InRange(scroll.VerticalOffset, expected - 48, expected + 48);
    }

    [Fact]
    public void AZoomWhileThePlainTextIsShownKeepsTheHtmlPlaceForLater()
    {
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        using var fixture = new Fixture(view);
        var scroll = view.Scroll;
        BRect Paragraph(int number) => view.Snapshot!.Diagnostics.Where(box => box.TagName == "p").ElementAt(number - 1).BorderBox;
        scroll.SetOffset(new BPoint(0, Paragraph(30).Y));
        fixture.Session.RenderFrame();

        view.Visibility = UiVisibility.Collapsed;
        fixture.Session.RenderFrame();
        view.SetZoom(2);
        fixture.Session.RenderFrame();
        view.Visibility = UiVisibility.Visible;
        fixture.Session.RenderFrame();
        double top = Paragraph(30).Y * 2;
        Assert.InRange(scroll.VerticalOffset, top - 48, top + 48);
    }

    [Fact]
    public void AReflowThatShortensTheDocumentKeepsTheReadingPlaceInsteadOfJumpingToTheEnd()
    {
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        using var fixture = new Fixture(view, width: 300);
        var scroll = view.Scroll;
        double Range() => scroll.ExtentSize.Height - scroll.ViewportSize.Height;
        scroll.SetOffset(new BPoint(0, Math.Round(Range() * 0.8)));
        fixture.Session.RenderFrame();

        // Three times as wide, the document is far shorter, and the old offset is past its end. The
        // scroll view clamps it while measuring; that is not where the reader was.
        fixture.Resize(900);
        Assert.InRange(scroll.VerticalOffset / Range(), 0.75, 0.85);
    }

    [Fact]
    public void ContentTooWideForTheZoomedPageScrollsSideways()
    {
        string html = "<p>Programme: https://example.test/" + new string('x', 24) + "</p>" + TallDocument;
        var view = new ScrollableHtmlView(html, () => null, _ => { });
        using var fixture = new Fixture(view);
        Assert.False(view.Scroll.HasHorizontalScrollbar);

        view.SetZoom(2.5);
        fixture.Session.RenderFrame();
        fixture.Session.RenderFrame();
        var scroll = view.Scroll;
        Assert.True(scroll.HasHorizontalScrollbar);
        Assert.True(scroll.ExtentSize.Width > scroll.ViewportSize.Width + 50, $"Extent {scroll.ExtentSize}, viewport {scroll.ViewportSize}.");
        Assert.Equal(view.Snapshot!.ExtentWidth * 2.5, view.Content.DesiredSize.Width, 1);

        fixture.Session.SetFocus(view.Content);
        Assert.True(fixture.Press(0x27)); // Right
        Assert.True(scroll.HorizontalOffset > 0);
        // Shift+wheel scrolls sideways too.
        double sideways = scroll.HorizontalOffset;
        Assert.True(fixture.Session.DispatchInput(Wheel(300, 200, -1, InputModifiers.Shift)));
        fixture.Session.RenderFrame();
        Assert.True(scroll.HorizontalOffset > sideways, $"Offset {scroll.HorizontalOffset}, before {sideways}.");
        // The page stays laid out at the viewport's width, not at its own wider extent.
        Assert.Equal(view.Content.ContentWidth / 2.5, view.Snapshot!.Width, 1);
    }

    [Fact]
    public void ZoomingInCanPassTheRenderBudgetAndTheBannerMovesWithTheZoom()
    {
        var renderer = new PixelRenderer();
        var view = new HtmlViewElement(BudgetDocument, () => renderer, _ => { });
        view.Measure(new BSize(900, 600));
        Assert.False(view.Snapshot!.IsTruncated);

        view.Zoom = 3;
        BSize size = view.Measure(new BSize(900, 600));
        Assert.True(view.Snapshot!.IsTruncated);
        double end = HtmlViewElement.MaxBudgetHeight * 3;
        Assert.Equal(end, size.Height, 1);

        // The banner sits at the cut, at the end of the zoomed page.
        view.Arrange(new BRect(0, -(end - 600), 900, end));
        var list = new BRenderList();
        view.Render(new UiRenderContext(list, new StandardUiSessionBuilder().Build(new Host { Width = 900, Height = 600 }), new Host { Width = 900, Height = 600 }));
        var banner = Assert.Single(list.Commands.OfType<BRenderCommand.DrawText>(), text => text.Text.Text.Contains("exceeds maximum render limit"));
        Assert.InRange(banner.Origin.Y, 600 - 48, 600);
    }

    [Theory]
    [InlineData(1.0, 1.5)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.0, 0.75)]
    [InlineData(1.0, 3.0)]
    public void ZoomedTilesShowTheirOwnPartOfTheDocument(double dpiScale, double zoom)
    {
        string html = string.Concat(Enumerable.Range(1, 120).Select(index =>
            $"<p>Paragraph {index}: {string.Join(" ", Enumerable.Repeat("text", (index * 7 % 23) + 3))}"
            + (index % 7 == 0 ? $" <a href='https://example.test/{index}'>link {index}</a>" : "") + "</p>"));
        var renderer = new PixelRenderer();
        var view = new HtmlViewElement(html, () => renderer, _ => { }, () => dpiScale) { Zoom = zoom };
        const double width = 600, tileTop = HtmlViewElement.DefaultTileHeight;
        BSize size = view.Measure(new BSize(width, 400));
        Assert.Equal(width / zoom, view.Snapshot!.Width, 1);

        // Show the second tile: DIPs 1,024 to 2,048 of the zoomed page.
        view.Arrange(new BRect(0, -1100, width, size.Height));
        var list = new BRenderList();
        var host = new Host { Width = (int)width, Height = 400, Scale = dpiScale };
        view.Render(new UiRenderContext(list, new StandardUiSessionBuilder().Build(host), host));
        var tile = list.Commands.OfType<BRenderCommand.DrawImage>().Single(draw => Math.Abs(draw.Destination.Y - (-1100 + tileTop)) < 0.5);
        var pixels = renderer.Pixels[tile.Image];

        var links = view.Snapshot.Links.Select(link => new BRect(link.Bounds.X * zoom, link.Bounds.Y * zoom, link.Bounds.Width * zoom, link.Bounds.Height * zoom))
            .Where(link => link.Y > tileTop + 4 && link.Bottom < tileTop + 1020).ToList();
        Assert.NotEmpty(links);
        double scale = (double)pixels.Width / width;
        foreach (var link in links)
            Assert.True(HasLinkColor(pixels, link, tileTop, scale), $"No link colour at {link}, display {dpiScale}, zoom {zoom}.");
    }

    [Fact]
    public void ZoomDiscardsEveryTileAndKeepsTheirPixelSize()
    {
        var renderer = new PixelRenderer();
        var view = new HtmlViewElement(TallDocument, () => renderer, _ => { }, () => 1.5);
        var host = new Host { Width = 600, Height = 400, Scale = 1.5 };
        var session = new StandardUiSessionBuilder().Build(host);
        void Render()
        {
            BSize size = view.Measure(new BSize(600, 400));
            view.Arrange(new BRect(0, 0, 600, size.Height));
            view.Render(new UiRenderContext(new BRenderList(), session, host));
        }
        Render();
        var before = renderer.Pixels.Keys.ToList();
        var sizes = view.CachedTileSizes.ToList();
        Assert.NotEmpty(before);

        view.Zoom = 2;
        Render();
        Assert.All(before, handle => Assert.Contains(handle, renderer.Released));
        // Tiles are cut from the page in DIPs: the same width makes the same tiles at any zoom.
        Assert.Equal(sizes[0], view.CachedTileSizes.First());
    }

    [Fact]
    public void TilesFollowThePageWidthWithoutARelayout()
    {
        var renderer = new PixelRenderer();
        var view = new HtmlViewElement(TallDocument, () => renderer, _ => { });
        var host = new Host { Width = 612, Height = 400 };
        var session = new StandardUiSessionBuilder().Build(host);
        BSize size = view.Measure(new BSize(600, 400));
        view.Arrange(new BRect(0, 0, 600, size.Height));
        view.Render(new UiRenderContext(new BRenderList(), session, host));
        var snapshot = view.Snapshot;
        var before = renderer.Pixels.Keys.ToList();

        // The scroll view arranges the page wider than it was measured once its scrollbar goes away;
        // the layout stays, and the tiles are drawn again at the new width instead of being stretched.
        view.Arrange(new BRect(0, 0, 612, size.Height));
        view.Render(new UiRenderContext(new BRenderList(), session, host));
        Assert.Same(snapshot, view.Snapshot);
        Assert.All(before, handle => Assert.Contains(handle, renderer.Released));
        Assert.All(view.CachedTileSizes, tile => Assert.Equal(612, tile.PixelWidth));
    }

    [Theory]
    [InlineData(2.0, 1400)]
    [InlineData(2.5, 900)]
    public void AtTheLargestZoomWideTilesKeepTheDisplayScaleWithinThePixelAndByteBudgets(double dpiScale, int viewportWidth)
    {
        // One address far wider than the width budget, and lines wider than the zoomed page.
        string html = $"<p>{new string('w', 3000)}</p>" + string.Concat(Enumerable.Range(1, 40).Select(index =>
            $"<p>Line {index}: https://example.test/{new string('x', 60)}</p>"));
        var renderer = new PixelRenderer();
        var view = new HtmlViewElement(html, () => renderer, _ => { }, () => dpiScale) { Zoom = PreviewZoom.Maximum };
        BSize size = view.Measure(new BSize(viewportWidth, 700));
        // The overflow is held at the width budget: 8,192 CSS pixels, drawn three times as large.
        Assert.Equal(HtmlViewElement.MaxBudgetWidth * PreviewZoom.Maximum, size.Width, 1);
        var host = new Host { Width = viewportWidth, Height = 700, Scale = dpiScale };
        var session = new StandardUiSessionBuilder().Build(host);

        // Across the first two rows, column by column: each tile is about 23 MB, so the cache reaches
        // its byte budget well before its count limit.
        int columns = (int)Math.Ceiling(size.Width / viewportWidth), largestCount = 0;
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < columns; column++)
            {
                view.Arrange(new BRect(-column * viewportWidth, -row * HtmlViewElement.DefaultTileHeight, size.Width, size.Height));
                view.Render(new UiRenderContext(new BRenderList(), session, host));
                // At the display scale, within the pixel cap, and no side longer than a Direct2D bitmap takes.
                Assert.All(view.CachedTileSizes, tile =>
                {
                    Assert.Equal(Math.Ceiling(tile.WidthDip * dpiScale), tile.PixelWidth);
                    Assert.Equal(Math.Ceiling(tile.HeightDip * dpiScale), tile.PixelHeight);
                    Assert.True((long)tile.PixelWidth * tile.PixelHeight <= HtmlViewElement.MaxTilePixels
                        && tile.PixelWidth <= HtmlViewElement.MaxTileSide && tile.PixelHeight <= HtmlViewElement.MaxTileSide, $"{tile}");
                });
                Assert.True(view.CachedTileBytes <= HtmlViewElement.MaxCachedTileBytes, $"{view.CachedTileBytes} bytes cached");
                Assert.Equal(view.CachedTileSizes.Sum(tile => (long)tile.PixelWidth * tile.PixelHeight * 4), view.CachedTileBytes);
                largestCount = Math.Max(largestCount, view.CachedTileCount);
            }

        // Tiles were let go while the cache held fewer than its count limit: the byte budget did that.
        Assert.NotEmpty(renderer.Released);
        Assert.True(largestCount < HtmlViewElement.MaxCachedTiles, $"{largestCount} tiles cached at most.");
    }

    [Fact]
    public void AShortTileOfAWidePageKeepsItsSidesWithinWhatDirect2DTakes()
    {
        // The short last tile of a 7,680-DIP page at 300 % is far under the pixel cap, but 23,040 pixels wide.
        var (scale, pixelWidth, pixelHeight) = HtmlViewElement.TilePixelSize(7680, 100, 3.0);
        Assert.True(scale < 3.0);
        Assert.InRange(pixelWidth, 1, HtmlViewElement.MaxTileSide);
        Assert.InRange(pixelHeight, 1, HtmlViewElement.MaxTileSide);
        Assert.True((long)pixelWidth * pixelHeight <= HtmlViewElement.MaxTilePixels);
    }

    [Theory]
    [InlineData(1.5, 2.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.0, 3.0)]
    public void APageWiderThanTheViewportIsDrawnInColumnsAtTheDisplayScale(double dpiScale, double zoom)
    {
        // An unbroken link running past the window, and below it unbroken text running three times as
        // far; the paragraphs after them fit the window.
        string html = $"<p><a href='https://example.test/far'>{new string('w', 100)}</a></p><p>{new string('w', 300)}</p>" + TallDocument;
        var renderer = new PixelRenderer();
        var view = new HtmlViewElement(html, () => renderer, _ => { }, () => dpiScale) { Zoom = zoom };
        const int width = 600;
        BSize size = view.Measure(new BSize(width, 400));
        var far = view.Snapshot!.Links.First(link => link.Href == "https://example.test/far").Bounds;
        var link = new BRect(far.X * zoom, far.Y * zoom, far.Width * zoom, far.Height * zoom);
        // The column the link ends in (with at least 20 DIPs of it), and the next, which it does not reach.
        int last = (int)((link.Right - 20) / width), after = last + 1;
        Assert.True(last >= 2 && (after + 1) * width < size.Width, $"The link at {link} should end in the third column or later, on a page {size.Width} wide.");

        var host = new Host { Width = width, Height = 400, Scale = dpiScale };
        var session = new StandardUiSessionBuilder().Build(host);
        BPixelBuffer ColumnShown(int column)
        {
            // Scrolled sideways to the column: it alone is drawn in view, at the display scale.
            view.Arrange(new BRect(-column * width, 0, size.Width, size.Height));
            var list = new BRenderList();
            view.Render(new UiRenderContext(list, session, host));
            var draws = list.Commands.OfType<BRenderCommand.DrawImage>().ToList();
            Assert.All(draws, draw => Assert.InRange(draw.Destination.X, -0.5, width));
            var tile = draws.Single(draw => Math.Abs(draw.Destination.X) < 0.5 && Math.Abs(draw.Destination.Y) < 0.5);
            Assert.Equal(width, tile.Destination.Width, 3);
            var pixels = renderer.Pixels[tile.Image];
            Assert.Equal((int)Math.Ceiling(width * dpiScale), pixels.Width);
            return pixels;
        }

        // Each column shows its own part of the page: the link's end where the layout puts it, and past
        // it no link at all, although the page's left edge has one at that height.
        double left = last * width;
        var end = new BRect(Math.Max(link.X, left) - left, link.Y, link.Right - Math.Max(link.X, left), link.Height);
        Assert.True(HasLinkColor(ColumnShown(last), end, 0, dpiScale), $"No link colour at {end} in column {last}, display {dpiScale}, zoom {zoom}.");
        var band = new BRect(0, link.Y, width, link.Height);
        Assert.False(HasLinkColor(ColumnShown(after), band, 0, dpiScale), $"Link colour in column {after}, past the link's end, display {dpiScale}, zoom {zoom}.");
    }

    [Fact]
    public async Task ThePreviewOpensAtTheSystemTextSize()
    {
        await InPreview(TallDocument, StandardThemeTokens.Light.WithTextScale(1.5), window =>
        {
            var view = window.HtmlView;
            Assert.Equal(1.5, window.Zoom);
            Assert.Equal(1.5, window.DefaultZoom);
            Assert.Equal(view.Content.ContentWidth / 1.5, view.Snapshot!.Width, 1);
            Assert.Equal("150 %", window.ZoomResetButton.Text);
            Assert.False(window.ZoomResetButton.IsEnabled);
            Assert.Equal("150 %, the default zoom", window.ZoomResetButton.GetSemanticNode().Name);
            // The plain text's font already has the text size; it is not enlarged twice.
            Assert.Equal(1, window.PlainTextView.Editor.Zoom);
        });
    }

    [Fact]
    public async Task ShortcutsButtonsAndTheWheelZoomByStepsAndAnnounceEachChange()
    {
        // Each step runs in its own posted callback; the announcement of a step's change is said after it.
        var announced = new List<string>();
        double offset = 0;
        UiInputEvent WheelOver(HtmlPreviewWindow window, double notches, InputModifiers modifiers = InputModifiers.None)
        {
            var over = window.HtmlView.Bounds;
            return Wheel(over.X + (over.Width / 2), over.Y + (over.Height / 2), notches, modifiers);
        }
        await InPreview(TallDocument, StandardThemeTokens.Light,
            window =>
            {
                window.Session.SemanticChanged += (_, change) => { if (change.Change == UiSemanticChangeKind.StatusAnnounced) announced.Add(change.Message!); };
                window.Dispatch(Key(0xBB, KeyboardModifierState.Control)); // Ctrl+=
                Assert.Equal(1.1, window.Zoom);
                Assert.Empty(announced); // said once this input has been handled
            },
            window =>
            {
                Assert.Equal(["Zoom 110 %."], announced);
                window.Dispatch(Key(0x6B, KeyboardModifierState.LeftControl)); // Ctrl+number pad plus
                Assert.Equal(1.25, window.Zoom);
                window.Dispatch(Key(0xBD, KeyboardModifierState.RightControl)); // Ctrl+-
                Assert.Equal(1.1, window.Zoom);
                window.Dispatch(Key(0x6D, KeyboardModifierState.Control)); // Ctrl+number pad minus
                window.Dispatch(Key(0x6D, KeyboardModifierState.Control));
                Assert.Equal(0.9, window.Zoom);
                window.Dispatch(Key(0x30, KeyboardModifierState.Control)); // Ctrl+0
                Assert.Equal(1.0, window.Zoom);
                window.Dispatch(Key(0xBB, KeyboardModifierState.Control | KeyboardModifierState.Shift)); // Ctrl+Plus on a US layout
                Assert.Equal(1.1, window.Zoom);
                window.Dispatch(Key(0x60, KeyboardModifierState.Control)); // Ctrl+number pad 0
                Assert.Equal(1.0, window.Zoom);
                // AltGr (Ctrl+Alt) types characters on many layouts; it and the bare keys do not zoom.
                window.Dispatch(Key(0xBB, KeyboardModifierState.Control | KeyboardModifierState.RightAlt));
                window.Dispatch(Key(0xBB));
                window.Dispatch(Key(0x30));
                Assert.Equal(1.0, window.Zoom);
            },
            window =>
            {
                // A burst of changes is said once, at the level it ended on.
                Assert.Equal(["Zoom 110 %.", "Zoom 100 %."], announced);
                // The wheel scrolls; with Ctrl it zooms instead, a touchpad's half notches adding up to a step.
                window.Dispatch(WheelOver(window, -2));
                window.Session.RenderFrame();
                offset = window.HtmlView.Scroll.VerticalOffset;
                Assert.True(offset > 0);
                window.Dispatch(WheelOver(window, 0.5, InputModifiers.Control));
                Assert.Equal(1.0, window.Zoom);
                window.Dispatch(WheelOver(window, 0.5, InputModifiers.Control));
                Assert.Equal(1.1, window.Zoom);
                Assert.Equal(offset, window.HtmlView.Scroll.VerticalOffset);
            },
            window =>
            {
                Assert.Equal("Zoom 110 %.", announced[^1]);
                window.Dispatch(WheelOver(window, -2, InputModifiers.LeftControl));
                Assert.Equal(0.9, window.Zoom);
            },
            window =>
            {
                // Two notches at once are one change.
                Assert.Equal(["Zoom 110 %.", "Zoom 100 %.", "Zoom 110 %.", "Zoom 90 %."], announced);
                // The buttons step too and stop at the limits; a button that becomes unavailable passes focus on.
                var session = window.Session;
                session.SetFocus(window.ZoomInButton);
                while (window.ZoomInButton.IsEnabled) window.Dispatch(Key(0x0D));
                Assert.Equal(PreviewZoom.Maximum, window.Zoom);
                Assert.Same(window.ZoomResetButton, session.FocusedElement);
                Assert.Equal("300 %", window.ZoomResetButton.Text);
                // The name starts with the text the button shows.
                Assert.Equal("300 %, reset zoom to 100 %", window.ZoomResetButton.GetSemanticNode().Name);
                window.StepZoom(1);
                Assert.Equal(PreviewZoom.Maximum, window.Zoom);
            },
            window =>
            {
                // Focus is on the reset button, whose name now holds the level, and a screen reader reads
                // that; a notification as well would say 300 % twice.
                Assert.Equal(4, announced.Count);
                // Past the limit nothing changes, so nothing is said.
                window.StepZoom(1);
            },
            window =>
            {
                Assert.Equal(4, announced.Count);
                var session = window.Session;
                window.Dispatch(Key(0x0D)); // Enter on the reset button
                Assert.Equal(1.0, window.Zoom);
                Assert.False(window.ZoomResetButton.IsEnabled);
                Assert.Same(window.ZoomInButton, session.FocusedElement);
                while (window.ZoomOutButton.IsEnabled) window.ZoomOutButton.Click();
                Assert.Equal(PreviewZoom.Minimum, window.Zoom);
                Assert.True(window.ZoomInButton.IsEnabled);
            },
            window =>
            {
                // With focus on Zoom in, whose name stays, the level is said: once, for the whole burst.
                Assert.Equal(5, announced.Count);
                Assert.Equal("Zoom 50 %.", announced[^1]);
            });
    }

    [Fact]
    public async Task TabReachesTheZoomButtonsAfterShowPlainText()
    {
        await InPreview(TallDocument, StandardThemeTokens.Light, window =>
        {
            var session = window.Session;
            session.SetFocus(window.ToggleButton);
            window.MoveFocus(1);
            Assert.Same(window.ZoomOutButton, session.FocusedElement);
            // At the default the reset button is unavailable and out of the Tab order.
            window.MoveFocus(1);
            Assert.Same(window.ZoomInButton, session.FocusedElement);
            window.MoveFocus(1);
            Assert.Same(window.HtmlView.Content, session.FocusedElement);

            window.StepZoom(1);
            session.SetFocus(window.ToggleButton);
            window.MoveFocus(1);
            window.MoveFocus(1);
            Assert.Same(window.ZoomResetButton, session.FocusedElement);
            Assert.All(new[] { window.ZoomOutButton, window.ZoomResetButton, window.ZoomInButton },
                button => Assert.False(string.IsNullOrWhiteSpace(button.GetSemanticNode().Name)));
            Assert.Equal(["Zoom out", "110 %, reset zoom to 100 %", "Zoom in"],
                new[] { window.ZoomOutButton, window.ZoomResetButton, window.ZoomInButton }.Select(button => button.GetSemanticNode().Name));
        });
    }

    [Fact]
    public async Task AnUnzoomedPreviewFollowsTheTextSizeAndAZoomedOneKeepsItsZoom()
    {
        await InPreview(TallDocument, StandardThemeTokens.Light,
            window => Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light.WithTextScale(1.5))).Wait(),
            window =>
            {
                // Not zoomed by the reader: the document follows the new text size.
                Assert.Equal(1.5, window.Zoom);
                Assert.Equal(1.5, window.DefaultZoom);
                Assert.Equal(1, window.PlainTextView.Editor.Zoom);
                window.StepZoom(1);
                window.StepZoom(1);
                Assert.Equal(2.0, window.Zoom);
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Dark.WithTextScale(1.25))).Wait();
            },
            window =>
            {
                // Zoomed by the reader: the zoom stays, and only Reset's target moves.
                Assert.Equal(2.0, window.Zoom);
                Assert.Equal(1.25, window.DefaultZoom);
                Assert.Equal("200 %, reset zoom to 125 %", window.ZoomResetButton.GetSemanticNode().Name);
                // The plain text (whose font now has 125 %) is drawn at the same 200 %.
                Assert.Equal(1.6, window.PlainTextView.Editor.Zoom, 6);
                window.ResetZoom();
                Assert.Equal(1.25, window.Zoom);
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light)).Wait();
            },
            window =>
            {
                // Reset makes it follow the system again.
                Assert.Equal(1.0, window.Zoom);
                Assert.False(window.ZoomResetButton.IsEnabled);
            });
    }

    [Fact]
    public async Task AZoomTheTextSizeMovesOntoStaysTheReadersOwn()
    {
        await InPreview(TallDocument, StandardThemeTokens.Light,
            window =>
            {
                window.StepZoom(1);
                window.StepZoom(1);
                Assert.Equal(1.25, window.Zoom);
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light.WithTextScale(1.25))).Wait();
            },
            window =>
            {
                // The system text size has come to the reader's zoom; that does not make it the system's.
                Assert.Equal(1.25, window.Zoom);
                Assert.Equal(1.25, window.DefaultZoom);
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light.WithTextScale(1.5))).Wait();
            },
            window =>
            {
                Assert.Equal(1.25, window.Zoom);
                Assert.Equal(1.5, window.DefaultZoom);
                Assert.Equal("125 %, reset zoom to 150 %", window.ZoomResetButton.GetSemanticNode().Name);
                // The reader's own step onto the default does make it follow again.
                window.StepZoom(1);
                Assert.Equal(1.5, window.Zoom);
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light.WithTextScale(1.75))).Wait();
            },
            window =>
            {
                Assert.Equal(1.75, window.Zoom);
                window.StepZoom(-1);
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light.WithTextScale(1.5))).Wait();
            },
            window =>
            {
                // So does Ctrl+0 (or Reset) at a zoom the text size has come to, although nothing changes.
                Assert.Equal(1.5, window.Zoom);
                Assert.Equal(1.5, window.DefaultZoom);
                window.ResetZoom();
                Task.Run(() => window.ApplyTheme(StandardThemeTokens.Light.WithTextScale(1.25))).Wait();
            },
            window => Assert.Equal(1.25, window.Zoom));
    }

    [Fact]
    public async Task TheShortenedPreviewNoticeFollowsTheZoomBothWays()
    {
        await InPreview(BudgetDocument, StandardThemeTokens.Light, window =>
        {
            void ZoomAllTheWay(int direction)
            {
                while (direction > 0 ? window.ZoomInButton.IsEnabled : window.ZoomOutButton.IsEnabled) window.StepZoom(direction);
                window.Session.RenderFrame();
                window.SyncTruncationNotice();
            }
            ZoomAllTheWay(-1);
            Assert.False(window.HtmlView.Snapshot!.IsTruncated);
            Assert.Equal(UiVisibility.Collapsed, window.TruncationNotice.Visibility);
            ZoomAllTheWay(1);
            Assert.True(window.HtmlView.Snapshot!.IsTruncated);
            Assert.Equal(UiVisibility.Visible, window.TruncationNotice.Visibility);
            ZoomAllTheWay(-1);
            Assert.Equal(UiVisibility.Collapsed, window.TruncationNotice.Visibility);
        });
    }

    [Fact]
    public async Task AThemeAppliedBeforeTheWindowExistsReachesIt()
    {
        var changed = StandardThemeTokens.Dark.WithTextScale(1.5);
        await InPreview(TallDocument, StandardThemeTokens.Light, [window =>
        {
            Assert.Same(changed, StandardControlPaint.GetTheme(window.Session));
            Assert.Equal(1.5, window.Zoom);
        }], beforeShown: window => window.ApplyTheme(changed));
    }

    [Fact]
    public async Task APreviewGetsAThemeChangedWhileItsWindowWasBeingBuilt()
    {
        var changed = StandardThemeTokens.Dark.WithTextScale(1.25);
        WindowsHtmlPreviewHost? host = null;
        // The main window's theme changes just after the preview has read it.
        host = new WindowsHtmlPreviewHost(() => { host!.ApplyTheme(changed); return StandardThemeTokens.Light; });
        using (host)
        {
            var body = new MailMessageBody(new MailMessageKey(new(Guid.NewGuid()), "INBOX", 1, 7), "Agenda as text", "<p>Agenda</p>");
            string status = await host.ShowAsync(body).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.StartsWith("HTML preview open", status);
            var window = host.Window!;
            var seen = new TaskCompletionSource<(StandardThemeTokens Theme, double Zoom)>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Post(() => seen.TrySetResult((StandardControlPaint.GetTheme(window.Session), window.Zoom)));
            var (theme, zoom) = await seen.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Same(changed, theme);
            Assert.Equal(1.25, zoom);
        }
    }

    /// <summary>
    /// Opens a real preview window on its own thread and runs each step there in turn, each in its own
    /// posted callback, so a theme a step applies has arrived before the next one runs.
    /// </summary>
    private static Task InPreview(string html, StandardThemeTokens theme, params Action<HtmlPreviewWindow>[] steps) =>
        InPreview(html, theme, steps, beforeShown: null);

    private static async Task InPreview(string html, StandardThemeTokens theme, Action<HtmlPreviewWindow>[] steps, Action<HtmlPreviewWindow>? beforeShown)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HtmlPreviewWindow(new HtmlPreviewDocument(html, new HashSet<string> { "https://example.test/end" }), "Agenda as text", _ => { }, theme: theme)
                { ShowInTaskbar = false, Opacity = 0 };
                beforeShown?.Invoke(window);
                window.Shown += (_, _) => RunStep(window, steps, 0, finished);
                window.Run();
            }
            catch (Exception error) { finished.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static void RunStep(HtmlPreviewWindow window, Action<HtmlPreviewWindow>[] steps, int index, TaskCompletionSource finished)
    {
        try
        {
            window.Session.RenderFrame();
            steps[index](window);
            if (index + 1 < steps.Length)
            {
                window.Post(() => RunStep(window, steps, index + 1, finished));
                return;
            }
            finished.TrySetResult();
        }
        catch (Exception error) { finished.TrySetException(error); }
        window.Close();
    }

    private static bool HasLinkColor(BPixelBuffer pixels, BRect linkDip, double tileTop, double scale)
    {
        int top = (int)((linkDip.Y - tileTop) * scale), bottom = (int)((linkDip.Bottom - tileTop) * scale);
        int left = (int)(linkDip.X * scale), right = Math.Min(pixels.Width, (int)(linkDip.Right * scale));
        for (int y = Math.Max(0, top); y < Math.Min(pixels.Height, bottom); y++)
            for (int x = left; x < right; x++)
            {
                int i = ((y * pixels.Width) + x) * 4;
                if (pixels.Rgba[i + 3] > 200 && pixels.Rgba[i + 2] > 150 && pixels.Rgba[i] < 120) return true;
            }
        return false;
    }

    private static InputEventHeader Header() => new(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);

    private static UiInputEvent Click(double x, double y) => UiInputEvent.FromMouseButton(new MouseButtonEvent(
        Header(), InputPoint.ClientDeviceIndependentPixels(x, y), MouseButtons.None, MouseButton.Left,
        MouseButtonTransition.Up, InputEventSource.Synthetic));

    private static UiInputEvent Wheel(double x, double y, double notches, InputModifiers modifiers) => UiInputEvent.FromMouseWheel(new MouseWheelEvent(
        Header(), InputPoint.ClientDeviceIndependentPixels(x, y), MouseButtons.None, MouseWheelAxis.Vertical, notches, InputEventSource.Synthetic, modifiers));

    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(Header(),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));

    /// <summary>Keeps every tile's pixels, so a test can look at what was drawn where.</summary>
    private sealed class PixelRenderer : IBroilerRenderer
    {
        private int _nextId = 1;
        public Dictionary<BImageHandle, BPixelBuffer> Pixels { get; } = [];
        public List<BImageHandle> Released { get; } = [];

        public BImageHandle CreateImage(ReadOnlySpan<byte> encodedData) => throw new NotSupportedException();

        public BImageHandle CreateImage(BPixelBuffer pixelBuffer)
        {
            var handle = new BImageHandle(new BResourceHandle(BResourceKind.Image, (ulong)_nextId++), new BSize(pixelBuffer.Width, pixelBuffer.Height));
            Pixels[handle] = pixelBuffer;
            return handle;
        }

        public void ReleaseImage(BImageHandle handle) => Released.Add(handle);
        public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext) { }
        public Broiler.Graphics.Imaging.BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Host _host;
        private readonly UiElement _root;

        public Fixture(UiElement root, int width = 600, int height = 400)
        {
            _root = root;
            _host = new Host { Width = width, Height = height };
            Session = new StandardUiSessionBuilder().WithDispatcher(new ImmediateUiDispatcher()).Build(_host);
            Session.AddRoot(root);
            Session.RenderFrame();
        }

        public UiSession Session { get; }

        public bool Press(int code, KeyboardModifierState modifiers = KeyboardModifierState.None)
        {
            bool handled = Session.DispatchInput(Key(code, modifiers));
            Session.RenderFrame();
            return handled;
        }

        public void Resize(int width)
        {
            _host.Width = width;
            _root.InvalidateMeasure();
            Session.RenderFrame();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            _root.Dispose();
        }
    }

    private sealed class Host : IUiHost
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double Scale { get; set; } = 1;
        public BSize ViewportSize => new(Width, Height);
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
