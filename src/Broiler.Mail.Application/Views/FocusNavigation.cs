using Broiler.UI;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.TabView;

namespace Broiler.Mail.Application.Views;

/// <summary>Moves focus programmatically and keeps the focused control visible in its scroll views.</summary>
internal static class FocusNavigation
{
    public static void FocusAndReveal(UiSession session, UiElement element)
    {
        session.SetFocus(element);
        Reveal(session, element);
    }

    /// <summary>
    /// Whether an asynchronous result may move focus into <paramref name="area"/>: only while the area
    /// is on screen and focus is inside it, on a container that holds it (such as the tab strip of the
    /// tab showing it), or nowhere. A result that arrives after the user moved on
    /// must not pull them back to another tab or away from what they are typing.
    /// </summary>
    public static bool MayTakeFocus(UiSession session, UiElement area)
    {
        for (UiElement child = area; child.Parent is { } parent; child = parent)
        {
            if (child.Visibility != UiVisibility.Visible || child.IsHiddenFromAccessibility) return false;
            // A tab view keeps every tab's content attached and visible, but shows only the selected one.
            if (parent is UiTabView tabs && tabs.SelectedTab?.Content != child) return false;
        }
        return session.FocusedElement is not { } focused || focused == area || focused.IsDescendantOf(area) || area.IsDescendantOf(focused);
    }

    /// <summary>
    /// Hands focus on from a control in <paramref name="scope"/> that can no longer take it, such as a
    /// command that disabled itself once its work was done: to <paramref name="preferred"/> when that
    /// can take focus, otherwise to the next tab stop after it, wrapping within the scope. Focus
    /// anywhere else is left alone, so a result arriving later never moves it. Callers wait until
    /// the operation has finished; while it runs, focus stays on the command.
    /// </summary>
    public static void KeepFocusUsable(UiSession session, UiElement scope, UiElement? preferred = null)
    {
        // Only controls that normally take focus: a read-only scroll view is a tab stop without being focusable.
        if (session.FocusedElement is not { Focusable: true, CanFocus: false } focused || !focused.IsDescendantOf(scope)) return;
        var target = preferred is { CanFocus: true } ? preferred
            : MailKeyboardNavigation.NextTabStop(scope, focused, 1) ?? MailKeyboardNavigation.TabStops(scope).FirstOrDefault();
        if (target is not null) session.SetFocus(target);
    }

    public static void Reveal(UiSession session, UiElement element)
    {
        // Layout first so newly selected tabs have useful bounds. Then reveal off-screen form fields.
        session.RenderFrame();
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not StandardScrollView scroll) continue;
            double above = element.Bounds.Top - scroll.ContentBounds.Top;
            double below = element.Bounds.Bottom - scroll.ContentBounds.Bottom;
            // A control taller than the viewport, such as the body editor, keeps its top in view.
            double delta = above < 0 ? above : below > 0 ? Math.Min(below, above) : 0;
            if (delta != 0) { scroll.ScrollBy(0, delta); session.RenderFrame(); }
        }
    }
}
