using Broiler.Hosting.Windows;
using Broiler.UI;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>
/// The operating system's appearance settings as the app sees them. A demo run may fix the text size
/// (--text-scale) or turn on high contrast (--contrast high), so both can be checked without changing
/// the Windows settings. The high-contrast palette is then the theme's own, not the system's colors.
/// </summary>
internal static class MailSystemSettings
{
    /// <summary>The text scale to use instead of the system's (1 = 100 %), or null for the system's.</summary>
    public static double? TextScaleOverride { get; set; }

    /// <summary>True to report high contrast as active regardless of the system.</summary>
    public static bool HighContrastOverride { get; set; }

    public static UiSystemSettings Query()
    {
        var settings = WindowsTheme.QuerySystemSettings();
        if (TextScaleOverride is { } scale) settings = settings with { TextScale = scale };
        if (HighContrastOverride) settings = settings with { ContrastPreference = UiContrastPreference.More };
        return settings;
    }
}
