// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>
/// Puts the icon the build embeds in the executable (<c>ApplicationIcon</c>) on the main window,
/// and reloads it at the right size when the window moves to a monitor with another DPI.
/// </summary>
internal sealed class MailWindowIcon : IDisposable
{
    private const int ApplicationIconId = 32512;

    private const uint WmSetIcon = 0x0080;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmDpiChanged = 0x02E0;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const int SmCxIcon = 11;
    private const int SmCyIcon = 12;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;

    private readonly SubclassProc _callback;
    private nint _window;
    private nint _big;
    private nint _small;

    private MailWindowIcon(nint window)
    {
        _window = window;
        _callback = HandleMessage;
    }

    public static MailWindowIcon? TryAttach(nint window)
    {
        var icon = new MailWindowIcon(window);
        if (!icon.Load(DpiOf(window)))
            return null;

        _ = SetWindowSubclass(window, icon._callback, 1, 0);
        return icon;
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            RemoveWindowSubclass(_window, _callback, 1);

            SendMessage(_window, WmSetIcon, IconBig, nint.Zero);
            SendMessage(_window, WmSetIcon, IconSmall, nint.Zero);
            _window = nint.Zero;
        }

        DestroyIcons(_big, _small);
        _big = nint.Zero;
        _small = nint.Zero;
    }

    private bool Load(uint dpi)
    {
        nint module = GetModuleHandle(nint.Zero);
        nint big = LoadImage(module, ApplicationIconId, ImageIcon,
            MetricForDpi(SmCxIcon, dpi), MetricForDpi(SmCyIcon, dpi), 0);
        nint small = LoadImage(module, ApplicationIconId, ImageIcon,
            MetricForDpi(SmCxSmIcon, dpi), MetricForDpi(SmCySmIcon, dpi), 0);

        if (big == nint.Zero || small == nint.Zero)
        {
            DestroyIcons(big, small);
            return false;
        }

        SendMessage(_window, WmSetIcon, IconBig, big);
        SendMessage(_window, WmSetIcon, IconSmall, small);

        DestroyIcons(_big, _small);
        _big = big;
        _small = small;
        return true;
    }

    private nint HandleMessage(nint window, uint message, nint wParam, nint lParam,
        nuint subclassId, nuint referenceData)
    {
        if (message == WmDpiChanged)
        {
            _ = Load(unchecked((uint)((long)wParam & 0xFFFF)));
        }
        else if (message == WmNcDestroy)
        {
            RemoveWindowSubclass(window, _callback, 1);
            _window = nint.Zero;
            Dispose();
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static void DestroyIcons(nint big, nint small)
    {
        if (big != nint.Zero)
            DestroyIcon(big);
        if (small != nint.Zero)
            DestroyIcon(small);
    }

    private static uint DpiOf(nint window)
    {
        uint dpi = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) ? GetDpiForWindow(window) : 0;
        return dpi == 0 ? 96u : dpi;
    }

    private static int MetricForDpi(int index, uint dpi) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
            ? GetSystemMetricsForDpi(index, dpi)
            : GetSystemMetrics(index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam,
        nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static extern nint GetModuleHandle(nint moduleName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW")]
    private static extern nint LoadImage(nint instance, nint name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
