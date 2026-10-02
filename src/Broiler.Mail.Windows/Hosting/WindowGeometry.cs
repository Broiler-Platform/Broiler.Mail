using System.Runtime.InteropServices;
using Broiler.Mail.Core.Settings;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>A rectangle in physical screen pixels.</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(Left, other.Left), top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? new(left, top, right, bottom) : default;
    }
}

/// <summary>
/// Decides where a remembered window may reopen. A window keeps its position only while enough of its
/// title bar lies inside a monitor work area to be grabbed; otherwise it moves onto the work area it
/// overlaps most, or the primary one, so a removed monitor or a resolution change cannot strand it.
/// </summary>
internal static class WindowGeometry
{
    /// <summary>The strip at the top of the outer window treated as its title bar.</summary>
    public const int CaptionHeight = 32;
    /// <summary>How much of that strip must be visible and reachable with the pointer.</summary>
    public const int MinimumVisibleCaptionWidth = 120;
    public const int MinimumVisibleCaptionHeight = 16;

    /// <param name="workAreas">Monitor work areas, primary first.</param>
    /// <returns>The outer top-left corner in physical pixels, or null when no monitor is known.</returns>
    public static (int Left, int Top)? Restore(WindowPlacement saved, IReadOnlyList<PixelRect> workAreas)
    {
        if (workAreas.Count == 0) return null;
        var caption = new PixelRect(saved.Left, saved.Top, saved.Left + saved.Width, saved.Top + CaptionHeight);
        foreach (var area in workAreas)
        {
            var visible = caption.Intersect(area);
            if (visible.Width >= MinimumVisibleCaptionWidth && visible.Height >= MinimumVisibleCaptionHeight)
                // Still reachable; only a caption tucked under the top edge is pulled down into view.
                return (saved.Left, Math.Max(saved.Top, area.Top));
        }

        var window = new PixelRect(saved.Left, saved.Top, saved.Left + saved.Width, saved.Top + saved.Height);
        var target = workAreas.MaxBy(area => (long)window.Intersect(area).Width * window.Intersect(area).Height);
        if (window.Intersect(target).Width == 0) target = workAreas[0];
        return (target.Left + Math.Max(0, (target.Width - saved.Width) / 2), target.Top + Math.Max(0, (target.Height - saved.Height) / 2));
    }
}

/// <summary>How the main window is created and placed from remembered settings.</summary>
/// <param name="Left">Option coordinates in DIPs, or null to let the window center itself.</param>
/// <param name="MoveAfterShow">A physical position the window must be moved to once it exists.</param>
/// <param name="Normal">The remembered normal bounds after clamping, kept while the window starts maximized.</param>
internal sealed record WindowRestorePlan(int ClientWidth, int ClientHeight, double? Left, double? Top,
    (int Left, int Top)? MoveAfterShow, bool Maximized, WindowPlacement? Normal)
{
    public static WindowRestorePlan For(ApplicationSettings settings, IReadOnlyList<PixelRect> workAreas, double systemScale)
    {
        if (settings.Window is not { } saved || WindowGeometry.Restore(saved, workAreas) is not { } position)
            return new(settings.WindowWidth, settings.WindowHeight, null, null, null, false, null);
        var normal = saved with { Left = position.Left, Top = position.Top };
        // Window options clamp coordinates to at least one pixel, so monitors left of or above the
        // primary one (negative coordinates) need a move once the native window exists.
        if (position.Left >= 1 && position.Top >= 1)
            return new(saved.ClientWidth, saved.ClientHeight, position.Left / systemScale, position.Top / systemScale, null, saved.Maximized, normal);
        return new(saved.ClientWidth, saved.ClientHeight, null, null, position, saved.Maximized, normal);
    }
}

/// <summary>Native window geometry helpers for the main window.</summary>
/// <remarks>Reusable host behavior: candidates for Broiler.Hosting.Windows alongside WindowsWindowSizing.</remarks>
internal static unsafe class WindowsScreen
{
    private const uint MonitorPrimary = 1;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    /// <summary>Work areas of all monitors, primary first.</summary>
    public static IReadOnlyList<PixelRect> WorkAreas()
    {
        var areas = new List<(PixelRect Area, bool Primary)>();
        var handle = GCHandle.Alloc(areas);
        try { EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(handle)); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
        finally { handle.Free(); }
        return areas.OrderByDescending(item => item.Primary).Select(item => item.Area).ToArray();
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* bounds, nint state)
    {
        var info = new MONITORINFO { CbSize = (uint)sizeof(MONITORINFO) };
        if (GetMonitorInfo(monitor, ref info) && GCHandle.FromIntPtr(state).Target is List<(PixelRect, bool)> areas)
            areas.Add((new(info.RcWork.Left, info.RcWork.Top, info.RcWork.Right, info.RcWork.Bottom), (info.DwFlags & MonitorPrimary) != 0));
        return 1;
    }

    public static PixelRect? OuterBounds(nint window) =>
        window != 0 && GetWindowRect(window, out RECT rect) ? new(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;

    public static void MoveTo(nint window, int left, int top) =>
        SetWindowPos(window, 0, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    /// <summary>Outer size in pixels; (0, 0) without a window.</summary>
    public static (int Width, int Height) OuterSize(nint window) =>
        OuterBounds(window) is { } bounds ? (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top) : (0, 0);

    /// <summary>Changes the outer size in pixels and keeps the position; used by the UI-12 resize workload.</summary>
    public static void Resize(nint window, int width, int height) =>
        SetWindowPos(window, 0, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate);

    /// <summary>The scale a new window is created at, which converts remembered pixels to option DIPs.</summary>
    public static double SystemScale()
    {
        try { return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) ? GetDpiForSystem() / 96.0 : 1.0; }
        catch (EntryPointNotFoundException) { return 1.0; }
    }

    [DllImport("user32.dll")]
    private static extern int EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, RECT*, nint, int> callback, nint state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
