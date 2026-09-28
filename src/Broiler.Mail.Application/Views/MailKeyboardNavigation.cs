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

namespace Broiler.Mail.Application.Views;

public sealed class MailKeyboardNavigation(UiSession session, MailShellView shell, MailShellViewModel model)
{
    public bool Handle(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        bool control = input.KeyModifiers.HasFlag(KeyboardModifierState.Control);
        bool shift = input.KeyModifiers.HasFlag(KeyboardModifierState.Shift);
        if (input.NativeKeyCode == 9)
        {
            if (control)
            {
                shell.Navigation.SelectedIndex = (shell.Navigation.SelectedIndex + (shift ? 2 : 1)) % 3;
                session.SetFocus(shell.Navigation);
            }
            else MoveFocus(shift ? -1 : 1);
            return true;
        }
        if (control && input.NativeKeyCode is >= 0x31 and <= 0x33)
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

    private static bool IsFocusable(UiElement element) => element switch
    {
        StandardEdit edit => edit.IsEnabled,
        StandardButton button => button.IsEnabled,
        StandardComboBox combo => combo.IsEnabled,
        StandardListView => true,
        StandardScrollView scroll => !Descendants(scroll).Any(item => item is StandardEdit or StandardButton or StandardComboBox or StandardListView),
        _ => false,
    };

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        if (element.Visibility != UiVisibility.Visible) yield break;
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
