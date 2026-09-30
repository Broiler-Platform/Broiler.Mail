// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   7
// Annotated:        7/7
// Exempt:           2
// Human-reviewed:   0/7
// IP risk:          Low
// Security risk:    Critical
// Criteria:         7/7
// Resource impact:  1/10 max
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Runtime.InteropServices;

namespace Broiler.Mail.Windows.Hosting;

// Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=1; Fingerprint=C8960E
// Broiler-Falsified-If: a MINMAXINFO or RECT is read from or written to lParam with a managed layout larger than the native structure, touching memory past it
// Broiler-Human:        PENDING
internal static class WindowsWindowSizing
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=1; Fingerprint=1B203E
    // Broiler-Falsified-If: StructureToPtr writes more bytes back through the WM_GETMINMAXINFO lParam than the 40-byte native MINMAXINFO it points to
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=0; Fingerprint=4F156D
    // Broiler-Falsified-If: Marshal.SizeOf of Rect differs from the 16 bytes of the native RECT that WM_DPICHANGED and AdjustWindowRectExForDpi use
    // Broiler-Human:        PENDING
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=0; Fingerprint=993869
    // Broiler-Falsified-If: Marshal.SizeOf of MinMaxInfo is not the 40 bytes of the native MINMAXINFO, so StructureToPtr writes outside it
    // Broiler-Human:        PENDING
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public int ReservedX, ReservedY, MaxX, MaxY, PositionX, PositionY, MinX, MinY, MaxTrackX, MaxTrackY; }
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=2AF1F3
    // Broiler-Falsified-If: the declaration binds a signature other than user32 GetWindowLongW(HWND, int) returning a 32-bit LONG, so the style bits read are wrong
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=B3B4F6
    // Broiler-Falsified-If: the declared parameter order differs from user32 AdjustWindowRectExForDpi(LPRECT, DWORD, BOOL, DWORD, UINT), so the DPI or extended style is read from the wrong argument
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint extendedStyle, uint dpi);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=D314B9
    // Broiler-Falsified-If: the declaration differs from user32 SetWindowPos(HWND, HWND, int, int, int, int, UINT), so the suggested DPI rectangle is applied with wrong coordinates or flags
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
