using Broiler.Hosting.Windows;
using Broiler.Mail.Windows.Hosting;
using Broiler.UI;
using Broiler.UI.Standard;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// In Windows high contrast the palette is built from the system's own contrast colors (Hosting's
/// <see cref="WindowsTheme.CreateHighContrastTheme"/>), and a demo run can simulate one of the Windows 11 contrast
/// themes. The system's settings are never changed: the demo overrides stand in for them.
/// </summary>
[Collection("UI theme")]
public sealed class SystemContrastTests
{
    private const uint WmSysColorChange = 0x0015;

    [Fact]
    public void ANamedContrastThemeBuildsTheSystemPaletteAndHighKeepsThePreset()
    {
        var settings = UiSystemSettings.Default with { ContrastPreference = UiContrastPreference.More, TextScale = 1.5 };
        try
        {
            MailSystemSettings.HighContrastOverride = true;
            MailSystemSettings.ContrastColorsOverride = WindowsSystemColors.Dusk;
            var dusk = MailSystemSettings.HighContrastTheme(settings);
            Assert.Equal(WindowsTheme.CreateHighContrastTheme(WindowsSystemColors.Dusk, settings), dusk);
            // Selections and control states in the highlight pair, at the system's text size.
            Assert.Equal(WindowsSystemColors.Dusk.HighlightText, dusk!.SelectionText);
            Assert.Equal(WindowsSystemColors.Dusk.Highlight, dusk.StateFill);
            Assert.Equal(StandardThemeTokens.HighContrastDark.FontBody.Size * 1.5, dusk.FontBody.Size, 2);

            // --contrast high: no system palette, so AppearancePolicy keeps the theme's preset.
            MailSystemSettings.ContrastColorsOverride = null;
            Assert.Null(MailSystemSettings.HighContrastTheme(settings));
        }
        finally { Reset(); }
    }

    [Fact]
    public void TheWindowTakesTheContrastThemesPaletteAndFollowsAColorChange()
    {
        try
        {
            MailSystemSettings.HighContrastOverride = true;
            MailSystemSettings.ContrastColorsOverride = WindowsSystemColors.Dusk;
            using var fixture = HiddenMailWindow.Start();
            var settings = fixture.Ui(() => fixture.Window.Host.Settings);
            Assert.Equal(UiContrastPreference.More, settings.ContrastPreference);
            Assert.Equal(WindowsTheme.CreateHighContrastTheme(WindowsSystemColors.Dusk, settings),
                fixture.Ui(() => StandardControlPaint.GetTheme(fixture.Window.Session)));

            // Switching from one contrast theme to another changes only the colors, and Windows says so with
            // WM_SYSCOLORCHANGE; the settings stay the same.
            MailSystemSettings.ContrastColorsOverride = WindowsSystemColors.Desert;
            Assert.True(PostMessage(fixture.Frame, WmSysColorChange, 0, 0));
            fixture.Settle();
            Assert.Equal(WindowsTheme.CreateHighContrastTheme(WindowsSystemColors.Desert, settings),
                fixture.Ui(() => StandardControlPaint.GetTheme(fixture.Window.Session)));
        }
        finally { Reset(); }
    }

    private static void Reset()
    {
        MailSystemSettings.HighContrastOverride = false;
        MailSystemSettings.ContrastColorsOverride = null;
    }
}
