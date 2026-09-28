using System.Runtime.InteropServices;
using Broiler.UI;

namespace Broiler.Mail.Windows.Services;

/// <summary>Unicode text clipboard. Read only in response to the user's paste command.</summary>
internal sealed class WindowsClipboard(Func<nint> owner) : IUiClipboardHost
{
    private const uint UnicodeText = 13;
    private const int MaximumBytes = 1024 * 1024;

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        if (!OpenClipboard(owner())) return false;
        try
        {
            nint handle = GetClipboardData(UnicodeText);
            nuint size = handle == 0 ? 0 : GlobalSize(handle);
            if (size < 2 || size > MaximumBytes) return false;
            nint data = GlobalLock(handle);
            if (data == 0) return false;
            try
            {
                string value = Marshal.PtrToStringUni(data, (int)size / 2) ?? string.Empty;
                int end = value.IndexOf('\0');
                if (end < 0) return false;
                text = value[..end];
                return true;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length >= MaximumBytes / 2 || !OpenClipboard(owner())) return;
        nint handle = 0;
        try
        {
            handle = GlobalAlloc(0x42, (nuint)((text.Length + 1) * 2)); // Movable, zero-initialized.
            if (handle == 0) return;
            nint data = GlobalLock(handle);
            if (data == 0) return;
            try { Marshal.Copy(text.ToCharArray(), 0, data, text.Length); }
            finally { GlobalUnlock(handle); }
            if (EmptyClipboard() && SetClipboardData(UnicodeText, handle) != 0) handle = 0; // OS owns it now.
        }
        finally
        {
            if (handle != 0) GlobalFree(handle);
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
