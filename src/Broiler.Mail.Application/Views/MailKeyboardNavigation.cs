// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   11
// Annotated:        5/11
// Exempt:           0
// Human-reviewed:   0/11
// IP risk:          Low
// Security risk:    Medium
// Criteria:         5/0
// Resource impact:  7/10 max
// Unverified:       11
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.ListView.Standard;

namespace Broiler.Mail.Application.Views;

// Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=6389B4
// Hairs-Falsified-If: a key the navigation does not handle, such as a letter typed in the composer, returns true and never reaches the focused editor
// Hairs-Human:        PENDING
public sealed class MailKeyboardNavigation(UiSession session, MailShellView shell, MailShellViewModel model)
{
    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=5630B5
    // Hairs-Falsified-If: a key the navigation does not handle, such as a letter typed in the composer, returns true and never reaches the focused editor
    // Hairs-Human:        PENDING
    public bool Handle(UiInputEvent input)
    {
        // Menus and combo-box popups own their arrows, Enter and Escape.
        if (shell.Menu.IsOpen)
        {
            if (MailShortcuts.Match(input)?.Command == MailCommand.BackOrCancel)
            {
                session.DispatchInput(input);
                shell.RestoreReaderFocus();
                return true;
            }
            return false;
        }
        if (session.CapturedElement is { } captured)
        {
            // A reader popup (Move, More message actions) holds capture while open; Tab and Shift+Tab close it, which
            // gives focus back to its button, and move on from there, as they would from the button.
            if (captured.Parent is MailMenuButton { IsExpanded: true } popup &&
                MailShortcuts.Match(input)?.Command is MailCommand.NextField or MailCommand.PreviousField)
            {
                bool next = MailShortcuts.Match(input)!.Command == MailCommand.NextField;
                popup.Collapse();
                MoveFocus(next ? 1 : -1);
                return true;
            }
            return false;
        }
        if (shell.ActiveAboutDialog is not null) return false;
        if (shell.ActiveFileDialog is { } fileDialog)
        {
            // A file dialog over a form owns Enter, Escape, Backspace and the arrows through the session's modal
            // routing; Tab only cycles inside it, so neither reaches the form underneath.
            if (shell.PreviewFileDialogInput(input)) return true;
            var command = MailShortcuts.Match(input)?.Command;
            if (command is not (MailCommand.NextField or MailCommand.PreviousField)) return false;
            MoveFocusWithin(fileDialog, command == MailCommand.NextField ? 1 : -1);
            return true;
        }
        if (shell.ActiveViewId == "inbox" && shell.Inbox.HandleReaderInput(input)) return true;
        if (MailShortcuts.Match(input) is { } shortcut)
        {
            if (shortcut.Command == MailCommand.OpenMessage && shell.ActiveDialog is { } dialog)
            {
                var activeSession = dialog.Session ?? session;
                // Route Enter to the editor/button, but never let it implicitly accept a form.
                if (activeSession.FocusedElement is { } focused && focused != dialog && focused.IsDescendantOf(dialog)) focused.DispatchInput(input);
                return true;
            }
            return Execute(shortcut.Command);
        }
        return false;
    }

    /// <summary>Runs a shell command. Returns false when the key should still reach the focused control.</summary>
    public bool Execute(MailCommand command)
    {
        switch (command)
        {
            case MailCommand.NextField or MailCommand.PreviousField:
                MoveFocus(command == MailCommand.NextField ? 1 : -1);
                return true;
            case MailCommand.OpenMenu:
                if (shell.ActiveDialog is not null) return true;
                shell.RefreshMenuCommands();
                shell.RememberReaderFocus();
                session.SetFocus(shell.Menu);
                shell.Menu.Open();
                session.CaptureInput(shell.Menu);
                return true;
            case MailCommand.UnifiedSavedInbox:
                return shell.ShowView("unified");
            case MailCommand.SavedOriginals:
                return shell.ShowView("originals");
            case MailCommand.Outbox:
                return shell.ShowView("outbox");
            case >= MailCommand.Inbox and <= MailCommand.Compose:
                return shell.ShowView(command switch
                {
                    MailCommand.Inbox => "inbox", MailCommand.Account => "account",
                    MailCommand.Settings => "settings", _ => "compose",
                });
            case MailCommand.Receive:
                if (shell.ActiveDialog is not null) return true;
                _ = model.Inbox.ReceiveAsync();
                return true;
            // Find and search are consumed even when unavailable, so the chord never types into an editor.
            case MailCommand.FindInMessage:
                // Find docks in the reader; without a message in the reader it focuses the search header instead. A
                // message whose body failed to load keeps the reader, its problem and Retry in view.
                if (shell.ActiveDialog is not null) return true;
                if (!shell.Inbox.ShowFindInMessage() && !shell.Inbox.ShowsMessage) shell.Inbox.ShowSearch();
                return true;
            case MailCommand.SearchMail:
                // Search is a header at the top of the list, not a dialog.
                if (shell.ActiveDialog is not null) return true;
                shell.Inbox.ShowSearch();
                return true;
            // Composition shortcuts are consumed even when unavailable, so the chord never reaches an editor.
            case MailCommand.NewMessage:
                if (shell.ActiveDialog is not null && shell.ActiveViewId != "compose") return true;
                model.Compose.StartNew();
                return true;
            case MailCommand.Reply or MailCommand.ReplyAll or MailCommand.Forward:
                if (shell.ActiveDialog is not null) return true;
                model.Compose.Respond(command switch
                {
                    MailCommand.Reply => CompositionKind.Reply,
                    MailCommand.ReplyAll => CompositionKind.ReplyAll,
                    _ => CompositionKind.Forward,
                });
                return true;
            case MailCommand.OpenMessage:
                // Do not let an unhandled Enter bubble into StandardDialog.Accept().
                if (shell.ActiveDialog is not null) return true;
                return session.FocusedElement is StandardListView && model.Inbox.SelectedMessage is not null && shell.Inbox.OpenSelected();
            // The .eml file commands are consumed even when unavailable, so the chord never types into an editor. Each
            // opens a file dialog only because the person asked for one: the documented exemption to the no-modal rule.
            case MailCommand.OpenLocalOriginal:
                if (shell.ActiveDialog is null || shell.ActiveViewId == "originals") _ = shell.OpenLocalOriginalAsync();
                return true;
            case MailCommand.SaveMessageAsEml:
                if (shell.ActiveDialog is null && shell.ActiveViewId == "inbox") _ = shell.Inbox.SaveMessageAsEml();
                return true;
            case MailCommand.Back:
                return shell.ActiveDialog is null && shell.Inbox.GoBackToList();
            case MailCommand.BackOrCancel:
                // Escape closes a dialog; then, unless work runs (which it cancels), it closes the open
                // message's find bar, then leaves a compact reader; with nothing to go back from, it cancels work.
                // While nothing runs, a find bar with focus in the reader, and the search header with focus in it,
                // already took Escape (HandleReaderInput, above); while work runs both leave it to cancel here, so
                // where focus is does not change what Escape does. A bar in a hidden compact reader is not closed.
                if (shell.ActiveViewId == "account" && model.Account.IsBusy) { model.Account.CancelConnectionTest(); return true; }
                // In Compose, Escape first dismisses the pre-send attachment reminder, then cancels a file being read.
                if (shell.ActiveViewId == "compose" && shell.Composer.HandleEscape()) return true;
                // In Saved originals, Escape keeps a saved original whose removal waits for confirmation.
                if (shell.ActiveViewId == "originals" && shell.SavedOriginals.HandleEscape()) return true;
                if (shell.CloseDialog()) return true;
                // A running attachment save is work too: Escape cancels it, wherever focus is, before it closes find.
                if (shell.Inbox.CancelAttachmentSave()) return true;
                if (!model.Inbox.IsBusy && shell.Inbox.CloseFindInMessage()) return true;
                if (!model.Inbox.IsBusy && shell.Inbox.GoBackToList()) return true;
                model.Inbox.Cancel();
                model.Account.CancelConnectionTest();
                return true;
            default:
                return false;
        }
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=B17051
    // Hairs-Falsified-If: Tab moves focus onto a disabled control or into the collapsed SMTP fields
    // Hairs-Human:        PENDING
    public void MoveFocus(int direction)
    {
        var activeSession = shell.ActiveDialog?.Session ?? session;
        // Layout first: the adaptive inbox decides during layout which pane is collapsed (and hidden).
        activeSession.RenderFrame();
        var controls = new List<UiElement> { shell.NavigationFocus };
        if (shell.ActiveDialog is null && shell.AccountSelectionContent is { } accounts)
            controls.AddRange(TabStops(accounts));
        var content = shell.ActiveContent;
        if (content is not null)
            controls.AddRange(TabStops(content));
        int current = controls.IndexOf(activeSession.FocusedElement!);
        UiElement next;
        if (current >= 0) next = controls[(current + direction + controls.Count) % controls.Count];
        // A control that is no longer a stop, such as a button disabled while its operation runs,
        // keeps its place: Tab continues from there instead of restarting at the navigation control.
        else if (activeSession.FocusedElement is { } focused && content is not null && focused.IsDescendantOf(content))
            next = NextTabStop(content, focused, direction) ?? shell.NavigationFocus;
        else next = direction > 0 ? controls[0] : controls[^1];
        activeSession.SetFocus(next);
        Reveal(next);
    }

    /// <summary>Moves focus to the next (or previous) tab stop inside <paramref name="scope"/>, wrapping at its ends.</summary>
    private void MoveFocusWithin(UiElement scope, int direction)
    {
        var activeSession = scope.Session ?? session;
        activeSession.RenderFrame();
        var stops = TabStops(scope);
        if (stops.Count == 0) return;
        var focused = activeSession.FocusedElement;
        int current = focused is null ? -1 : stops.ToList().IndexOf(focused);
        UiElement next = current >= 0 ? stops[(current + direction + stops.Count) % stops.Count]
            : focused is not null && focused.IsDescendantOf(scope) ? NextTabStop(scope, focused, direction) ?? stops[0]
            : direction > 0 ? stops[0] : stops[^1];
        activeSession.SetFocus(next);
    }

    /// <summary>
    /// The tab stop after <paramref name="from"/> in Tab order (before it for a negative
    /// <paramref name="direction"/>), for a control that is not a stop itself, such as a button that
    /// disabled itself: its place is where it would be among <see cref="TabStops"/>, by
    /// <see cref="UiElement.TabIndex"/> and then document order. Null when <paramref name="from"/> is
    /// not in <paramref name="scope"/> or no stop follows.
    /// </summary>
    internal static UiElement? NextTabStop(UiElement scope, UiElement from, int direction)
    {
        // Every element, shown or not, so a control that was just hidden still has its place.
        var position = new Dictionary<UiElement, int>(ReferenceEqualityComparer.Instance);
        foreach (var element in AllDescendants(scope)) position[element] = position.Count;
        if (!position.TryGetValue(from, out int place)) return null;
        // TabStops is sorted by TabIndex, then document position, so the neighbour is the first stop
        // past that key in the direction of travel.
        int Compare(UiElement stop) => stop.TabIndex != from.TabIndex ? stop.TabIndex.CompareTo(from.TabIndex) : position[stop].CompareTo(place);
        var stops = TabStops(scope);
        return direction > 0 ? stops.FirstOrDefault(stop => Compare(stop) > 0) : stops.LastOrDefault(stop => Compare(stop) < 0);
    }

    private static IEnumerable<UiElement> AllDescendants(UiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in AllDescendants(child)) yield return descendant;
    }

    /// <summary>
    /// Tab stops in document order, then by <see cref="UiElement.TabIndex"/> (a stable sort). Any control
    /// that reports <see cref="UiElement.CanFocus"/> and <see cref="UiElement.IsTabStop"/> takes part,
    /// including the splitter, so new controls need no registration here.
    /// </summary>
    public static IReadOnlyList<UiElement> TabStops(UiElement content) =>
        Descendants(content).Where(IsTabStop).OrderBy(element => element.TabIndex).ToList();

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=5AB857
    // Hairs-Falsified-If: a focused field below the visible area of its scroll view stays out of view after Tab
    // Hairs-Human:        PENDING
    private void Reveal(UiElement element) => FocusNavigation.Reveal(shell.ActiveDialog?.Session ?? session, element);

    // A scroll view of read-only content is a stop through StandardScrollView.FocusWhenScrollable, which
    // Mail sets on its named read-only areas: only while it has something to scroll, so a form's short
    // feedback area never takes focus invisibly with nothing for a screen reader to announce.
    private static bool IsTabStop(UiElement element) => element.CanFocus && element.IsTabStop;

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=563E24
    // Hairs-Falsified-If: an element inside a collapsed panel is returned
    // Hairs-Human:        PENDING
    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        // Collapsed, or hidden by a container: inactive tab content and collapsed split panes.
        if (element.Visibility != UiVisibility.Visible || element.IsHiddenFromAccessibility) yield break;
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
