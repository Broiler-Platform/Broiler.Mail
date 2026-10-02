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
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Splitter;

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
        // Layout first: a pane collapsed by the adaptive inbox only loses its bounds when arranged.
        session.RenderFrame();
        var controls = new List<UiElement> { shell.Navigation };
        if (shell.Navigation.SelectedTab?.Content is { } content)
            controls.AddRange(TabStops(content));
        int current = controls.IndexOf(session.FocusedElement!);
        int next = current < 0 ? (direction > 0 ? 0 : controls.Count - 1) : (current + direction + controls.Count) % controls.Count;
        session.SetFocus(controls[next]);
        Reveal(controls[next]);
    }

    /// <summary>
    /// Tab stops in document order, then by <see cref="UiElement.TabIndex"/> (a stable sort). Any control
    /// that reports <see cref="UiElement.CanFocus"/> and <see cref="UiElement.IsTabStop"/> takes part, so
    /// new controls need no registration here.
    /// </summary>
    public static IReadOnlyList<UiElement> TabStops(UiElement content) =>
        Descendants(content).Where(IsTabStop).OrderBy(element => element.TabIndex).ToList();

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=36A581
    // Broiler-Falsified-If: a focused field below the visible area of its scroll view stays out of view after Tab
    // Broiler-Human:        PENDING
    private void Reveal(UiElement element) => FocusNavigation.Reveal(session, element);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=A40427
    // Broiler-Falsified-If: a disabled edit, button or combo box is reported focusable
    // Broiler-Human:        PENDING
    private static bool IsTabStop(UiElement element) => element switch
    {
        _ when element.CanFocus && element.IsTabStop => true,
        // The splitter resizes with the arrow keys but does not report itself focusable.
        UiSplitter splitter => splitter.IsEnabled,
        // A scroll view of read-only content, such as the reader, is a stop so the keyboard can scroll it.
        StandardScrollView scroll => !Descendants(scroll).Skip(1).Any(item => item.CanFocus || item is UiSplitter),
        _ => false,
    };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=7860E1
    // Broiler-Falsified-If: an element inside a collapsed panel is returned
    // Broiler-Human:        PENDING
    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        // Collapsed, hidden by a container (inactive tab content), or arranged away (a collapsed split pane).
        if (element.Visibility != UiVisibility.Visible || element.IsHiddenFromAccessibility || element.Bounds.IsEmpty) yield break;
        yield return element;
        foreach (var child in VisualOrder(element))
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    // A split container adds its splitter first; Tab should meet it between the panes, as it appears.
    private static IEnumerable<UiElement> VisualOrder(UiElement element) => element is UiSplitContainer split
        ? new[] { split.FirstPane, split.Splitter, split.SecondPane }.OfType<UiElement>()
            .Concat(split.Children.Where(child => child != split.FirstPane && child != split.Splitter && child != split.SecondPane))
        : element.Children;
}
