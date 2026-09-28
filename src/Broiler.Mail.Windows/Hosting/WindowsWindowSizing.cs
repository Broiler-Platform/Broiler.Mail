using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Hosting;

internal static class WindowsWindowSizing
{
    public static void OnMessage(nint window, uint message, nint data, double scale)
    {
        if (message == 0x0024 && data != 0) // WM_GETMINMAXINFO
        {
            var limits = Marshal.PtrToStructure<MinMaxInfo>(data);
            var rect = new Rect { Right = (int)Math.Ceiling(640 * scale), Bottom = (int)Math.Ceiling(480 * scale) };
            AdjustWindowRectExForDpi(ref rect, (uint)GetWindowLongW(window, -16), false, (uint)GetWindowLongW(window, -20), (uint)Math.Round(96 * scale));
            limits.MinX = rect.Right - rect.Left;
            limits.MinY = rect.Bottom - rect.Top;
            Marshal.StructureToPtr(limits, data, false);
        }
        else if (message == 0x02E0 && data != 0) // WM_DPICHANGED: respect the suggested monitor-scaled rectangle.
        {
            var rect = Marshal.PtrToStructure<Rect>(data);
            SetWindowPos(window, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0014);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public int ReservedX, ReservedY, MaxX, MaxY, PositionX, PositionY, MinX, MinY, MaxTrackX, MaxTrackY; }
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint extendedStyle, uint dpi);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
