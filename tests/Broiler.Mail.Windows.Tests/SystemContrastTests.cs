using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Hosting.Windows;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
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
            // Selections and control states in the highlight pair, at the system's text size. The state text is what a
            // hovered secondary button's label and focus ring are drawn in.
            Assert.Equal(WindowsSystemColors.Dusk.HighlightText, dusk!.SelectionText);
            Assert.Equal(WindowsSystemColors.Dusk.Highlight, dusk.StateFill);
            Assert.Equal(WindowsSystemColors.Dusk.HighlightText, dusk.StateText);
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
            fixture.Ui(() => fixture.Window.Shell.ShowView("account"));
            AssertRingStandsOut(fixture.Ui(() => (DrawnRing(fixture.Window.Session, save), save.PrimaryBackground)));

            // Switching from one contrast theme to another changes only the colors, and Windows says so with
            // WM_SYSCOLORCHANGE; the settings stay the same.
            MailSystemSettings.ContrastColorsOverride = WindowsSystemColors.Desert;
            Assert.True(PostMessage(fixture.Frame, WmSysColorChange, 0, 0));
            fixture.Settle();
            Assert.Equal(WindowsTheme.CreateHighContrastTheme(WindowsSystemColors.Desert, settings),
                fixture.Ui(() => StandardControlPaint.GetTheme(fixture.Window.Session)));
            AssertRingStandsOut(fixture.Ui(() => (DrawnRing(fixture.Window.Session, save), save.PrimaryBackground)));
        }
        finally { Reset(); }
    }

    /// <summary>
    /// Hosting maps the accent, the state fill and the focus ring of every Windows contrast theme to Highlight, with
    /// HighlightText the label drawn on it. Broiler.UI strokes a button's ring inside the fill of its state, so on the
    /// highlight (Send, Save account and Save settings in every state; a secondary button the pointer rests on) it draws
    /// the ring in that label color (ADR 0032). A secondary button on the window color, at rest or held down, keeps the
    /// Highlight ring.
    /// </summary>
    [Theory]
    [InlineData("aquatic")]
    [InlineData("desert")]
    [InlineData("dusk")]
    [InlineData("night-sky")]
    public void EveryWindowsContrastThemeLeavesAFocusedButtonsRingVisibleInEachState(string theme)
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
            StandardThemeController.Apply(session, palette);
            session.RenderFrame();
            // Pressed last: Space is never let go, which would click the button.
            foreach (var state in new[] { ButtonState.Rest, ButtonState.Hovered, ButtonState.Pressed })
            {
                var drawn = Drawn(session, send, state);
                Assert.Equal((colors.Highlight, colors.HighlightText, colors.HighlightText), drawn);
                AssertRingStandsOut((drawn.Ring, drawn.Fill));
            }
            foreach (var state in new[] { ButtonState.Rest, ButtonState.Hovered, ButtonState.Pressed })
            {
                var drawn = Drawn(session, check, state);
                Assert.Equal(state == ButtonState.Hovered ? (colors.Highlight, colors.HighlightText, colors.HighlightText) : (colors.Window, colors.WindowText, colors.Highlight), drawn);
                AssertRingStandsOut((drawn.Ring, drawn.Fill));
            }
        }
        finally { StandardControlPaint.ApplyTheme(before); }
    }

    private enum ButtonState { Rest, Hovered, Pressed }

    /// <summary>The ring <paramref name="button"/> draws while it has keyboard focus, read from a rendered frame.</summary>
    private static BColor DrawnRing(UiSession session, StandardButton button) => Drawn(session, button, ButtonState.Rest).Ring;

    /// <summary>
    /// Puts <paramref name="button"/> into <paramref name="state"/> with its keyboard focus shown, as a user does: a key
    /// press shows the focus (hovered: while the pointer rests on the button), or Space is held down. Returns the fill,
    /// label and focus ring it then draws, read from a rendered frame.
    /// </summary>
    private static (BColor Fill, BColor Label, BColor Ring) Drawn(UiSession session, StandardButton button, ButtonState state)
    {
        if (state == ButtonState.Hovered)
            session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(Header(),
                InputPoint.ClientDeviceIndependentPixels(button.Bounds.Left + (button.Bounds.Width / 2), button.Bounds.Top + (button.Bounds.Height / 2)),
                MouseButtons.None, InputEventSource.Synthetic)));
        session.SetFocus(button);
        // Shift does nothing on a button but counts as keyboard use; Space holds the button down until it is let go.
        int key = state == ButtonState.Pressed ? 0x20 : 0x10;
        session.DispatchInput(UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(Header(), KeyboardKey.FromName("VirtualKey:" + key),
            KeyboardKeyTransition.Down, KeyboardModifierState.None, key, 0, 0, false, false, Source: InputEventSource.Synthetic)));
        Assert.True(session.IsFocusVisible);
        Assert.Equal(state == ButtonState.Pressed, button.IsPressed);

        var commands = session.RenderFrame()!.Commands.ToArray();
        var fill = commands.OfType<BRenderCommand.FillRoundedRect>().Last(command => command.Rect == button.Bounds).Color;
        var label = commands.OfType<BRenderCommand.DrawText>().Last(command => command.Text.Text == button.Text).Text.Color;
        var ring = commands.OfType<BRenderCommand.StrokeRoundedRect>().Single(command => command.Rect == StandardControlPaint.Inset(button.Bounds, 2)).Color;
        return (fill, label, ring);
    }

    private static InputEventHeader Header() => new(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);

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
