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
        // Where the footer points, in the compact reader as beside the list: the explanation, then Retry
        // loading, below the message's subject, sender and date.
        void AssertDetailsAndRetryBelowTheHeader()
        {
            var reader = Descendants(shell.Window).OfType<StandardSplitContainer>().Single().SecondPane!;
            BRect date = Descendants(reader).OfType<StandardLabel>().Single(label => label.Text.StartsWith("Received ", StringComparison.Ordinal)).Bounds;
            BRect details = Descendants(reader).OfType<InlineFeedback>().Single(feedback => feedback.Message.Length > 0).Bounds;
            BRect retry = Descendants(reader).OfType<StandardButton>().Single(button => button.Text == "Retry loading").Bounds;
            Assert.True(date.Bottom <= details.Top + 0.5 && details.Bottom <= retry.Top + 0.5, $"The date is at {date}, the details at {details}, Retry at {retry}.");
        }

        // Moving through the compact list loads a message without opening it, so its problem is out of sight.
        const string open = "The message could not be loaded. Open it to see the details and Retry.";
        const string below = "The message could not be loaded. Details and Retry are below the message header.";
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
        AssertDetailsAndRetryBelowTheHeader();
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
            if (expected == below) AssertDetailsAndRetryBelowTheHeader();
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
        const string limit = "Session limit reached (500 messages): older messages cannot be loaded now. Receive mail to start again from the newest page.";
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
        Assert.Equal("The message could not be loaded. Details and Retry are below the message header.", shell.Footer.Text);
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
        receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await model.Inbox.ReceiveAsync();
        Settle();
        var layout = Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();
        var inbox = shell.Navigation.Tabs.Single(tab => tab.Id == "inbox").Content!;
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
    /// too, past the share that would cut it.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0, "receive-error", false)]
    [InlineData(640, 480, 1.0, "receive-canceled", false)]
    [InlineData(640, 480, 1.0, "load-error", false)]
    [InlineData(640, 480, 2.0, "receive-error", true)]
    [InlineData(640, 480, 2.0, "receive-canceled", false)]
    [InlineData(640, 480, 2.0, "load-error", true)]
    [InlineData(640, 450, 2.0, "receive-canceled", false)]
    [InlineData(640, 320, 2.0, "receive-error", true)]
    [InlineData(1100, 720, 2.0, "receive-error", false)]
    [InlineData(1100, 720, 2.0, "receive-canceled", false)]
    [InlineData(1100, 720, 2.0, "load-error", false)]
    [InlineData(1100, 560, 2.0, "receive-error", true)]
    public async Task TheInboxNoticeEndsBetweenItsLinesAndKeepsRetryInView(int width, int height, double textScale, string problem, bool scrolls)
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
            Assert.True(scrolls == notice.Scroll.HasVerticalScrollbar, $"{where}: the notice {(scrolls ? "does not scroll" : "scrolls")}.");

            // Retry's row is whole right below what the notice shows, and the list follows it.
            var retry = fixture.Button(problem == "load-error" ? "Retry loading older" : "Retry receiving");
            BRect row = retry.Parent!.Bounds;
            Assert.False(retry.IsDescendantOf(notice), where);
            Assert.Equal(notice.Bounds.Bottom, row.Top, 0.5);
            Assert.True(row.Top <= retry.Bounds.Top && retry.Bounds.Bottom <= row.Bottom && row.Height >= retry.Bounds.Height, $"{where}: Retry is at {retry.Bounds} in its row at {row}.");
            Assert.Equal(row.Bottom, fixture.List.Bounds.Top, 0.5);

            // Past its share, the notice shows its whole explanation and leaves the list two rows (the margin
            // below the last line is not a line), or else only its first line, which the share would cut.
            double share = available * 0.4;
            if (notice.Bounds.Height > share + 0.5)
            {
                double line = BTextMeasurer.GetLineHeight(label.Font);
                if (scrolls) Assert.Equal(label.Bounds.Top + line, shown.Bottom, 0.5);
                else Assert.True(available - (label.Bounds.Bottom - shown.Top) >= (2 * fixture.List.EffectiveItemHeight) - 0.5, $"{where}; the list has {fixture.List.Bounds.Height}.");
            }
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
        // Retry is inset within its row as Receive mail is within the toolbar, so the two line up.
        Assert.Equal(receive.Bounds.Left, retry.Bounds.Left, 0.5);
        Assert.Equal(receive.Bounds.Top - toolbar.Bounds.Top, retry.Bounds.Top - row.Bounds.Top, 0.5);
        Assert.Equal(toolbar.Bounds.Bottom - receive.Bounds.Bottom, row.Bounds.Bottom - retry.Bounds.Bottom, 0.5);
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
            Assert.True(Math.Abs(receive.Bounds.Left - toolbar.Bounds.Left - (button.Bounds.Left - row.Left)) < 0.5, where);
            Assert.True(Math.Abs(receive.Bounds.Top - toolbar.Bounds.Top - (button.Bounds.Top - row.Top)) < 0.5, where);
            Assert.True(Math.Abs(toolbar.Bounds.Bottom - receive.Bounds.Bottom - (row.Bottom - button.Bounds.Bottom)) < 0.5, where);
        }
    }

    /// <summary>
    /// A message whose text could not be loaded has nothing below its header, so the header may take
    /// the reader: opened in a short compact window, the problem and Retry are on screen, not scrolled
    /// away above an empty text area. Once the text arrives, the header is bounded again.
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
        BRect shown = header.Scroll.ContentBounds;
        var problem = Descendants(header).OfType<InlineFeedback>().Single();
        var retry = fixture.Button("Retry loading");
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
    /// line and six lines of text. In a reader too narrow or short for that, such as at 700x480, 700x1000
    /// or 1100x500 with twice the text size, they end the header, as in the compact reader, which has no
    /// height to spare for them: at 700x480, pinned on two rows, they left the subject less than a line and
    /// the text less than one. Beside the list the subject's first line and two lines of text are always
    /// shown. A line across the reader separates the header from the message text.
    /// </summary>
    [Theory]
    [InlineData(640, 480, 1.0, "plain", false, true, false)]
    [InlineData(640, 480, 1.0, "html", false, true, true)]
    [InlineData(640, 480, 1.0, "long", false, false, true)]
    [InlineData(640, 520, 1.0, "plain", false, true, false)]
    [InlineData(640, 540, 1.0, "plain", false, true, false)]
    [InlineData(640, 640, 1.0, "long", false, true, false)]
    [InlineData(640, 552, 1.25, "html", false, true, true)]
    [InlineData(640, 480, 2.0, "plain", false, false, true)]
    [InlineData(640, 640, 2.0, "long", false, false, true)]
    [InlineData(700, 480, 2.0, "plain", false, false, true)]
    [InlineData(700, 520, 2.0, "long", false, false, true)]
    [InlineData(700, 1000, 2.0, "plain", false, true, false)]
    [InlineData(1100, 720, 1.0, "plain", true, true, false)]
    [InlineData(1100, 720, 1.0, "longer", true, true, false)]
    [InlineData(1100, 720, 2.0, "plain", true, true, false)]
    [InlineData(1100, 720, 2.0, "long", true, true, true)]
    [InlineData(1100, 680, 2.0, "plain", true, true, false)]
    [InlineData(1100, 740, 2.0, "plain", true, true, false)]
    [InlineData(1100, 640, 2.0, "plain", true, true, true)]
    [InlineData(1100, 500, 2.0, "plain", false, false, true)]
    public async Task TheReaderHeaderEndsBetweenItsRows(int width, int height, double textScale, string message, bool pinned, bool replyShown, bool scrolls)
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
            Assert.True(scrolls == header.Scroll.HasVerticalScrollbar, $"{where}: the header {(scrolls ? "does not scroll" : "scrolls")}.");

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
    /// reader is wide enough for them on one row, and end the header in a narrower reader, where they would
    /// wrap and take the text's room; resizing moves them as it is laid out, once, and keeps keyboard focus.
    /// </summary>
    [Fact]
    public async Task BesideTheListTheCommandsEndTheHeaderWhereTheyWouldWrapAndKeepTheirFocus()
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

            reader.Resize(700);
            Assert.False(reader.Layout.IsCompact);
            Assert.True(replyAll.IsDescendantOf(header));
            Assert.Same(replyAll, reader.Session.FocusedElement);
            BRect shown = replyAll.Bounds;
            reader.Session.RenderFrame();
            Assert.Equal(shown, replyAll.Bounds);
            Assert.True(replyAll.IsDescendantOf(header));

            reader.Resize(1100);
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
            using var reader = await ReaderFixture.OpenAsync(640, 360);
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
    /// the sender and recipients, and the date, cut between two of its lines.
    /// </summary>
    private static void AssertEndsBetweenRows(BoundedScrollArea header, string where)
    {
        BRect shown = header.Scroll.ContentBounds;
        foreach (var row in HeaderRows(header))
        {
            double line = row switch
            {
                StandardLabel label => BTextMeasurer.GetLineHeight(label.Font),
                StandardRichEdit edit => BTextMeasurer.GetLineHeight(edit.Font),
                _ => 0,
            };
            // The header ends between lines at the line height it is given, which is the text's.
            if (line > 0)
                Assert.True(IsWhole(row.Bounds.Height / line), $"{where}: the {row.GetType().Name} at {row.Bounds} is not lines of {line}.");
            if (row.Bounds.Bottom <= shown.Bottom + 0.5 || row.Bounds.Top >= shown.Bottom - 0.5) continue;
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
    private sealed class ReaderFixture(MailShellView shell, UiSession session, TestDirectory directory, Host host, TestQueueDispatcher dispatcher) : IDisposable
    {
        private const string LongSender = "Maximilian Alexander von Langenstein-Habsburg <maximilian.alexander.von.langenstein-habsburg.office@subdomain.example.test>";
        private const string LongSubject = "Re: Fwd: Agenda, travel arrangements, accessibility requirements, and the revised budget spreadsheet for the cross-team planning workshop in Zürich";
        private static readonly string LongerSubject = string.Join(" ", Enumerable.Repeat("Agenda, travel arrangements, and the revised budget", 6));

        public AdaptiveInboxLayout Layout => Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single();
        public BoundedScrollArea Header => Descendants(shell.Window).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
        public ScrollableMessageText Text => Descendants(shell.Window).OfType<ScrollableMessageText>().Single();
        /// <summary>The reader pane: the header, the commands below it, the line, and the message text.</summary>
        public UiElement Pane => Descendants(shell.Window).OfType<StandardSplitContainer>().Single().SecondPane!;
        public Divider Divider => Descendants(Pane).OfType<Divider>().Single();
        public UiSession Session => session;
        public StandardButton Button(string text) => Descendants(Pane).OfType<StandardButton>().Single(button => button.Text == text);
        /// <summary>A command of the inbox outside the reader, such as Receive mail on the toolbar.</summary>
        public StandardButton InboxButton(string text) => Descendants(shell.Window).OfType<StandardButton>().Single(button => button.Text == text);

        public void Resize(int width)
        {
            host.Width = width;
            shell.Window.InvalidateMeasure();
            session.RenderFrame();
            dispatcher.Drain();
            session.RenderFrame();
        }

        /// <param name="message">
        /// "plain"; "html", which also has HTML, so the header ends with the HTML preview's row; "long",
        /// with a long subject and sender, as the long-message fixture; "longer", with a subject of six
        /// lines at 1100x720, as in ShellLayoutTests; or "error" and "long error", whose body fails to load,
        /// so the reader has no message text, only the problem and Retry.
        /// </param>
        /// <param name="received">
        /// When the message was received, which the reader shows as the gallery does, in US English ("9/28/2026
        /// 10:00 AM"); without it, the date is unknown.
        /// </param>
        public static async Task<ReaderFixture> OpenAsync(int width, int height, string message = "plain", DateTimeOffset? received = null)
        {
            bool html = message == "html";
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            var messages = Fixture.CreateMessages(account, 5);
            if (message == "longer") messages[0] = messages[0] with { Subject = LongerSubject };
            else if (message.StartsWith("long", StringComparison.Ordinal)) messages[0] = messages[0] with { Subject = LongSubject, Sender = LongSender };
            if (received is not null) messages[0] = messages[0] with { ReceivedAt = received };
            var receiver = Fixture.CreateReceiver(messages);
            receiver.Body = (key, _) => message.EndsWith("error", StringComparison.Ordinal)
                ? throw new MailConnectionException(message == "error" ? "The connection closed."
                    : "The server closed the connection while sending this message. It may be busy or restarting. Check the connection, then retry.")
                : Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Repeat("A paragraph of the message body.", 20)), html ? "<p>HTML body</p>" : null)
                {
                    Composition = new MailCompositionSource { From = ["sender@example.test"], To = [account.EmailAddress], Subject = "Subject", MessageId = "message@example.test" },
                });
            var dispatcher = new TestQueueDispatcher();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model, html ? new NoPreviewHost() : null,
                received is null ? null : new MessageDateFormatter(culture: CultureInfo.GetCultureInfo("en-US")));
            var host = new Host(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            session.AddRoot(shell.Window);
            shell.Navigation.SelectTab("inbox");
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
            return new(shell, session, directory, host, dispatcher);
        }

        public void Dispose()
        {
            session.Dispose();
            shell.Dispose();
            directory.Dispose();
        }
    }

    private sealed class NoPreviewHost : IHtmlPreviewHost
    {
        public event EventHandler<HtmlPreviewChange>? Changed { add { } remove { } }
        public MailMessageKey? Current => null;
        public Task<string> ShowAsync(MailMessageBody message) => Task.FromResult("");
        public void Close() { }
        public void Dispose() { }
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
