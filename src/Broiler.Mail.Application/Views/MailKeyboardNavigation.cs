// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   6
// Annotated:        6/6
// Exempt:           0
// Human-reviewed:   0/6
// IP risk:          Low
// Security risk:    Medium
// Criteria:         6/0
// Resource impact:  7/10 max
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.ListView.Standard;

namespace Broiler.Mail.Application.Views;

// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=6389B4
// Broiler-Falsified-If: a key the navigation does not handle, such as a letter typed in the composer, returns true and never reaches the focused editor
// Broiler-Human:        PENDING
public sealed class MailKeyboardNavigation(UiSession session, MailShellView shell, MailShellViewModel model)
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=D11D0A
    // Broiler-Falsified-If: a key the navigation does not handle, such as a letter typed in the composer, returns true and never reaches the focused editor
    // Broiler-Human:        PENDING
    public bool Handle(UiInputEvent input) => MailShortcuts.Match(input) is { } shortcut && Execute(shortcut.Command);

    /// <summary>Runs a shell command. Returns false when the key should still reach the focused control.</summary>
    public bool Execute(MailCommand command)
    {
        var tabs = shell.Navigation;
        switch (command)
        {
            case MailCommand.NextField or MailCommand.PreviousField:
                MoveFocus(command == MailCommand.NextField ? 1 : -1);
                return true;
            case MailCommand.NextTab or MailCommand.PreviousTab:
                int count = tabs.Tabs.Count;
                tabs.SelectedIndex = (tabs.SelectedIndex + (command == MailCommand.PreviousTab ? count - 1 : 1)) % count;
                session.SetFocus(tabs);
                return true;
            case >= MailCommand.InboxTab and <= MailCommand.ComposeTab:
                tabs.SelectedIndex = command - MailCommand.InboxTab;
                session.SetFocus(tabs);
                return true;
            case MailCommand.Receive:
                tabs.SelectTab("inbox");
                _ = model.Inbox.ReceiveAsync();
                return true;
            // Composition shortcuts are consumed even when unavailable, so the chord never reaches an editor.
            case MailCommand.NewMessage:
                model.Compose.StartNew();
                return true;
            case MailCommand.Reply or MailCommand.ReplyAll or MailCommand.Forward:
                model.Compose.Respond(command switch
                {
                    MailCommand.Reply => CompositionKind.Reply,
                    MailCommand.ReplyAll => CompositionKind.ReplyAll,
                    _ => CompositionKind.Forward,
                });
                return true;
            case MailCommand.OpenMessage:
                return session.FocusedElement is StandardListView && model.Inbox.SelectedMessage is not null && shell.Inbox.OpenSelected();
            case MailCommand.Back:
                return tabs.SelectedTab?.Id == "inbox" && shell.Inbox.GoBackToList();
            case MailCommand.BackOrCancel:
                // Escape first leaves a compact reader; with nothing to go back from, it cancels work.
                if (!model.Inbox.IsBusy && tabs.SelectedTab?.Id == "inbox" && shell.Inbox.GoBackToList()) return true;
                model.Inbox.Cancel();
                model.Account.CancelConnectionTest();
                return true;
            default:
                return false;
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=609020
    // Broiler-Falsified-If: Tab moves focus onto a disabled control or into the collapsed SMTP fields
    // Broiler-Human:        PENDING
    public void MoveFocus(int direction)
    {
        // Layout first: the adaptive inbox decides during layout which pane is collapsed (and hidden).
        session.RenderFrame();
        var controls = new List<UiElement> { shell.Navigation };
        var content = shell.Navigation.SelectedTab?.Content;
        if (content is not null)
            controls.AddRange(TabStops(content));
        int current = controls.IndexOf(session.FocusedElement!);
        UiElement next;
        if (current >= 0) next = controls[(current + direction + controls.Count) % controls.Count];
        // A control that is no longer a stop, such as a button disabled while its operation runs,
        // keeps its place: Tab continues from there instead of restarting at the tabs.
        else if (session.FocusedElement is { } focused && content is not null && focused.IsDescendantOf(content))
            next = NextTabStop(content, focused, direction) ?? shell.Navigation;
        else next = direction > 0 ? controls[0] : controls[^1];
        session.SetFocus(next);
        Reveal(next);
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=36A581
    // Broiler-Falsified-If: a focused field below the visible area of its scroll view stays out of view after Tab
    // Broiler-Human:        PENDING
    private void Reveal(UiElement element) => FocusNavigation.Reveal(session, element);

    // A scroll view of read-only content is a stop through StandardScrollView.FocusWhenScrollable, which
    // Mail sets on its named read-only areas: only while it has something to scroll, so a form's short
    // feedback area never takes focus invisibly with nothing for a screen reader to announce.
    private static bool IsTabStop(UiElement element) => element.CanFocus && element.IsTabStop;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=7860E1
    // Broiler-Falsified-If: an element inside a collapsed panel is returned
    // Broiler-Human:        PENDING
    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        // Collapsed, or hidden by a container: inactive tab content and collapsed split panes.
        if (element.Visibility != UiVisibility.Visible || element.IsHiddenFromAccessibility) yield break;
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
