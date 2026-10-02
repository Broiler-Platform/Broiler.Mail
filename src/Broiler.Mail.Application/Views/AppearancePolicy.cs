using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Chooses the palette from the saved theme preference and the operating system's appearance.
/// Precedence: an active system high-contrast mode always wins, because it is an accessibility
/// setting; otherwise an explicit Light or Dark choice wins; System follows the OS color scheme.
/// Reduced motion and density always come from the system.
/// </summary>
public static class AppearancePolicy
{
    public static StandardThemeTokens Resolve(AppTheme preference, UiSystemSettings system)
    {
        if (system.ContrastPreference == UiContrastPreference.More)
            return StandardThemeTokens.Select(system);
        bool dark = preference switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => system.ColorScheme == UiColorScheme.Dark,
        };
        return StandardThemeTokens.Select(system.ContrastPreference, dark, system.Density, system.ReducedMotion);
    }
}
