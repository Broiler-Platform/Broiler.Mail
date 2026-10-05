using Broiler.Hosting.Windows;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>
/// The operating system's appearance settings as the app sees them. A demo run may fix the text size
/// (--text-scale) or turn on high contrast (--contrast), so both can be checked without changing the
/// Windows settings. With --contrast high the palette is the theme's high-contrast preset; with a named
/// Windows contrast theme (--contrast dusk and the others) it is the palette that theme's colors give.
/// </summary>
internal static class MailSystemSettings
{
    /// <summary>The text scale to use instead of the system's (1 = 100 %), or null for the system's.</summary>
    public static double? TextScaleOverride { get; set; }

    /// <summary>True to report high contrast as active regardless of the system.</summary>
    public static bool HighContrastOverride { get; set; }

    /// <summary>
    /// With <see cref="HighContrastOverride"/>: the Windows contrast theme whose colors to use instead of the
    /// system's, or null for the theme's high-contrast preset.
    /// </summary>
    public static WindowsSystemColors? ContrastColorsOverride { get; set; }

    public static UiSystemSettings Query()
    {
        var settings = WindowsTheme.QuerySystemSettings();
        if (TextScaleOverride is { } scale) settings = settings with { TextScale = scale };
        if (HighContrastOverride) settings = settings with { ContrastPreference = UiContrastPreference.More };
        return settings;
    }

    /// <summary>
    /// The high-contrast palette for <paramref name="settings"/>, for <c>AppearancePolicy</c>: built from
    /// the system's own colors, as Windows draws with them (the highlight pair for selections and control
    /// states) and at the settings' text scale; from the simulated contrast theme in a demo run; or null,
    /// for the theme's preset, with --contrast high or where the colors cannot be read.
    /// </summary>
    public static StandardThemeTokens? HighContrastTheme(UiSystemSettings settings)
    {
        var colors = HighContrastOverride ? ContrastColorsOverride : WindowsSystemColors.Query();
        return colors is { } system ? WindowsTheme.CreateHighContrastTheme(system, settings) : null;
    }
}
