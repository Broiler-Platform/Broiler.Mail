// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Chooses the palette from the saved theme preference and the operating system's appearance.
/// Precedence: an active system high-contrast mode always wins, because it is an accessibility
/// setting; otherwise an explicit Light or Dark choice wins; System follows the OS color scheme.
/// Reduced motion, density, and the text size always come from the system.
/// </summary>
public static class AppearancePolicy
{
    /// <param name="highContrast">
    /// The palette of the system's own contrast colors for the given settings, or null where the host
    /// has none. A platform host supplies it (on Windows, the user's contrast theme); without it, or
    /// when it returns null, high contrast uses the theme's high-contrast preset.
    /// </param>
    public static StandardThemeTokens Resolve(AppTheme preference, UiSystemSettings system,
        Func<UiSystemSettings, StandardThemeTokens?>? highContrast = null)
    {
        if (system.ContrastPreference == UiContrastPreference.More)
            return highContrast?.Invoke(system) ?? StandardThemeTokens.Select(system);
        bool dark = preference switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => system.ColorScheme == UiColorScheme.Dark,
        };
        return StandardThemeTokens.Select(system.ContrastPreference, dark, system.Density, system.ReducedMotion)
            .WithTextScale(double.IsFinite(system.TextScale) && system.TextScale > 0 ? system.TextScale : 1);
    }
}
