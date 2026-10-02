using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>
/// Matches the native caption to the applied palette. Windows keeps a light caption for dark content
/// unless the window opts in, which made dark mode look half-applied.
/// </summary>
/// <remarks>Reusable host behavior: move into Broiler.Hosting.Windows and consume it from there once published.</remarks>
internal static class WindowsTitleBar
{
    private const int ImmersiveDarkMode = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE, Windows 10 20H1 and later.

    /// <returns>False when the system does not support the attribute; the caption then keeps its default.</returns>
    public static bool Apply(nint window, bool dark)
    {
        if (window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        int value = dark ? 1 : 0;
        return DwmSetWindowAttribute(window, ImmersiveDarkMode, ref value, sizeof(int)) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
