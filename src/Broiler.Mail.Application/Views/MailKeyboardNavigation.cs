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

using Broiler.Graphics.Geometry;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.TabView.Standard;
using Broiler.UI.RichEdit.Standard;

namespace Broiler.Mail.Application.Views;

// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=6389B4
// Broiler-Falsified-If: a key the navigation does not handle, such as a letter typed in the composer, returns true and never reaches the focused editor
// Broiler-Human:        PENDING
public sealed class MailKeyboardNavigation(UiSession session, MailShellView shell, MailShellViewModel model)
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=D11D0A
    // Broiler-Falsified-If: a key the navigation does not handle, such as a letter typed in the composer, returns true and never reaches the focused editor
    // Broiler-Human:        PENDING
    public bool Handle(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        bool control = input.KeyModifiers.HasFlag(KeyboardModifierState.Control);
        bool shift = input.KeyModifiers.HasFlag(KeyboardModifierState.Shift);
        if (input.NativeKeyCode == 9)
        {
            if (control)
            {
                int count = shell.Navigation.Tabs.Count;
                shell.Navigation.SelectedIndex = (shell.Navigation.SelectedIndex + (shift ? count - 1 : 1)) % count;
                session.SetFocus(shell.Navigation);
            }
            else MoveFocus(shift ? -1 : 1);
            return true;
        }
        if (control && input.NativeKeyCode is >= 0x31 and <= 0x34)
        {
            shell.Navigation.SelectedIndex = input.NativeKeyCode - 0x31;
            session.SetFocus(shell.Navigation);
            return true;
        }
        if (input.NativeKeyCode == 0x74) // F5
        {
            shell.Navigation.SelectTab("inbox");
            _ = model.Inbox.ReceiveAsync();
            return true;
        }
        if (input.NativeKeyCode == 0x1B)
        {
            model.Inbox.Cancel();
            model.Account.CancelConnectionTest();
            return true;
        }
        if (input.NativeKeyCode == 13 && session.FocusedElement is StandardListView && model.Inbox.SelectedMessage is { } message)
        {
            _ = model.Inbox.SelectAsync(message.Key);
            return true;
        }
        return false;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=609020
    // Broiler-Falsified-If: Tab moves focus onto a disabled control or into the collapsed SMTP fields
    // Broiler-Human:        PENDING
    public void MoveFocus(int direction)
    {
        var controls = new List<UiElement> { shell.Navigation };
        if (shell.Navigation.SelectedTab?.Content is { } content)
            controls.AddRange(Descendants(content).Where(IsFocusable));
        int current = controls.IndexOf(session.FocusedElement!);
        int next = current < 0 ? (direction > 0 ? 0 : controls.Count - 1) : (current + direction + controls.Count) % controls.Count;
        session.SetFocus(controls[next]);
        Reveal(controls[next]);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=36A581
    // Broiler-Falsified-If: a focused field below the visible area of its scroll view stays out of view after Tab
    // Broiler-Human:        PENDING
    private void Reveal(UiElement element)
    {
        // Layout first so newly selected tabs have useful bounds. Then reveal off-screen form fields.
        session.RenderFrame();
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not StandardScrollView scroll) continue;
            double delta = element.Bounds.Top < scroll.ContentBounds.Top ? element.Bounds.Top - scroll.ContentBounds.Top
                : element.Bounds.Bottom > scroll.ContentBounds.Bottom ? element.Bounds.Bottom - scroll.ContentBounds.Bottom : 0;
            if (delta != 0) { scroll.ScrollBy(0, delta); session.RenderFrame(); }
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=A40427
    // Broiler-Falsified-If: a disabled edit, button or combo box is reported focusable
    // Broiler-Human:        PENDING
    private static bool IsFocusable(UiElement element) => element switch
    {
        StandardEdit edit => edit.IsEnabled,
        StandardRichEdit edit => edit.IsEnabled,
        StandardButton button => button.IsEnabled,
        StandardComboBox combo => combo.IsEnabled,
        StandardListView => true,
        StandardScrollView scroll => !Descendants(scroll).Any(item => item is StandardEdit or StandardRichEdit or StandardButton or StandardComboBox or StandardListView),
        _ => false,
    };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=7860E1
    // Broiler-Falsified-If: an element inside a collapsed panel is returned
    // Broiler-Human:        PENDING
    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        if (element.Visibility != UiVisibility.Visible) yield break;
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
