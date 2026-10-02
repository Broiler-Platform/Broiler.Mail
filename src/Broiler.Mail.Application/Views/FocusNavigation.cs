using Broiler.UI;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Moves focus programmatically and keeps the focused control visible in its scroll views.</summary>
internal static class FocusNavigation
{
    public static void FocusAndReveal(UiSession session, UiElement element)
    {
        session.SetFocus(element);
        Reveal(session, element);
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
