using System.Runtime.InteropServices;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Preview;
using Broiler.UI.Standard;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// The native caption matches the applied palette at each point Mail sets it: when a window is created,
/// when the saved theme changes, when a measurement run switches themes, and when an open preview is
/// re-themed. The caption is read back from the window manager, so a dropped call shows.
/// </summary>
[Collection("UI theme")]
public sealed class WindowCaptionTests
{
    [Fact]
    public async Task TheMainCaptionIsDarkFromCreationAndFollowsEveryThemeChange()
    {
        if (!DwmCaption.IsSupported) return; // Older builds keep the default caption by design.
        // High contrast decides the palette over the saved theme; expect whatever the policy chooses.
        bool darkWhenDark = AppearancePolicy.Resolve(AppTheme.Dark, MailSystemSettings.Query(), MailSystemSettings.HighContrastTheme).IsDark;
        bool darkWhenLight = AppearancePolicy.Resolve(AppTheme.Light, MailSystemSettings.Query(), MailSystemSettings.HighContrastTheme).IsDark;
        var palette = StandardControlPaint.Theme;
        var result = new TaskCompletionSource<(bool Created, bool Saved, bool Measured)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = DemoApplication.Create(new DemoOptions(DemoScenario.Inbox, AppTheme.Dark));
                app.InitializeAsync().GetAwaiter().GetResult();
                using var window = new WindowsMailWindow(app);
                var settings = window.Model.Settings;
                bool created = false;
                settings.Changed += (_, _) =>
                {
                    if (settings.Settings.Theme != AppTheme.Light || result.Task.IsCompleted) return;
                    // The window's appearance controller subscribed first, so the saved theme is applied.
                    bool saved = DwmCaption.IsDark(window.NativeHandle);
                    window.ApplyThemeForMeasurement(StandardThemeTokens.Dark);
                    result.TrySetResult((created, saved, DwmCaption.IsDark(window.NativeHandle)));
                    PostMessage(window.NativeHandle, 0x0010, 0, 0); // WM_CLOSE
                };
                // The dark theme was applied before the window existed; only the creation call reaches the caption.
                window.Show();
                ShowWindow(window.NativeHandle, 0); // Keep the native fixture hidden.
                created = DwmCaption.IsDark(window.NativeHandle);
                settings.Theme = AppTheme.Light;
                _ = settings.SaveAsync();
                while (GetMessage(out MSG message, nint.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                result.TrySetException(new InvalidOperationException("The window closed before the saved theme was applied."));
            }
            catch (Exception error) { result.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        bool created, saved, measured;
        // The window sets the process-wide palette; the next window or test starts from the one before.
        try { (created, saved, measured) = await result.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { StandardControlPaint.ApplyTheme(palette); }
        Assert.Equal(darkWhenDark, created);
        Assert.Equal(darkWhenLight, saved);
        Assert.True(measured);
    }

    [Fact]
    public async Task APreviewOpenedFromADarkShellHasADarkCaptionAtOnceAndFollowsALightTheme()
    {
        if (!DwmCaption.IsSupported) return; // Older builds keep the default caption by design.
        var result = new TaskCompletionSource<(bool Opened, bool Changed)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HtmlPreviewWindow(new HtmlPreviewDocument("<p>Agenda</p>", new HashSet<string>()), "Agenda as text", _ => { }, theme: StandardThemeTokens.Dark)
                { ShowInTaskbar = false, Opacity = 0 };
                window.Shown += (_, _) =>
                {
                    // The theme given at construction reaches the session only; until ApplyTheme, only the
                    // creation call can have set the caption.
                    bool opened = DwmCaption.IsDark(window.NativeHandle);
                    window.ApplyTheme(StandardThemeTokens.Light);
                    // Queued after the theme, so it runs once the theme is applied.
                    window.Post(() =>
                    {
                        result.TrySetResult((opened, DwmCaption.IsDark(window.NativeHandle)));
                        window.Close();
                    });
                };
                window.Run();
            }
            catch (Exception error) { result.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (opened, changed) = await result.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(opened);
        Assert.False(changed);
    }
}

/// <summary>Reads the caption mode back from the window manager.</summary>
internal static class DwmCaption
{
    /// <summary>A dark caption needs Windows 10 build 19041 or later; earlier builds keep the default.</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE as the window manager reports it; false when it cannot be read.</summary>
    public static bool IsDark(nint window) => DwmGetWindowAttribute(window, 20, out int value, sizeof(int)) == 0 && value != 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
