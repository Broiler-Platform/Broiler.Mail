using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
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

    [Fact]
    public async Task MessageActionsAreInlineAndTheFileMenuOffersNoMessageActionsDialog()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        var shell = fixture.Shell;
        var session = fixture.Session;
        Render(session);
        var inbox = shell.GetContent("inbox");
        var file = shell.Menu.Items[0];
        Assert.Equal("File", file.Text);
        // The File menu keeps the mailbox's own dialogs, but none for the open message.
        var group = file.Children.Single(item => item.Id == "mailbox-tools");
        Assert.DoesNotContain(group.Children, item => item.Id is "message-actions" or "find-message");
        Assert.False(shell.ShowView("message-actions"));
        Assert.DoesNotContain(Descendants(inbox), element => element is Broiler.UI.Button.Standard.StandardButton { Text: "Find text" });

        // More message actions opens a popup menu anchored below its button, not a dialog.
        var more = ReaderMenus.Button(inbox, "More message actions");
        more.Trigger.Click();
        Assert.True(more.IsExpanded);
        Assert.IsType<Broiler.UI.Menu.Standard.StandardMenu>(session.CapturedElement);
        Assert.Null(shell.ActiveDialog);
        Assert.Null(session.ModalElement);
        Assert.Equal("inbox", shell.ActiveViewId);
        more.Trigger.Click();
        Assert.False(more.IsExpanded);
        Assert.Same(inbox, shell.ActiveContent);
    }

    [Fact]
    public async Task OriginalMessageExportIsInlineAndReplacedWithTheBody()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720, withSourceExports: true);
        var (shell, session, model) = (fixture.Shell, fixture.Session, fixture.Model);
        Render(session);
        var inbox = shell.GetContent("inbox");
        Assert.False(shell.ShowView("message-actions"));
        var first = Descendants(inbox).OfType<Broiler.Mail.Application.Preview.MessageSourceExportActions>().Single();
        var header = Descendants(inbox).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
        Assert.True(first.IsDescendantOf(header));
        var innerScrolls = Descendants(first).OfType<UiScrollView>().ToArray();

        // More message actions opens the reader's own .eml section and focuses it, with no dialog.
        ReaderMenus.Invoke(session, inbox, "More message actions", "Original message (.eml)…");
        Render(session);
        Assert.Null(shell.ActiveDialog);
        Assert.Equal("inbox", shell.ActiveViewId);
        Assert.True(session.FocusedElement?.IsDescendantOf(first), $"Focus is on {session.FocusedElement}.");

        // Selecting another message replaces the section and disposes the old one's controls.
        await model.Inbox.SelectAsync(model.Inbox.Messages[1].Key);
        ((TestQueueDispatcher)session.Dispatcher).DrainUntil(() => !model.Inbox.IsBusy);
        Render(session);
        Assert.True(first.IsDisposed);
        Assert.All(innerScrolls, scroll => Assert.True(scroll.IsDisposed));
        Assert.True(session.FocusedElement is null || !session.FocusedElement.IsDisposed);
        var second = Descendants(inbox).OfType<Broiler.Mail.Application.Preview.MessageSourceExportActions>().Single();
        ReaderMenus.Invoke(session, inbox, "More message actions", "Original message (.eml)…");
        Render(session);
        Assert.Null(shell.ActiveDialog);
        Assert.True(session.FocusedElement?.IsDescendantOf(second), $"Focus is on {session.FocusedElement}.");
    }

    [Fact]
    public async Task FileMenuOffersOpenAndSaveEmlWithAcceleratorsAndEnablement()
    {
        using (var fixture = await Fixture.OpenAsync(1100, 720, withSourceExports: true))
        {
            var (shell, session, model) = (fixture.Shell, fixture.Session, fixture.Model);
            Render(session);
            var file = shell.Menu.Items[0];
            var ids = file.Children.Select(item => item.Id).ToList();
            // Right after New message, and not in the Mailbox and message group.
            Assert.Equal(new[] { "inbox", "new", "open-eml", "save-eml", "compose", "receive" }, ids.Take(6).ToArray());
            Assert.DoesNotContain(file.Children.Single(item => item.Id == "mailbox-tools").Children, item => item.Id is "open-eml" or "save-eml");
            var open = file.Children.Single(item => item.Id == "open-eml");
            var save = file.Children.Single(item => item.Id == "save-eml");
            Assert.Equal("Open .eml file…", open.Text);
            Assert.Equal("Ctrl+O", open.Accelerator);
            Assert.Equal("Save message as .eml…", save.Text);
            Assert.Equal("Ctrl+Shift+S", save.Accelerator);
            Assert.True(open.IsEnabled);
            Assert.True(save.IsEnabled);
            // New and Receive keep their own enablement, now found by reference instead of position.
            Assert.Equal(model.Compose.CanCompose, file.Children.Single(item => item.Id == "new").IsEnabled);
            Assert.Equal(model.Inbox.CanReceive, file.Children.Single(item => item.Id == "receive").IsEnabled);
        }
        using (var fixture = await Fixture.OpenAsync(1100, 720))
        {
            // Without source exports neither is available; New and Receive still are.
            var file = fixture.Shell.Menu.Items[0];
            Assert.False(file.Children.Single(item => item.Id == "open-eml").IsEnabled);
            Assert.False(file.Children.Single(item => item.Id == "save-eml").IsEnabled);
            Assert.Equal(fixture.Model.Compose.CanCompose, file.Children.Single(item => item.Id == "new").IsEnabled);
            Assert.Equal(fixture.Model.Inbox.CanReceive, file.Children.Single(item => item.Id == "receive").IsEnabled);
        }
        using (var fixture = await Fixture.OpenAsync(1100, 720, withSourceExports: true, files: UnavailableMailFilePicker.NotOnThisDeviceYet))
        {
            // Without a file picker (Android until UX-M5) both are unavailable.
            var file = fixture.Shell.Menu.Items[0];
            Assert.False(file.Children.Single(item => item.Id == "open-eml").IsEnabled);
            Assert.False(file.Children.Single(item => item.Id == "save-eml").IsEnabled);
        }
    }

    [Fact]
    public async Task MoreActionsMenuOffersSaveAsEml()
    {
        var files = new FakeMailFilePicker();
        using var fixture = await Fixture.OpenAsync(1100, 720, withSourceExports: true, files: files);
        var (shell, session, model) = (fixture.Shell, fixture.Session, fixture.Model);
        Render(session);
        var inbox = shell.GetContent("inbox");
        var items = ReaderMenus.Items(inbox, "More message actions");
        var saveIndex = items.ToList().FindIndex(item => item.Id == "save-eml");
        Assert.True(saveIndex >= 0);
        Assert.Equal("Save as .eml…", items[saveIndex].Text);
        Assert.Equal("Ctrl+Shift+S", items[saveIndex].Accelerator);
        Assert.True(items[saveIndex].IsEnabled);
        Assert.Equal("source", items[saveIndex + 1].Id);

        Directory.CreateDirectory(fixture.Root);
        string target = Path.Combine(fixture.Root, "chosen.eml");
        files.WillSave(target);
        ReaderMenus.Invoke(session, inbox, "More message actions", "Save as .eml…");
        ((TestQueueDispatcher)session.Dispatcher).DrainUntil(() => File.Exists(target) && shell.Inbox.CanSaveMessageAsEml);
        Render(session);
        Assert.Equal("Subject: Test\r\n\r\nBody"u8.ToArray(), File.ReadAllBytes(target));
        Assert.Equal(Broiler.Mail.Core.Services.MailFilePurpose.SaveMessage, Assert.Single(files.Requests).Request.Purpose);
        // Saved inline, with no dialog of Mail's own, and the section says what happened.
        Assert.Null(shell.ActiveDialog);
        Assert.Equal("inbox", shell.ActiveViewId);
        var actions = Descendants(inbox).OfType<Broiler.Mail.Application.Preview.MessageSourceExportActions>().Single();
        Assert.Equal("Message saved as chosen.eml. The file was not opened.", actions.Status);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-exports")));
        Assert.NotNull(model.Inbox.Body);
    }

    [Fact]
    public async Task FindInMessageFromMoreMessageActionsFocusesTheInlineQuery()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        var (shell, session) = (fixture.Shell, fixture.Session);
        Render(session);
        var inbox = shell.GetContent("inbox");
        var more = ReaderMenus.Button(inbox, "More message actions");
        var item = ReaderMenus.Items(inbox, "More message actions").Single(entry => entry.Id == "find");
        Assert.Equal("Find in message", item.Text);
        Assert.Equal("Ctrl+F", item.Accelerator);

        ReaderMenus.Invoke(session, inbox, "More message actions", "Find in message");
        Render(session);

        // The menu gave focus back to its button before the posted command focused the query.
        Assert.Null(shell.ActiveDialog);
        Assert.Equal("inbox", shell.ActiveViewId);
        var find = Descendants(inbox).OfType<Broiler.Mail.Application.Preview.MessageTextFindBar>().Single();
        Assert.Equal(UiVisibility.Visible, find.Visibility);
        Assert.Same(Descendants(find).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(edit => edit.AccessibleName == "Find in message text"),
            session.FocusedElement);
        Assert.NotSame(more.Trigger, session.FocusedElement);
        Assert.False(shell.ShowView("find-message"));
    }

    [Fact]
    public async Task MessageMenuFindInMessageOpensTheInlineFindBar()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        var (shell, session, model) = (fixture.Shell, fixture.Session, fixture.Model);
        Render(session);
        // The File menu no longer offers Find as a dialog; the Message menu offers it inline.
        var file = shell.Menu.Items[0];
        Assert.DoesNotContain(file.Children.Single(item => item.Id == "mailbox-tools").Children, item => item.Id is "find-message");
        int messageIndex = shell.Menu.Items.ToList().FindIndex(item => item.Id == "message");
        var message = shell.Menu.Items[messageIndex];
        int findIndex = message.Children.ToList().FindIndex(item => item.Id == "find-message");
        Assert.True(findIndex >= 0);
        Assert.True(message.Children[findIndex].IsEnabled);

        var keyboard = shell.CreateKeyboardNavigation(session);
        keyboard.Execute(MailCommand.OpenMenu);
        Assert.True(shell.Menu.SetSelectedPath([messageIndex, findIndex]));
        Assert.True(session.DispatchInput(UiInputEvent.FromKeyboardKey(new Broiler.Input.Keyboard.KeyboardKeyEvent(
            new Broiler.Input.InputEventHeader(Broiler.Input.InputDeviceId.FromOpaqueValue("message-menu-test"),
                new Broiler.Input.InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            Broiler.Input.Keyboard.KeyboardKey.FromName("Enter"), Broiler.Input.Keyboard.KeyboardKeyTransition.Down,
            Broiler.Input.Keyboard.KeyboardModifierState.None, 0x0D, 0, 1, false, false,
            Source: Broiler.Input.InputEventSource.Synthetic))));
        ((TestQueueDispatcher)session.Dispatcher).Drain();
        Render(session);

        // Not Settings, the menu's fallback, and not a dialog.
        Assert.Null(shell.ActiveDialog);
        Assert.Equal("inbox", shell.ActiveViewId);
        var find = Descendants(shell.GetContent("inbox")).OfType<Broiler.Mail.Application.Preview.MessageTextFindBar>().Single();
        Assert.Equal(UiVisibility.Visible, find.Visibility);
        Assert.Same(Descendants(find).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(edit => edit.AccessibleName == "Find in message text"),
            session.FocusedElement);
        Assert.NotNull(model.Inbox.Body);
    }

    [Theory]
    [InlineData(360, 640, 1.0)]
    [InlineData(412, 400, 1.0)]
    [InlineData(360, 640, 1.3)]
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
    private sealed class Fixture(MailShellView shell, MailShellViewModel model, UiSession session, Host host, TestDirectory directory) : IDisposable
    {
        public MailShellView Shell { get; } = shell;
        public MailShellViewModel Model { get; } = model;
        public UiSession Session { get; } = session;
        public Host Host { get; } = host;
        public string Root => directory.Root;

        public static async Task<Fixture> OpenAsync(int width, int height, bool withSourceExports = false, IMailFilePicker? files = null)
        {
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            // A long subject fills the reader header, so its height cap matters.
            string subject = string.Join(" ", Enumerable.Repeat("Agenda, travel arrangements, and the revised budget", 6));
            var messages = Enumerable.Range(1, 30).Reverse().Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = $"sender{uid}@example.test", Subject = uid == 30 ? subject : $"Subject {uid}",
            }).ToArray();
            IMailReceiver receiver = withSourceExports
                ? new SourceCapableReceiver(messages)
                : new TestMailReceiver
                {
                    Inbox = (_, _) => Task.FromResult(new MailInboxPage(messages, null)),
                    Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Repeat("A paragraph of the message body.", 20)))),
                };
            var dispatcher = new TestQueueDispatcher();
            var cache = withSourceExports ? new SqliteMailCache(directory.File("mail.db")) : null;
            var exports = withSourceExports ? new FileMessageSourceExports(directory.Root) : null;
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher, cache, sourceExports: exports),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model, files: files);
            var host = new Host(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            shell.Attach(session);
            Assert.True(model.Composer.StartNew());
            await model.Inbox.CacheLoadTask;
            dispatcher.Drain();
            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            await model.Inbox.SelectAsync(messages[0].Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            return new(shell, model, session, host, directory);
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            directory.Dispose();
        }
    }

    private sealed class SourceCapableReceiver(MailMessageSummary[] messages) : IMailReceiver, IMailSourceReceiver
    {
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailInboxPage(messages, null));

        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Repeat("A paragraph of the message body.", 20))));

        public Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailMessageSource(key, "Subject: Test\r\n\r\nBody"u8.ToArray()));

        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
