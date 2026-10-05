using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-10: one shortcut table, exact modifiers, reply shortcuts, and Tab traversal that follows what is shown.</summary>
[Collection("UI theme")]
public sealed class KeyboardShortcutTests
{
    [Fact]
    public void EveryShortcutIsUniqueAndMatchesItsOwnGesture()
    {
        Assert.Equal(MailShortcuts.All.Count, MailShortcuts.All.Select(s => (s.Key, s.Control, s.Shift, s.Alt)).Distinct().Count());
        foreach (var shortcut in MailShortcuts.All)
        {
            var modifiers = (shortcut.Control ? KeyboardModifierState.Control : 0) | (shortcut.Shift ? KeyboardModifierState.Shift : 0) | (shortcut.Alt ? KeyboardModifierState.Alt : 0);
            Assert.Same(shortcut, MailShortcuts.Match(Key(shortcut.Key, modifiers)));
            Assert.Equal(shortcut.Gesture.Contains("Ctrl+"), shortcut.Control);
            Assert.Equal(shortcut.Gesture.Contains("Shift+"), shortcut.Shift);
            Assert.Equal(shortcut.Gesture.Contains("Alt+"), shortcut.Alt);
        }
    }

    [Fact]
    public async Task AltGrCharactersAreNotTakenForTabShortcuts()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Shell.Navigation.SelectTab("compose");
        // AltGr is Ctrl+Alt: AltGr+2 types "²" on a German layout and must reach the editor.
        Assert.False(fixture.Keyboard.Handle(Key('2', KeyboardModifierState.Control | KeyboardModifierState.Alt | KeyboardModifierState.RightAlt)));
        Assert.Equal("compose", fixture.Shell.Navigation.SelectedTab!.Id);
        // A side-specific flag counts like the generic one.
        Assert.True(fixture.Keyboard.Handle(Key('2', KeyboardModifierState.LeftControl)));
        Assert.Equal("account", fixture.Shell.Navigation.SelectedTab!.Id);
        // Windows-key chords belong to the system.
        Assert.False(fixture.Keyboard.Handle(Key('1', KeyboardModifierState.Control | KeyboardModifierState.LeftWindows)));
    }

    [Theory]
    [InlineData('R', false, "Re: Plans & budget", "body")]
    [InlineData('R', true, "Re: Plans & budget", "body")]
    [InlineData('F', false, "Fwd: Plans & budget", "to")]
    public async Task ReplyShortcutsStartTheDraftLikeTheReaderButtons(char key, bool shift, string subject, string focus)
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Session.SetFocus(fixture.List);

        Assert.True(fixture.Keyboard.Handle(Key(key, KeyboardModifierState.Control | (shift ? KeyboardModifierState.Shift : 0))));

        Assert.Equal("compose", fixture.Shell.Navigation.SelectedTab!.Id);
        Assert.Equal(subject, fixture.Model.Composer.Subject);
        if (shift) Assert.Contains("copy@example.test", fixture.Model.Composer.Cc);
        Assert.Same(focus == "body" ? fixture.ComposerBody : fixture.ComposerTo, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task UnavailableReplyIsStillConsumedAndNewMessageFocusesTheRecipients()
    {
        using var fixture = await Fixture.OpenAsync(select: false);
        Assert.False(fixture.Model.Compose.CanRespond);
        // Consumed, so the chord never reaches an editor, and nothing starts.
        Assert.True(fixture.Keyboard.Handle(Key('R', KeyboardModifierState.Control)));
        Assert.False(fixture.Model.Composer.HasDraft);

        Assert.True(fixture.Keyboard.Handle(Key('N', KeyboardModifierState.Control)));
        Assert.True(fixture.Model.Composer.HasDraft);
        Assert.Same(fixture.ComposerTo, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task TabReachesTheSplitterInTheWideInboxAndSkipsTheCollapsedPaneWhenNarrow()
    {
        using var fixture = await Fixture.OpenAsync(width: 1100);
        var stops = MailKeyboardNavigation.TabStops(fixture.InboxContent);
        // Visual order: list, then the splitter between the panes, then the reader.
        int list = stops.ToList().IndexOf(fixture.List);
        int splitter = stops.ToList().FindIndex(stop => stop is UiSplitter);
        int reader = stops.ToList().IndexOf(fixture.ReaderText);
        Assert.True(list >= 0 && list < splitter && splitter < reader, $"list {list}, splitter {splitter}, reader {reader}");

        // Narrow with a message open: only the reader is shown, so the list pane and splitter are collapsed.
        fixture.Resize(500);
        stops = MailKeyboardNavigation.TabStops(fixture.InboxContent);
        Assert.Contains(fixture.ReaderText, stops);
        Assert.DoesNotContain(fixture.List, stops);
        Assert.DoesNotContain(stops, stop => stop is UiSplitter);
        Assert.All(stops, stop => Assert.False(stop.Bounds.IsEmpty));

        // Cycling through every stop never focuses something without bounds.
        fixture.Session.SetFocus(fixture.Shell.Navigation);
        for (int i = 0; i < stops.Count + 2; i++)
        {
            Assert.True(fixture.Keyboard.Handle(Key(0x09)));
            Assert.False(fixture.Session.FocusedElement!.Bounds.IsEmpty);
        }
    }

    [Fact]
    public async Task TabIntoTheListBringsItsSelectedRowIntoView()
    {
        // New mail or scrolling can leave the selected row out of view; the list shows its focus on that row.
        using var fixture = await Fixture.OpenAsync(height: 480, messages: 40);
        var list = fixture.List;
        Assert.Equal(0, list.SelectedIndex);
        list.ScrollIntoView(39);
        fixture.Session.RenderFrame();
        Assert.False(RowShows(list, 0));

        fixture.Session.SetFocus(fixture.Shell.Navigation);
        for (int step = 0; step < 10 && fixture.Session.FocusedElement != list; step++)
            Assert.True(fixture.Keyboard.Handle(Key(0x09)));
        Assert.Same(list, fixture.Session.FocusedElement);
        Assert.True(RowShows(list, 0));
    }

    private static bool RowShows(UiListView list, int index) =>
        list.GetItemSemanticNode(index) is { } row && !list.Bounds.Intersect(row.Bounds).IsEmpty;

    [Fact]
    public async Task AltLeftLeavesTheCompactReaderAndOtherwisePassesThrough()
    {
        using var fixture = await Fixture.OpenAsync(width: 500, select: false);
        fixture.Session.SetFocus(fixture.List);
        // Nothing to go back from: the key stays with the focused control.
        Assert.False(fixture.Keyboard.Handle(Key(0x25, KeyboardModifierState.Alt)));
        fixture.Session.DispatchInput(Key(0x28)); // Down selects the first row without opening it.
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.Inbox.IsBusy);
        Assert.True(fixture.Keyboard.Handle(Key(0x0D)));
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.Inbox.IsBusy);
        fixture.Session.RenderFrame();
        var layout = Descendants(fixture.InboxContent).OfType<AdaptiveInboxLayout>().Single();
        Assert.True(layout.ShowsReaderOnly);

        Assert.True(fixture.Keyboard.Handle(Key(0x25, KeyboardModifierState.Alt)));
        fixture.Session.RenderFrame();
        Assert.False(layout.ShowsReaderOnly);
        Assert.Same(fixture.List, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task SettingsListsEveryShortcutFromTheTable()
    {
        using var fixture = await Fixture.OpenAsync();
        var settings = fixture.Shell.Navigation.Tabs.Single(tab => tab.Id == "settings").Content!;
        var lines = Descendants(settings).OfType<StandardLabel>().Select(label => label.Text).ToHashSet();
        foreach (var shortcut in MailShortcuts.All)
            Assert.Contains($"{shortcut.Gesture}: {shortcut.Description}", lines);
    }

    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestDirectory _directory;
        private readonly Host _host;

        private Fixture(TestDirectory directory, Host host, TestQueueDispatcher dispatcher, MailShellViewModel model, MailShellView shell, UiSession session)
        {
            _directory = directory; _host = host; Dispatcher = dispatcher; Model = model; Shell = shell; Session = session;
            Keyboard = shell.CreateKeyboardNavigation(session);
        }

        public TestQueueDispatcher Dispatcher { get; }
        public MailShellViewModel Model { get; }
        public MailShellView Shell { get; }
        public UiSession Session { get; }
        public MailKeyboardNavigation Keyboard { get; }
        public UiElement InboxContent => Shell.Navigation.Tabs.Single(tab => tab.Id == "inbox").Content!;
        private UiElement ComposeContent => Shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!;
        public StandardListView List => Descendants(InboxContent).OfType<StandardListView>().Single();
        public StandardRichEdit ReaderText => Descendants(InboxContent).OfType<StandardRichEdit>().Single(edit => edit.AccessibleName == "Message text");
        public StandardRichEdit ComposerBody => Descendants(ComposeContent).OfType<StandardRichEdit>().Single();
        public StandardEdit ComposerTo => (StandardEdit)Descendants(ComposeContent).OfType<StandardLabel>().Single(label => label.Text == "To").Target!;

        public static async Task<Fixture> OpenAsync(int width = 1100, int height = 720, bool select = true, int messages = 1)
        {
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            var message = new MailMessageSummary { Key = new(account.Id, "INBOX", 7, (uint)messages), Sender = "Author & Co <author@example.test>", Subject = "Plans & budget" };
            var older = Enumerable.Range(1, messages - 1).Reverse()
                .Select(uid => new MailMessageSummary { Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "Older <older@example.test>", Subject = $"Older message {uid}" });
            var receiver = new TestMailReceiver
            {
                Inbox = (_, _) => Task.FromResult(new MailInboxPage([message, .. older], null)),
                Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Some message text.")
                {
                    Composition = new MailCompositionSource
                    {
                        Subject = "Plans & budget", From = ["author@example.test"], To = ["test@example.test"], Cc = ["copy@example.test"],
                        MessageId = "parent@example.test",
                    },
                }),
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
            session.AddRoot(shell.Window);
            shell.Navigation.SelectTab("inbox");
            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            if (select) await model.Inbox.SelectAsync(message.Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            session.RenderFrame();
            return new(directory, host, dispatcher, model, shell, session);
        }

        public void Resize(int width)
        {
            _host.Width = width;
            Shell.Window.InvalidateMeasure();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            _directory.Dispose();
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
