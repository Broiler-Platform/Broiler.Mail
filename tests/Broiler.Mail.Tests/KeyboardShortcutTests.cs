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
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-10: one shortcut table, exact modifiers, reply shortcuts, and Tab traversal that follows what is shown.</summary>
[Collection("UI theme")]
public sealed class KeyboardShortcutTests
{
    [Fact]
    public async Task MenuOpensSettingsAndEscapeReturnsToTheInbox()
    {
        using var fixture = await Fixture.OpenAsync();
        bool Dispatch(UiInputEvent input) => fixture.Keyboard.Handle(input) || fixture.Session.DispatchInput(input);
        fixture.Session.SetFocus(fixture.List);
        Assert.True(Dispatch(Key(0x79))); // F10
        Assert.True(fixture.Shell.Menu.IsOpen);
        Assert.True(Dispatch(Key(0x27))); // Message
        Assert.True(Dispatch(Key(0x27))); // Tools
        Assert.True(Dispatch(Key(0x28))); // Account
        Assert.True(Dispatch(Key(0x28))); // Settings
        Assert.True(Dispatch(Key(0x0D)));
        fixture.Dispatcher.Drain();

        Assert.Equal("settings", fixture.Shell.ActiveViewId);
        Assert.Same(fixture.Shell.ActiveDialog, fixture.Session.ModalElement);
        Assert.Equal(UiSemanticRole.Dialog, fixture.Shell.ActiveDialog!.GetSemanticNode().Role);
        Assert.True(fixture.Session.FocusedElement!.IsDescendantOf(fixture.Shell.ActiveDialog));
        Assert.False(fixture.Shell.Menu.IsOpen);
        Assert.Null(fixture.Session.CapturedElement);
        Assert.True(Dispatch(Key(0x1B)));
        Assert.Null(fixture.Session.ModalElement);
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);
        Assert.Same(fixture.List, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task DialogRetainsUnsavedFieldsAndTrapsTabFocus()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Shell.ShowView("account");
        var field = Descendants(fixture.Shell.ActiveContent).OfType<StandardEdit>().First();
        field.Text = "Unsaved account name";
        for (int i = 0; i < 70; i++)
        {
            fixture.Keyboard.Execute(i < 35 ? MailCommand.NextField : MailCommand.PreviousField);
            Assert.True(fixture.Session.FocusedElement!.IsDescendantOf(fixture.Shell.ActiveDialog!));
        }
        fixture.Shell.CloseDialog();
        fixture.Shell.ShowView("account");
        Assert.Equal("Unsaved account name", field.Text);
        Assert.True(field.IsAttached);
        Assert.Single(fixture.Shell.Window.OwnedWindows);
        fixture.Shell.ShowView("account");
        Assert.Single(fixture.Shell.Window.OwnedWindows);
    }

    [Fact]
    public async Task DialogBlocksBackgroundCommandsAndEnterDoesNotAcceptTheForm()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Shell.ShowView("settings");
        var dialog = fixture.Shell.ActiveDialog;
        fixture.Session.SetFocus(Descendants(fixture.Shell.ActiveContent).OfType<StandardEdit>().First());
        Assert.True(fixture.Keyboard.Handle(Key(0x0D)));
        Assert.Same(dialog, fixture.Shell.ActiveDialog);
        Assert.True(fixture.Keyboard.Execute(MailCommand.Receive));
        Assert.True(fixture.Keyboard.Execute(MailCommand.NewMessage));
        Assert.True(fixture.Keyboard.Execute(MailCommand.Reply));
        Assert.False(fixture.Model.Composer.HasDraft);
        Assert.Same(dialog, fixture.Session.ModalElement);
    }

    [Fact]
    public async Task ClosingComposerKeepsTheDraftAndRestoresTheReadersFocus()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Session.SetFocus(fixture.List);
        fixture.Keyboard.Execute(MailCommand.NewMessage);
        fixture.ComposerBody.SetPlainText("Keep this unfinished message.");
        fixture.Keyboard.Execute(MailCommand.BackOrCancel);
        Assert.Same(fixture.List, fixture.Session.FocusedElement);
        Assert.True(fixture.Model.Composer.HasDraft);
        fixture.Keyboard.Execute(MailCommand.Compose);
        Assert.Equal("Keep this unfinished message.", fixture.ComposerBody.GetPlainText());
    }

    [Fact]
    public async Task EscapeClosesTheMenuWithoutClosingTheApplication()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Keyboard.Handle(Key(0x79));
        Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
        Assert.False(fixture.Shell.Menu.IsOpen);
        Assert.Null(fixture.Session.CapturedElement);
        Assert.False(fixture.Shell.Window.IsClosed);
    }

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
        fixture.Shell.ShowView("compose");
        // AltGr is Ctrl+Alt: AltGr+2 types "²" on a German layout and must reach the editor.
        Assert.False(fixture.Keyboard.Handle(Key('2', KeyboardModifierState.Control | KeyboardModifierState.Alt | KeyboardModifierState.RightAlt)));
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        // A side-specific flag counts like the generic one.
        Assert.True(fixture.Keyboard.Handle(Key('2', KeyboardModifierState.LeftControl)));
        Assert.Equal("account", fixture.Shell.ActiveViewId);
        // Windows-key chords belong to the system.
        Assert.False(fixture.Keyboard.Handle(Key('1', KeyboardModifierState.Control | KeyboardModifierState.LeftWindows)));
    }

    [Theory]
    [InlineData('R', false, "Re: Plans & budget", "body")]
    [InlineData('R', true, "Re: Plans & budget", "body")]
    [InlineData('L', false, "Fwd: Plans & budget", "to")]
    public async Task ReplyShortcutsStartTheDraftLikeTheReaderButtons(char key, bool shift, string subject, string focus)
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Session.SetFocus(fixture.List);

        Assert.True(fixture.Keyboard.Handle(Key(key, KeyboardModifierState.Control | (shift ? KeyboardModifierState.Shift : 0))));

        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.Equal(subject, fixture.Model.Composer.Subject);
        if (shift) Assert.Contains("copy@example.test", fixture.Model.Composer.Cc);
        Assert.Same(focus == "body" ? fixture.ComposerBody : fixture.ComposerTo, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task CtrlShiftFIsTheAlternativeForwardShortcut()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Session.SetFocus(fixture.List);
        Assert.True(fixture.Keyboard.Handle(Key('F', KeyboardModifierState.Control | KeyboardModifierState.Shift)));
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.Equal("Fwd: Plans & budget", fixture.Model.Composer.Subject);
        Assert.Same(fixture.ComposerTo, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task CtrlFFindsInTheOpenMessageAndNeverStartsAForward()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Session.SetFocus(fixture.List);
        Assert.True(fixture.Keyboard.Handle(Key('F', KeyboardModifierState.Control)));
        // Find docks in the reader above the text: no dialog opens, and its query has focus.
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);
        Assert.Null(fixture.Shell.ActiveDialog);
        var find = Descendants(fixture.InboxContent).OfType<Broiler.Mail.Application.Preview.MessageTextFindBar>().Single();
        Assert.Equal(UiVisibility.Visible, find.Visibility);
        Assert.Same(Descendants(find).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Find in message text"), fixture.Session.FocusedElement);
        Assert.False(fixture.Model.Composer.HasDraft);
        // Escape closes the bar and returns to the message text.
        Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
        Assert.Equal(UiVisibility.Collapsed, find.Visibility);
        Assert.Same(fixture.ReaderText, fixture.Session.FocusedElement);
        // AltGr+F types a character on some layouts and must reach the editor.
        fixture.Shell.ShowView("compose");
        Assert.False(fixture.Keyboard.Handle(Key('F', KeyboardModifierState.Control | KeyboardModifierState.Alt)));
        Assert.False(fixture.Keyboard.Handle(Key('L', KeyboardModifierState.Control | KeyboardModifierState.Alt)));
        Assert.False(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control | KeyboardModifierState.Alt)));
    }

    [Fact]
    public async Task CtrlFWithoutAnOpenMessageIsConsumedWithoutComposing()
    {
        using var fixture = await Fixture.OpenAsync(select: false);
        Assert.True(fixture.Keyboard.Handle(Key('F', KeyboardModifierState.Control)));
        Assert.True(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control)));
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.True(fixture.Keyboard.Handle(Key('E', KeyboardModifierState.Control)));
        // Without a cache there is no saved search, and nothing opens a dialog in its place.
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);
        Assert.False(fixture.Shell.ShowView("search-mail"));
        Assert.DoesNotContain(Descendants(fixture.InboxContent).OfType<Broiler.Mail.Application.Preview.MessageTextFindBar>(),
            bar => bar.Visibility == UiVisibility.Visible);
        Assert.False(fixture.Shell.ShowView("find-message"));
        // The Message menu's Find is unavailable without an open message.
        Assert.False(fixture.Shell.Menu.Items.Single(item => item.Id == "message").Children.Single(item => item.Id == "find-message").IsEnabled);
        Assert.False(fixture.Model.Composer.HasDraft);
    }

    [Fact]
    public void ViewShortcutsAreListedInAscendingOrderAndNoneUsesCtrlAlt()
    {
        var digits = MailShortcuts.All.Where(s => s.Control && !s.Shift && !s.Alt && s.Key is >= '1' and <= '9').Select(s => s.Key).ToArray();
        Assert.Equal(['1', '2', '3', '4', '5', '6', '7'], digits);
        Assert.DoesNotContain(MailShortcuts.All, s => s.Control && s.Alt);
        Assert.Equal("Ctrl+L", MailShortcuts.For(MailCommand.Forward).Gesture);
        Assert.Equal("Ctrl+F", MailShortcuts.For(MailCommand.FindInMessage).Gesture);
        Assert.Equal("Ctrl+K", MailShortcuts.For(MailCommand.SearchMail).Gesture);
    }

    [Fact]
    public async Task FileReceiveIsEnabledExactlyWhenTheToolbarReceiveIs()
    {
        using var fixture = await Fixture.OpenAsync();
        var receive = fixture.Shell.Menu.Items.Single(item => item.Id == "file").Children.Single(item => item.Id == "receive");
        Assert.Equal(fixture.Model.Inbox.CanReceive, receive.IsEnabled);
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
        fixture.Session.SetFocus(fixture.Shell.NavigationFocus);
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

        fixture.Session.SetFocus(fixture.Shell.NavigationFocus);
        for (int step = 0; step < 10 && fixture.Session.FocusedElement != list; step++)
            Assert.True(fixture.Keyboard.Handle(Key(0x09)));
        Assert.Same(list, fixture.Session.FocusedElement);
        Assert.True(RowShows(list, 0));
    }

    private static bool RowShows(UiListView list, int index) =>
        list.GetItemSemanticNode(index) is { } row && !list.Bounds.Intersect(row.Bounds).IsEmpty;

    [Theory]
    [InlineData("account")]
    [InlineData("settings")]
    [InlineData("compose")]
    public async Task TabAndShiftTabScrollAFieldInWithRoomForItsRing(string tab)
    {
        // A short window, so each form scrolls and Mail's own Tab handling brings the fields in.
        using var fixture = await Fixture.OpenAsync(width: 640, height: 300);
        if (tab == "compose") fixture.Model.Compose.StartNew();
        fixture.Shell.ShowView(tab);
        fixture.Session.RenderFrame();
        var stops = MailKeyboardNavigation.TabStops(fixture.Shell.ActiveContent);
        // An edit strokes its 2 DIP ring centered on its bounds, so 1 DIP of it lies outside them. A fixed
        // value rather than the form's inset, so Mail notices if the room goes away.
        const double ringOutside = 1;
        var cut = new List<string>();
        bool scrolled = false;
        foreach (var modifiers in new[] { KeyboardModifierState.None, KeyboardModifierState.Shift })
        {
            fixture.Session.SetFocus(fixture.Shell.NavigationFocus);
            for (int step = 0; step <= stops.Count; step++)
            {
                Assert.True(fixture.Keyboard.Handle(Key(0x09, modifiers)));
                var focused = fixture.Session.FocusedElement!;
                for (var parent = focused.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is not StandardScrollView { HasVerticalScrollbar: true } scroll) continue;
                    scrolled = true;
                    double above = focused.Bounds.Top - scroll.ContentBounds.Top;
                    double below = scroll.ContentBounds.Bottom - focused.Bounds.Bottom;
                    // A control taller than the view, such as the message body, keeps only its top in view.
                    bool fits = focused.Bounds.Height + (2 * ringOutside) <= scroll.ContentBounds.Height;
                    if (above < ringOutside - 0.01 || (fits && below < ringOutside - 0.01))
                        cut.Add($"{modifiers} {focused.GetType().Name} {focused.Bounds} in {scroll.ContentBounds}");
                }
            }
        }
        Assert.True(scrolled, $"Nothing in the {tab} tab scrolled; the window is not short enough.");
        Assert.True(cut.Count == 0, string.Join(Environment.NewLine, cut));
    }

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
    public async Task TheReadersEditorsAreTabStopsInReadingOrderAndAreRingedWhileFocused()
    {
        using var fixture = await Fixture.OpenAsync();
        var stops = MailKeyboardNavigation.TabStops(fixture.InboxContent).ToList();
        int details = stops.IndexOf(fixture.ReaderDetails);
        int reply = stops.FindIndex(stop => stop is StandardButton { Text: "Reply" });
        int text = stops.IndexOf(fixture.ReaderText);
        Assert.True(stops.IndexOf(fixture.List) < details && details < reply && reply < text, $"list, details {details}, Reply {reply}, text {text}");

        var theme = StandardControlPaint.GetTheme(fixture.ReaderText);
        bool Ringed(BRect ring) => fixture.Session.RenderFrame().Commands.OfType<BRenderCommand.StrokeRect>()
            .Any(stroke => stroke.Rect == ring && stroke.Color == theme.FocusRing && stroke.Thickness == theme.FocusRingThickness);
        double offset = theme.FocusRingOffset;
        // The header details: just outside the frameless editor, clear of its text.
        BRect detailsBounds = fixture.ReaderDetails.Bounds;
        var detailsRing = new BRect(detailsBounds.X - offset, detailsBounds.Y - offset, detailsBounds.Width + 2 * offset, detailsBounds.Height + 2 * offset);
        // The message text can run past the view; its ring goes around the view, the part on screen.
        var view = Descendants(fixture.InboxContent).OfType<Broiler.Mail.Application.Preview.ScrollableMessageText>().Single();
        var textRing = StandardControlPaint.Inset(view.Bounds, offset);

        fixture.Session.SetFocus(fixture.List);
        Assert.False(Ringed(detailsRing));
        Assert.False(Ringed(textRing));
        fixture.Session.SetFocus(fixture.ReaderDetails);
        Assert.True(Ringed(detailsRing));
        Assert.False(Ringed(textRing));
        fixture.Session.SetFocus(fixture.ReaderText);
        Assert.True(Ringed(textRing));
        Assert.False(Ringed(detailsRing));
    }

    [Fact]
    public void ABoundedAreaIsAStopOnlyWhileItScrollsAndNothingInsideTakesFocus()
    {
        var notice = new StandardLabel { Text = string.Join(" ", Enumerable.Repeat("A notice long enough to wrap many times.", 30)), Wrapping = UiTextWrapping.Wrap };
        var content = new StandardPanel();
        content.AddChild(notice);
        var area = new BoundedScrollArea(content, 0.4, "Inbox notice");
        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        root.AddChild(area);
        root.SetDock(area, UiDock.Top);
        using var session = new StandardUiSessionBuilder().Build(new Host(400, 300));
        session.AddRoot(root);
        session.RenderFrame();

        // Long read-only text scrolls: the area is the toolkit's keyboard stop, named for a screen reader.
        Assert.True(area.Scroll.CanFocus);
        Assert.Equal([area.Scroll], MailKeyboardNavigation.TabStops(root));
        Assert.Equal("Inbox notice", area.Scroll.GetSemanticNode().Name);

        // A control inside takes the stop instead.
        var retry = new StandardButton { Text = "Retry receiving" };
        content.AddChild(retry);
        session.RenderFrame();
        Assert.False(area.Scroll.CanFocus);
        Assert.Equal([retry], MailKeyboardNavigation.TabStops(root));

        // Nothing to scroll, nothing to stop at.
        content.RemoveChild(retry);
        notice.Text = "The inbox is empty.";
        session.RenderFrame();
        Assert.False(area.Scroll.CanFocus);
        Assert.Empty(MailKeyboardNavigation.TabStops(root));
    }

    /// <summary>
    /// A focused area that stops scrolling is no stop any more. Broiler.UI hands its focus to the next stop when the
    /// area is next drawn (ADR 0032), through the session's dispatcher, so Mail needs no rule of its own for the
    /// inbox notice, the message header or a form's status area.
    /// </summary>
    [Fact]
    public void AFocusedBoundedAreaThatStopsScrollingHandsFocusOn()
    {
        var notice = new StandardLabel { Text = string.Join(" ", Enumerable.Repeat("A notice long enough to wrap many times.", 30)), Wrapping = UiTextWrapping.Wrap };
        var content = new StandardPanel();
        content.AddChild(notice);
        var area = new BoundedScrollArea(content, 0.4, "Inbox notice");
        var receive = new StandardButton { Text = "Receive" };
        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        root.AddChild(area);
        root.SetDock(area, UiDock.Top);
        root.AddChild(receive);
        var dispatcher = new TestQueueDispatcher();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(400, 300));
        session.AddRoot(root);
        session.RenderFrame();
        session.SetFocus(area.Scroll);
        Assert.Same(area.Scroll, session.FocusedElement);

        notice.Text = "The inbox is empty.";
        session.RenderFrame();
        dispatcher.Drain();
        Assert.False(area.Scroll.CanFocus);
        Assert.Same(receive, session.FocusedElement);
    }

    [Fact]
    public async Task SettingsListsEveryShortcutFromTheTable()
    {
        using var fixture = await Fixture.OpenAsync();
        var settings = fixture.Shell.GetContent("settings");
        var lines = Descendants(settings).OfType<StandardLabel>().Select(label => label.Text).ToHashSet();
        foreach (var shortcut in MailShortcuts.All)
            Assert.Contains($"{shortcut.Gesture}: {shortcut.Description}", lines);
    }

    [Fact]
    public async Task EscapeOverFileDialogCancelsOnlyFileDialog()
    {
        using var fixture = await Fixture.OpenAsync();
        bool Dispatch(UiInputEvent input) => fixture.Keyboard.Handle(input) || fixture.Session.DispatchInput(input);
        fixture.Shell.ShowView("compose");
        var compose = fixture.Shell.ActiveDialog!;
        var pick = fixture.Shell.Files.PickOpenFileAsync(new(Broiler.Mail.Core.Services.MailFilePurpose.Attachment, "Attach file"));
        var file = Assert.IsType<Broiler.UI.FileDialog.Standard.StandardFileDialog>(fixture.Shell.ActiveFileDialog);

        Assert.False(fixture.Keyboard.Handle(Key(0x1B)));
        Assert.True(Dispatch(Key(0x1B)));
        fixture.Dispatcher.Drain();

        Assert.Equal(Broiler.Mail.Core.Services.MailFilePickStatus.Canceled, (await pick).Status);
        Assert.True(file.IsResultCompleted);
        Assert.Null(fixture.Shell.ActiveFileDialog);
        Assert.Same(compose, fixture.Shell.ActiveDialog);
        Assert.Same(compose, fixture.Session.ModalElement);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        // The next Escape closes the form, as before.
        Assert.True(Dispatch(Key(0x1B)));
        Assert.Null(fixture.Shell.ActiveDialog);
    }

    [Fact]
    public async Task TabAndEnterStayInsideFileDialog()
    {
        using var fixture = await Fixture.OpenAsync();
        using var directory = new TestDirectory();
        directory.Create();
        File.WriteAllText(directory.File("notes.txt"), "notes");
        bool Dispatch(UiInputEvent input) => fixture.Keyboard.Handle(input) || fixture.Session.DispatchInput(input);
        fixture.Shell.ShowView("compose");
        var compose = fixture.Shell.ActiveDialog!;
        var pick = fixture.Shell.Files.PickOpenFileAsync(new(Broiler.Mail.Core.Services.MailFilePurpose.Attachment, "Attach file") { InitialDirectory = directory.Root });
        var file = Assert.IsType<Broiler.UI.FileDialog.Standard.StandardFileDialog>(fixture.Shell.ActiveFileDialog);

        var seen = new HashSet<UiElement>();
        for (int i = 0; i < 24; i++)
        {
            Assert.True(Dispatch(Key(0x09, i < 12 ? KeyboardModifierState.None : KeyboardModifierState.Shift)));
            var focused = fixture.Session.FocusedElement!;
            Assert.True(focused.IsDescendantOf(file), focused.GetType().Name);
            seen.Add(focused);
        }
        Assert.Contains(file.FileNameEdit, seen);
        Assert.Contains(file.OkButton, seen);
        Assert.Contains(file.CancelButton, seen);

        fixture.Session.SetFocus(file.FileNameEdit);
        file.FileNameEdit.Text = "notes.txt";
        Assert.True(Dispatch(Key(0x0D)));
        fixture.Dispatcher.Drain();

        var result = await pick;
        Assert.Equal(Broiler.Mail.Core.Services.MailFilePickStatus.Picked, result.Status);
        Assert.Equal(directory.File("notes.txt"), result.Item!.LocalPath);
        // Enter accepted the file dialog only; the form underneath is still open.
        Assert.Same(compose, fixture.Shell.ActiveDialog);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
    }

    [Fact]
    public async Task CloseDialogCancelsFileDialogFirst()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Shell.ShowView("compose");
        var pick = fixture.Shell.Files.PickSaveFileAsync(new(Broiler.Mail.Core.Services.MailFilePurpose.SaveAttachment, "Save attachment"));
        Assert.NotNull(fixture.Shell.ActiveFileDialog);
        // Nothing replaces the form underneath an open file dialog.
        Assert.False(fixture.Shell.ShowView("settings"));
        Assert.False(fixture.Shell.ShowAbout());
        Assert.Equal("compose", fixture.Shell.ActiveViewId);

        Assert.True(fixture.Shell.CloseDialog());
        fixture.Dispatcher.Drain();
        Assert.Equal(Broiler.Mail.Core.Services.MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Null(fixture.Shell.ActiveFileDialog);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.NotNull(fixture.Shell.ActiveDialog);

        Assert.True(fixture.Shell.CloseDialog());
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);
    }

    private static readonly byte[] Original = System.Text.Encoding.ASCII.GetBytes(
        "From: Author <author@example.test>\r\nSubject: Plans & budget\r\nContent-Type: text/plain\r\n\r\nSome message text.\r\n");

    [Fact]
    public async Task CtrlOPicksThenShowsSavedOriginalsAndCancelChangesNothing()
    {
        var files = new FakeMailFilePicker();
        using var fixture = await Fixture.OpenAsync(files: files, sourceExports: true);
        var ctrlO = Key('O', KeyboardModifierState.Control);
        Assert.Equal(MailCommand.OpenLocalOriginal, MailShortcuts.Match(ctrlO)!.Command);
        Assert.True(fixture.Shell.CanOpenLocalOriginal);

        // Canceling the picker changes nothing: no dialog, still the inbox.
        fixture.Session.SetFocus(fixture.List);
        Assert.True(fixture.Keyboard.Handle(ctrlO));
        fixture.Dispatcher.Drain();
        var (kind, request) = Assert.Single(files.Requests);
        Assert.Equal("open", kind);
        Assert.Same(LocalOriginalsView.OpenRequest, request);
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);
        Assert.Same(fixture.List, fixture.Session.FocusedElement);

        // A chosen file opens Saved originals and shows it there, decoded and read-only.
        string path = Path.Combine(fixture.Root, "picked.eml");
        File.WriteAllBytes(path, Original);
        files.WillOpen(path);
        Assert.True(fixture.Keyboard.Handle(ctrlO));
        var imports = fixture.Shell.SavedOriginals.LocalImports;
        fixture.Dispatcher.DrainUntil(() => !imports.IsBusy && imports.PreviewText.Length > 0);
        Assert.Equal("originals", fixture.Shell.ActiveViewId);
        Assert.NotNull(fixture.Shell.ActiveDialog);
        Assert.Contains("Subject: Plans & budget", imports.PreviewText);
        Assert.True(imports.ShowsDecoded);
        Assert.Equal(Original, File.ReadAllBytes(path));

        // Inside Saved originals Ctrl+O asks again; inside another dialog it is consumed and asks nothing.
        Assert.True(fixture.Keyboard.Handle(ctrlO));
        fixture.Dispatcher.Drain();
        Assert.Equal(3, files.Requests.Count);
        Assert.Equal("originals", fixture.Shell.ActiveViewId);
        fixture.Shell.ShowView("compose");
        Assert.True(fixture.Keyboard.Handle(ctrlO));
        Assert.Equal(3, files.Requests.Count);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
    }

    [Fact]
    public async Task CtrlOWithoutLocalOriginalsOrAPickerAsksNothing()
    {
        var files = new FakeMailFilePicker();
        using (var fixture = await Fixture.OpenAsync(files: files))
        {
            Assert.False(fixture.Shell.CanOpenLocalOriginal);
            Assert.True(fixture.Keyboard.Handle(Key('O', KeyboardModifierState.Control)));
            Assert.Empty(files.Requests);
            Assert.Null(fixture.Shell.ActiveDialog);
        }
        // Product-owner decision 2: without a picker, Saved originals keeps its typed path instead.
        using var phone = await Fixture.OpenAsync(files: UnavailableMailFilePicker.NotOnThisDeviceYet, sourceExports: true);
        Assert.False(phone.Shell.CanOpenLocalOriginal);
        Assert.True(phone.Keyboard.Handle(Key('O', KeyboardModifierState.Control)));
        Assert.Null(phone.Shell.ActiveDialog);
        Assert.Contains(Descendants(phone.Shell.SavedOriginals).OfType<StandardEdit>(), edit => edit.AccessibleName == "Local .eml file path");
    }

    [Fact]
    public async Task CtrlOWhileLocalOriginalsIsBusySaysWhyAndAsksForNoFile()
    {
        var files = new FakeMailFilePicker();
        using var fixture = await Fixture.OpenAsync(files: files, sourceExports: true);
        var ctrlO = Key('O', KeyboardModifierState.Control);
        string path = Path.Combine(fixture.Root, "picked.eml");
        File.WriteAllBytes(path, Original);
        files.WillOpen(path);
        Assert.True(fixture.Keyboard.Handle(ctrlO));
        var imports = fixture.Shell.SavedOriginals.LocalImports;
        fixture.Dispatcher.DrainUntil(() => !imports.IsBusy && imports.PreviewText.Length > 0);
        await imports.ImportOpenedAsync(); fixture.Dispatcher.DrainUntil(() => !imports.IsBusy);
        await imports.ListAsync(); fixture.Dispatcher.DrainUntil(() => !imports.IsBusy);
        var receipt = Assert.Single(imports.Imports);

        // An export whose write waits keeps Local originals busy with something other than an open.
        var target = new WaitingWritableFile();
        files.SaveResults.Enqueue(Broiler.Mail.Core.Services.MailFilePick<Broiler.Mail.Core.Services.IMailWritableFile>.Picked(target));
        var exporting = imports.ExportWithPickerAsync(receipt);
        await target.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(imports.IsBusy);
        int asked = files.Requests.Count;

        files.WillOpen(path);
        Assert.True(fixture.Keyboard.Handle(ctrlO));
        fixture.Dispatcher.Drain();
        Assert.Equal(asked, files.Requests.Count);
        Assert.Contains("Nothing was opened", imports.Status);
        Assert.NotNull(imports.OpenBlockedReason);
        Assert.False(await fixture.Shell.OpenLocalOriginalAsync());
        Assert.Equal(asked, files.Requests.Count);

        target.Release.SetResult();
        await exporting; fixture.Dispatcher.DrainUntil(() => !imports.IsBusy);
        Assert.Null(imports.OpenBlockedReason);
        Assert.True(await fixture.Shell.OpenLocalOriginalAsync());
        Assert.Equal(asked + 1, files.Requests.Count);
    }

    /// <summary>A picked save location whose write waits until released.</summary>
    private sealed class WaitingWritableFile : Broiler.Mail.Core.Services.IMailWritableFile
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string DisplayName => "export.eml";
        public string? LocalPath => null;
        public async Task<Broiler.Mail.Core.Services.MailWrittenFile> WriteNewAsync(Func<Stream, CancellationToken, Task> write, CancellationToken token = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(DisplayName, null, 0);
        }
    }

    [Fact]
    public async Task CtrlShiftSSavesOpenMessageAndIsConsumedElsewhere()
    {
        var files = new FakeMailFilePicker();
        using var fixture = await Fixture.OpenAsync(files: files, sourceExports: true);
        var ctrlShiftS = Key('S', KeyboardModifierState.Control | KeyboardModifierState.Shift);
        Assert.Equal(MailCommand.SaveMessageAsEml, MailShortcuts.Match(ctrlShiftS)!.Command);
        Assert.True(fixture.Shell.Inbox.CanSaveMessageAsEml);

        string target = Path.Combine(fixture.Root, "saved.eml");
        files.WillSave(target);
        fixture.Session.SetFocus(fixture.List);
        Assert.True(fixture.Keyboard.Handle(ctrlShiftS));
        fixture.Dispatcher.DrainUntil(() => File.Exists(target) && fixture.Shell.Inbox.CanSaveMessageAsEml);
        Assert.Equal(Original, File.ReadAllBytes(target));
        var (kind, request) = Assert.Single(files.Requests);
        Assert.Equal("save", kind);
        Assert.Equal(Broiler.Mail.Core.Services.MailFilePurpose.SaveMessage, request.Purpose);
        Assert.StartsWith("Plans & budget", request.SuggestedFileName);
        Assert.EndsWith(".eml", request.SuggestedFileName);
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);

        // In a dialog the chord is consumed and saves nothing.
        fixture.Shell.ShowView("compose");
        Assert.True(fixture.Keyboard.Handle(ctrlShiftS));
        fixture.Dispatcher.Drain();
        Assert.Single(files.Requests);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
    }

    [Fact]
    public async Task CtrlShiftSWithoutAnOpenMessageIsConsumed()
    {
        var files = new FakeMailFilePicker();
        using var fixture = await Fixture.OpenAsync(select: false, files: files, sourceExports: true);
        Assert.False(fixture.Shell.Inbox.CanSaveMessageAsEml);
        Assert.True(fixture.Keyboard.Handle(Key('S', KeyboardModifierState.Control | KeyboardModifierState.Shift)));
        fixture.Dispatcher.Drain();
        Assert.Empty(files.Requests);
        Assert.Null(fixture.Shell.ActiveDialog);
    }

    [Fact]
    public async Task AltGrOAndAltGrSReachTheEditor()
    {
        var files = new FakeMailFilePicker();
        using var fixture = await Fixture.OpenAsync(files: files, sourceExports: true);
        fixture.Shell.ShowView("compose");
        // AltGr arrives as Ctrl+Alt (with or without Shift): those characters belong to the editor.
        foreach (var key in new[] { 'O', 'S' })
        {
            Assert.False(fixture.Keyboard.Handle(Key(key, KeyboardModifierState.Control | KeyboardModifierState.Alt | KeyboardModifierState.RightAlt)));
            Assert.False(fixture.Keyboard.Handle(Key(key, KeyboardModifierState.Control | KeyboardModifierState.Alt | KeyboardModifierState.Shift)));
        }
        fixture.Dispatcher.Drain();
        Assert.Empty(files.Requests);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
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
        public UiElement InboxContent => Shell.GetContent("inbox");
        private UiElement ComposeContent => Shell.GetContent("compose");
        public StandardListView List => Descendants(InboxContent).OfType<StandardListView>().Single();
        public StandardRichEdit ReaderText => Descendants(InboxContent).OfType<StandardRichEdit>().Single(edit => edit.AccessibleName == "Message text");
        public StandardRichEdit ReaderDetails => Descendants(InboxContent).OfType<StandardRichEdit>().Single(edit => edit.AccessibleName == "Sender and recipients");
        public StandardRichEdit ComposerBody => Descendants(ComposeContent).OfType<StandardRichEdit>().Single();
        public StandardEdit ComposerTo => (StandardEdit)Descendants(ComposeContent).OfType<StandardLabel>().Single(label => label.Text == "To").Target!;

        public static async Task<Fixture> OpenAsync(int width = 1100, int height = 720, bool select = true, int messages = 1,
            Broiler.Mail.Core.Services.IMailFilePicker? files = null, bool sourceExports = false)
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
            // With source exports the receiver also returns originals, and the exports keep local .eml files.
            Broiler.Mail.Core.Services.IMailReceiver mail = sourceExports ? new SourceMailReceiver(receiver, Original) : receiver;
            var exports = sourceExports ? new FileMessageSourceExports(directory.File("profile")) : null;
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), mail, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(mail, dispatcher, sourceExports: exports),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model, files: files);
            var host = new Host(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            shell.Attach(session);
            shell.ShowView("inbox");
            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            if (select) await model.Inbox.SelectAsync(message.Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            session.RenderFrame();
            return new(directory, host, dispatcher, model, shell, session);
        }

        /// <summary>The fixture's folder, created on first use, for files the tests pick.</summary>
        public string Root => Directory.CreateDirectory(_directory.Root).FullName;

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

    /// <summary>A receiver that also returns each message's original source.</summary>
    private sealed class SourceMailReceiver(TestMailReceiver mail, byte[] original) : Broiler.Mail.Core.Services.IMailReceiver, Broiler.Mail.Core.Services.IMailSourceReceiver
    {
        public Task<MailInboxPage> GetInboxAsync(Broiler.Mail.Core.Accounts.AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            mail.GetInboxAsync(account, maximumCount, older, cancellationToken);
        public Task<MailMessageBody> GetBodyAsync(Broiler.Mail.Core.Accounts.AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default) =>
            mail.GetBodyAsync(account, message, cancellationToken);
        public Task TestConnectionAsync(Broiler.Mail.Core.Accounts.AccountProfile account, CancellationToken cancellationToken = default) =>
            mail.TestConnectionAsync(account, cancellationToken);
        public Task<Broiler.Mail.Core.Services.MailMessageSource> GetSourceAsync(Broiler.Mail.Core.Accounts.AccountProfile account, MailMessageKey message, CancellationToken token = default) =>
            Task.FromResult(new Broiler.Mail.Core.Services.MailMessageSource(message, original));
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
