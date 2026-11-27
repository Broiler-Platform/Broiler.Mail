using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-04: toolbars wrap predictably: reading order kept, every action whole and inside the window.</summary>
[Collection("UI theme")]
public sealed class ToolbarWrapTests
{
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 2.0)]
    public void EveryTabsToolbarsWrapInReadingOrderWithWholeActions(double width, double height, double textScale)
    {
        using var directory = new TestDirectory();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var shell = new MailShellView(model);
            using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
            shell.Attach(session);
            int checkedToolbars = 0, wrapped = 0;
            foreach (string tab in new[] { "inbox", "compose", "account", "settings" })
            {
                shell.ShowView(tab);
                session.RenderFrame();
                session.RenderFrame();
                foreach (var toolbar in Descendants(shell.ActiveContent).OfType<StandardToolbar>().Where(Shown))
                {
                    checkedToolbars++;
                    wrapped += AssertWrapsInReadingOrder(toolbar, $"{tab} toolbar {toolbar.AccessibleName ?? toolbar.GetType().Name} at {width}x{height}, text {textScale:P0}", width);
                }
            }
            Assert.True(checkedToolbars >= 3, $"Only {checkedToolbars} toolbars were shown.");
            if (width == 640 && textScale == 2)
                Assert.True(wrapped > 0, "At the minimum size with doubled text some toolbar must wrap.");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// The rows UX-M1 made inline are collapsed until used, so the tab walk above never sees them: the reader's
    /// command strip, the remote-images banner and the find bar below the line, and the search header with its
    /// results strip. Opened, each wraps as every toolbar does, and Tab visits its controls in reading order.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    public async Task InlineReaderAndSearchRowsWrapInReadingOrderAndTabFollowsIt(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var app = await InlineMailFixture.OpenAsync(width, height);
            string at = $"{width}x{height}, text {textScale:P0}";
            await app.OpenMessageAsync(0);
            // The HTML reader shows the remote-images banner; Find searches the text reader, where the banner is hidden.
            var strip = Assert.IsType<StandardToolbar>(app.Button("Reply").Parent);
            Assert.True(Shown(strip), $"{at}: the command strip is hidden.");
            AssertWrapsInReadingOrder(strip, $"command strip at {at}", width);
            var banner = Descendants(app.Banner).OfType<StandardToolbar>().Where(Shown).ToArray();
            Assert.NotEmpty(banner);
            foreach (var row in banner) AssertWrapsWholeAboveTheMinimumSize(app, row, $"remote images row at {at}", width, textScale);
            AssertTabOrder(app, app.Banner, $"remote images banner at {at}");

            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('F', KeyboardModifierState.Control)));
            app.Render();
            var find = Descendants(app.Find).OfType<StandardToolbar>().Where(Shown).ToArray();
            Assert.NotEmpty(find);
            foreach (var row in find) AssertWrapsWholeAboveTheMinimumSize(app, row, $"find bar at {at}", width, textScale);
            AssertTabOrder(app, app.Find, $"find bar at {at}");

            app.Session.SetFocus(app.FindQuery);
            app.Press(InlineMailFixture.Key(0x1B));
            app.Render();
            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('K', KeyboardModifierState.Control)));
            app.Render();
            app.Query.Text = "needle";
            app.Press(InlineMailFixture.Key(0x0D));
            app.Drain();
            Assert.True(app.Inbox.IsSearchActive);
            var header = Descendants(app.Content).OfType<FirstTakesRestStack>().Single(stack => app.Query.IsDescendantOf(stack));
            var rows = Descendants(header).OfType<StandardToolbar>().Where(Shown).ToArray();
            Assert.Contains(app.Strip, rows);
            Assert.True(rows.Length >= 2, $"{at}: only {rows.Length} search rows are shown.");
            foreach (var row in rows) AssertWrapsWholeAboveTheMinimumSize(app, row, $"search row {row.AccessibleName} at {at}", width, textScale);
            AssertTabOrder(app, app.Strip, $"results strip at {at}");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// Learn more opens the privacy paragraph below the remote-images row, and a search with no match shows the long
    /// feedback below the find bar. Both stay in the reader's banner slot, which scrolls rather than take the message's
    /// lines: the body keeps six lines where the reader has room for them below a header at its share, and otherwise
    /// its share of what the slot leaves; and every control, the paragraph and the feedback can be scrolled into view.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    public async Task LearnMoreAndFindFeedbackScrollInTheBannerSlotAndLeaveTheTextItsLines(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var app = await InlineMailFixture.OpenAsync(width, height);
            string at = $"{width}x{height}, text {textScale:P0}";
            await app.OpenMessageAsync(0);
            app.Click("Learn more");
            var details = Descendants(app.Banner).OfType<Broiler.UI.Label.Standard.StandardLabel>().Single(label => label.Text == Broiler.Mail.Application.Preview.RemoteImageActions.PrivacyDetails);
            app.AssertReachable(details, $"privacy paragraph at {at}");
            foreach (var control in Descendants(app.Banner).Where(element => element.CanFocus).ToArray())
                app.AssertReachable(control, $"remote images {Name(control)} at {at}");
            AssertBodyKeepsItsLines(app, app.Surface, $"Learn more at {at}");

            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('F', KeyboardModifierState.Control)));
            app.Render();
            app.FindQuery.Text = "absent";
            app.Press(InlineMailFixture.Key(0x0D));
            app.Render();
            Assert.Contains("No matches", app.Find.Status);
            var feedback = Descendants(app.Find).OfType<Broiler.UI.Forms.Standard.InlineFeedback>().Single();
            app.AssertReachable(feedback, $"find feedback at {at}");
            foreach (var control in Descendants(app.Find).Where(element => element.CanFocus).ToArray())
                app.AssertReachable(control, $"find {Name(control)} at {at}");
            AssertBodyKeepsItsLines(app, app.Text, $"find feedback at {at}");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// UX-03.3: the attachment strip is the first of the reader's notices. With 32 attachments shown in full, the
    /// remote-images banner and then the find bar, its rows wrap whole in reading order, every row and control can be
    /// scrolled into view in the banner slot, Tab follows the strip's order, and the message keeps its lines.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    public async Task StripWrapsWholeItemsAndKeepsSixTextLines(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var app = await InlineMailFixture.OpenAsync(width, height, attachments: 32, files: new FakeMailFilePicker());
            string at = $"{width}x{height}, text {textScale:P0}";
            await app.OpenMessageAsync(0);
            var strip = app.Attachments;
            Assert.Equal(32, strip.Attachments.Count);
            Assert.True(app.Shown(app.Banner), $"{at}: the remote images banner is hidden.");
            app.Click("Show all 32 attachments");
            var rows = Descendants(strip).OfType<StandardToolbar>().Where(Shown).ToArray();
            Assert.True(rows.Length >= 33, $"{at}: only {rows.Length} strip rows are shown.");
            foreach (var row in rows) AssertWrapsWholeAboveTheMinimumSize(app, row, $"attachment row {row.AccessibleName} at {at}", width, textScale);
            AssertBodyKeepsItsLines(app, app.Surface, $"attachments and remote images at {at}");
            AssertTabOrder(app, strip, $"attachment strip at {at}");

            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('F', KeyboardModifierState.Control)));
            app.Render();
            Assert.True(app.Shown(app.Find), $"{at}: the find bar is hidden.");
            app.AssertReachable(app.FindQuery, $"find query below 32 attachments at {at}");
            app.AssertReachable(Descendants(strip).OfType<Broiler.UI.Button.Standard.StandardButton>().Last(button => button.Text == "Save as…"), $"last Save as… at {at}");
            AssertBodyKeepsItsLines(app, app.Text, $"attachments and find at {at}");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// Below the commands and the line, the banner slot leaves the header and the body six lines below a header at
    /// its 45 % share, or else takes at most 35 % of that height; so the body keeps six lines, or its share of that.
    /// </summary>
    private static void AssertBodyKeepsItsLines(InlineMailFixture app, UiElement body, string where)
    {
        app.Render();
        double below = app.Header.AvailableHeight + app.Notices.Bounds.Height;
        double kept = Math.Min(app.Text.HeightOfLines(6), (1 - 0.45) * (1 - 0.35) * below);
        Assert.True(body.Bounds.Height >= kept - 0.5, $"{where}: the body keeps {body.Bounds.Height}, less than {kept}, below a banner slot of {app.Notices.Bounds.Height}.");
        Assert.True(app.Notices.Bounds.Bottom <= body.Bounds.Top + 0.5, $"{where}: the banner slot at {app.Notices.Bounds} covers the body at {body.Bounds}.");
    }

    /// <summary>Asserts every shown item is whole, inside the toolbar and after the one before it; returns how many times the row wrapped.</summary>
    private static int AssertWrapsInReadingOrder(StandardToolbar toolbar, string where, double width)
    {
        int wrapped = 0;
        Assert.True(toolbar.Bounds.Right <= width + 0.5, $"{where} ends at {toolbar.Bounds.Right}, past the window.");
        UiElement[] items = toolbar.Children.Where(child => child.Visibility == UiVisibility.Visible && child.Bounds.Width > 0).ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            BRect item = items[i].Bounds;
            Assert.True(Inside(item, toolbar.Bounds), $"{where}: item {i} at {item} is outside {toolbar.Bounds}, which wanted {toolbar.DesiredSize}.");
            Assert.True(item.Width >= items[i].DesiredSize.Width - 0.5, $"{where}: item {i} is cut to {item.Width} of {items[i].DesiredSize.Width}.");
            if (i == 0) continue;
            BRect previous = items[i - 1].Bounds;
            bool sameRow = Math.Min(item.Bottom, previous.Bottom) > Math.Max(item.Top, previous.Top) && item.Left >= previous.Right - 0.5;
            bool nextRow = item.Top >= previous.Bottom - 0.5;
            Assert.True(sameRow || nextRow, $"{where}: item {i} at {item} breaks reading order after {previous}.");
            if (!sameRow) wrapped++;
        }
        return wrapped;
    }

    /// <summary>From the row's first control, Tab visits each of its shown controls once, in the order they are laid out.</summary>
    private static void AssertTabOrder(InlineMailFixture app, UiElement row, string where)
    {
        UiElement[] expected = Descendants(row).Where(element => element != row && element.CanFocus && Shown(element)).ToArray();
        Assert.True(expected.Length >= 2, $"{where}: only {expected.Length} controls can take focus.");
        app.Session.SetFocus(expected[0]);
        var visited = new List<UiElement> { expected[0] };
        for (int i = 1; i < expected.Length; i++)
        {
            app.Press(InlineMailFixture.Key(0x09));
            Assert.NotNull(app.Session.FocusedElement);
            visited.Add(app.Session.FocusedElement!);
        }
        Assert.True(expected.SequenceEqual(visited), $"{where}: Tab visits {string.Join(", ", visited.Select(Name))}, not {string.Join(", ", expected.Select(Name))}.");
    }

    /// <summary>
    /// An inline row wraps whole, in reading order, at every size, and each of its controls can be reached: at 640x480
    /// with 200 % text the reader's banner slot is shorter than a banner or find bar that wraps to a second row (Load
    /// images and Learn more, Copy match and Close), so the slot scrolls, as the search header's results strip does in
    /// a list pane too short for it; a control scrolled into view is inside the window, clipped by nothing above it,
    /// and whole unless it is taller than the scroll view, which then shows as much of it as it can.
    /// </summary>
    private static void AssertWrapsWholeAboveTheMinimumSize(InlineMailFixture app, StandardToolbar row, string where, double width, double textScale)
    {
        app.Render();
        double whole = row.Measure(new BSize(row.Bounds.Width, double.PositiveInfinity)).Height;
        app.Render();
        Assert.True(whole <= row.Bounds.Height + 0.5, $"{where} is {row.Bounds.Height} high of the {whole} it needs, at text {textScale:P0}.");
        AssertWrapsInReadingOrder(row, where, width);
        foreach (var item in row.Children.Where(child => child.Visibility == UiVisibility.Visible).ToArray())
            app.AssertReachable(item, $"{where}: {Name(item)}");
    }

    private static string Name(UiElement element) => element.AccessibleName ?? (element as Broiler.UI.Button.Standard.StandardButton)?.Text ?? element.GetType().Name;

    private static bool Inside(BRect inner, BRect outer) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 && inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static bool Shown(UiElement element)
    {
        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0) return false;
        for (UiElement? current = element; current is not null; current = current.Parent)
            if (current.Visibility != UiVisibility.Visible) return false;
        return true;
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Host(double width, double height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
