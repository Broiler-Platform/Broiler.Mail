using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.Resources;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>UI-11: the HTML preview by keyboard and screen reader, and a reading position that survives reflow.</summary>
[Collection("UI theme")]
public sealed class HtmlPreviewKeyboardTests
{
    private static readonly string TallDocument = string.Concat(Enumerable.Range(1, 60).Select(index => $"<p>Paragraph {index}: a longer line of text that wraps differently once the window becomes narrower than before, so a resize changes how tall the whole document is.</p>"))
        + "<p>Last: <a href='https://example.test/end'>the closing link</a></p>";

    [Fact]
    public void OpenableLinksBecomeNamedHyperlinksThatEnterOpens()
    {
        string html = "<p>Read <a href='https://example.test/a'>the agenda</a>, write <a href='mailto:someone@example.test'>mail</a>, "
            + "and see <a href=\"https://example.test/b?x=1&amp;y=2\"><b>bold</b>  link</a>.</p>";
        var opened = new List<string>();
        var view = new HtmlViewElement(html, () => null, opened.Add, canOpenLink: href => href.StartsWith("https:", StringComparison.Ordinal));
        using var fixture = new Fixture(view);

        var targets = view.LinkTargets;
        Assert.Equal(["https://example.test/a", "https://example.test/b?x=1&y=2"], targets.Select(target => target.Href));
        Assert.Equal(["the agenda", "bold link"], targets.Select(target => target.GetSemanticNode().Name));
        Assert.All(targets, target => Assert.Equal(UiSemanticRole.Hyperlink, target.GetSemanticNode().Role));
        // Each target sits over its painted link.
        var link = view.Snapshot!.Links.First(item => item.Href == targets[0].Href);
        Assert.Equal(new BRect(view.Bounds.X + link.Bounds.X, view.Bounds.Y + link.Bounds.Y, link.Bounds.Width, link.Bounds.Height), targets[0].Bounds);

        fixture.Session.SetFocus(targets[1]);
        Assert.True(fixture.Session.DispatchInput(Key(0x0D)));
        Assert.Equal(["https://example.test/b?x=1&y=2"], opened);
    }

    [Fact]
    public void ALinkWrappedOverSeveralLinesIsOneTargetAndItsLaterLinesStillOpenByMouse()
    {
        string html = "<p><a href='https://example.test/long'>a very long link text that has to wrap over several lines here</a></p>";
        var opened = new List<string>();
        var view = new HtmlViewElement(html, () => null, opened.Add);
        using var fixture = new Fixture(view, width: 160);

        // Broiler.HTML reports a wrapped link's first line only, so its single target covers that line.
        var target = Assert.Single(view.LinkTargets);
        Assert.Equal(view.Snapshot!.Links[0].Bounds.Height, target.Bounds.Height, 3);
        view.SendInput(Click(target.Bounds.X + 10, target.Bounds.Bottom + 10, MouseButtonTransition.Down));
        Assert.True(view.SendInput(Click(target.Bounds.X + 10, target.Bounds.Bottom + 10)));
        Assert.Equal(["https://example.test/long"], opened);
    }

    [Fact]
    public void ZoomedHtmlCopiesADragAcrossALinkAndSupportsSelectAll()
    {
        var opened = new List<string>();
        var view = new HtmlViewElement(
            "<p style='font:24px monospace'><a href='https://example.test'>Alpha Beta Gamma</a> Delta</p>",
            () => null, opened.Add);
        view.Zoom = 1.5;
        using var fixture = new Fixture(view);
        BRect link = Assert.Single(view.LinkTargets).Bounds;
        double x = link.Left + 1;
        double y = link.Top + link.Height / 2;
        double endX = link.Left + link.Width * 0.7;
        Assert.True(HtmlViewElement.HasSelectableTextAt(fixture.Session, new BPoint(x, y)));
        Assert.False(HtmlViewElement.HasSelectableTextAt(fixture.Session, new BPoint(x, link.Bottom + 60)));
        var header = new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);

        fixture.Session.DispatchInput(UiInputEvent.FromMouseButton(new MouseButtonEvent(
            header, InputPoint.ClientDeviceIndependentPixels(x, y), MouseButtons.Left, MouseButton.Left,
            MouseButtonTransition.Down, InputEventSource.Synthetic)));
        fixture.Session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(
            header, InputPoint.ClientDeviceIndependentPixels(endX, y), MouseButtons.Left, InputEventSource.Synthetic)));
        fixture.Session.DispatchInput(Click(endX, y));
        Assert.Empty(opened);
        Assert.Null(fixture.Session.CapturedElement);
        Assert.True(fixture.Press(0x43, KeyboardModifierState.Control));
        Assert.Contains("Alpha Beta", fixture.Clipboard);
        Assert.DoesNotContain("Delta", fixture.Clipboard);

        Assert.True(fixture.Press(0x41, KeyboardModifierState.Control));
        Assert.True(fixture.Press(0x43, KeyboardModifierState.Control));
        Assert.Equal("Alpha Beta Gamma Delta", fixture.Clipboard);

        view.UpdateHtml("<p>Replacement</p>");
        Assert.True(fixture.Press(0x43, KeyboardModifierState.Control));
        Assert.Equal("Alpha Beta Gamma Delta", fixture.Clipboard);
    }

    [Fact]
    public void ContextMenuCopiesTextOrLinkAndOpensLinksOnlyOnRequest()
    {
        var opened = new List<string>();
        const string url = "https://example.test/agenda";
        var view = new HtmlViewElement($"<p><a href='{url}'>Agenda</a></p>", () => null, opened.Add);
        using var fixture = new Fixture(view);
        BRect link = Assert.Single(view.LinkTargets).Bounds;
        double x = link.Left + 4;
        double y = link.Top + link.Height / 2;

        StandardMenu OpenMenu()
        {
            fixture.Session.DispatchInput(Click(x, y, MouseButtonTransition.Down, MouseButton.Right));
            fixture.Session.DispatchInput(Click(x, y, MouseButtonTransition.Up, MouseButton.Right));
            return Assert.IsType<StandardMenu>(fixture.Session.CapturedElement);
        }

        void Choose(StandardMenu menu, string label)
        {
            BRect row = Assert.Single(menu.GetSemanticNode().Children, item => item.Name == label).Bounds;
            fixture.Session.DispatchInput(Click(row.Left + 5, row.Top + 5, MouseButtonTransition.Down));
            fixture.Session.DispatchInput(Click(row.Left + 5, row.Top + 5));
        }

        var menu = OpenMenu();
        Assert.False(Assert.Single(menu.Items, item => item.Id == "copy").IsEnabled);
        Choose(menu, "Copy Link Address");
        Assert.Equal(url, fixture.Clipboard);
        Assert.Empty(opened);

        Choose(OpenMenu(), "Select All");
        menu = OpenMenu();
        Assert.True(Assert.Single(menu.Items, item => item.Id == "copy").IsEnabled);
        Choose(menu, "Copy");
        Assert.Equal("Agenda", fixture.Clipboard);
        Assert.Empty(opened);

        Choose(OpenMenu(), "Open Link");
        Assert.Equal([url], opened);
        Assert.Null(fixture.Session.CapturedElement);
    }

    [Fact]
    public void TheFocusedDocumentScrollsByKeyboard()
    {
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        using var fixture = new Fixture(view);
        var scroll = view.Scroll;
        fixture.Session.SetFocus(view.Content);

        Assert.True(fixture.Press(0x22)); // Page Down
        double afterPage = scroll.VerticalOffset;
        Assert.True(afterPage > 100);
        Assert.True(fixture.Press(0x20)); // Space
        Assert.True(scroll.VerticalOffset > afterPage);
        Assert.True(fixture.Press(0x20, KeyboardModifierState.LeftShift));
        Assert.Equal(afterPage, scroll.VerticalOffset, 3);
        fixture.Press(0x23); // End
        Assert.Equal(scroll.ExtentSize.Height - scroll.ViewportSize.Height, scroll.VerticalOffset, 3);
        fixture.Press(0x24); // Home
        Assert.Equal(0, scroll.VerticalOffset);
        fixture.Press(0x28); // Down
        Assert.True(scroll.VerticalOffset > 0);
    }

    [Fact]
    public void TabReachesTheDocumentAndThenItsLinksWhichScrollIntoView()
    {
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        using var fixture = new Fixture(view);

        var stops = MailKeyboardNavigation.TabStops(view);
        var link = Assert.Single(view.Content.LinkTargets);
        Assert.Equal(new UiElement[] { view.Content, link }, stops);

        fixture.Session.SetFocus(link);
        view.Reveal(link);
        fixture.Session.RenderFrame();
        Assert.True(link.Bounds.Top >= view.Scroll.ContentBounds.Top && link.Bounds.Bottom <= view.Scroll.ContentBounds.Bottom,
            $"Link {link.Bounds} should be inside the viewport {view.Scroll.ContentBounds}.");
    }

    [Fact]
    public void TheReadingPositionSurvivesStatusChangesAndReflowButNotANewDocument()
    {
        var status = new StandardLabel { Text = "Simplified HTML.", Wrapping = UiTextWrapping.Wrap };
        var view = new ScrollableHtmlView(TallDocument, () => null, _ => { });
        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        root.AddChild(status);
        root.SetDock(status, UiDock.Top);
        root.AddChild(view);
        using var fixture = new Fixture(root);
        var scroll = view.Scroll;
        double Range() => scroll.ExtentSize.Height - scroll.ViewportSize.Height;
        scroll.SetOffset(new BPoint(0, Math.Round(Range() / 2)));
        fixture.Session.RenderFrame();
        double offset = scroll.VerticalOffset;

        // A longer status line shrinks the viewport but changes nothing about the document.
        status.Text = string.Join(" ", Enumerable.Repeat("Loaded 3 of 5 remote images. Some images failed or exceeded limits.", 4));
        fixture.Session.RenderFrame();
        Assert.Equal(offset, scroll.VerticalOffset, 3);

        // A narrower window reflows the document; the reader stays about halfway through it.
        double extentBefore = scroll.ExtentSize.Height;
        fixture.Resize(300);
        Assert.NotEqual(extentBefore, scroll.ExtentSize.Height);
        Assert.InRange(scroll.VerticalOffset / Range(), 0.45, 0.55);

        // Another document starts at its top.
        view.UpdateHtml("<p>Replacement document</p>" + TallDocument);
        fixture.Session.RenderFrame();
        Assert.Equal(0, scroll.VerticalOffset);
    }

    [Fact]
    public async Task ThePreviewWindowStartsOnTheDocumentAndTabCyclesThroughButtonsDocumentAndLinks()
    {
        const string link = "https://example.test/agenda";
        string html = $"<h1>Agenda</h1><p>See <a href='{link}'>the agenda</a>.</p>";
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var opened = new List<Uri>();
                using var window = new HtmlPreviewWindow(new HtmlPreviewDocument(html, new HashSet<string> { link }), "Agenda as text", opened.Add)
                { ShowInTaskbar = false, Opacity = 0 };
                window.Shown += (_, _) =>
                {
                    try
                    {
                        var session = window.Session;
                        var document = window.HtmlView.Content;
                        session.RenderFrame();
                        Assert.Same(document, session.FocusedElement);
                        Assert.Single(document.LinkTargets);

                        // A relayout rebuilds the targets, so each check looks the link up again.
                        window.MoveFocus(1);
                        Assert.Same(Assert.Single(document.LinkTargets), session.FocusedElement);
                        Assert.True(session.DispatchInput(Key(0x0D)));
                        Assert.Equal(link, Assert.Single(opened).AbsoluteUri);

                        window.MoveFocus(1);
                        Assert.Same(window.ToggleButton, session.FocusedElement); // wraps to the first stop
                        window.MoveFocus(-1);
                        Assert.Same(Assert.Single(document.LinkTargets), session.FocusedElement);

                        // Hiding the HTML moves focus from the link to the text that replaces it, and back.
                        window.ToggleView();
                        Assert.IsType<Broiler.UI.RichEdit.Standard.StandardRichEdit>(session.FocusedElement);
                        window.ToggleView();
                        Assert.Same(document, session.FocusedElement);
                        finished.TrySetResult();
                    }
                    catch (Exception error) { finished.TrySetException(error); }
                    finally { window.Close(); }
                };
                window.Run();
            }
            catch (Exception error) { finished.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AnOpenPreviewFollowsAThemeAndTextSizeChangeOnItsOwnThread()
    {
        StandardThemeTokens changed = StandardThemeTokens.Dark.WithTextScale(1.5);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // Opened light, as from a light shell; without a theme it would take the process-wide palette.
                using var window = new HtmlPreviewWindow(new HtmlPreviewDocument("<p>Agenda</p>", new HashSet<string>()), "Agenda as text", _ => { },
                    theme: StandardThemeTokens.Light)
                { ShowInTaskbar = false, Opacity = 0 };
                window.Shown += (_, _) =>
                {
                    bool lightCaption = !DwmCaption.IsDark(window.NativeHandle);
                    // Applied from another thread, as the main window does; it runs on the preview's thread.
                    Task.Run(() => window.ApplyTheme(changed)).Wait();
                    window.Post(() =>
                    {
                        try
                        {
                            Assert.Same(changed, StandardControlPaint.GetTheme(window.Session));
                            Assert.Equal(changed.FontBody, window.ToggleButton.Font);
                            Assert.Equal(changed.FontBody, window.Status.Font);
                            Assert.Equal(changed.Surface, window.Root.Background);
                            // The caption follows as well, from light (opened from a light shell) to dark,
                            // where Windows supports a dark caption. WindowCaptionTests covers the other calls.
                            if (DwmCaption.IsSupported)
                            {
                                Assert.True(lightCaption);
                                Assert.True(DwmCaption.IsDark(window.NativeHandle));
                            }
                            window.Session.RenderFrame();
                            finished.TrySetResult();
                        }
                        catch (Exception error) { finished.TrySetException(error); }
                        finally { window.Close(); }
                    });
                };
                window.Run();
            }
            catch (Exception error) { finished.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotSame(changed, StandardControlPaint.Theme); // the process-wide palette is the main window's
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TilesBelowTheFirstShowTheirOwnPartOfTheDocumentAtEveryScale(double scale)
    {
        // Paragraphs of irregular height, some with a link, so no two parts of the document look alike.
        string html = string.Concat(Enumerable.Range(1, 80).Select(index =>
            $"<p>Paragraph {index}: {string.Join(" ", Enumerable.Repeat("text", (index * 7 % 23) + 3))}"
            + (index % 9 == 0 ? $" <a href='https://example.test/{index}'>link {index}</a>" : "") + "</p>"));
        var view = new HtmlViewElement(html, () => null, _ => { });
        var layout = view.CalculateLayout(600);
        const double tileTop = 1024;
        var links = layout.Links.Where(link => link.Bounds.Y > tileTop + 4 && link.Bounds.Bottom < tileTop + 1020).ToList();
        Assert.NotEmpty(links);

        using var tile = view.PaintTile(tileTop, scale, (int)Math.Ceiling(600 * scale), (int)Math.Ceiling(1024 * scale));
        var pixels = tile.ToPixelBuffer();
        // The layout says where each link is; the painted tile must show link-blue there.
        foreach (var link in links)
            Assert.True(HasLinkColor(pixels, link.Bounds, tileTop, scale), $"No link colour at {link.Href}, {link.Bounds}, scale {scale}.");
    }

    [Fact]
    public void PointerSelectionOnlyInvalidatesAffectedTilesInMultiTileDocument()
    {
        var renderer = new TileCountingRenderer();
        string html = "<p style='font:20px monospace'>Selectable start line in tile zero.</p>" + TallDocument;
        var view = new ScrollableHtmlView(html, () => renderer, _ => { });
        using var fixture = new Fixture(view, width: 600, height: 400);

        // Scroll to tile 1 so both tile 0 and tile 1 are cached, then back to top.
        view.Scroll.SetOffset(new BPoint(0, HtmlViewElement.DefaultTileHeight + 10));
        fixture.Session.RenderFrame();
        view.Scroll.SetOffset(new BPoint(0, 0));
        fixture.Session.RenderFrame();
        Assert.True(view.Content.IsTileCached(0));
        Assert.True(view.Content.IsTileCached(1));

        int createdBefore = renderer.CreatedCount;
        int releasedBefore = renderer.ReleasedCount;

        var header = new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);
        fixture.Session.DispatchInput(UiInputEvent.FromMouseButton(new MouseButtonEvent(
            header, InputPoint.ClientDeviceIndependentPixels(20, 20), MouseButtons.Left, MouseButton.Left,
            MouseButtonTransition.Down, InputEventSource.Synthetic)));
        fixture.Session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(
            header, InputPoint.ClientDeviceIndependentPixels(160, 20), MouseButtons.Left, InputEventSource.Synthetic)));
        fixture.Session.DispatchInput(Click(160, 20));
        fixture.Session.RenderFrame();

        // Only tile 0 should have been invalidated and re-rastered; tile 1 stays cached.
        Assert.Equal(releasedBefore + 1, renderer.ReleasedCount);
        Assert.Equal(createdBefore + 1, renderer.CreatedCount);
        Assert.True(view.Content.IsTileCached(0));
        Assert.True(view.Content.IsTileCached(1));
    }

    [Fact]
    public void LiveResizeDefersHtmlReflowAndReusesLinkTargetsOnSettle()
    {
        var renderer = new TileCountingRenderer();
        var view = new ScrollableHtmlView(
            "<p><a href='https://example.test/one'>First link</a></p>" + TallDocument,
            () => renderer, _ => { });
        using var fixture = new Fixture(view, width: 600, height: 400);

        double initialLayoutWidth = view.Snapshot!.Width;
        var firstLinkTarget = view.Content.LinkTargets[0];
        int releasedBefore = renderer.ReleasedCount;

        view.SetLiveResizing(true);
        fixture.Resize(480);
        // During live resize, layout and tiles are preserved without discarding GPU tiles.
        Assert.Equal(initialLayoutWidth, view.Snapshot.Width);
        Assert.Equal(releasedBefore, renderer.ReleasedCount);
        Assert.True(view.Content.CachedTileCount > 0);

        view.SetLiveResizing(false);
        fixture.Session.RenderFrame();
        fixture.Session.RenderFrame();

        Assert.True(view.Snapshot.Width < initialLayoutWidth);
        Assert.Same(firstLinkTarget, view.Content.LinkTargets[0]);
    }

    [Fact]
    public async Task InteractiveSessionPrefetchesLookaheadTileInBackground()
    {
        var renderer = new TileCountingRenderer();
        var dispatcher = new QueuedCallbackDispatcher();
        using var view = new ScrollableHtmlView(TallDocument, () => renderer, _ => { });
        var host = new Host { Width = 600, Height = 850 };
        using var session = new StandardUiSessionBuilder()
            .WithDispatcher(dispatcher)
            .Build(host);
        StandardControlPaint.SetSessionTheme(session, StandardThemeTokens.Light);
        StandardThemeController.ApplyToSubtree(view, StandardThemeTokens.Light);
        session.AddRoot(view);
        session.RenderFrame();

        // Viewport height 850 leaves tile 1 within the lookahead window (850 + 512 > 1024) without being visible yet.
        Assert.True(view.Content.IsTileCached(0));
        await dispatcher.Signal.WaitAsync(TimeSpan.FromSeconds(10));
        dispatcher.Drain();
        Assert.True(view.Content.IsTileCached(1));
    }

    [Fact]
    public void BackgroundTilesDiscardStaleResultsAndSelectionReusesTheBitmap()
    {
        var renderer = new TileCountingRenderer();
        var dispatcher = new StandardQueuedUiDispatcher();
        using var view = new ScrollableHtmlView("<p>Earlier message</p>", () => renderer, _ => { }, useBackgroundRaster: true);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host { Width = 600, Height = 400 });
        StandardControlPaint.SetSessionTheme(session, StandardThemeTokens.Light);
        StandardThemeController.ApplyToSubtree(view, StandardThemeTokens.Light);
        session.AddRoot(view);
        session.RenderFrame();
        Assert.Equal(0, renderer.CreatedCount);
        Assert.True(SpinWait.SpinUntil(() => dispatcher.HasPendingWork, TimeSpan.FromSeconds(10)));

        view.UpdateHtml("<p style='font:20px monospace'>Selectable start line of the new message.</p>");
        session.RenderFrame();
        dispatcher.Drain();
        Assert.Equal(0, renderer.CreatedCount);
        Assert.True(SpinWait.SpinUntil(() => dispatcher.HasPendingWork, TimeSpan.FromSeconds(10)));
        dispatcher.Drain();
        Assert.Equal(1, renderer.CreatedCount);

        var header = new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);
        session.DispatchInput(Click(20, 20, MouseButtonTransition.Down));
        session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(
            header, InputPoint.ClientDeviceIndependentPixels(160, 20), MouseButtons.Left, InputEventSource.Synthetic)));
        session.DispatchInput(Click(160, 20));
        var selected = session.RenderFrame().Commands.OfType<BRenderCommand.FillRect>();
        var highlight = BColor.FromArgb(0x60, 0x33, 0x99, 0xFF);
        Assert.Contains(selected, command => command.Color == highlight && command.Rect.Width > 0);
        Assert.Equal(1, renderer.CreatedCount);
        Assert.Equal(0, renderer.ReleasedCount);

        session.DispatchInput(Click(500, 70, MouseButtonTransition.Down));
        session.DispatchInput(Click(500, 70));
        Assert.DoesNotContain(session.RenderFrame().Commands.OfType<BRenderCommand.FillRect>(), command => command.Color == highlight);
        Assert.Equal(1, renderer.CreatedCount);
    }

    private static bool HasLinkColor(Broiler.Graphics.Imaging.BPixelBuffer pixels, BRect linkBounds, double tileTop, double scale)
    {
        int top = (int)((linkBounds.Y - tileTop) * scale), bottom = (int)((linkBounds.Bottom - tileTop) * scale);
        int left = (int)(linkBounds.X * scale), right = Math.Min(pixels.Width, (int)(linkBounds.Right * scale));
        for (int y = Math.Max(0, top); y < Math.Min(pixels.Height, bottom); y++)
            for (int x = left; x < right; x++)
            {
                int i = ((y * pixels.Width) + x) * 4;
                if (pixels.Rgba[i + 3] > 200 && pixels.Rgba[i + 2] > 150 && pixels.Rgba[i] < 120) return true;
            }
        return false;
    }

    private static UiInputEvent Click(double x, double y, MouseButtonTransition transition = MouseButtonTransition.Up,
        MouseButton button = MouseButton.Left) => UiInputEvent.FromMouseButton(new MouseButtonEvent(
        new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
        InputPoint.ClientDeviceIndependentPixels(x, y), MouseButtons.None, button, transition, InputEventSource.Synthetic));

    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));

    private sealed class Fixture : IDisposable
    {
        private readonly Host _host;
        private readonly UiElement _root;

        public Fixture(UiElement root, int width = 600, int height = 400)
        {
            _root = root;
            _host = new Host { Width = width, Height = height };
            Session = new StandardUiSessionBuilder().WithDispatcher(new ImmediateUiDispatcher()).Build(_host);
            // Light at 100 %, whatever the process-wide palette is: at a large text size, the status line
            // in the reflow test fills the whole window and leaves the document no viewport.
            StandardControlPaint.SetSessionTheme(Session, StandardThemeTokens.Light);
            StandardThemeController.ApplyToSubtree(root, StandardThemeTokens.Light);
            Session.AddRoot(root);
            Session.RenderFrame();
        }

        public UiSession Session { get; }
        public string Clipboard => _host.Clipboard;

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

    private sealed class Host : IUiHost, IUiClipboardHost
    {
        public string Clipboard { get; private set; } = "";
        public bool TryGetText(out string text) { text = Clipboard; return true; }
        public void SetText(string text) => Clipboard = text;
        public int Width { get; set; }
        public int Height { get; set; }
        public BSize ViewportSize => new(Width, Height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    private sealed class TileCountingRenderer : IBroilerRenderer
    {
        private uint _nextId = 1;
        public int CreatedCount { get; private set; }
        public int ReleasedCount { get; private set; }
        public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage) => Create(100, 100);
        public BImageHandle CreateImage(BPixelBuffer pixels) => Create(pixels.Width, pixels.Height);
        public void ReleaseImage(BImageHandle image)
        {
            if (image.IsValid) ReleasedCount++;
        }
        public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor) => throw new NotImplementedException();
        public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext) { }
        public Broiler.Graphics.Imaging.BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext) => throw new NotImplementedException();
        public void Dispose() { }
        private BImageHandle Create(int width, int height)
        {
            CreatedCount++;
            return new BImageHandle(new BResourceHandle(BResourceKind.Image, _nextId++), new BSize(width, height));
        }
    }

    private sealed class QueuedCallbackDispatcher : IUiDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();
        private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Signal => _signal.Task;
        public bool CheckAccess() => true;
        public void Post(Action action)
        {
            _queue.Enqueue(action);
            _signal.TrySetResult();
        }
        public void Drain()
        {
            while (_queue.TryDequeue(out var action))
                action();
        }
    }
}
