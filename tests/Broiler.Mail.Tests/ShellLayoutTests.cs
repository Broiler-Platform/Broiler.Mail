using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.ScrollView;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>Workspace/dialog layout after resize, and preserved scroll state when reopening forms.</summary>
[Collection("UI theme")]
public sealed class ShellLayoutTests
{
    private static readonly string[] ViewIds = ["inbox", "compose", "account", "settings"];

    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    public async Task EveryViewIsMeasuredAtItsArrangedSize(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var fixture = await Fixture.OpenAsync(width, height);
            var (shell, session, host) = (fixture.Shell, fixture.Session, fixture.Host);

            foreach (string tab in ViewIds)
            {
                shell.ShowView(tab);
                Render(session);
                AssertLaidOutAtItsRectangle(shell, $"{tab} at {width}x{height}, text {textScale:P0}");

                // Both width-only and height-only resizes must reach the open form.
                foreach ((int w, int h) in new[] { (width, height + 240), (width + 200, height + 240), (width + 200, height), (width, height) })
                {
                    host.Width = w;
                    host.Height = h;
                    shell.Window.InvalidateMeasure();
                    Render(session);
                    AssertLaidOutAtItsRectangle(shell, $"{tab} after resizing to {w}x{h}, text {textScale:P0}");
                }
            }
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// Reopening a form must preserve its scroll positions, including the independently scrolling
    /// status area in a small window with enlarged text.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    public async Task ReopeningAViewPreservesItsLayoutAndScrollOffsets(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var fixture = await Fixture.OpenAsync(width, height);
            var (shell, session) = (fixture.Shell, fixture.Session);
            int scrolledForms = 0;

            foreach (string tab in ViewIds)
            {
                shell.ShowView(tab);
                Render(session);
                var content = shell.ActiveContent;
                // Part way down everything shown that scrolls, so a reset or a clamp on the way back shows.
                var scrolled = Shown(content).OfType<UiScrollView>()
                    .Where(scroll => scroll.ExtentSize.Height - scroll.ViewportSize.Height > 2).ToArray();
                foreach (var scroll in scrolled)
                {
                    scroll.SetOffset(new BPoint(0, Math.Round((scroll.ExtentSize.Height - scroll.ViewportSize.Height) / 2)));
                }
                Render(session);
                var offsets = scrolled.Select(scroll => scroll.Offset).ToArray();
                var layout = ShownLayout(content);
                if (tab is "compose" or "account" or "settings" && scrolled.Length > 0) scrolledForms++;

                shell.ShowView(tab == "inbox" ? "settings" : "inbox");
                Render(session);
                shell.ShowView(tab);
                Render(session);

                string where = $"{tab} at {width}x{height}, text {textScale:P0}";
                for (int index = 0; index < scrolled.Length; index++)
                    Assert.True(offsets[index] == scrolled[index].Offset,
                        $"{where}: {Describe(scrolled[index])} was scrolled to {offsets[index]}, but is at {scrolled[index].Offset} after another tab was shown.");
                var after = ShownLayout(content);
                Assert.Equal(layout.Length, after.Length);
                for (int index = 0; index < layout.Length; index++)
                    Assert.True(layout[index] == after[index], $"{where}: {layout[index].Element} was at {layout[index].Bounds}, but at {after[index].Bounds} after another tab was shown.");
            }
            // A form is taller than the window at these sizes, so a scroll position was really checked.
            Assert.NotEqual(0, scrolledForms);
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// The footer's text starts where the tab names do and stays clear of the window's bottom edge. At
    /// the minimum size with doubled text it wraps inside that inset and is still shown whole.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    public async Task TheFooterIsInsetFromTheWindowsEdgesAndShownWhole(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var fixture = await Fixture.OpenAsync(width, height);
            var shell = fixture.Shell;
            Render(fixture.Session);
            var footer = shell.Footer;
            BRect area = shell.Window.ChromeLayout.Content;
            BRect text = footer.Bounds;
            string where = $"at {width}x{height}, text {textScale:P0}: the footer is at {text} in {area}";

            // The tab names start this far into the tab view, which spans the window.
            Assert.Equal(area.Left, shell.NavigationFocus.Bounds.Left, 0.5);
            Assert.Equal(area.Left + 12, text.Left, 0.5);
            Assert.Equal(area.Right - 12, text.Right, 0.5);
            Assert.Equal(area.Bottom - MailShellView.FooterPadding, text.Bottom, 0.5);
            Assert.True(shell.NavigationFocus.Bounds.Bottom <= text.Top - MailShellView.FooterPadding + 0.5, where);
            // Shown whole: the text has the height it asked for at that width.
            Assert.True(text.Height >= footer.DesiredSize.Height - 0.5, where);
            if (width == 640)
                Assert.True(BTextMeasurer.MeasureAdvance(footer.Text, footer.Font) > text.Width, $"{where}; '{footer.Text}' does not wrap.");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    private static void Render(UiSession session)
    {
        session.RenderFrame();
        session.RenderFrame();
    }

    private static void AssertLaidOutAtItsRectangle(MailShellView shell, string where)
    {
        Assert.True(shell.Menu.MenuBarHeight >= BTextMeasurer.GetLineHeight(shell.Menu.Font) + 8);
        if (shell.ActiveDialog is { } dialog)
        {
            var owner = shell.Window.ChromeLayout.Content;
            Assert.InRange(dialog.Bounds.Left, owner.Left, owner.Right);
            Assert.InRange(dialog.Bounds.Right, dialog.Bounds.Left, owner.Right);
            Assert.InRange(dialog.Bounds.Top, owner.Top, owner.Bottom);
            Assert.InRange(dialog.Bounds.Bottom, dialog.Bounds.Top, owner.Bottom);
        }
        var content = shell.ActiveContent;
        BRect area = shell.ActiveDialog?.ChromeLayout.Content ?? shell.Window.ChromeLayout.Content;
        BRect bounds = content.Bounds;
        // The content fills the tab view below its headers.
        Assert.True(bounds.Height > 0, $"{where}: the content was not arranged.");
        Assert.Equal(area.Left, bounds.Left, 0.5);
        Assert.Equal(area.Right, bounds.Right, 0.5);
        Assert.InRange(bounds.Bottom, bounds.Top, area.Bottom);
        Assert.InRange(bounds.Top, area.Top, area.Top + (area.Height / 2));

        // Measuring again at the arranged size changes nothing: the content was already measured at the
        // size it was given.
        var before = Layout(content);
        content.InvalidateMeasure();
        content.Measure(bounds.Size);
        content.Arrange(bounds);
        var after = Layout(content);
        Assert.Equal(before.Length, after.Length);
        for (int index = 0; index < before.Length; index++)
            Assert.True(before[index] == after[index], $"{where}: {before[index].Element} was at {before[index].Bounds}, but {after[index].Bounds} once measured at the arranged size.");

        // The height caps hold against the height the content was given: for the reader's header, the
        // height its commands below leave. An area passes its share only to show its whole content or to
        // end below a row of buttons and the gap after it, and the rows it shows leave the rest its minimum
        // (the margin below the last row and that gap are not rows), or, if it keeps lines whole, to show
        // its first line.
        foreach (var capped in Descendants(content).OfType<BoundedScrollArea>().Where(element => element.Bounds.Height > 0))
        {
            double available = capped.AvailableHeight;
            double share = available * capped.MaximumFraction;
            string what = $"{where}: {capped.Scroll.AccessibleName} is {capped.Bounds.Height} of {available}";
            if (capped.Bounds.Height <= share + 1) continue;
            var rows = capped.Rows?.Invoke().ToArray() ?? [];
            Assert.True(rows.Length > 0 && capped.MinimumRemaining is not null, $"{what}, past its share without rows or a minimum.");
            bool whole = !capped.Scroll.HasVerticalScrollbar;
            if (capped.KeepsLinesWhole && rows[0].LineHeight > 0 && Math.Abs(rows[0].Start + rows[0].LineHeight - capped.Bounds.Height) < 0.5) continue;
            // Below a row of buttons, and the gap between it and the next row.
            var buttons = rows.Where((row, index) => row.Grows
                && Math.Abs((index + 1 < rows.Length ? rows[index + 1].Start : row.End) - capped.Bounds.Height) < 0.5).ToArray();
            Assert.True(whole || buttons.Length > 0, $"{what}, past its share but not below a row of buttons.");
            double rowsEnd = whole ? rows[^1].End : buttons[0].End;
            Assert.True(rowsEnd <= Math.Max(share, available - capped.MinimumRemaining!()) + 0.5, $"{what}; its rows end at {rowsEnd}.");
        }
    }

    private static (string Element, BRect Bounds)[] Layout(UiElement root) =>
        Descendants(root).Select(element => (element.GetType().Name, element.Bounds)).ToArray();

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>The elements on screen: a collapsed pane, such as the reader beside a compact list, keeps stale bounds.</summary>
    private static IEnumerable<UiElement> Shown(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children.Where(child => child.Visibility == UiVisibility.Visible && !child.Bounds.IsEmpty))
            foreach (var item in Shown(child)) yield return item;
    }

    private static string Describe(UiScrollView scroll) => string.IsNullOrEmpty(scroll.AccessibleName)
        ? $"the scroll view around {scroll.Children.FirstOrDefault()?.GetType().Name}" : scroll.AccessibleName;

    private static (string Element, BRect Bounds)[] ShownLayout(UiElement root) =>
        Shown(root).Select(element => (element.GetType().Name, element.Bounds)).ToArray();

    /// <summary>The shell with a received inbox, a selected message with a long subject, and a new draft.</summary>
    private sealed class Fixture(MailShellView shell, UiSession session, Host host, TestDirectory directory) : IDisposable
    {
        public MailShellView Shell { get; } = shell;
        public UiSession Session { get; } = session;
        public Host Host { get; } = host;

        public static async Task<Fixture> OpenAsync(int width, int height)
        {
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            // A long subject fills the reader header, so its height cap matters.
            string subject = string.Join(" ", Enumerable.Repeat("Agenda, travel arrangements, and the revised budget", 6));
            var messages = Enumerable.Range(1, 30).Reverse().Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = $"sender{uid}@example.test", Subject = uid == 30 ? subject : $"Subject {uid}",
            }).ToArray();
            var receiver = new TestMailReceiver
            {
                Inbox = (_, _) => Task.FromResult(new MailInboxPage(messages, null)),
                Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Repeat("A paragraph of the message body.", 20)))),
            };
            var dispatcher = new TestQueueDispatcher();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model);
            var host = new Host(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            shell.Attach(session);
            Assert.True(model.Composer.StartNew());
            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            await model.Inbox.SelectAsync(messages[0].Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            return new(shell, session, host, directory);
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            directory.Dispose();
        }
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public int Width { get; set; } = width;
        public int Height { get; set; } = height;
        public BSize ViewportSize => new(Width, Height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
