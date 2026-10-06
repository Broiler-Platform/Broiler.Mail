// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   7
// Annotated:        0/7
// Exempt:           16
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
    Receive, NewMessage, Reply, ReplyAll, Forward, OpenMessage, Back, BackOrCancel,
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
        new(MailCommand.Receive, F5, false, false, false, "F5", "Receive mail"),
        new(MailCommand.NewMessage, 'N', true, false, false, "Ctrl+N", "New message"),
        new(MailCommand.Reply, 'R', true, false, false, "Ctrl+R", "Reply"),
        new(MailCommand.ReplyAll, 'R', true, true, false, "Ctrl+Shift+R", "Reply all"),
        new(MailCommand.Forward, 'F', true, false, false, "Ctrl+F", "Forward"),
        new(MailCommand.OpenMessage, Enter, false, false, false, "Enter", "Open the selected message (in the message list)"),
        new(MailCommand.Back, Left, false, false, true, "Alt+Left", "Back to the message list (narrow window)"),
        new(MailCommand.BackOrCancel, Escape, false, false, false, "Escape", "Close the dialog, cancel a connection test, or return to the message list"),
    ];

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
