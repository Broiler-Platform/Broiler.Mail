// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   7
// Annotated:        0/7
// Exempt:           23
// Human-reviewed:   0/7
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Input.Keyboard;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>Shell commands that have a keyboard shortcut.</summary>
public enum MailCommand
{
    NextField, PreviousField, OpenMenu, Inbox, Account, Settings, Compose,
    Receive, NewMessage, Reply, ReplyAll, Forward, OpenMessage, Back, BackOrCancel, Outbox, SavedOriginals, UnifiedSavedInbox,
    FindInMessage, SearchMail,
    // Appended, so the Inbox..Compose range that MailKeyboardNavigation.Execute switches on stays intact.
    OpenLocalOriginal, SaveMessageAsEml,
}

/// <summary>One key with an exact set of Ctrl, Shift, and Alt modifiers.</summary>
public sealed record MailShortcut(MailCommand Command, int Key, bool Control, bool Shift, bool Alt, string Gesture, string Description);

/// <summary>
/// The one definition of the shell's keyboard shortcuts: key handling matches against it, and the
/// Settings dialog and the demo text list it, so a shortcut is never shown differently from how it works.
/// </summary>
public static class MailShortcuts
{
    private const int Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Left = 0x25, F5 = 0x74;

    public static IReadOnlyList<MailShortcut> All { get; } =
    [
        new(MailCommand.NextField, Tab, false, false, false, "Tab", "Next control"),
        new(MailCommand.PreviousField, Tab, false, true, false, "Shift+Tab", "Previous control"),
        new(MailCommand.OpenMenu, 0x79, false, false, false, "F10", "Open the menu"),
        new(MailCommand.Inbox, '1', true, false, false, "Ctrl+1", "Inbox"),
        new(MailCommand.Account, '2', true, false, false, "Ctrl+2", "Account"),
        new(MailCommand.Settings, '3', true, false, false, "Ctrl+3", "Settings"),
        new(MailCommand.Compose, '4', true, false, false, "Ctrl+4", "Compose"),
        new(MailCommand.Outbox, '5', true, false, false, "Ctrl+5", "Outbox"),
        new(MailCommand.SavedOriginals, '6', true, false, false, "Ctrl+6", "Saved originals"),
        new(MailCommand.UnifiedSavedInbox, '7', true, false, false, "Ctrl+7", "Unified saved inbox"),
        new(MailCommand.FindInMessage, 'F', true, false, false, "Ctrl+F", "Find in the open message (search mail when none is open)"),
        new(MailCommand.SearchMail, 'K', true, false, false, "Ctrl+K", "Search saved mail"),
        new(MailCommand.SearchMail, 'E', true, false, false, "Ctrl+E", "Search saved mail (alternative)"),
        new(MailCommand.Receive, F5, false, false, false, "F5", "Receive mail"),
        new(MailCommand.NewMessage, 'N', true, false, false, "Ctrl+N", "New message"),
        // Neither uses Ctrl+Alt, so AltGr+O and AltGr+S still type their characters.
        new(MailCommand.OpenLocalOriginal, 'O', true, false, false, "Ctrl+O", "Open a local .eml file"),
        new(MailCommand.SaveMessageAsEml, 'S', true, true, false, "Ctrl+Shift+S", "Save the open message as an .eml file"),
        new(MailCommand.Reply, 'R', true, false, false, "Ctrl+R", "Reply"),
        new(MailCommand.ReplyAll, 'R', true, true, false, "Ctrl+Shift+R", "Reply all"),
        new(MailCommand.Forward, 'L', true, false, false, "Ctrl+L", "Forward"),
        new(MailCommand.Forward, 'F', true, true, false, "Ctrl+Shift+F", "Forward (alternative)"),
        new(MailCommand.OpenMessage, Enter, false, false, false, "Enter", "Open the selected message (in the message list)"),
        new(MailCommand.Back, Left, false, false, true, "Alt+Left", "Back to the message list (narrow window)"),
        new(MailCommand.BackOrCancel, Escape, false, false, false, "Escape", "Close the dialog or the find bar, clear or exit search, cancel a connection test, or return to the message list"),
    ];

    /// <summary>The primary shortcut for a command; alternatives follow it in <see cref="All"/>.</summary>
    public static MailShortcut For(MailCommand command) => All.First(shortcut => shortcut.Command == command);

    /// <summary>
    /// The shortcut for a key press, or null. Modifiers must match exactly: AltGr arrives as Ctrl+Alt,
    /// so AltGr+2 types "²" on a German layout instead of switching to the Account dialog.
    /// </summary>
    public static MailShortcut? Match(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return null;
        var modifiers = input.KeyModifiers;
        bool control = Has(modifiers, KeyboardModifierState.Control | KeyboardModifierState.LeftControl | KeyboardModifierState.RightControl);
        bool shift = Has(modifiers, KeyboardModifierState.Shift | KeyboardModifierState.LeftShift | KeyboardModifierState.RightShift);
        bool alt = Has(modifiers, KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt | KeyboardModifierState.RightAlt);
        if (Has(modifiers, KeyboardModifierState.LeftWindows | KeyboardModifierState.RightWindows)) return null;
        return All.FirstOrDefault(shortcut => shortcut.Key == input.NativeKeyCode
            && shortcut.Control == control && shortcut.Shift == shift && shortcut.Alt == alt);
    }

    private static bool Has(KeyboardModifierState state, KeyboardModifierState any) => (state & any) != 0;
}
