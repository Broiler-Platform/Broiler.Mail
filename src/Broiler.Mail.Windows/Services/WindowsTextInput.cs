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
// Security risk:    High
// Criteria:         6/6
// Resource impact:  0/10 max
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Runtime.InteropServices;
using Broiler.UI;

namespace Broiler.Mail.Windows.Services;

/// <summary>Positions the default Windows IME composition UI; committed text arrives through WM_CHAR.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=4FCA45
// Broiler-Falsified-If: ImmSetCompositionWindow is handed a form whose layout differs from Win32 COMPOSITIONFORM, so IMM32 reads the caret position from the wrong offsets
// Broiler-Human:        PENDING
internal sealed class WindowsTextInput(Func<nint> window, Func<double> scale) : IUiTextInputHost
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=F8B574
    // Broiler-Falsified-If: an input context from ImmGetContext is left unreleased when the scale callback or ImmSetCompositionWindow throws
    // Broiler-Human:        PENDING
    public void PublishCaret(UiTextCaretInfo caret)
    {
        nint hwnd = window();
        if (hwnd == 0) return;
        nint context = ImmGetContext(hwnd);
        if (context == 0) return;
        try
        {
            var form = new CompositionForm
            {
                Style = 2, // CFS_POINT, in client pixels.
                X = (int)Math.Round(caret.Bounds.Left * scale()),
                Y = (int)Math.Round(caret.Bounds.Bottom * scale()),
            };
            ImmSetCompositionWindow(context, ref form);
        }
        finally { ImmReleaseContext(hwnd, context); }
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=1F0AE4
    // Broiler-Human:        PENDING
    public void ClearCaret(UiElement owner) { }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=7817E6
    // Broiler-Falsified-If: the struct is smaller than Win32 COMPOSITIONFORM (28 bytes), so ImmSetCompositionWindow reads past the end of the caller's copy
    // Broiler-Human:        PENDING
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm { public uint Style; public int X, Y, Left, Top, Right, Bottom; }
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=964CC6
    // Broiler-Falsified-If: the window or context handle is declared narrower than a pointer, so a 64-bit HIMC is truncated before ImmSetCompositionWindow uses it
    // Broiler-Human:        PENDING
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint window);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=1742AC
    // Broiler-Falsified-If: a parameter is declared narrower than a pointer, so the context released is not the one ImmGetContext returned
    // Broiler-Human:        PENDING
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmReleaseContext(nint window, nint context);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=3139A5
    // Broiler-Falsified-If: the form is passed by value rather than by reference, so IMM32 dereferences the struct's first field as a pointer
    // Broiler-Human:        PENDING
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmSetCompositionWindow(nint context, ref CompositionForm form);
}
