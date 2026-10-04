using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Splitter.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-04: one pane at a time when two readable panes do not fit, without losing context.</summary>
[Collection("UI theme")]
public sealed class ResponsiveInboxTests
{
    [Fact]
    public async Task WideLayoutShowsBothPanesAndCompactLayoutStartsWithTheList()
    {
        using var fixture = await Fixture.OpenAsync(1000);
        Assert.False(fixture.Layout.IsCompact);
        Assert.True(fixture.List.Bounds.Width > 0);
        Assert.True(fixture.Reader.Bounds.Width > 0);

        fixture.Resize(500);
        Assert.True(fixture.Layout.IsCompact);
        Assert.False(fixture.Layout.ShowsReaderOnly);
        Assert.True(fixture.List.Bounds.Width >= 450);
        Assert.Equal(BRect.Empty, fixture.Reader.Bounds);
    }

    [Fact]
    public async Task ArrowKeysBrowseTheCompactListAndEnterOpensTheReader()
    {
        using var fixture = await Fixture.OpenAsync(500);
        fixture.Session.SetFocus(fixture.List);
        fixture.Key(0x28); // Down
        fixture.Key(0x28);
        fixture.Session.RenderFrame();
        Assert.Equal(fixture.Messages[1].Key, fixture.Model.SelectedMessage?.Key);
        Assert.False(fixture.Layout.ShowsReaderOnly);
        Assert.Same(fixture.List, fixture.Session.FocusedElement);

        fixture.Key(13); // Enter activates the row.
        fixture.Session.RenderFrame();
        Assert.True(fixture.Layout.ShowsReaderOnly);
        Assert.Equal(BRect.Empty, fixture.List.Bounds);
        Assert.True(fixture.BackButton.Bounds.Width > 0);
        // Focus left the hidden list for the message text.
        Assert.Same(fixture.ReaderText.Editor, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task ClickingARowOpensTheReaderAndBackRestoresTheList()
    {
        using var fixture = await Fixture.OpenAsync(500, 400);
        fixture.List.ScrollIntoView(20);
        fixture.Session.RenderFrame();
        int row = fixture.List.FirstVisibleIndex + 1;
        var point = new BPoint(fixture.List.Bounds.X + 40, fixture.List.Bounds.Y + (row - fixture.List.FirstVisibleIndex + 0.5) * fixture.List.EffectiveItemHeight);
        fixture.Click(point);
        fixture.Session.RenderFrame();
        Assert.True(fixture.Layout.ShowsReaderOnly);
        var opened = fixture.Model.SelectedMessage!.Key;
        Assert.Equal(fixture.Messages[row].Key, opened);

        fixture.BackButton.Click();
        fixture.Session.RenderFrame();
        Assert.False(fixture.Layout.ShowsReaderOnly);
        Assert.Equal(opened, fixture.Model.SelectedMessage?.Key);
        Assert.Same(fixture.List, fixture.Session.FocusedElement);
        int index = fixture.List.Items.ToList().FindIndex(item => item.Id == fixture.List.SelectedItemId);
        Assert.InRange(index, fixture.List.FirstVisibleIndex, fixture.List.FirstVisibleIndex + fixture.List.VisibleItemCount);
        Assert.Equal(BRect.Empty, fixture.Reader.Bounds);
    }

    [Fact]
    public async Task ResizingKeepsTheOpenMessageFocusAndTheWideSplitRatio()
    {
        using var fixture = await Fixture.OpenAsync(1000);
        fixture.Split.SplitterFraction = 0.42;
        fixture.Session.RenderFrame();
        double ratio = fixture.Model.SplitterFraction;
        await fixture.Model.SelectAsync(fixture.Messages[3].Key);
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        fixture.Session.SetFocus(fixture.List);
        fixture.Session.RenderFrame();

        for (int round = 0; round < 3; round++)
        {
            fixture.Resize(500);
            // A message being read side by side stays open in the compact reader.
            Assert.True(fixture.Layout.ShowsReaderOnly);
            Assert.Same(fixture.ReaderText.Editor, fixture.Session.FocusedElement);
            fixture.Resize(1000);
            Assert.False(fixture.Layout.IsCompact);
            Assert.Equal(ratio, fixture.Model.SplitterFraction, 3);
            Assert.Equal(ratio, fixture.Split.SplitterFraction, 3);
        }
        Assert.Equal(fixture.Messages[3].Key, fixture.Model.SelectedMessage?.Key);
        Assert.NotNull(fixture.Model.Body);
    }

    [Theory]
    [InlineData(700)]
    [InlineData(780)]
    public async Task BetweenTheCompactSwitchAndAWideWindowBothPanesKeepTheirReadableWidths(int width)
    {
        using var fixture = await Fixture.OpenAsync(1100);
        Assert.Equal(0.35, fixture.Split.SplitterFraction, 3);

        // At 35 %, the list would be narrower than the width the compact switch keeps readable.
        fixture.Resize(width);
        Assert.False(fixture.Layout.IsCompact);
        Assert.True(fixture.List.Bounds.Width >= AdaptiveInboxLayout.ListReadableWidth - 0.5, $"The list is {fixture.List.Bounds.Width} DIP wide.");
        Assert.True(fixture.Reader.Bounds.Width >= AdaptiveInboxLayout.ReaderReadableWidth - 0.5, $"The reader is {fixture.Reader.Bounds.Width} DIP wide.");

        // Only the display was clamped: the saved ratio is unchanged and shows again in a wider window.
        Assert.Equal(0.35, fixture.Model.SplitterFraction, 3);
        fixture.Resize(1100);
        Assert.Equal(0.35, fixture.Model.SplitterFraction, 3);
        Assert.Equal(0.35, fixture.Split.SplitterFraction, 3);
        Assert.Equal(Math.Round(0.35 * (1100 - fixture.Split.Splitter.PreferredSize.Width)), fixture.List.Bounds.Width, 0);

        // The compact switch has not moved.
        fixture.Resize(679);
        Assert.True(fixture.Layout.IsCompact);
        fixture.Resize(680);
        Assert.False(fixture.Layout.IsCompact);
    }

    [Theory]
    [InlineData(680)]
    [InlineData(684)]
    [InlineData(687)]
    public async Task JustAboveTheCompactSwitchThePanesShareTheSplittersWidth(int width)
    {
        using var fixture = await Fixture.OpenAsync(1100);
        fixture.Resize(width);
        Assert.False(fixture.Layout.IsCompact);

        // The switch compares the two readable widths with the whole width, without the splitter,
        // so here the panes are together that much narrower, and neither by more than that.
        double splitter = fixture.Split.Splitter.PreferredSize.Width;
        Assert.Equal(width - splitter, fixture.List.Bounds.Width + fixture.Reader.Bounds.Width, 0);
        Assert.InRange(fixture.List.Bounds.Width, AdaptiveInboxLayout.ListReadableWidth - splitter, AdaptiveInboxLayout.ListReadableWidth + 0.5);
        Assert.InRange(fixture.Reader.Bounds.Width, AdaptiveInboxLayout.ReaderReadableWidth - splitter, AdaptiveInboxLayout.ReaderReadableWidth + 0.5);
        Assert.Equal(0.35, fixture.Model.SplitterFraction, 3);
    }

    [Fact]
    public async Task TheListCannotBeDraggedNarrowerThanItsReadableWidthAndItsRowsStayInside()
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2.25));
        try
        {
            using var fixture = await Fixture.OpenAsync(1100, 720, received: DateTimeOffset.Now.AddYears(-2));
            fixture.Split.SplitterFraction = 0.05;
            var frame = fixture.Session.RenderFrame();

            var list = fixture.List;
            Assert.True(list.Bounds.Width >= AdaptiveInboxLayout.ListReadableWidth - 0.5, $"The list is {list.Bounds.Width} DIP wide.");
            Assert.Equal(list.Bounds.Width / (1100 - fixture.Split.Splitter.PreferredSize.Width), fixture.Model.SplitterFraction, 3);
            var rows = frame.Commands.OfType<BRenderCommand.DrawText>()
                .Where(text => list.ContentBounds.Contains(text.Origin)).ToArray();
            Assert.NotEmpty(rows);
            foreach (var text in rows)
            {
                double right = text.Origin.X + BTextMeasurer.MeasureAdvance(text.Text.Text, text.Text.Font);
                Assert.True(right <= list.ContentBounds.Right + 0.5, $"'{text.Text.Text}' ends at {right}, the list's rows at {list.ContentBounds.Right}.");
            }
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    [Fact]
    public async Task MessageThatDisappearsReturnsTheCompactLayoutToTheList()
    {
        using var fixture = await Fixture.OpenAsync(500);
        await fixture.Model.SelectAsync(fixture.Messages[0].Key);
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        Assert.True(fixture.View.OpenSelected());
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        fixture.Session.RenderFrame();
        Assert.True(fixture.Layout.ShowsReaderOnly);

        fixture.Receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage(fixture.Messages.Skip(1).ToArray(), null));
        await fixture.Model.ReceiveAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        fixture.Session.RenderFrame();

        Assert.Null(fixture.Model.SelectedMessage);
        Assert.False(fixture.Layout.ShowsReaderOnly);
    }

    [Fact]
    public async Task EscapeLeavesTheCompactReaderWithoutTouchingTheDraft()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var messages = Fixture.CreateMessages(account, 5);
        var receiver = Fixture.CreateReceiver(messages);
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(500, 600));
        session.AddRoot(shell.Window);
        var keyboard = shell.CreateKeyboardNavigation(session);
        shell.Navigation.SelectTab("inbox");
        Assert.True(model.Composer.StartNew());
        model.Composer.Edit("kept@example.test", "", "", "Draft", "Text");
        shell.Navigation.SelectTab("inbox");
        await model.Inbox.ReceiveAsync();
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        await model.Inbox.SelectAsync(messages[2].Key);
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        session.RenderFrame();
        session.SetFocus(Descendants(shell.Window).OfType<StandardListView>().Single());
        Assert.True(keyboard.Handle(Fixture.KeyEvent(13)));
        // Enter reloads the body; Escape during that load would cancel it rather than go back.
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        session.RenderFrame();
        var layout = Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();
        Assert.True(layout.ShowsReaderOnly);

        Assert.True(keyboard.Handle(Fixture.KeyEvent(0x1B)));
        session.RenderFrame();
        Assert.False(layout.ShowsReaderOnly);
        Assert.Equal(messages[2].Key, model.Inbox.SelectedMessage?.Key);
        Assert.Equal("Draft", model.Composer.Subject);
        Assert.False(shell.Inbox.GoBackToList());
    }

    /// <summary>
    /// The footer points to a problem's details and Retry. While compact mode hides the pane they are
    /// in, it says how to show that pane instead, and points there again once the pane is shown:
    /// opened, after Back, or beside the other pane in a wider window.
    /// </summary>
    [Fact]
    public async Task WhileCompactModeHidesAProblemsPaneTheFooterSaysHowToShowIt()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var messages = Fixture.CreateMessages(account, 5);
        var receiver = Fixture.CreateReceiver(messages);
        receiver.Body = async (key, token) =>
        {
            if (key == messages[2].Key) throw new MailConnectionException("The connection closed.");
            if (key == messages[3].Key) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new MailMessageBody(key, $"Body {key.Uid}");
        };
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        using var shell = new MailShellView(model);
        var host = new Host(640, 480);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        session.AddRoot(shell.Window);
        shell.Navigation.SelectTab("inbox");
        void Settle()
        {
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            session.RenderFrame();
            // What the frame's layout posted, as the window drains it before the next frame.
            dispatcher.Drain();
            session.RenderFrame();
        }
        await model.Inbox.ReceiveAsync();
        Settle();
        var layout = Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();

        // Moving through the compact list loads a message without opening it, so its problem is out of sight.
        const string open = "The message could not be loaded. Open it to see the details and Retry.";
        const string beside = "The message could not be loaded. Details and Retry are beside it.";
        await model.Inbox.SelectAsync(messages[2].Key);
        Settle();
        Assert.Equal(InboxProblemScope.Message, model.Inbox.ProblemScope);
        Assert.True(layout.IsCompact && !layout.ShowsReaderOnly);
        Assert.Equal(open, shell.Footer.Text);

        // Opened (the reload fails again), the reader shows the details and Retry.
        Assert.True(shell.Inbox.OpenSelected());
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal(InboxProblemScope.Message, model.Inbox.ProblemScope);
        Assert.Equal(beside, shell.Footer.Text);
        Assert.True(shell.Inbox.GoBackToList());
        Settle();
        Assert.Equal(open, shell.Footer.Text);

        // Resizing switches between both panes and the list alone.
        foreach (var (width, expected) in new[] { (1100, beside), (640, open) })
        {
            host.Width = width;
            shell.Window.InvalidateMeasure();
            Settle();
            Assert.Equal(width == 640, layout.IsCompact);
            Assert.Equal(expected, shell.Footer.Text);
        }

        // A receive failure while the compact reader is shown: the list, with its details and Retry, is
        // hidden. The footer names the button on screen that leads back to it.
        await model.Inbox.SelectAsync(messages[1].Key);
        Settle();
        Assert.True(shell.Inbox.OpenSelected());
        Settle();
        receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await model.Inbox.ReceiveAsync();
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal("Mail could not be received. Use Back to inbox to see the details and Retry.", shell.Footer.Text);
        var back = Descendants(shell.Window).OfType<StandardButton>().Single(button => button.Text == "Back to inbox");
        Assert.True(back.Bounds.Width > 0 && back.Bounds.Height > 0, $"Back to inbox is at {back.Bounds}.");

        // So does a failure to load the older page.
        receiver.Inbox = (cursor, _) => cursor is null
            ? Task.FromResult(new MailInboxPage(messages, new(account.Id, 7, 6, 10, 5)))
            : throw new MailConnectionException("The server did not respond.");
        await model.Inbox.ReceiveAsync();
        Settle();
        Assert.True(model.Inbox.CanLoadOlder);
        await model.Inbox.LoadOlderAsync();
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.True(model.Inbox.ProblemIsOlderPage);
        Assert.Equal("Older messages could not be loaded. Use Back to inbox to see the details and Retry.", shell.Footer.Text);

        // Canceled, Retry is in that pane too.
        receiver.Inbox = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null!; };
        _ = model.Inbox.ReceiveAsync();
        model.Inbox.Cancel();
        Settle();
        Assert.Equal("Canceled. Use Back to inbox to retry.", shell.Footer.Text);
        Assert.True(shell.Inbox.GoBackToList());
        Settle();
        Assert.Equal("Canceled. Retry is available.", shell.Footer.Text);
        _ = model.Inbox.SelectAsync(messages[3].Key);
        model.Inbox.Cancel();
        Settle();
        Assert.False(layout.ShowsReaderOnly);
        Assert.Equal("Canceled. Open the message to retry.", shell.Footer.Text);

        // Back in the list, a receive failure is explained above it.
        receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await model.Inbox.ReceiveAsync();
        Settle();
        Assert.Equal("Mail could not be received. Details and Retry are above the list.", shell.Footer.Text);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    [Theory]
    [InlineData(640)]
    [InlineData(1100)]
    public async Task TheListNoticeTakesNoSpaceOnceMessagesArrive(int width)
    {
        // As in the app, the first frame shows the empty-inbox notice; receiving then hides it.
        var account = TestDirectory.Profile();
        var dispatcher = new TestQueueDispatcher();
        var model = new InboxViewModel(Fixture.CreateReceiver(Fixture.CreateMessages(account, 10)), dispatcher);
        model.SetAccount(account);
        var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, 480));
        session.AddRoot(content);
        session.RenderFrame();
        await model.ReceiveAsync();
        dispatcher.DrainUntil(() => !model.IsBusy);
        session.RenderFrame();
        // The demo also opens the newest message, as a returning user would find it.
        await model.SelectAsync(model.Messages[0].Key);
        dispatcher.DrainUntil(() => !model.IsBusy);
        session.RenderFrame();

        var list = Descendants(content).OfType<StandardListView>().Single();
        Assert.Equal(list.Parent!.Bounds.Top, list.Bounds.Top, 1);
        content.Dispose();
        model.Dispose();
    }

    /// <summary>
    /// A list problem's explanation keeps the toolbar's inset from the list pane's edges, so its accent
    /// stripe stays clear of the frame drawn around the tabs. Its Retry row is laid out like the toolbar:
    /// the row spans the pane, and Retry sits inside it as Receive mail sits inside the toolbar.
    /// </summary>
    [Theory]
    [InlineData(640)]
    [InlineData(1100)]
    public async Task TheListNoticeAndItsRetryKeepTheToolbarsInset(int width)
    {
        using var fixture = await Fixture.OpenAsync(width);
        fixture.Receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await fixture.Model.ReceiveAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        fixture.Session.RenderFrame();

        var receive = fixture.Button("Receive mail");
        var toolbar = (StandardToolbar)receive.Parent!;
        double inset = toolbar.Padding;
        BRect pane = fixture.Split.FirstPane!.Bounds;
        var notice = Descendants(fixture.Content).OfType<InlineFeedback>().First();
        var retry = fixture.Button("Retry receiving");
        var row = retry.Parent!;
        Assert.Equal(FeedbackKind.Error, notice.Kind);
        Assert.True(Math.Abs(notice.Bounds.Left - (pane.Left + inset)) < 0.5, $"The notice starts at {notice.Bounds.Left}, the list pane at {pane.Left}.");
        Assert.True(Math.Abs(notice.Bounds.Right - (pane.Right - inset)) < 0.5, $"The notice ends at {notice.Bounds.Right}, the list pane at {pane.Right}.");
        Assert.Equal(pane.Top + inset, notice.Bounds.Top, 0.5);

        // The row spans the pane below the notice's inset, as the toolbar spans the view, and the list follows it.
        Assert.Equal(toolbar.Bounds.Left, pane.Left, 0.5);
        Assert.Equal(pane.Left, row.Bounds.Left, 0.5);
        Assert.Equal(pane.Right, row.Bounds.Right, 0.5);
        Assert.Equal(notice.Bounds.Bottom + inset, row.Bounds.Top, 0.5);
        Assert.Equal(row.Bounds.Bottom, fixture.List.Bounds.Top, 0.5);
        // Retry is inset within its row as Receive mail is within the toolbar, so the two line up.
        Assert.Equal(receive.Bounds.Left, retry.Bounds.Left, 0.5);
        Assert.Equal(receive.Bounds.Top - toolbar.Bounds.Top, retry.Bounds.Top - row.Bounds.Top, 0.5);
        Assert.Equal(toolbar.Bounds.Bottom - receive.Bounds.Bottom, row.Bounds.Bottom - retry.Bounds.Bottom, 0.5);
    }

    [Fact]
    public async Task AtTwiceTheTextSizeTheHeaderAndNoticeScrollInsteadOfCrowdingOutTheContent()
    {
        var account = TestDirectory.Profile();
        string subject = string.Join(" ", Enumerable.Repeat("Agenda, travel arrangements, and the revised budget", 6));
        var message = new MailMessageSummary { Key = new(account.Id, "INBOX", 7, 1), Sender = "author@example.test", Subject = subject };
        bool fail = false;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => fail
                ? throw new MailConnectionException(string.Join(" ", Enumerable.Repeat("The server did not respond in time.", 8)))
                : Task.FromResult(new MailInboxPage([message], null)),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Body text")),
        };
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        var dispatcher = new TestQueueDispatcher();
        var model = new InboxViewModel(receiver, dispatcher);
        model.SetAccount(account);
        var content = new InboxView(model).CreateContent();
        try
        {
            var host = new Host(1100, 720);
            using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            session.AddRoot(content);
            await model.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.IsBusy);
            await model.SelectAsync(message.Key);
            dispatcher.DrainUntil(() => !model.IsBusy);
            session.RenderFrame();

            var areas = Descendants(content).OfType<BoundedScrollArea>().ToArray();
            var header = areas.Single(area => area.MaximumFraction == 0.45);
            Assert.InRange(header.Bounds.Height, 1, (header.Parent!.Bounds.Height * 0.45) + 1);
            Assert.True(header.Scroll.HasVerticalScrollbar, "The long subject must scroll within the header.");

            // A long failure keeps most of the list pane for the list.
            fail = true;
            await model.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.IsBusy);
            session.RenderFrame();
            session.RenderFrame();
            var notice = areas.Single(area => area.MaximumFraction == 0.4);
            var list = Descendants(content).OfType<StandardListView>().Single();
            Assert.InRange(notice.Bounds.Height, 1, (notice.Parent!.Bounds.Height * 0.4) + 1);
            Assert.True(list.Bounds.Height > notice.Parent.Bounds.Height * 0.5);
        }
        finally
        {
            StandardControlPaint.ApplyTheme(previous);
            content.Dispose();
            model.Dispose();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Host _host;
#pragma warning disable CS0618
        private readonly StandardLegacyGraphicsInputAdapter _input = new("responsive-test");
#pragma warning restore CS0618

        private Fixture(Host host, TestQueueDispatcher dispatcher, UiSession session, InboxViewModel model, InboxView view, UiElement content, TestMailReceiver receiver, MailMessageSummary[] messages)
        {
            _host = host; Dispatcher = dispatcher; Session = session; Model = model; View = view; Content = content; Receiver = receiver; Messages = messages;
        }

        public TestQueueDispatcher Dispatcher { get; }
        public UiSession Session { get; }
        public InboxViewModel Model { get; }
        public InboxView View { get; }
        public UiElement Content { get; }
        public TestMailReceiver Receiver { get; }
        public MailMessageSummary[] Messages { get; }
        public AdaptiveInboxLayout Layout => Descendants(Content).OfType<AdaptiveInboxLayout>().Single();
        public StandardSplitContainer Split => Descendants(Content).OfType<StandardSplitContainer>().Single();
        public StandardListView List => Descendants(Content).OfType<StandardListView>().Single();
        public UiElement Reader => Split.SecondPane!;
        public ScrollableMessageText ReaderText => Descendants(Content).OfType<ScrollableMessageText>().Single();
        public StandardButton BackButton => Button("Back to inbox");
        public StandardButton Button(string text) => Descendants(Content).OfType<StandardButton>().Single(button => button.Text == text);

        public static MailMessageSummary[] CreateMessages(AccountProfile account, int count, DateTimeOffset? received = null) => Enumerable.Range(1, count).Reverse()
            .Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = $"sender{uid}@example.test", Subject = $"Subject {uid}",
                ReceivedAt = received,
            }).ToArray();

        public static TestMailReceiver CreateReceiver(MailMessageSummary[] messages) => new()
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage(messages, null)),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, $"Body {key.Uid}")),
        };

        public static async Task<Fixture> OpenAsync(int width, int height = 600, DateTimeOffset? received = null)
        {
            var account = TestDirectory.Profile();
            var messages = CreateMessages(account, 40, received);
            var receiver = CreateReceiver(messages);
            var dispatcher = new TestQueueDispatcher();
            var model = new InboxViewModel(receiver, dispatcher);
            model.SetAccount(account);
            var view = new InboxView(model);
            var content = view.CreateContent();
            var host = new Host(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            session.AddRoot(content);
            await model.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.IsBusy);
            session.RenderFrame();
            return new(host, dispatcher, session, model, view, content, receiver, messages);
        }

        public void Resize(int width)
        {
            _host.Width = width;
            Content.InvalidateMeasure();
            Session.RenderFrame();
        }

        public void Key(int code)
        {
            Session.DispatchInput(KeyEvent(code));
            Dispatcher.DrainUntil(() => !Model.IsBusy);
        }

        public void Click(BPoint point)
        {
#pragma warning disable CS0618
            Session.DispatchInput(_input.FromPointerButton(new BPointerEventArgs(point, BMouseButtons.Left, BMouseButtons.Left)));
            Session.DispatchInput(_input.FromPointerButton(new BPointerEventArgs(point, BMouseButtons.None, BMouseButtons.Left)));
#pragma warning restore CS0618
            Dispatcher.DrainUntil(() => !Model.IsBusy);
        }

        public static UiInputEvent KeyEvent(int code)
        {
#pragma warning disable CS0618
            return new StandardLegacyGraphicsInputAdapter("responsive-test").FromKey(new BKeyEventArgs(code, false, false, false), KeyboardKeyTransition.Down);
#pragma warning restore CS0618
        }

        public void Dispose()
        {
            Session.Dispose();
            Content.Dispose();
            Model.Dispose();
        }
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public int Width { get; set; } = width;
        public BSize ViewportSize => new(Width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
