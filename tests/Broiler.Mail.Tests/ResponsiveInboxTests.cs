using System.Globalization;
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
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Splitter.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-04: one pane at a time when two readable panes do not fit, without losing context.</summary>
[Collection("UI theme")]
public sealed class ResponsiveInboxTests
{
    /// <summary>The gap the reader's commands keep from the line below them, as the header's rows keep between them.</summary>
    private const double CommandsGap = 4;

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
        shell.Attach(session);
        var keyboard = shell.CreateKeyboardNavigation(session);
        shell.ShowView("inbox");
        Assert.True(model.Composer.StartNew());
        model.Composer.Edit("kept@example.test", "", "", "Draft", "Text");
        shell.ShowView("inbox");
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
        shell.Attach(session);
        shell.ShowView("inbox");
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
        // Where the footer points, in the compact reader as beside the list: the explanation, then Retry
        // loading, below the message's subject, sender and date.
        void AssertDetailsAndRetryBelowTheDate()
        {
            var reader = Descendants(shell.Window).OfType<StandardSplitContainer>().Single().SecondPane!;
            BRect date = Descendants(reader).OfType<StandardLabel>().Single(label => label.Text.StartsWith("Received ", StringComparison.Ordinal)).Bounds;
            BRect details = Descendants(reader).OfType<InlineFeedback>().Single(feedback => feedback.Message.Length > 0).Bounds;
            BRect retry = Descendants(reader).OfType<StandardButton>().Single(button => button.Text == "Retry loading").Bounds;
            Assert.True(date.Bottom <= details.Top + 0.5 && details.Bottom <= retry.Top + 0.5, $"The date is at {date}, the details at {details}, Retry at {retry}.");
        }

        // Moving through the compact list loads a message without opening it, so its problem is out of sight.
        const string open = "The message could not be loaded. Open it to see the details and Retry.";
        const string below = "The message could not be loaded. Details and Retry are below its date.";
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
        Assert.Equal(below, shell.Footer.Text);
        AssertDetailsAndRetryBelowTheDate();
        Assert.True(shell.Inbox.GoBackToList());
        Settle();
        Assert.Equal(open, shell.Footer.Text);

        // Resizing switches between both panes and the list alone.
        foreach (var (width, expected) in new[] { (1100, below), (640, open) })
        {
            host.Width = width;
            shell.Window.InvalidateMeasure();
            Settle();
            Assert.Equal(width == 640, layout.IsCompact);
            Assert.Equal(expected, shell.Footer.Text);
            if (expected == below) AssertDetailsAndRetryBelowTheDate();
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

    /// <summary>
    /// Moving through the compact list loads the selected message without showing it, so the footer does
    /// not describe reading it: it says how to read it, until the reader shows it, alone or beside the list.
    /// </summary>
    [Fact]
    public async Task TheCompactListsFooterSaysHowToReadTheSelectedMessage()
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
        var host = new Host(640, 480);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);
        shell.ShowView("inbox");
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
        Assert.Equal(model.Inbox.Status, shell.Footer.Text);

        const string selected = "Message selected. Open it to read.";
        const string reading = "Reading plain text. This does not mark the message as read on the server.";
        await model.Inbox.SelectAsync(messages[1].Key);
        Settle();
        Assert.True(layout.IsCompact && !layout.ShowsReaderOnly);
        Assert.Equal(reading, model.Inbox.Status);
        Assert.Equal(selected, shell.Footer.Text);

        Assert.True(shell.Inbox.OpenSelected());
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal(reading, shell.Footer.Text);
        Assert.True(shell.Inbox.GoBackToList());
        Settle();
        Assert.Equal(selected, shell.Footer.Text);

        // Beside the list, the reader shows the message.
        host.Width = 1100;
        shell.Window.InvalidateMeasure();
        Settle();
        Assert.False(layout.IsCompact);
        Assert.Equal(reading, shell.Footer.Text);
    }

    /// <summary>
    /// The session limit is explained above the list, so the footer does not repeat it while the list is
    /// shown. The compact reader hides the list but not the unavailable Load older, so its footer points to
    /// that explanation instead of giving the reading status, as for a list problem, and is no longer than
    /// that status. A message problem, beside the reader, comes first; while Receive mail runs, its progress
    /// does, without asking for Receive mail; once it returns to the newest page, the limit is gone.
    /// </summary>
    [Fact]
    public async Task TheCompactReadersFooterPointsToTheSessionLimitWhileTheListIsHidden()
    {
        const string limit = "Session limit reached (500 messages). Older ones cannot be loaded in this session. Receive mail to start again.";
        const string pointer = "Session limit reached. Use Back to inbox to see the details.";
        const string reading = "Reading plain text. This does not mark the message as read on the server.";
        const string selected = "Message selected. Open it to read.";
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var messages = Fixture.CreateMessages(account, InboxViewModel.MaximumLoadedMessages + InboxViewModel.PageSize);
        var receiver = Fixture.CreateReceiver(messages);
        var body = receiver.Body;
        // Pages from the newest; the last loaded page still has older messages behind it.
        Func<MailInboxCursor?, CancellationToken, Task<MailInboxPage>> pages = (cursor, _) =>
        {
            int start = cursor?.NextIndex ?? 0;
            var page = messages.Skip(start).Take(InboxViewModel.PageSize).ToArray();
            int next = start + page.Length;
            return Task.FromResult(new MailInboxPage(page, next < messages.Length ? new(account.Id, 7, (uint)messages.Length + 1, messages.Length, next) : null));
        };
        receiver.Inbox = pages;
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        using var shell = new MailShellView(model);
        var host = new Host(640, 480);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);
        shell.ShowView("inbox");
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
        while (model.Inbox.CanLoadOlder)
        {
            await model.Inbox.LoadOlderAsync();
            Settle();
        }
        var layout = Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();
        Assert.Equal(limit, model.Inbox.SessionLimitNotice);
        Assert.Equal(model.Inbox.Status, shell.Footer.Text);
        Assert.DoesNotContain("Session limit", shell.Footer.Text);

        var newest = model.Inbox.Messages[0].Key;
        await model.Inbox.SelectAsync(newest);
        Settle();
        Assert.True(layout.IsCompact && !layout.ShowsReaderOnly);
        Assert.Equal(selected, shell.Footer.Text);

        Assert.True(shell.Inbox.OpenSelected());
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal(reading, model.Inbox.Status);
        Assert.Equal(pointer, shell.Footer.Text);
        Assert.True(shell.Inbox.GoBackToList());
        Settle();
        Assert.Equal(selected, shell.Footer.Text);

        // A message that fails to load is explained first, in the reader; once it loads, the pointer is back.
        receiver.Body = (_, _) => throw new MailConnectionException("The connection closed.");
        Assert.True(shell.Inbox.OpenSelected());
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal("The message could not be loaded. Details and Retry are below its date.", shell.Footer.Text);
        receiver.Body = body;
        await model.Inbox.RetryAsync(InboxProblemScope.Message);
        Settle();
        Assert.Equal(pointer, shell.Footer.Text);

        // Beside the list, the notice above it explains the limit.
        host.Width = 1100;
        shell.Window.InvalidateMeasure();
        Settle();
        Assert.False(layout.IsCompact);
        Assert.Equal(reading, shell.Footer.Text);
        host.Width = 640;
        shell.Window.InvalidateMeasure();
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal(pointer, shell.Footer.Text);

        // While Receive mail runs, the footer gives its progress; the newest page ends the limit.
        var pending = new TaskCompletionSource<MailInboxPage>();
        receiver.Inbox = (_, token) => pending.Task.WaitAsync(token);
        var receiving = model.Inbox.ReceiveAsync();
        dispatcher.Drain();
        session.RenderFrame();
        Assert.True(model.Inbox.IsLoadingList && layout.ShowsReaderOnly);
        Assert.Null(model.Inbox.SessionLimitNotice);
        Assert.Equal("Receiving newest messages…", shell.Footer.Text);
        pending.SetResult(await pages(null, CancellationToken.None));
        Settle();
        await receiving;
        Assert.Equal(InboxViewModel.PageSize, model.Inbox.Messages.Count);
        Assert.Null(model.Inbox.SessionLimitNotice);
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal(model.Inbox.Status, shell.Footer.Text);
        Assert.DoesNotContain("Session limit", shell.Footer.Text);
    }

    /// <summary>
    /// Reading a message after a receive failed keeps that problem: the compact reader's footer says how
    /// to get back to its details and Retry, and after Back to inbox they are above the list again,
    /// where Tab reaches Retry.
    /// </summary>
    [Fact]
    public async Task OpeningAMessageAfterAListProblemKeepsItsDetailsAndRetry()
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
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(640, 480));
        shell.Attach(session);
        shell.ShowView("inbox");
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
        receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await model.Inbox.ReceiveAsync();
        Settle();
        var layout = Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();
        var inbox = shell.GetContent("inbox");
        StandardButton Button(string text) => Descendants(inbox).OfType<StandardButton>().Single(button => button.Text == text);
        const string above = "Mail could not be received. Details and Retry are above the list.";
        Assert.Equal(above, shell.Footer.Text);

        // Moving through the compact list loads a message without opening it.
        await model.Inbox.SelectAsync(messages[1].Key);
        Settle();
        Assert.False(layout.ShowsReaderOnly);
        Assert.Equal(above, shell.Footer.Text);

        // Read message loads it again and shows it alone.
        Button("Read message").Click();
        Settle();
        Assert.True(layout.ShowsReaderOnly);
        Assert.Equal($"Body {messages[1].Key.Uid}", Descendants(inbox).OfType<ScrollableMessageText>().Single().Text);
        Assert.Equal("Mail could not be received. Use Back to inbox to see the details and Retry.", shell.Footer.Text);

        Assert.True(shell.Inbox.GoBackToList());
        Settle();
        Assert.Equal(above, shell.Footer.Text);
        var retry = Button("Retry receiving");
        Assert.True(retry.IsEnabled && retry.Bounds.Height > 0, $"Retry receiving is at {retry.Bounds}.");
        Assert.Contains(retry, MailKeyboardNavigation.TabStops(inbox));
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
    /// The inbox notice's explanation takes up to 40 % of the list pane that Retry below it leaves, and
    /// scrolls the rest, but, like the reader's header, it does not end inside one of its lines, not even
    /// its first in a short pane, and it shows them all while the list keeps two rows, as for a failed
    /// receive at 1100x720 with twice the text size. Retry is always whole below it, the list right after,
    /// so the footer's "Details and Retry are above the list" holds. Before, at 640x480 with twice the text
    /// size, the notice cut through a line and through Retry, and then hid Retry, which a failed receive
    /// at 640x480 now shows below one whole line; in a pane as short as at 640x320, that line is whole
    /// too, past the share that would cut it. How many lines an explanation takes depends on the platform's
    /// fonts, so whether the notice scrolls follows the lines it was laid out in, as the notice compares them;
    /// <see cref="PastItsShareTheInboxNoticeShowsItsWholeExplanationWhileTheListKeepsTwoRows"/> shows them all
    /// in a window made for it.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0, "receive-error")]
    [InlineData(640, 480, 1.0, "receive-canceled")]
    [InlineData(640, 480, 1.0, "load-error")]
    [InlineData(640, 480, 2.0, "receive-error")]
    [InlineData(640, 480, 2.0, "receive-canceled")]
    [InlineData(640, 480, 2.0, "load-error")]
    [InlineData(640, 450, 2.0, "receive-canceled")]
    [InlineData(640, 320, 2.0, "receive-error")]
    [InlineData(1100, 720, 2.0, "receive-error")]
    [InlineData(1100, 720, 2.0, "receive-canceled")]
    [InlineData(1100, 720, 2.0, "load-error")]
    [InlineData(1100, 560, 2.0, "receive-error")]
    public async Task TheInboxNoticeEndsBetweenItsLinesAndKeepsRetryInView(int width, int height, double textScale, string problem)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var fixture = await Fixture.OpenAsync(width, height);
            var older = fixture.Receiver.Inbox;
            // As in the gallery: the newest page, with older messages to load.
            fixture.Receiver.Inbox = (cursor, token) => cursor is null
                ? Task.FromResult(new MailInboxPage(fixture.Messages[..20], new(fixture.Messages[0].Key.AccountId, 7, 41, 40, 20)))
                : problem == "load-error" ? throw new MailConnectionException("The demo server did not respond while loading older messages. Check the connection, then retry.")
                : older(cursor, token);
            await fixture.Model.ReceiveAsync();
            fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
            if (problem == "receive-error")
                fixture.Receiver.Inbox = (_, _) => throw new MailConnectionException("The demo server did not respond. Check the connection, then retry.");
            if (problem == "receive-canceled")
                fixture.Receiver.Inbox = (_, token) => new TaskCompletionSource<MailInboxPage>().Task.WaitAsync(token);
            _ = problem == "load-error" ? fixture.Model.LoadOlderAsync() : fixture.Model.ReceiveAsync();
            if (problem == "receive-canceled") fixture.Model.Cancel();
            fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
            fixture.Session.RenderFrame();
            fixture.Session.RenderFrame();
            Assert.NotNull(fixture.Model.ListProblem);

            var notice = Descendants(fixture.Content).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Inbox notice");
            BRect shown = notice.Scroll.ContentBounds;
            double available = notice.AvailableHeight;
            string where = $"At {width}x{height}, text {textScale:P0}, {problem}: the notice shows {shown} of {available}";
            Assert.False(fixture.Layout.ShowsReaderOnly);
            var label = AssertNoticeEndsBetweenRows(notice, where);
            // Past its share, the notice shows its whole explanation where that leaves the list two rows (the margin
            // below the last line is not a line), and scrolls otherwise. Lines end on whole pixels and the share
            // need not, so the share is compared as the notice compares it.
            double share = available * 0.4;
            double lines = label.Bounds.Bottom - shown.Top;
            bool scrolls = lines > share && available - lines < (2 * fixture.List.EffectiveItemHeight) - 0.5;
            Assert.True(scrolls == notice.Scroll.HasVerticalScrollbar, $"{where}: the notice {(scrolls ? "does not scroll" : "scrolls")}; its lines end at {lines}.");

            // Retry's row is whole right below what the notice shows, and the list follows it.
            var retry = fixture.Button(problem == "load-error" ? "Retry loading older" : "Retry receiving");
            BRect row = retry.Parent!.Bounds;
            Assert.False(retry.IsDescendantOf(notice), where);
            Assert.Equal(notice.Bounds.Bottom, row.Top, 0.5);
            Assert.True(row.Top <= retry.Bounds.Top && retry.Bounds.Bottom <= row.Bottom && row.Height >= retry.Bounds.Height, $"{where}: Retry is at {retry.Bounds} in its row at {row}.");
            Assert.Equal(row.Bottom, fixture.List.Bounds.Top, 0.5);

            // A notice that scrolls passes its share only to show its first line, which the share would cut.
            if (scrolls && notice.Bounds.Height > share + 0.5)
                Assert.Equal(label.Bounds.Top + BTextMeasurer.GetLineHeight(label.Font), shown.Bottom, 0.5);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// Past its share, the inbox notice shows its whole explanation while that leaves the list two rows. The
    /// window is made as tall as that needs from where the explanation's lines were laid out, so the rule is
    /// covered whatever the platform's fonts make of the lines.
    /// </summary>
    [Fact]
    public async Task PastItsShareTheInboxNoticeShowsItsWholeExplanationWhileTheListKeepsTwoRows()
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        try
        {
            using var fixture = await Fixture.OpenAsync(1100, 560);
            fixture.Receiver.Inbox = (_, _) => throw new MailConnectionException("The demo server did not respond. Check the connection, then retry.");
            await fixture.Model.ReceiveAsync();
            fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
            fixture.Session.RenderFrame();
            fixture.Session.RenderFrame();

            // At 1100x560 the notice scrolls, so its lines are laid out beside the scroll bar, where it measures them.
            var notice = Descendants(fixture.Content).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Inbox notice");
            var label = Descendants(notice).OfType<StandardLabel>().Single();
            Assert.True(notice.Scroll.HasVerticalScrollbar, $"At 1100x560, the notice shows {notice.Scroll.ContentBounds} of {notice.AvailableHeight} and does not scroll.");
            double lines = label.Bounds.Bottom - notice.Scroll.ContentBounds.Top;
            double rows = 2 * fixture.List.EffectiveItemHeight;

            // The window just tall enough to leave the list two rows below the explanation's lines.
            double needed = lines + rows;
            fixture.Resize(1100, 560 + (int)Math.Ceiling(needed - notice.AvailableHeight));
            double available = notice.AvailableHeight;
            BRect shown = notice.Scroll.ContentBounds;
            string where = $"With its lines ending at {lines}, the notice shows {shown} of {available}";
            Assert.True(available >= needed - 0.5 && available < needed + 1, $"{where}: the window does not leave the list just two rows.");
            Assert.True(label.Bounds.Bottom - shown.Top > available * 0.4, $"{where}: the explanation at {label.Bounds} does not pass the notice's share.");

            // It shows the whole explanation and the margin below it, as above it; that margin is not a line, so it
            // may take from the list's two rows.
            Assert.False(notice.Scroll.HasVerticalScrollbar, $"{where}: the notice scrolls.");
            Assert.True(label.Bounds.Bottom <= shown.Bottom + 0.5, $"{where}: the explanation at {label.Bounds} is cut.");
            Assert.Equal(label.Bounds.Top - shown.Top, shown.Bottom - label.Bounds.Bottom, 0.5);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
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
        Assert.Equal(inset, retry.Bounds.Left - row.Bounds.Left, 0.5);
        Assert.Equal(inset, retry.Bounds.Top - row.Bounds.Top, 0.5);
        Assert.Equal(inset, row.Bounds.Bottom - retry.Bounds.Bottom, 0.5);
    }

    /// <summary>
    /// The reader's rows of commands (Back to inbox, Retry loading, and Reply, Reply all and Forward) are
    /// framed strips like the toolbar, and inset their buttons as the toolbar insets Receive mail, so a
    /// button's border does not lie on its row's.
    /// </summary>
    [Theory]
    [InlineData(640, 480)]
    [InlineData(1100, 720)]
    public async Task TheReadersCommandRowsKeepTheToolbarsInset(int width, int height)
    {
        // A message whose text could not be loaded shows Retry loading under its header.
        using var reader = await ReaderFixture.OpenAsync(width, height, "error");
        var receive = reader.InboxButton("Receive mail");
        var toolbar = (StandardToolbar)receive.Parent!;
        Assert.True(toolbar.Padding > 0);
        var commands = new List<string> { "Retry loading", "Reply", "Reply all", "Forward" };
        if (reader.Layout.ShowsReaderOnly) commands.Add(InboxView.BackText);
        foreach (var text in commands)
        {
            var button = reader.Button(text);
            BRect row = button.Parent!.Bounds;
            string where = $"At {width}x{height}, {text} is at {button.Bounds} in its row at {row}";
            Assert.True(button.Bounds.Height > 0, where);
            if (text is "Reply all" or "Forward") continue;
            // The first button of a row is inset from its start, top and bottom as Receive mail is in the toolbar.
            Assert.True(Math.Abs(toolbar.Padding - (button.Bounds.Left - row.Left)) < 0.5, where);
            Assert.True(Math.Abs(toolbar.Padding - (button.Bounds.Top - row.Top)) < 0.5, where);
            Assert.True(Math.Abs(toolbar.Padding - (row.Bottom - button.Bounds.Bottom)) < 0.5, where);
        }
    }

    /// <summary>
    /// A message whose text could not be loaded has nothing below its header, so the header may take
    /// the reader: in a short compact window, the problem and Retry are on screen, not scrolled away
    /// above an empty text area. Once the text arrives, the header is bounded again. How many lines the
    /// long subject takes depends on the platform's fonts, so the window is made from where Retry's row
    /// was laid out: the reader ends a line of text below it, far past the header's share, and too short
    /// for the header with the text's six lines once the text arrives.
    /// </summary>
    [Fact]
    public async Task WithoutMessageTextTheCompactReaderShowsTheProblemAndRetry()
    {
        using var fixture = await Fixture.OpenAsync(640, 400);
        var message = fixture.Messages[2] with { Subject = string.Join(" ", Enumerable.Repeat("Agenda, travel arrangements, and the revised budget", 5)) };
        fixture.Receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage([message], null));
        bool fail = true;
        fixture.Receiver.Body = (key, _) => fail ? throw new MailConnectionException("The connection closed.") : Task.FromResult(new MailMessageBody(key, "Body text"));
        await fixture.Model.ReceiveAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        await fixture.Model.SelectAsync(message.Key);
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        Assert.True(fixture.View.OpenSelected());
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        fixture.Session.RenderFrame();
        Assert.True(fixture.Layout.ShowsReaderOnly);
        Assert.Equal(InboxProblemScope.Message, fixture.Model.ProblemScope);

        var header = Descendants(fixture.Content).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
        var problem = Descendants(header).OfType<InlineFeedback>().Single(feedback => feedback.Kind == FeedbackKind.Error);
        var retry = fixture.Button("Retry loading");
        // The reader a line of text taller than Retry's row needs.
        double needed = retry.Parent!.Bounds.Bottom - header.Bounds.Top + BTextMeasurer.GetLineHeight(fixture.ReaderText.Editor.Font);
        fixture.Resize(640, 400 + (int)Math.Ceiling(needed - header.AvailableHeight));
        Assert.True(header.AvailableHeight >= needed - 0.5 && header.AvailableHeight < needed + 1, $"The reader has {header.AvailableHeight} of the {needed} made for it.");
        Assert.True(retry.Parent.Bounds.Bottom - header.Bounds.Top > header.AvailableHeight * 0.45, $"Retry's row at {retry.Parent.Bounds} fits the header's share of {header.AvailableHeight}; the window is not short enough.");
        BRect shown = header.Scroll.ContentBounds;
        foreach (var (name, bounds) in new[] { ("The problem", problem.Bounds), ("Retry loading", retry.Bounds) })
            Assert.True(bounds.Height > 0 && bounds.Top >= shown.Top - 0.5 && bounds.Bottom <= shown.Bottom + 0.5, $"{name} is at {bounds}, the header shows {shown}.");

        fail = false;
        await fixture.Model.RetryAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.IsBusy);
        fixture.Session.RenderFrame();
        Assert.Equal("Body text", fixture.ReaderText.Text);
        Assert.True(header.Scroll.HasVerticalScrollbar, "The long subject must scroll within the header.");
        Assert.InRange(header.Bounds.Height, 1, (header.AvailableHeight * 0.45) + 1);
    }

    /// <summary>
    /// The reader header takes up to 45 % of the height the commands and the line below it leave, and
    /// scrolls the rest, but it does not end inside one of its rows or lines. While the message text keeps
    /// six lines, it grows to show the whole header, as in the compact reader at 640x480 with Reply, Reply
    /// all and Forward, or a long sender at 640x640, or to show the next row of buttons whole, as Reply
    /// above the HTML preview's row, and the gap below it: ending at the row left it flush with the line,
    /// from which the commands below the header keep a gap. A scroll bar that would scroll only the margin
    /// below Reply does not appear, as it did at 640x520. Otherwise the header ends above the row its share
    /// would cut, or between two of its lines, as for a long sender at 640x480 or a long subject at twice
    /// the text size, so the text keeps its room. Beside the list, Reply, Reply all and Forward stay in
    /// view below the header, even when it scrolls, where they fit on one row and leave the subject's first
    /// line and six lines of text. In a reader too narrow or short for that, such as at 700x480 or 1100x500 with twice the text size, they end the header, as in the compact reader, which has no
    /// height to spare for them: at 700x480, pinned on two rows, they left the subject less than a line and
    /// the text less than one. Beside the list the subject's first line and two lines of text are always
    /// shown. A line across the reader separates the header from the message text. How the rows wrap, and
    /// the height the shell's footer leaves the reader, depend on the platform's fonts, so whether the
    /// header scrolls follows the rows it was laid out in, as the header compares them (with wider fonts
    /// the footer may take two lines, and the reader is shorter);
    /// <see cref="TheReaderHeaderShowsTheMarginBelowItsLastRowWithoutAScrollBar"/> shows the margin in a window
    /// made for it.
    /// </summary>
    /// <summary>
    /// Show parsed headers is an item of the reader's More message actions popup: it shows the parsed headers in
    /// place of the sender and recipients, in the shell's reader, and the header still ends between its rows. Back
    /// has its row to itself, so that row never wraps.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    public async Task ParsedHeadersMenuItemShowsHeadersInline(int width, int height, double textScale)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height);
            string where = $"At {width}x{height}, text {textScale:P0}";
            Assert.DoesNotContain(Descendants(reader.Pane).OfType<StandardButton>(), button => button.Text is "Show parsed headers" or "Hide parsed headers");
            var details = Descendants(reader.Header).OfType<StandardRichEdit>().Single();
            Assert.Equal("Sender and recipients", details.AccessibleName);
            if (reader.Layout.ShowsReaderOnly)
            {
                var backRow = Assert.IsType<StandardToolbar>(reader.Button(InboxView.BackText).Parent);
                Assert.Equal(1, backRow.Children.Count(child => child.Visibility != UiVisibility.Collapsed));
            }

            var content = reader.Shell.GetContent("inbox");
            ReaderMenus.Invoke(reader.Session, content, "More message actions", "Show parsed headers");
            reader.Render();
            Assert.Null(reader.Shell.ActiveDialog);
            Assert.Equal("inbox", reader.Shell.ActiveViewId);
            Assert.Equal("Parsed message headers", details.AccessibleName);
            Assert.Contains("Message-ID: message@example.test", details.GetPlainText());
            Assert.True(ReaderMenus.Items(content, "More message actions").Single(item => item.Id == "headers").IsChecked, where);
            AssertEndsBetweenRows(reader.Header, where);
            AssertLineBetweenHeaderAndText(reader, where);

            ReaderMenus.Invoke(reader.Session, content, "More message actions", "Show parsed headers");
            reader.Render();
            Assert.Equal("Sender and recipients", details.AccessibleName);
            Assert.DoesNotContain("Message-ID:", details.GetPlainText());
        }
        finally
        {
            StandardControlPaint.ApplyTheme(previous);
        }
    }

    /// <summary>
    /// The triage fixture opens the same message as the plain one, but on an account that can archive, trash,
    /// mark read, star and permanently delete, so reader layout tests can see those controls.
    /// </summary>
    [Theory]
    [InlineData(1100, 720)]
    [InlineData(640, 480)]
    public async Task TheTriageReaderFixtureOpensAMessageThatCanBeTriaged(int width, int height)
    {
        using var reader = await ReaderFixture.OpenAsync(width, height, triage: true);
        Assert.NotNull(reader.Triage);
        Assert.NotNull(reader.Inbox.Body);
        Assert.True(reader.Inbox.CanArchive);
        Assert.True(reader.Inbox.CanTrash);
        Assert.Empty(reader.Triage!.Moves);
        Assert.Empty(reader.Triage.Deletions);

        using var plain = await ReaderFixture.OpenAsync(width, height);
        Assert.Null(plain.Triage);
        Assert.False(plain.Inbox.CanArchive);
        Assert.False(plain.Inbox.CanTrash);
    }

    private static UiInputEvent FindKey(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new Broiler.Input.InputEventHeader(Broiler.Input.InputDeviceId.FromOpaqueValue("reader-find"), new Broiler.Input.InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: Broiler.Input.InputEventSource.Synthetic));

    /// <summary>
    /// Ctrl+F docks the find bar in the reader, below the line under the header and above the message text,
    /// which keeps the rest of the reader and its six lines: no dialog opens, and the query has focus.
    /// </summary>
    [Theory]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    public async Task CtrlFDocksTheFindBarBetweenTheLineAndTheText(int width, int height, double textScale)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height);
            var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
            Assert.Equal(UiVisibility.Collapsed, reader.Banners.Visibility);
            reader.Session.SetFocus(reader.Text.Editor);
            double closedText = reader.Text.Bounds.Height;

            Assert.True(keyboard.Handle(FindKey(0x46, KeyboardModifierState.Control)));
            reader.Render();

            string where = $"{width}x{height} at {textScale}";
            Assert.Null(reader.Shell.ActiveDialog);
            Assert.Equal("inbox", reader.Shell.ActiveViewId);
            Assert.Same(reader.FindQuery, reader.Session.FocusedElement);
            Assert.Equal(UiVisibility.Visible, reader.Banners.Visibility);
            // The bar is in the banner slot, which scrolls where it is shorter than the bar.
            BRect bar = reader.Banners.Bounds, line = reader.Divider.Bounds, text = reader.Text.Bounds, pane = reader.Pane.Bounds;
            Assert.True(bar.Height > 0 && reader.Find.Bounds.Height > 0, $"{where}: the find bar was not arranged.");
            Assert.True(line.Bottom <= bar.Top + 0.5, $"{where}: the find bar at {bar} is above the line at {line}.");
            Assert.True(bar.Bottom <= text.Top + 0.5, $"{where}: the find bar at {bar} covers the text at {text}.");
            foreach (var control in Descendants(reader.Find).Where(element => element.CanFocus).ToArray())
                Reachability.Assert(reader.Session, reader.Render, control, $"{where}: {control.AccessibleName ?? (control as StandardButton)?.Text}");
            text = reader.Text.Bounds;
            Assert.Equal(pane.Bottom, text.Bottom, 0.5);
            // The header's share is of the height the bar leaves, so the text keeps its six lines, or where the
            // reader is too short for them, the rest of the header's share, as it does without the bar.
            double kept = Math.Min(reader.Text.HeightOfLines(6), (1 - 0.45) * reader.Header.AvailableHeight);
            Assert.True(text.Height >= kept - 0.5, $"{where}: the text keeps {text.Height}, less than {kept}; closed it had {closedText}.");
            Assert.Equal(pane.Height - (text.Top - reader.Header.Bounds.Bottom), reader.Header.AvailableHeight, 0.5);
            // At 200 % in 640x480 the bar leaves the header only about one line of its share, and the header's
            // line rule is checked only at 100 % here.
            if (textScale == 1.0) AssertEndsBetweenRows(reader.Header, where);

            // Escape closes it, the text takes its place again below the line, and has focus.
            Assert.True(keyboard.Handle(FindKey(0x1B)));
            reader.Render();
            Assert.Equal(UiVisibility.Collapsed, reader.Banners.Visibility);
            Assert.Same(reader.Text.Editor, reader.Session.FocusedElement);
            Assert.Equal(reader.Divider.Bounds.Bottom, reader.Text.Bounds.Top, 0.5);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>Enter searches the query and then, while it is unchanged, moves on as F3 does; Shift moves back.</summary>
    [Fact]
    public async Task EnterF3AndShiftMoveTheFindSelectionWithoutADialog()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        bool Dispatch(UiInputEvent input) => keyboard.Handle(input) || reader.Session.DispatchInput(input);
        reader.Session.SetFocus(reader.Text.Editor);
        Assert.True(Dispatch(FindKey(0x46, KeyboardModifierState.Control)));
        reader.Render();
        reader.FindQuery.Text = "paragraph";
        string Selected() => reader.Text.Editor.Document.Slice(reader.Text.Editor.Selection).PlainText;

        Assert.True(Dispatch(FindKey(0x0D))); reader.Render();
        Assert.Contains("Match 1 of 20", reader.Find.Status);
        Assert.Equal("paragraph", Selected());
        Assert.True(Dispatch(FindKey(0x0D))); Assert.Contains("Match 2 of 20", reader.Find.Status);
        Assert.True(Dispatch(FindKey(0x0D, KeyboardModifierState.Shift))); Assert.Contains("Match 1 of 20", reader.Find.Status);
        Assert.True(Dispatch(FindKey(0x72))); Assert.Contains("Match 2 of 20", reader.Find.Status);
        Assert.True(Dispatch(FindKey(0x72, KeyboardModifierState.Shift))); Assert.Contains("Match 1 of 20", reader.Find.Status);
        Assert.True(Dispatch(FindKey(0x72, KeyboardModifierState.Shift))); Assert.Contains("Match 20 of 20", reader.Find.Status);
        Assert.Contains("Wrapped", reader.Find.Status);
        Assert.Equal("paragraph", Selected());
        Assert.Null(reader.Shell.ActiveDialog);

        // An edited query is searched again from its first match.
        reader.FindQuery.Text = "message body";
        Assert.True(Dispatch(FindKey(0x0D))); Assert.Contains("Match 1 of 20", reader.Find.Status);
        Assert.Equal("message body", Selected());
        Assert.Same(reader.FindQuery, reader.Session.FocusedElement);
    }

    /// <summary>
    /// In the compact reader Escape first closes Find, with focus on the text, and only then goes back to the
    /// list; Android's Back does the same.
    /// </summary>
    [Fact]
    public async Task EscapeClosesFindBeforeItLeavesTheCompactReader()
    {
        using var reader = await ReaderFixture.OpenAsync(640, 480);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        Assert.True(reader.Layout.ShowsReaderOnly);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        Assert.Same(reader.FindQuery, reader.Session.FocusedElement);

        Assert.True(keyboard.Handle(FindKey(0x1B)));
        reader.Render();
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);
        Assert.True(reader.Layout.ShowsReaderOnly);
        Assert.Same(reader.Text.Editor, reader.Session.FocusedElement);

        Assert.True(keyboard.Handle(FindKey(0x1B)));
        reader.Render();
        Assert.False(reader.Layout.ShowsReaderOnly);
        Assert.Same(reader.List, reader.Session.FocusedElement);
    }

    /// <summary>Beside the list, Escape with focus in the list closes an open Find before anything else.</summary>
    [Fact]
    public async Task EscapeFromTheListClosesAnOpenFind()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        Assert.False(reader.Layout.IsCompact);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        reader.Session.SetFocus(reader.List);

        Assert.True(keyboard.Handle(FindKey(0x1B)));
        reader.Render();
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);
        Assert.Equal(UiVisibility.Collapsed, reader.Banners.Visibility);
        Assert.Null(reader.Shell.ActiveDialog);
        Assert.False(reader.Shell.Inbox.CloseFindInMessage());
    }

    /// <summary>
    /// Leaving the compact reader with Back to inbox closes its find bar: Escape afterwards is not taken by a bar the
    /// user cannot see, and focus stays on the list, out of the hidden reader.
    /// </summary>
    [Fact]
    public async Task BackToInboxClosesTheCompactReadersFindBar()
    {
        using var reader = await ReaderFixture.OpenAsync(640, 480);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        Assert.True(reader.Layout.ShowsReaderOnly);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        Assert.Equal(UiVisibility.Visible, reader.Find.Visibility);

        Assert.True(reader.Shell.Inbox.GoBackToList());
        reader.Render();
        Assert.False(reader.Layout.ShowsReaderOnly);
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);
        Assert.False(reader.Shell.Inbox.CloseFindInMessage());
        Assert.Same(reader.List, reader.Session.FocusedElement);

        // Escape has nothing to go back from and nothing to close, so focus stays on the list.
        Assert.True(keyboard.Handle(FindKey(0x1B)));
        reader.Render();
        Assert.Same(reader.List, reader.Session.FocusedElement);
    }

    /// <summary>
    /// While work runs, Escape cancels it first, with focus in the find query as with focus in the list, and the
    /// bar stays open; once nothing runs, Escape closes the bar.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapeCancelsRunningWorkBeforeItClosesFind(bool focusInList)
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        bool Dispatch(UiInputEvent input) => keyboard.Handle(input) || reader.Session.DispatchInput(input);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        if (focusInList) reader.Session.SetFocus(reader.List);
        else Assert.Same(reader.FindQuery, reader.Session.FocusedElement);

        var receive = reader.Inbox.ReceiveAsync();
        Assert.True(reader.Inbox.IsBusy);
        Assert.False(reader.Find.HandleFindInput(FindKey(0x1B)));
        Assert.True(Dispatch(FindKey(0x1B)));
        Assert.Equal(UiVisibility.Visible, reader.Find.Visibility);
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        try { await receive; } catch (OperationCanceledException) { }
        reader.Render();
        Assert.Equal(UiVisibility.Visible, reader.Find.Visibility);

        Assert.True(Dispatch(FindKey(0x1B)));
        reader.Render();
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);
    }

    /// <summary>Receiving again keeps the open message, so its find bar, query and counter stay as they were.</summary>
    [Fact]
    public async Task FindSurvivesReceivingAgain()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        var find = reader.Find;
        reader.FindQuery.Text = "paragraph";
        find.Search();
        string status = find.Status;
        Assert.Contains("Match 1 of 20", status);

        var receive = reader.Inbox.ReceiveAsync();
        reader.Render();
        await receive;
        reader.Render();

        Assert.Same(find, reader.Find);
        Assert.False(find.IsDisposed);
        Assert.Equal(UiVisibility.Visible, find.Visibility);
        Assert.Equal(UiVisibility.Visible, reader.Banners.Visibility);
        Assert.Equal("paragraph", reader.FindQuery.Text);
        Assert.Equal(status, find.Status);
    }

    /// <summary>
    /// Opening another message replaces the find bar; focus that was in its query moves to the new message's
    /// text, never to a disposed control.
    /// </summary>
    [Fact]
    public async Task OpeningAnotherMessageMovesFocusFromFindToTheReader()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        var find = reader.Find;
        Assert.Same(reader.FindQuery, reader.Session.FocusedElement);

        var select = reader.Inbox.SelectAsync(reader.Inbox.Messages[1].Key);
        reader.Render();
        await select;
        reader.Render();

        Assert.True(find.IsDisposed);
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);
        Assert.Equal(UiVisibility.Collapsed, reader.Banners.Visibility);
        Assert.Same(reader.Text.Editor, reader.Session.FocusedElement);
        Assert.False(reader.Session.FocusedElement!.IsDisposed);
    }

    /// <summary>AltGr arrives as Ctrl+Alt: it types a character and never opens or drives Find.</summary>
    [Fact]
    public async Task AltGrNeverOpensOrDrivesFind()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        reader.Session.SetFocus(reader.Text.Editor);
        Assert.False(keyboard.Handle(FindKey(0x46, KeyboardModifierState.Control | KeyboardModifierState.Alt)));
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);

        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        Assert.False(reader.Shell.Inbox.HandleReaderInput(FindKey(0x72, KeyboardModifierState.Alt)));
        Assert.False(reader.Shell.Inbox.HandleReaderInput(FindKey(0x1B, KeyboardModifierState.RightAlt)));
        Assert.Equal(UiVisibility.Visible, reader.Find.Visibility);
    }

    /// <summary>
    /// Beside the list, where Reply stays pinned below the header, an open Find takes its height from the
    /// header's budget: the commands stay pinned only while the text still keeps six lines.
    /// </summary>
    [Theory]
    [InlineData(1100, 720)]
    [InlineData(1100, 480)]
    [InlineData(900, 400)]
    public async Task AnOpenFindKeepsTheTextsLinesWhereTheCommandsArePinned(int width, int height)
    {
        using var reader = await ReaderFixture.OpenAsync(width, height);
        Assert.False(reader.Layout.IsCompact);
        Assert.True(reader.Shell.Inbox.ShowFindInMessage());
        reader.Render();
        var reply = reader.Button("Reply");
        bool pinned = !reply.IsDescendantOf(reader.Header);
        double lines = reader.Text.HeightOfLines(6);
        if (pinned)
            Assert.True(reader.Text.Bounds.Height >= lines - 0.5, $"{width}x{height}: pinned commands leave the text {reader.Text.Bounds.Height}, less than {lines}.");
        Assert.True(reader.Banners.Bounds.Bottom <= reader.Text.Bounds.Top + 0.5);
    }

    /// <summary>
    /// The find bar keeps a line for its feedback while it is open, and keeps the room the feedback has needed since it
    /// opened, so the text below does not move as the feedback appears, changes and clears while the user types and
    /// searches, and the commands stay where they are.
    /// </summary>
    [Theory]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    [InlineData(640, 480, 2.0)]
    public async Task TheFindFeedbackKeepsItsRoomWhileTheUserTypes(int width, int height, double textScale)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height);
            var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
            reader.Session.SetFocus(reader.Text.Editor);
            Assert.True(keyboard.Handle(FindKey(0x46, KeyboardModifierState.Control)));
            reader.Render();
            string where = $"{width}x{height} at {textScale}";
            double top = reader.Text.Bounds.Top;
            var strip = reader.Button("Reply").Parent;
            void Unmoved(string step)
            {
                reader.Render();
                Assert.True(Math.Abs(reader.Text.Bounds.Top - top) <= 0.5, $"{where}, {step}: the text moved from {top} to {reader.Text.Bounds.Top} ({reader.Find.Status}).");
                Assert.Same(strip, reader.Button("Reply").Parent);
            }
            reader.FindQuery.Text = "p"; Unmoved("first character");
            reader.FindQuery.Text = "paragraph"; Unmoved("typing");
            Assert.True(keyboard.Handle(FindKey(0x0D)) || reader.Session.DispatchInput(FindKey(0x0D)));
            Assert.Contains("Match 1 of 20", reader.Find.Status);
            Unmoved("a match");
            // A longer result may take more room once; what follows keeps it.
            reader.FindQuery.Text = "absent";
            reader.Find.Search();
            Assert.Contains("No matches", reader.Find.Status);
            reader.Render();
            top = reader.Text.Bounds.Top;
            reader.FindQuery.Text = "paragraph"; Unmoved("typing after no match");
            reader.Find.Search(); Unmoved("a match after no match");
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// The strip, with More message actions, moves between the header and its place below it as the window is
    /// resized; a focused More message actions keeps focus, and its open popup closes rather than float where
    /// the strip was.
    /// </summary>
    [Fact]
    public async Task AFocusedMoreMessageActionsKeepsFocusWhenTheStripMoves()
    {
        using var reader = await ReaderFixture.OpenAsync(640, 480);
        var more = ReaderMenus.Button(reader.Pane, "More message actions");
        Assert.True(more.IsDescendantOf(reader.Header));
        Assert.True(more.Expand());
        Assert.Same(more.Menu, reader.Session.CapturedElement);

        reader.Resize(1100, 720);
        Assert.False(reader.Layout.IsCompact);
        Assert.False(more.IsDescendantOf(reader.Header));
        Assert.False(more.IsExpanded);
        Assert.Null(reader.Session.CapturedElement);
        Assert.Same(more.Trigger, reader.Session.FocusedElement);
        Assert.True(more.Trigger.Bounds.Width > 0);

        reader.Resize(640, 480);
        Assert.True(more.IsDescendantOf(reader.Header));
        Assert.Same(more.Trigger, reader.Session.FocusedElement);
    }

    /// <summary>
    /// In a narrow compact reader whose header scrolls, Back has its row to itself (the parsed headers are a menu
    /// item now), so that first row is one line however narrow the reader is.
    /// </summary>
    [Theory]
    [InlineData(1.0, 260)]
    [InlineData(1.0, 320)]
    [InlineData(1.25, 300)]
    public async Task InANarrowScrollingHeaderBacksRowNeverWraps(double textScale, int height)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(479, height, "long");
            var back = reader.Button(InboxView.BackText);
            var backRow = Assert.IsType<StandardToolbar>(back.Parent);
            int scrolled = 0;
            for (int width = 479; width >= 300; width -= 3)
            {
                reader.Resize(width, height);
                Assert.True(reader.Layout.ShowsReaderOnly);
                if (reader.Header.Scroll.HasVerticalScrollbar) scrolled++;
                string where = $"At {width}x{height}, text {textScale:P0}, Back at {back.Bounds} in its row at {backRow.Bounds}";
                Assert.Equal(1, backRow.Children.Count(child => child.Visibility != UiVisibility.Collapsed));
                Assert.True(Math.Abs(backRow.Bounds.Bottom - back.Bounds.Bottom - backRow.Padding) < 0.5, $"{where}: the row wraps.");
            }
            Assert.True(scrolled > 0, $"scrolled {scrolled}");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(previous);
        }
    }

    /// <summary>
    /// With an account that can triage, the reader's strip has Reply, Reply all and Forward; Archive, Trash and
    /// Move; and More message actions, with separators between the groups; the star is beside the subject. None
    /// of them opens a dialog.
    /// </summary>
    [Theory]
    [InlineData(1100, 720)]
    [InlineData(640, 480)]
    public async Task TriageAndRespondCommandsAreInlineWithoutADialog(int width, int height)
    {
        using var reader = await ReaderFixture.OpenAsync(width, height, triage: true);
        var content = reader.Shell.GetContent("inbox");
        var reply = reader.Button("Reply");
        var strip = Assert.IsType<StandardToolbar>(reply.Parent);
        foreach (var text in new[] { "Reply all", "Forward", "Archive", "Trash", "Move ▾", "More message actions" })
        {
            var button = reader.Button(text);
            Assert.True(button.IsDescendantOf(strip), $"{text} is not in the strip.");
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0, $"{text} is at {button.Bounds}.");
        }
        Assert.Equal(UiVisibility.Collapsed, reader.Button("Delete permanently…").Visibility);
        Assert.Equal("Move to trash", reader.Button("Trash").ToolTipText);
        Assert.Equal(Broiler.UI.Toolbar.UiToolbarBreak.Separator, strip.GetBreakBefore(reader.Button("Archive")));
        Assert.Equal(Broiler.UI.Toolbar.UiToolbarBreak.Separator, strip.GetBreakBefore(ReaderMenus.Button(content, "More message actions")));
        var star = reader.Button("Star message");
        var subject = Assert.IsType<SubjectRow>(star.Parent);
        Assert.True(star.Bounds.Top >= subject.Bounds.Top - 0.5 && star.Bounds.Bottom <= subject.Bounds.Top + BTextMeasurer.GetLineHeight(subject.Label.Font) + 0.5,
            $"The star at {star.Bounds} is not on the subject's first line at {subject.Bounds}.");

        star.Click();
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        reader.Render();
        Assert.Single(reader.Triage!.StarChanges);
        Assert.Null(reader.Shell.ActiveDialog);

        var more = ReaderMenus.Button(content, "More message actions");
        more.Trigger.Click();
        Assert.True(more.IsExpanded);
        Assert.Null(reader.Shell.ActiveDialog);
        Assert.Null(reader.Session.ModalElement);
        Assert.Equal(["Mark read", "Find in message", "Show parsed headers", "Top of message", "End of message"],
            more.Menu.GetSemanticNode().Children.Select(child => child.Name).ToArray());
        more.Collapse();

        reader.Button("Archive").Click();
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        reader.Render();
        Assert.Equal("Archive", Assert.Single(reader.Triage.Moves).Destination);
        Assert.Null(reader.Shell.ActiveDialog);
        Assert.Equal("inbox", reader.Shell.ActiveViewId);
        Assert.False(reader.Shell.ShowView("message-actions"));
    }

    /// <summary>
    /// More message actions opens its popup below its button (or above it near the window's bottom); Escape closes
    /// it and focus returns to the button, as a click outside it does.
    /// </summary>
    [Theory]
    [InlineData(1100, 720)]
    [InlineData(640, 480)]
    public async Task OverflowMenuOpensAnchoredToItsTriggerAndRestoresFocus(int width, int height)
    {
        using var reader = await ReaderFixture.OpenAsync(width, height, triage: true);
        var more = ReaderMenus.Button(reader.Pane, "More message actions");
        reader.Session.SetFocus(reader.Text.Editor);
        more.Trigger.Click();
        reader.Render();
        Assert.Same(more.Menu, reader.Session.CapturedElement);
        BRect popup = more.Menu.OverlayBounds, trigger = more.Trigger.Bounds;
        Assert.True(popup.Height > 0, $"The popup is at {popup}.");
        Assert.True(popup.Top >= trigger.Bottom - 0.5 || popup.Bottom <= trigger.Top + 0.5, $"The popup at {popup} covers its button at {trigger}.");
        Assert.True(popup.Left >= -0.5 && popup.Right <= width + 0.5 && popup.Bottom <= height + 0.5, $"The popup at {popup} leaves the window.");
        Assert.All(more.Menu.GetSemanticNode().Children, child => Assert.Equal(UiSemanticRole.MenuItem, child.Role));

        Assert.True(reader.Session.DispatchInput(ReaderMenus.Key(0x1B, "Escape")));
        Assert.False(more.IsExpanded);
        Assert.Same(more.Trigger, reader.Session.FocusedElement);

        more.Trigger.Click();
        Assert.True(more.IsExpanded);
        var outside = reader.Text.Bounds;
#pragma warning disable CS0618
        var pointer = new StandardLegacyGraphicsInputAdapter("strip-test");
        reader.Session.DispatchInput(pointer.FromPointerButton(new BPointerEventArgs(new BPoint(outside.Left + 20, outside.Bottom - 20), BMouseButtons.Left, BMouseButtons.Left)));
#pragma warning restore CS0618
        Assert.False(more.IsExpanded);
        Assert.Null(reader.Session.CapturedElement);
        Assert.Same(more.Trigger, reader.Session.FocusedElement);
    }

    /// <summary>
    /// Move offers the folders with a role (and pinned ones) as quick targets, and Choose folder… opens the full
    /// folder picker inline below the strip; another message closes it.
    /// </summary>
    [Fact]
    public async Task MovePopupOffersQuickTargetsAndChooseFolderRevealsTheInlinePicker()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720, triage: true);
        var content = reader.Shell.GetContent("inbox");
        var items = ReaderMenus.Items(content, "Move ▾");
        Assert.Equal(["to:Archive", "to:Trash", "choose-separator", "choose"], items.Select(item => item.Id).ToArray());
        Assert.Equal("Archive — Archive", items[0].Text);

        ReaderMenus.Invoke(reader.Session, content, "Move ▾", "Trash — Trash");
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        reader.Render();
        Assert.Equal("Trash", Assert.Single(reader.Triage!.Moves).Destination);
        Assert.Null(reader.Shell.ActiveDialog);

        var picker = Descendants(reader.Pane).OfType<Broiler.UI.ComboBox.Standard.StandardComboBox>().Single(combo => combo.AccessibleName == "Move destination");
        Assert.False(picker.CanFocus);
        ReaderMenus.Invoke(reader.Session, content, "Move ▾", "Choose folder…");
        reader.Render();
        Assert.Same(picker, reader.Session.FocusedElement);
        Assert.True(reader.Button("Move message").Bounds.Height > 0);
        Assert.Null(reader.Shell.ActiveDialog);

        await reader.Inbox.SelectAsync(reader.Inbox.Messages[1].Key);
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        reader.Render();
        Assert.False(picker.CanFocus);
    }

    /// <summary>
    /// A popup belongs to the message it was opened for: selecting another message closes it and nothing it
    /// offered runs; Mark read in a popup opened for the new message marks that message.
    /// </summary>
    [Fact]
    public async Task APopupClosesWhenAnotherMessageIsSelected()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720, triage: true);
        var content = reader.Shell.GetContent("inbox");
        var more = ReaderMenus.Button(content, "More message actions");
        Assert.True(more.Expand());
        var other = reader.Inbox.Messages[1].Key;
        await reader.Inbox.SelectAsync(other);
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        reader.Render();
        Assert.False(more.IsExpanded);
        Assert.Null(reader.Session.CapturedElement);
        Assert.Empty(reader.Triage!.ReadChanges);

        ReaderMenus.Invoke(reader.Session, content, "More message actions", "Mark read");
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        Assert.Equal((other, true), Assert.Single(reader.Triage.ReadChanges));
        Assert.Null(reader.Shell.ActiveDialog);
    }

    /// <summary>
    /// Tab and Shift+Tab with a reader popup open close it and move to the next or previous control, as they do
    /// from its button, with the window's keyboard navigation running first as it does in the app.
    /// </summary>
    [Theory]
    [InlineData("More message actions", false)]
    [InlineData("More message actions", true)]
    [InlineData("Move ▾", false)]
    [InlineData("Move ▾", true)]
    public async Task TabWithAPopupOpenClosesItAndMovesOn(string trigger, bool shift)
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720, triage: true);
        var button = ReaderMenus.Button(reader.Pane, trigger);
        reader.Session.SetFocus(button.Trigger);
        reader.Navigation.MoveFocus(shift ? -1 : 1);
        var expected = reader.Session.FocusedElement;
        Assert.NotSame(button.Trigger, expected);

        reader.Session.SetFocus(reader.Text.Editor);
        button.Trigger.Click();
        Assert.Same(button.Menu, reader.Session.CapturedElement);
        var tab = ReaderMenus.Key(0x09, "Tab", shift ? KeyboardModifierState.Shift : KeyboardModifierState.None);
        Assert.True(reader.Navigation.Handle(tab) || reader.Session.DispatchInput(tab));
        Assert.False(button.IsExpanded);
        Assert.Null(reader.Session.CapturedElement);
        Assert.Same(expected, reader.Session.FocusedElement);
    }

    /// <summary>
    /// The reader popups' rows are as tall as their text, as the menu bar's are, and follow a change of text size.
    /// </summary>
    [Theory]
    [InlineData("More message actions")]
    [InlineData("Move ▾")]
    public async Task PopupRowsFollowTheTextSize(string trigger)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(1100, 720, triage: true);
            var button = ReaderMenus.Button(reader.Pane, trigger);
            Assert.True(button.Expand());
            double line = BTextMeasurer.GetLineHeight(button.Menu.Font);
            Assert.True(line + 6 > 26, $"The line height {line} at 200% does not need taller rows.");
            Assert.Equal(line + 6, button.Menu.ItemHeight, 0.01);
            Assert.True(button.Collapse());

            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
            reader.Render();
            Assert.True(button.Expand());
            Assert.Equal(Math.Max(26, BTextMeasurer.GetLineHeight(button.Menu.Font) + 6), button.Menu.ItemHeight, 0.01);
            Assert.True(button.Collapse());
        }
        finally
        {
            StandardControlPaint.ApplyTheme(previous);
        }
    }

    /// <summary>
    /// Mark read does what its label said when the popup opened: when the message became read while the popup
    /// was open, choosing it leaves the message read rather than marking it unread.
    /// </summary>
    [Fact]
    public async Task MarkReadDoesWhatItsLabelSaidWhenTheStateChangedWhileOpen()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720, triage: true);
        var content = reader.Shell.GetContent("inbox");
        var more = ReaderMenus.Button(content, "More message actions");
        reader.Triage!.FlagStatus = MailFlagChangeStatus.Confirmed;
        Assert.False(reader.Inbox.SelectedMessage!.IsRead);
        Assert.True(more.Expand());
        Assert.Equal("Mark read", more.Menu.Items.Single(item => item.Id == "read").Text);

        // The message becomes read while the popup is open, as a Receive that brings it back read does.
        await reader.Inbox.SetSelectedReadStateAsync(true);
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        reader.Render();
        Assert.True(reader.Inbox.SelectedMessage!.IsRead);
        reader.Triage.ReadChanges.Clear();
        Assert.True(more.IsExpanded);

        int index = more.Menu.Items.ToList().FindIndex(item => item.Id == "read");
        more.Menu.SetSelectedPath([index]);
        Assert.True(reader.Session.DispatchInput(ReaderMenus.Key(0x0D, "Enter")));
        reader.Dispatcher.DrainUntil(() => !reader.Inbox.IsBusy);
        Assert.DoesNotContain(reader.Triage.ReadChanges, change => !change.IsRead);
        Assert.True(reader.Inbox.SelectedMessage!.IsRead);
    }

    /// <summary>
    /// The star sits beside the subject without making its row taller than its lines, so the header still ends
    /// between them, at either text size, beside the list and in the compact reader.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    public async Task StarBesideSubjectKeepsHeaderRowsWhole(int width, int height, double textScale)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height, "long", triage: true);
            string where = $"At {width}x{height}, text {textScale:P0}";
            var subject = Descendants(reader.Header).OfType<SubjectRow>().Single();
            double line = BTextMeasurer.GetLineHeight(subject.Label.Font);
            double lines = subject.Bounds.Height / line;
            Assert.True(lines >= 0.99 && Math.Abs(lines - Math.Round(lines)) < 0.01, $"{where}: the subject row at {subject.Bounds} is not lines of {line}.");
            Assert.True(subject.Star.Bounds.Height <= line + 0.5 && subject.Star.Bounds.Top >= subject.Bounds.Top - 0.5, $"{where}: the star is at {subject.Star.Bounds}.");
            Assert.True(subject.Label.Bounds.Right <= subject.Star.Bounds.Left + 0.5, $"{where}: the subject at {subject.Label.Bounds} runs under the star at {subject.Star.Bounds}.");
            AssertEndsBetweenRows(reader.Header, where);
            if (!reader.Layout.IsCompact)
                Assert.True(reader.Text.Bounds.Height >= reader.Text.HeightOfLines(2) - 0.5, $"{where}; the text has {reader.Text.Bounds.Height}.");
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// Beside the list, the strip with its triage group and separators is pinned below the header only where it
    /// fits on one row; otherwise it ends the header, as in the compact reader.
    /// </summary>
    [Theory]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    [InlineData(1400, 800, 1.0)]
    [InlineData(820, 720, 1.0)]
    public async Task StripPinsBelowHeaderOnlyWhenItFitsOnOneRow(int width, int height, double textScale)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height, triage: true);
            string where = $"At {width}x{height}, text {textScale:P0}";
            var reply = reader.Button("Reply");
            var strip = Assert.IsType<StandardToolbar>(reply.Parent);
            var shown = strip.Children.Where(child => child.Visibility != UiVisibility.Collapsed).ToArray();
            if (!reply.IsDescendantOf(reader.Header))
            {
                foreach (var child in shown)
                    Assert.True(Math.Abs(child.Bounds.Top - reply.Bounds.Top) < 0.5, $"{where}: the pinned strip wraps at {child.Bounds}.");
                Assert.True(reader.Text.Bounds.Height >= reader.Text.HeightOfLines(6) - 0.5, $"{where}; the text has {reader.Text.Bounds.Height}.");
            }
            // A wide reader has room for the whole strip on one row.
            if (width >= 1400) Assert.False(reply.IsDescendantOf(reader.Header), $"{where}: the strip is in the header.");
            AssertEndsBetweenRows(reader.Header, where);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>AltGr arrives as Ctrl+Alt: with a popup open, it drives none of the reader's commands.</summary>
    [Fact]
    public async Task AltGrDoesNotTriggerReaderCommands()
    {
        using var reader = await ReaderFixture.OpenAsync(1100, 720, triage: true);
        var keyboard = reader.Shell.CreateKeyboardNavigation(reader.Session);
        var more = ReaderMenus.Button(reader.Pane, "More message actions");
        reader.Session.SetFocus(more.Trigger);
        foreach (int code in new[] { 0x45, 0x4B, 0x46, 0x52 })
            Assert.False(keyboard.Handle(FindKey(code, KeyboardModifierState.Control | KeyboardModifierState.Alt)));
        Assert.True(more.Expand());
        foreach (int code in new[] { 0x45, 0x4B, 0x46, 0x52 })
        {
            var key = FindKey(code, KeyboardModifierState.Control | KeyboardModifierState.Alt);
            Assert.False(keyboard.Handle(key));
            reader.Session.DispatchInput(key);
        }
        reader.Dispatcher.Drain();
        Assert.True(more.IsExpanded);
        Assert.Empty(reader.Triage!.ReadChanges);
        Assert.Equal(UiVisibility.Collapsed, reader.Find.Visibility);
        Assert.Null(reader.Shell.ActiveDialog);
        more.Collapse();
    }

    [Theory]
    [InlineData(640, 480, 1.0, "plain", false, true)]
    [InlineData(640, 480, 1.0, "html", false, true)]
    [InlineData(640, 480, 1.0, "long", false, false)]
    [InlineData(640, 520, 1.0, "plain", false, true)]
    [InlineData(640, 540, 1.0, "plain", false, true)]
    [InlineData(640, 640, 1.0, "long", false, true)]
    [InlineData(640, 480, 2.0, "plain", false, false)]
    [InlineData(640, 640, 2.0, "long", false, false)]
    [InlineData(700, 480, 2.0, "plain", false, false)]
    [InlineData(700, 520, 2.0, "long", false, false)]
    // Labeled Reply, Reply all and Forward need more than one row in this 412-DIP pane, so they end the header.
    [InlineData(700, 1000, 2.0, "plain", false, true)]
    [InlineData(1100, 720, 1.0, "plain", true, true)]
    [InlineData(1100, 720, 1.0, "longer", true, true)]
    [InlineData(1100, 720, 2.0, "plain", true, true)]
    [InlineData(1100, 720, 2.0, "long", true, true)]
    [InlineData(1100, 680, 2.0, "plain", true, true)]
    [InlineData(1100, 740, 2.0, "plain", true, true)]
    [InlineData(1100, 640, 2.0, "plain", true, true)]
    [InlineData(1100, 500, 2.0, "plain", false, false)]
    public async Task TheReaderHeaderEndsBetweenItsRows(int width, int height, double textScale, string message, bool pinned, bool replyShown)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height, message);
            var header = reader.Header;
            BRect shown = header.Scroll.ContentBounds;
            double available = header.AvailableHeight;
            double share = available * 0.45;
            string where = $"At {width}x{height}, text {textScale:P0}, {message} message, the header shows {shown} of {available} in {reader.Pane.Bounds}";
            Assert.Equal(width < 680, reader.Layout.ShowsReaderOnly);
            AssertEndsBetweenRows(header, where);
            // Past its share, the header shows all its rows where that leaves the text six lines (the margin below
            // the last row is not a row), and scrolls otherwise. Rows end on whole pixels and the share need not,
            // so the share is compared as the header compares it.
            double rows = HeaderRows(header).Max(row => row.Bounds.Bottom) - shown.Top;
            bool scrolls = rows > share && available - rows < reader.Text.HeightOfLines(6) - 0.5;
            Assert.True(scrolls == header.Scroll.HasVerticalScrollbar, $"{where}: the header {(scrolls ? "does not scroll" : "scrolls")}; its rows end at {rows}.");

            // Beside the list, the commands are below the header where they fit; otherwise, and in the
            // compact reader, they end it.
            var reply = reader.Button("Reply");
            bool inHeader = reply.IsDescendantOf(header);
            Assert.True(pinned != inHeader, $"{where}: Reply is {(inHeader ? "in" : "below")} the header.");
            bool onScreen = inHeader ? reply.Bounds.Bottom <= shown.Bottom + 0.5
                : reply.Bounds.Top >= header.Bounds.Bottom - 0.5 && reply.Bounds.Bottom <= reader.Divider.Bounds.Top + 0.5;
            Assert.True(replyShown == onScreen, $"{where}: Reply is at {reply.Bounds}.");
            if (replyShown)
            {
                BRect row = Assert.IsType<StandardToolbar>(reply.Parent).Bounds;
                Assert.True(reader.Divider.Bounds.Top - row.Bottom >= CommandsGap - 0.5, $"{where}: Reply's row ends at {row.Bottom}, the line is at {reader.Divider.Bounds}.");
            }
            // Below the header, they are on one row.
            if (pinned)
                foreach (var command in new[] { "Reply all", "Forward" })
                    Assert.Equal(reply.Bounds.Top, reader.Button(command).Bounds.Top, 0.5);

            // Past its share, the rows the header shows end within it or leave the text six lines (the margin
            // below the last row, and the gap below a row of buttons it ends with, are not rows); with Reply
            // below, it keeps to its share.
            double rowsEnd = HeaderRows(header).Where(row => row.Bounds.Top < shown.Bottom - 0.5).Max(row => Math.Min(row.Bounds.Bottom, shown.Bottom)) - shown.Top;
            if (header.Bounds.Height > share + 0.5 && rowsEnd > share + 0.5)
                Assert.True(available - rowsEnd >= reader.Text.HeightOfLines(6) - 0.5, $"{where}; the text has {reader.Text.Bounds.Height}.");
            if (!replyShown)
                Assert.True(header.Bounds.Height <= share + 0.5, where);
            AssertLineBetweenHeaderAndText(reader, where);

            // Beside the list, the header shows the subject's first line whole and leaves the text two lines.
            if (!reader.Layout.IsCompact)
            {
                var subject = HeaderRows(header)[0];
                Assert.IsType<StandardLabel>(subject);
                Assert.True(shown.Bottom >= subject.Bounds.Top + BTextMeasurer.GetLineHeight(((StandardLabel)subject).Font) - 0.5, $"{where}: the subject at {subject.Bounds} is cut.");
                Assert.True(reader.Text.Bounds.Height >= reader.Text.HeightOfLines(2) - 0.5, $"{where}; the text has {reader.Text.Bounds.Height}.");
            }
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// In the compact reader, the header shows Reply past its share exactly where that leaves the text six
    /// lines, and scrolls it away one pixel shorter. The window is made from where the rows were laid out, as
    /// how the shell's toolbar wraps, and so where the reader starts, depends on the platform's fonts. The gap
    /// below Reply's row may take from the six lines, so it is not counted.
    /// </summary>
    [Theory]
    [InlineData(640, 552, 1.25, "html")]
    [InlineData(640, 480, 1.0, "plain")]
    public async Task TheCompactReaderShowsReplyExactlyWhereThatLeavesTheTextSixLines(int width, int height, double textScale, string message)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height, message);
            var header = reader.Header;
            var reply = reader.Button("Reply");
            var row = Assert.IsType<StandardToolbar>(reply.Parent);
            double needed = row.Bounds.Bottom - header.Scroll.ContentBounds.Top + reader.Text.HeightOfLines(6);
            int tall = height + (int)Math.Ceiling(needed - header.AvailableHeight);
            reader.Resize(width, tall);
            string where = $"At {width}x{tall}, text {textScale:P0}, the header shows {header.Scroll.ContentBounds} of {header.AvailableHeight}";
            Assert.True(reader.Layout.ShowsReaderOnly, where);
            // Reply's row is past the share, so the six lines, not the share, decide whether it is shown.
            Assert.True(row.Bounds.Bottom - header.Scroll.ContentBounds.Top > (header.AvailableHeight * header.MaximumFraction) + 0.5, $"{where}: Reply's row ends at {row.Bounds.Bottom}, within the share.");
            Assert.True(reply.Bounds.Bottom <= header.Scroll.ContentBounds.Bottom + 0.5, $"{where}: Reply is at {reply.Bounds}.");
            AssertEndsBetweenRows(header, where);
            AssertLineBetweenHeaderAndText(reader, where);

            reader.Resize(width, tall - 1);
            where = $"At {width}x{tall - 1}, text {textScale:P0}, the header shows {header.Scroll.ContentBounds} of {header.AvailableHeight}";
            Assert.False(reply.Bounds.Bottom <= header.Scroll.ContentBounds.Bottom + 0.5, $"{where}: Reply is at {reply.Bounds}.");
            Assert.True(header.Scroll.HasVerticalScrollbar, $"{where}: the header does not scroll.");
            AssertEndsBetweenRows(header, where);
            AssertLineBetweenHeaderAndText(reader, where);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// Where the reader header may end below its last row, but not below the margin under it, it shows the
    /// margin too, without a scroll bar that would scroll only the margin and narrow every row. The window is
    /// made from where the rows were laid out, so that the header may end halfway into the margin, whatever
    /// the platform's fonts make of the rows: in the compact reader, which ends with Reply, the six lines the
    /// header leaves the text set that end; beside the list, which ends with the date, its share does.
    /// </summary>
    [Theory]
    [InlineData(640, 400, 1.0)]
    [InlineData(1100, 640, 2.0)]
    public async Task TheReaderHeaderShowsTheMarginBelowItsLastRowWithoutAScrollBar(int width, int height, double textScale)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            // Here the header scrolls, so its rows are laid out beside the scroll bar, where it measures them.
            using var reader = await ReaderFixture.OpenAsync(width, height);
            var header = reader.Header;
            Assert.True(header.Scroll.HasVerticalScrollbar, $"At {width}x{height}, the header shows {header.Scroll.ContentBounds} of {header.AvailableHeight} and does not scroll.");
            double rows = HeaderRows(header).Max(row => row.Bounds.Bottom) - header.Scroll.ContentBounds.Top;
            double margin = ((ReadingColumn)header.Scroll.Children.Single()).VerticalMargin;
            double text = reader.Text.HeightOfLines(6);

            // The header may take its share, or more where that leaves the text six lines; the reader is made as
            // tall as lets it end halfway into the margin below its last row.
            double end = rows + (margin / 2);
            reader.Resize(width, height + (int)Math.Round(Math.Min(end / 0.45, end + text) - header.AvailableHeight));
            double available = header.AvailableHeight;
            double limit = Math.Max(available * 0.45, available - text);
            BRect shown = header.Scroll.ContentBounds;
            double last = HeaderRows(header).Max(row => row.Bounds.Bottom) - shown.Top;
            string where = $"At {width}, text {textScale:P0}, the header's rows end at {last} and it may end at {limit} of {available}; it shows {shown}";
            Assert.True(reader.Button("Reply").IsDescendantOf(header) == reader.Layout.IsCompact, $"{where}: Reply is {(reader.Layout.IsCompact ? "below" : "in")} the header.");
            Assert.True(last <= limit && limit < last + margin - 0.5, $"{where}: the window does not let the header end inside its margin.");

            Assert.False(header.Scroll.HasVerticalScrollbar, $"{where}: the header scrolls.");
            Assert.Equal(last + margin, header.Bounds.Height, 0.5);
            AssertLineBetweenHeaderAndText(reader, where);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// Resizing between the side-by-side layout and the compact reader moves Reply, Reply all and Forward
    /// between their place below the header and the end of the header, and keeps keyboard focus on them.
    /// </summary>
    [Fact]
    public async Task ResizingMovesTheReaderCommandsAndKeepsTheirFocus()
    {
        using var reader = await ReaderFixture.OpenAsync(640, 480);
        var header = reader.Header;
        var replyAll = reader.Button("Reply all");
        Assert.True(replyAll.IsDescendantOf(header));
        reader.Session.SetFocus(replyAll);

        reader.Resize(1100);
        Assert.False(reader.Layout.IsCompact);
        Assert.False(replyAll.IsDescendantOf(header));
        Assert.True(replyAll.Bounds.Top >= header.Bounds.Bottom - 0.5 && replyAll.Bounds.Bottom <= reader.Divider.Bounds.Top + 0.5, $"Reply all is at {replyAll.Bounds}.");
        Assert.Same(replyAll, reader.Session.FocusedElement);

        reader.Resize(640);
        Assert.True(reader.Layout.ShowsReaderOnly);
        Assert.True(replyAll.IsDescendantOf(header));
        Assert.Same(replyAll, reader.Session.FocusedElement);
        // Tab order follows: the header's rows, Reply, Reply all and Forward, then the message text.
        var stops = MailKeyboardNavigation.TabStops(reader.Pane).ToList();
        Assert.True(stops.IndexOf(reader.Button(InboxView.BackText)) < stops.IndexOf(reader.Button("Reply")));
        Assert.True(stops.IndexOf(reader.Button("Forward")) < stops.IndexOf(reader.Text.Editor));
    }

    /// <summary>
    /// Beside the list, at twice the text size, Reply, Reply all and Forward stay below the header while the
    /// reader has room, and move into the header when the reader is too short to leave room for the message.
    /// Resizing keeps keyboard focus on the same action.
    /// </summary>
    [Fact]
    public async Task BesideTheListTheCommandsMoveIntoAShortHeaderAndKeepTheirFocus()
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(1100, 720);
            var header = reader.Header;
            var replyAll = reader.Button("Reply all");
            Assert.False(replyAll.IsDescendantOf(header));
            reader.Session.SetFocus(replyAll);

            reader.Resize(700, 340);
            Assert.False(reader.Layout.IsCompact);
            Assert.True(replyAll.IsDescendantOf(header));
            Assert.Same(replyAll, reader.Session.FocusedElement);
            BRect shown = replyAll.Bounds;
            reader.Session.RenderFrame();
            Assert.Equal(shown, replyAll.Bounds);
            Assert.True(replyAll.IsDescendantOf(header));

            reader.Resize(1100, 720);
            Assert.False(replyAll.IsDescendantOf(header));
            Assert.Same(replyAll, reader.Session.FocusedElement);
            Assert.Equal(reader.Button("Reply").Bounds.Top, replyAll.Bounds.Top, 0.5);
            Assert.True(replyAll.Bounds.Top >= header.Bounds.Bottom - 0.5 && replyAll.Bounds.Bottom <= reader.Divider.Bounds.Top + 0.5, $"Reply all is at {replyAll.Bounds}.");
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// The line between the reader's header and the message text is drawn in the theme's border color, and
    /// follows a change of theme.
    /// </summary>
    [Fact]
    public async Task TheLineBelowTheReaderHeaderHasTheThemesBorderColor()
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        try
        {
            using var reader = await ReaderFixture.OpenAsync(1100, 720);
            foreach (var theme in new[] { StandardThemeTokens.Light, StandardThemeTokens.Dark, StandardThemeTokens.HighContrastDark })
            {
                StandardThemeController.Apply(reader.Session, theme);
                var frame = reader.Session.RenderFrame();
                BRect line = reader.Divider.Bounds;
                var fills = frame!.Commands.OfType<BRenderCommand.FillRect>().Where(fill => Math.Abs(fill.Rect.Bottom - line.Bottom) < 0.5).ToArray();
                Assert.Contains(fills, fill => fill.Color == theme.Border && fill.Rect.Height <= 1.5
                    && Math.Abs(fill.Rect.Left - reader.Pane.Bounds.Left) < 0.5 && Math.Abs(fill.Rect.Right - reader.Pane.Bounds.Right) < 0.5);
            }
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// The reader's date line wraps after its separator, which ends the date, so the next line starts with
    /// the read state, whole, and not with a "·" that reads as a bullet, as it did when the separator was
    /// joined to the read state. Narrower, it also wraps after "Received", but never inside the date, which
    /// left the separator in the middle of the next line, as in the gallery's inbox at 700x480 with twice the
    /// text size ("Received 9/28/2026 10:00" above "AM · Unread on server").
    /// </summary>
    [Fact]
    public async Task TheDateLineWrapsAfterItsSeparator()
    {
        using var reader = await ReaderFixture.OpenAsync(640, 480, received: new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        var date = Descendants(reader.Pane).OfType<StandardLabel>().Single(label => label.Text.StartsWith("Received ", StringComparison.Ordinal));
        double line = BTextMeasurer.GetLineHeight(date.Font);
        // From the widest compact reader in which it wraps (the line's last word moves to the next line) to
        // one in which the date takes a line of its own.
        int lineCount = 1;
        for (int width = 640; lineCount < 3; width -= 4)
        {
            Assert.True(width > 160, $"The date line wraps into {lineCount} lines at most.");
            reader.Resize(width);
            lineCount = (int)Math.Round(date.Bounds.Height / line);
            if (lineCount < 2) continue;
            string[] lines = reader.Session.RenderFrame().Commands.OfType<BRenderCommand.DrawText>().Select(text => text.Text.Text)
                .SkipWhile(text => !text.StartsWith("Received", StringComparison.Ordinal)).Take(lineCount).Select(text => text.TrimEnd()).ToArray();
            string where = $"At {width}, the date line wraps as '{string.Join("' / '", lines)}'.";
            Assert.True(lines.SkipLast(1).All(text => text == "Received" || text.EndsWith("\u00A0·", StringComparison.Ordinal)), where);
            Assert.True(lines[^1] is "Read\u00A0on\u00A0server" or "Unread\u00A0on\u00A0server", where);
        }
    }

    /// <summary>
    /// With no message text, the header may take the whole reader. It does not end above a row it would cut
    /// there: nothing below would use the space, so the problem and Retry would only be scrolled away
    /// above an empty text area, as they were at 640x570 with twice the text size.
    /// </summary>
    [Theory]
    [InlineData(640, 570, 2.0, "error")]
    [InlineData(640, 480, 1.5, "error")]
    [InlineData(640, 600, 1.5, "long error")]
    public async Task WithoutMessageTextAHeaderThatScrollsTakesTheWholeReader(int width, int height, double textScale, string message)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(width, height, message);
            var header = reader.Header;
            string where = $"At {width}x{height}, text {textScale:P0}, {message}, the header is {header.Bounds} of {header.AvailableHeight} in {reader.Pane.Bounds}";
            Assert.True(reader.Layout.ShowsReaderOnly);
            Assert.Equal("", reader.Text.Text);
            Assert.True(header.Scroll.HasVerticalScrollbar, $"{where}: the header fits; the window is not short enough.");
            // All the height the line below it leaves: the compact reader ends its header with the commands.
            Assert.True(Math.Abs(header.AvailableHeight - header.Bounds.Height) < 0.5, where);
            AssertLineBetweenHeaderAndText(reader, where);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// Ending above a row near the top would leave a very short header showing nothing, so there the row
    /// is cut at the share as before: part of Back to inbox stays on screen.
    /// </summary>
    [Fact]
    public async Task AHeaderTooShortForBackToInboxStillShowsPartOfIt()
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        try
        {
            using var reader = await ReaderFixture.OpenAsync(640, 340);
            var header = reader.Header;
            BRect shown = header.Scroll.ContentBounds;
            var back = Descendants(header).OfType<StandardButton>().Single(button => button.Text == InboxView.BackText);
            Assert.True(reader.Layout.ShowsReaderOnly);
            Assert.True(back.Bounds.Bottom > shown.Bottom, $"Back to inbox at {back.Bounds} fits the header's {shown}; the window is not short enough.");
            Assert.True(back.Bounds.Top < shown.Bottom - 0.5, $"Back to inbox is at {back.Bounds}, the header shows {shown}.");
            Assert.Equal(header.AvailableHeight * 0.45, header.Bounds.Height, 0.5);
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
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
            Assert.InRange(header.Bounds.Height, 1, (header.AvailableHeight * 0.45) + 1);
            Assert.True(header.Scroll.HasVerticalScrollbar, "The long subject must scroll within the header.");

            // A long failure keeps most of the list pane for the list.
            fail = true;
            await model.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.IsBusy);
            session.RenderFrame();
            session.RenderFrame();
            var notice = areas.Single(area => area.MaximumFraction == 0.4);
            var list = Descendants(content).OfType<StandardListView>().Single();
            var pane = Descendants(content).OfType<StandardSplitContainer>().Single().FirstPane!;
            Assert.InRange(notice.Bounds.Height, 1, (notice.AvailableHeight * 0.4) + 1);
            Assert.True(list.Bounds.Height > pane.Bounds.Height * 0.5);
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

        public void Resize(int width, int height)
        {
            _host.Height = height;
            Resize(width);
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

    /// <summary>
    /// The notice's explanation is on screen whole, or cut between two of its lines, never inside one, and
    /// shows at least one; returns its text.
    /// </summary>
    private static StandardLabel AssertNoticeEndsBetweenRows(BoundedScrollArea notice, string where)
    {
        BRect shown = notice.Scroll.ContentBounds;
        var explanation = Descendants(notice).OfType<StandardLabel>().Single();
        if (explanation.Bounds.Bottom <= shown.Bottom + 0.5) return explanation;
        double line = BTextMeasurer.GetLineHeight(explanation.Font);
        double lines = (shown.Bottom - explanation.Bounds.Top) / line;
        Assert.True(lines >= 0.99 && Math.Abs(lines - Math.Round(lines)) < 0.01, $"{where}: the explanation at {explanation.Bounds} is cut inside a line of {line}.");
        return explanation;
    }

    /// <summary>The rows of the reader header: Back, the subject, the sender and recipients, the date, Reply, the HTML preview.</summary>
    private static UiElement[] HeaderRows(BoundedScrollArea header) =>
        header.Scroll.Children.Single().Children.Single().Children.Where(row => row.Visibility == UiVisibility.Visible && row.Bounds.Height > 0).ToArray();

    /// <summary>
    /// Each row of the header is on screen whole, or scrolled below the header whole, or, for the subject,
    /// the sender and recipients, and the date, cut between two of its lines. A row so near the top that
    /// ending above it would leave less than half the share, such as Back to inbox in a very short window,
    /// may be cut exactly at the share, as <see cref="BoundedScrollArea.Rows"/> says.
    /// </summary>
    private static void AssertEndsBetweenRows(BoundedScrollArea header, string where)
    {
        BRect shown = header.Scroll.ContentBounds;
        double share = header.AvailableHeight * header.MaximumFraction;
        foreach (var row in HeaderRows(header))
        {
            double line = row switch
            {
                StandardLabel label => BTextMeasurer.GetLineHeight(label.Font),
                SubjectRow subject => BTextMeasurer.GetLineHeight(subject.Label.Font),
                FocusRingFrame { Content: StandardRichEdit edit } => BTextMeasurer.GetLineHeight(edit.Font),
                _ => 0,
            };
            // The header ends between lines at the line height it is given, which is the text's.
            if (line > 0)
                Assert.True(IsWhole(row.Bounds.Height / line), $"{where}: the {row.GetType().Name} at {row.Bounds} is not lines of {line}.");
            if (row.Bounds.Bottom <= shown.Bottom + 0.5 || row.Bounds.Top >= shown.Bottom - 0.5) continue;
            if (row.Bounds.Top - shown.Top <= (share / 2) + 0.5 && Math.Abs(shown.Height - share) < 0.5) continue;
            Assert.True(line > 0 && IsWhole((shown.Bottom - row.Bounds.Top) / line), $"{where}: the {row.GetType().Name} at {row.Bounds} is cut.");
        }

        bool IsWhole(double lines) => Math.Abs(lines - Math.Round(lines)) < 0.01;
    }

    /// <summary>
    /// A line across the reader separates what is above it, the header and its commands, from the message
    /// text, which has the rest of the reader. The header's share is of the height the commands and the
    /// line leave.
    /// </summary>
    private static void AssertLineBetweenHeaderAndText(ReaderFixture reader, string where)
    {
        BRect pane = reader.Pane.Bounds, header = reader.Header.Bounds, line = reader.Divider.Bounds;
        Assert.True(line.Height > 0 && line.Top >= header.Bottom - 0.5, $"{where}: the line is at {line}, the header at {header}.");
        Assert.Equal(pane.Left, line.Left, 0.5);
        Assert.Equal(pane.Right, line.Right, 0.5);
        Assert.Equal(line.Bottom, reader.Text.Bounds.Top, 0.5);
        Assert.Equal(pane.Bottom, reader.Text.Bounds.Bottom, 0.5);
        Assert.Equal(pane.Height - (line.Bottom - header.Bottom), reader.Header.AvailableHeight, 0.5);
    }

    /// <summary>The shell's inbox with a message open in the reader: its sender, its recipient, and Reply.</summary>
    private sealed class ReaderFixture(MailShellView shell, UiSession session, TestDirectory directory, Host host, TestQueueDispatcher dispatcher, MailShellViewModel model, TriageReceiver? triage = null) : IDisposable
    {
        private const string LongSender = "Maximilian Alexander von Langenstein-Habsburg <maximilian.alexander.von.langenstein-habsburg.office@subdomain.example.test>";
        private const string LongSubject = "Re: Fwd: Agenda, travel arrangements, accessibility requirements, and the revised budget spreadsheet for the cross-team planning workshop in Zürich";
        private static readonly string LongerSubject = string.Join(" ", Enumerable.Repeat("Agenda, travel arrangements, and the revised budget", 6));

        public AdaptiveInboxLayout Layout => Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();
        public BoundedScrollArea Header => Descendants(shell.Window).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
        public ScrollableMessageText Text => Descendants(shell.GetContent("inbox")).OfType<ScrollableMessageText>().Single();
        /// <summary>The reader pane: the header, the commands below it, the line, and the message text.</summary>
        public UiElement Pane => Descendants(shell.Window).OfType<StandardSplitContainer>().Single().SecondPane!;
        public Divider Divider => Descendants(Pane).OfType<Divider>().Single();
        public UiSession Session => session;
        /// <summary>The receiver that records triage, when the fixture was opened with <c>triage: true</c>.</summary>
        public TriageReceiver? Triage => triage;
        public InboxViewModel Inbox => model.Inbox;
        /// <summary>The shell's keyboard navigation, which the window runs before it dispatches a key.</summary>
        public MailKeyboardNavigation Navigation { get; } = new(session, shell, model);
        public StandardButton Button(string text) => Descendants(Pane).OfType<StandardButton>().Single(button => button.Text == text);
        /// <summary>A command of the inbox outside the reader, such as Receive mail on the toolbar.</summary>
        public StandardButton InboxButton(string text) => Descendants(shell.Window).OfType<StandardButton>().Single(button => button.Text == text);
        public MailShellView Shell => shell;
        public TestQueueDispatcher Dispatcher => dispatcher;
        public StandardListView List => Descendants(shell.Window).OfType<StandardListView>().Single();
        /// <summary>The open message's find bar, which docks in the reader's banner slot below the line.</summary>
        public MessageTextFindBar Find => Descendants(Pane).OfType<MessageTextFindBar>().Single();
        public Broiler.UI.Edit.Standard.StandardEdit FindQuery => Descendants(Find).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(edit => edit.AccessibleName == "Find in message text");
        /// <summary>The slot below the line that holds the reader's banners, collapsed while none is shown.</summary>
        public BoundedScrollArea Banners => Descendants(Pane).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Reader notices");

        /// <summary>Lays the window out again and runs what its layout posted, as the next frame would.</summary>
        public void Render()
        {
            session.RenderFrame();
            dispatcher.Drain();
            session.RenderFrame();
        }

        public void Resize(int width)
        {
            host.Width = width;
            shell.Window.InvalidateMeasure();
            session.RenderFrame();
            dispatcher.Drain();
            session.RenderFrame();
        }

        public void Resize(int width, int height)
        {
            host.Height = height;
            Resize(width);
        }

        /// <param name="message">
        /// "plain"; "html", which adds an HTML unavailability notice; "long",
        /// with a long subject and sender, as the long-message fixture; "longer", with a subject of six
        /// lines at 1100x720, as in ShellLayoutTests; or "error" and "long error", whose body fails to load,
        /// so the reader has no message text, only the problem and Retry.
        /// </param>
        /// <param name="received">
        /// When the message was received, which the reader shows as the gallery does, in US English ("9/28/2026
        /// 10:00 AM"); without it, the date is unknown.
        /// </param>
        /// <param name="triage">
        /// Whether the account can triage: the receiver then discovers INBOX, Archive and Trash and can move,
        /// mark read, star and permanently delete, so the reader shows those controls as production does. The
        /// default keeps the plain receiver, which offers none of them.
        /// </param>
        public static async Task<ReaderFixture> OpenAsync(int width, int height, string message = "plain", DateTimeOffset? received = null, bool triage = false)
        {
            bool html = message == "html";
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            var messages = Fixture.CreateMessages(account, 5);
            if (message == "longer") messages[0] = messages[0] with { Subject = LongerSubject };
            else if (message.StartsWith("long", StringComparison.Ordinal)) messages[0] = messages[0] with { Subject = LongSubject, Sender = LongSender };
            if (received is not null) messages[0] = messages[0] with { ReceivedAt = received };
            var plain = Fixture.CreateReceiver(messages);
            plain.Body = (key, _) => message.EndsWith("error", StringComparison.Ordinal)
                ? throw new MailConnectionException(message == "error" ? "The connection closed."
                    : "The server closed the connection while sending this message. It may be busy or restarting. Check the connection, then retry.")
                : Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Repeat("A paragraph of the message body.", 20)), null)
                {
                    HtmlUnavailableReason = html ? "HTML exceeds the preview limits. The text reader remains available." : null,
                    Composition = new MailCompositionSource { From = ["sender@example.test"], To = [account.EmailAddress], Subject = "Subject", MessageId = "message@example.test" },
                });
            var triageReceiver = triage ? new TriageReceiver(plain) : null;
            IMailReceiver receiver = (IMailReceiver?)triageReceiver ?? plain;
            var dispatcher = new TestQueueDispatcher();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model, null,
                received is null ? null : new MessageDateFormatter(culture: CultureInfo.GetCultureInfo("en-US")));
            var host = new Host(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            shell.Attach(session);
            shell.ShowView("inbox");
            if (triage)
            {
                // Discover the folders first, so Archive and Trash are known targets when the message opens.
                await model.Inbox.RefreshFoldersAsync();
                dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            }
            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            await model.Inbox.SelectAsync(messages[0].Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            Assert.True(shell.Inbox.OpenSelected());
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            session.RenderFrame();
            // What the frame's layout posted, as the window drains it before the next frame.
            dispatcher.Drain();
            session.RenderFrame();
            return new(shell, session, directory, host, dispatcher, model, triageReceiver);
        }

        public void Dispose()
        {
            session.Dispose();
            shell.Dispose();
            directory.Dispose();
        }
    }

    /// <summary>
    /// A receiver that can triage, as an IMAP account can: it discovers INBOX, Archive and Trash, reads INBOX
    /// through the plain receiver it wraps, and records every move, read, star and permanent delete. Each change
    /// answers with the status its property names, NotApplied unless a test sets another.
    /// </summary>
    private sealed class TriageReceiver(TestMailReceiver inner)
        : IMailFolderReceiver, IMailMover, IMailReadStateWriter, IMailStarStateWriter, IMailPermanentDeleter
    {
        public List<(MailMessageKey Key, string Destination)> Moves { get; } = [];
        public List<(MailMessageKey Key, bool IsRead)> ReadChanges { get; } = [];
        public List<(MailMessageKey Key, bool IsStarred)> StarChanges { get; } = [];
        public List<MailMessageKey> Deletions { get; } = [];
        public MailMoveStatus MoveStatus { get; set; } = MailMoveStatus.NotApplied;
        public MailFlagChangeStatus FlagStatus { get; set; } = MailFlagChangeStatus.NotApplied;
        public MailDeleteStatus DeleteStatus { get; set; } = MailDeleteStatus.NotApplied;

        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => inner.TestConnectionAsync(account, cancellationToken);
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", "/", true, []), new("Archive", "/", true, ["\\Archive"]), new("Trash", "/", true, ["\\Trash"])]);
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            inner.GetInboxAsync(account, maximumCount, older, cancellationToken);
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            mailboxId == "INBOX" ? inner.GetInboxAsync(account, maximumCount, older, cancellationToken) : Task.FromResult(new MailInboxPage([], null));
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default) =>
            inner.GetBodyAsync(account, message, cancellationToken);

        public Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId, CancellationToken cancellationToken = default)
        {
            Moves.Add((source, destinationMailboxId));
            return Task.FromResult(new MailMoveResult(source, destinationMailboxId, MoveStatus, null, "Fixture move " + MoveStatus));
        }

        public Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead, CancellationToken cancellationToken = default)
        {
            ReadChanges.Add((key, isRead));
            return Task.FromResult(new ReadStateChangeResult(key, isRead, FlagStatus, "Fixture read state " + FlagStatus));
        }

        public Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred, CancellationToken cancellationToken = default)
        {
            StarChanges.Add((key, isStarred));
            return Task.FromResult(new MailFlagChangeResult(key, isStarred, FlagStatus, "Fixture star state " + FlagStatus));
        }

        public Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default)
        {
            Deletions.Add(key);
            return Task.FromResult(new MailDeleteResult(key, DeleteStatus, "Fixture deletion " + DeleteStatus));
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
