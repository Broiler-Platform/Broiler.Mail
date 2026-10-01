// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   15
// Annotated:        15/15
// Exempt:           0
// Human-reviewed:   0/15
// IP risk:          Low
// Security risk:    Critical
// Criteria:         15/15
// Resource impact:  3/10 max
// Unverified:       15
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Runtime.InteropServices;
using Broiler.UI;

namespace Broiler.Mail.Windows.Services;

/// <summary>Unicode text clipboard. Read only in response to the user's paste command.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=3; Fingerprint=8C35C2
// Broiler-Falsified-If: TryGetText reads past the GlobalSize of a clipboard block that another process placed on the clipboard
// Broiler-Human:        PENDING
internal sealed class WindowsClipboard(Func<nint> owner) : IUiClipboardHost
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=EE45C3
    // Broiler-Falsified-If: the value is not CF_UNICODETEXT (13), so a clipboard block in another format is read as NUL-terminated UTF-16 text
    // Broiler-Human:        PENDING
    private const uint UnicodeText = 13;
    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=0; Fingerprint=81EAFA
    // Broiler-Falsified-If: a clipboard block larger than 1,048,576 bytes is marshalled into a managed string instead of being refused
    // Broiler-Human:        PENDING
    private const int MaximumBytes = 1024 * 1024;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=3; Fingerprint=108A7F
    // Broiler-Falsified-If: PtrToStringUni reads more characters than half the GlobalSize of the locked clipboard block
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=3; Fingerprint=D29CC4
    // Broiler-Falsified-If: after SetClipboardData succeeds the finally block still passes the handle the system now owns to GlobalFree
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=186A76
    // Broiler-Falsified-If: a failed OpenClipboard, with the clipboard held by another window, is returned as true, so TryGetText reads data it has not opened
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint window);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=9C1DBE
    // Broiler-Falsified-If: the clipboard stays open to other processes after TryGetText or SetText returns, because the declaration does not bind user32 CloseClipboard
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=49EEBD
    // Broiler-Falsified-If: a failed EmptyClipboard is returned as true, so SetText hands its block to SetClipboardData on a clipboard it does not own
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=A95361
    // Broiler-Falsified-If: the format argument is marshalled other than as a 32-bit UINT, so user32 is asked for a format other than the one requested
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=D4C996
    // Broiler-Falsified-If: a successful SetClipboardData is marshalled as a zero handle, so SetText frees a block the system now owns
    // Broiler-Human:        PENDING
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=7B51B1
    // Broiler-Falsified-If: the byte count is marshalled narrower than SIZE_T, so the block allocated is smaller than the text SetText copies into it
    // Broiler-Human:        PENDING
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=C12DF1
    // Broiler-Falsified-If: the pointer result is marshalled narrower than a pointer, so a 64-bit address is truncated before Marshal reads or writes through it
    // Broiler-Human:        PENDING
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=7FE0EF
    // Broiler-Falsified-If: the handle argument is marshalled narrower than a pointer, so GlobalUnlock is applied to a different block than the one locked
    // Broiler-Human:        PENDING
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=B2A448
    // Broiler-Falsified-If: the SIZE_T result is declared 32 bits wide, so a clipboard block larger than 4 GiB wraps below MaximumBytes and passes the size check
    // Broiler-Human:        PENDING
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=43E113
    // Broiler-Falsified-If: the handle argument is marshalled narrower than a pointer, so GlobalFree releases a different block than the one SetText allocated
    // Broiler-Human:        PENDING
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
