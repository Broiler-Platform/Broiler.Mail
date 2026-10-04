using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Hosting.Windows;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Windows.Hosting;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Panel.Standard;
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
            var save = fixture.Ui(() => HiddenMailWindow.Descendants(fixture.Window.Shell.Window).OfType<StandardButton>().Single(button => button.Text == "Save account"));
            AssertRingStandsOut(fixture.Ui(() => (save.FocusRing, save.PrimaryBackground)));

            // Switching from one contrast theme to another changes only the colors, and Windows says so with
            // WM_SYSCOLORCHANGE; the settings stay the same.
            MailSystemSettings.ContrastColorsOverride = WindowsSystemColors.Desert;
            Assert.True(PostMessage(fixture.Frame, WmSysColorChange, 0, 0));
            fixture.Settle();
            Assert.Equal(WindowsTheme.CreateHighContrastTheme(WindowsSystemColors.Desert, settings),
                fixture.Ui(() => StandardControlPaint.GetTheme(fixture.Window.Session)));
            AssertRingStandsOut(fixture.Ui(() => (save.FocusRing, save.PrimaryBackground)));
        }
        finally { Reset(); }
    }

    /// <summary>
    /// Hosting maps the accent and the focus ring of every Windows contrast theme to Highlight, and Broiler.UI strokes a
    /// default button's ring inside its accent fill, so Mail rings Send, Save account and Save settings in their label
    /// color (HighlightText) instead; buttons on the window color keep the Highlight ring.
    /// </summary>
    [Theory]
    [InlineData("aquatic")]
    [InlineData("desert")]
    [InlineData("dusk")]
    [InlineData("night-sky")]
    public void EveryWindowsContrastThemeLeavesTheDefaultButtonsFocusRingVisible(string theme)
    {
        var colors = theme switch
        {
            "aquatic" => WindowsSystemColors.Aquatic,
            "desert" => WindowsSystemColors.Desert,
            "dusk" => WindowsSystemColors.Dusk,
            _ => WindowsSystemColors.NightSky,
        };
        var palette = WindowsTheme.CreateHighContrastTheme(colors, UiSystemSettings.Default with { ContrastPreference = UiContrastPreference.More });
        Assert.Equal(palette.Accent, palette.FocusRing);
        var send = new StandardButton { Text = "Send", IsDefault = true };
        var check = new StandardButton { Text = "Check draft" };
        var bar = new StandardPanel();
        bar.AddChild(send);
        bar.AddChild(check);
        var before = StandardControlPaint.Theme;
        using var session = new StandardUiSessionBuilder().Build(new Host());
        session.AddRoot(bar);
        try
        {
            AppearanceController.Theme(session, palette);
            Assert.Equal(colors.HighlightText, send.FocusRing);
            AssertRingStandsOut((send.FocusRing, send.PrimaryBackground));
            Assert.Equal(colors.Highlight, check.FocusRing);
        }
        finally { StandardControlPaint.ApplyTheme(before); }
    }

    private static void AssertRingStandsOut((BColor Ring, BColor Fill) button) =>
        Assert.True(StandardContrast.Ratio(button.Ring, button.Fill) >= StandardContrast.AaLargeOrUi, $"ring {button.Ring} on {button.Fill}");

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(400, 100);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    private static void Reset()
    {
        MailSystemSettings.HighContrastOverride = false;
        MailSystemSettings.ContrastColorsOverride = null;
    }
}
