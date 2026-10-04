using Broiler.Graphics.Color;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Keeps the keyboard focus ring of a default button (Send, Save account, Save settings) visible. Broiler.UI
/// strokes a button's ring inside its fill and fills a default button with the accent, so a palette whose focus
/// ring is its accent draws the ring on itself: the Light preset, and the palette Windows' own contrast colors
/// give, where both are Highlight. Such a button rings itself in its label color instead, as Broiler.UI rings a
/// selected row in high contrast. A secondary button's ring on its hover fill has the same problem; one ring
/// color cannot serve both of its fills, so that stays with Broiler.UI.
/// </summary>
internal static class DefaultButtonFocus
{
    public static void KeepRingsVisible(UiSession session, StandardThemeTokens theme)
    {
        foreach (var root in session.Roots)
            foreach (var element in StandardTreeTraversal.PreOrder(root))
                if (element is StandardButton { IsDefault: true } button)
                    button.FocusRing = RingFor(button, theme.FocusRing);
    }

    /// <summary>
    /// <paramref name="ring"/> where it stands out (3:1) against every fill the button draws while enabled
    /// (at rest, hovered, pressed); otherwise the button's label color, which reads on all of them.
    /// </summary>
    internal static BColor RingFor(StandardButton button, BColor ring) =>
        new[] { button.PrimaryBackground, button.HoverBackground, button.PressedBackground }
            .All(fill => StandardContrast.Ratio(ring, fill) >= StandardContrast.AaLargeOrUi) ? ring : button.PrimaryForeground;
}
