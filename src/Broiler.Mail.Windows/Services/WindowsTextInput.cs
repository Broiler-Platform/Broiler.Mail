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
using Broiler.UI.Edit;
using Broiler.UI.RichEdit.Standard;

namespace Broiler.Mail.Windows.Services;

/// <summary>
/// Places the Windows IME composition window at the caret, in physical pixels, and turns the IME off while the focus
/// is on anything that draws no composition of its own: a password field, a read-only editor, a list, a button.
/// Hosting's input bridge delivers the composition and the committed text, and keeps the IME from drawing a copy of
/// the composition (DrawsCompositionInline), so outside an editor that draws it a composition would be invisible.
/// </summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=4FCA45
// Broiler-Falsified-If: ImmSetCompositionWindow is handed a form whose layout differs from Win32 COMPOSITIONFORM, so IMM32 reads the caret position from the wrong offsets
// Broiler-Human:        PENDING
internal sealed class WindowsTextInput(Func<nint> window, Func<double> scale) : IUiTextInputHost
{
    // IACE_DEFAULT: the window gets its own input context back.
    private const uint RestoreDefaultContext = 0x0010;
    // The focus owner the IME was turned off for (null for no focus), and the window it was turned off in.
    private UiElement? _imeOffFor;
    private nint _imeOffIn;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=F8B574
    // Broiler-Falsified-If: an input context from ImmGetContext is left unreleased when the scale callback or ImmSetCompositionWindow throws
    // Broiler-Human:        PENDING
    public void PublishCaret(UiTextCaretInfo caret)
    {
        nint hwnd = window();
        if (hwnd == 0) return;
        // Broiler.UI draws no composition in a password field or a read-only editor. A native password box takes no
        // IME either: its keys are typed as they are.
        if (!DrawsComposition(caret.Owner))
        {
            TurnImeOff(hwnd, caret.Owner);
            return;
        }
        RestoreIme();
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
    public void ClearCaret(UiElement owner)
    {
        // Focus leaving the field, or the field going away, clears its caret; FollowFocus then decides for the next.
        if (_imeOffIn != 0 && ReferenceEquals(owner, _imeOffFor)) RestoreIme();
    }

    /// <summary>
    /// Turns the IME off when <paramref name="focused"/> (the session's new focus, or null for none) draws no
    /// composition, and gives it back for an editor that does. Elements that are no editor publish no caret,
    /// so the host calls this on every focus change, and once the window exists.
    /// </summary>
    public void FollowFocus(UiElement? focused)
    {
        nint hwnd = window();
        if (hwnd == 0) return;
        if (DrawsComposition(focused)) RestoreIme();
        else TurnImeOff(hwnd, focused);
    }

    /// <summary>
    /// Whether <paramref name="element"/> draws the IME's composition inline and takes its commit: an enabled,
    /// writable edit that is no password field, or an enabled, writable rich edit.
    /// </summary>
    internal static bool DrawsComposition(UiElement? element) => element switch
    {
        UiEdit edit => !edit.IsPassword && !edit.IsReadOnly && edit.IsEnabled,
        StandardRichEdit editor => !editor.IsReadOnly && editor.IsEnabled,
        _ => false,
    };

    private void TurnImeOff(nint hwnd, UiElement? owner)
    {
        if (_imeOffIn != hwnd)
        {
            RestoreIme();
            // A null context turns the IME off for the window until the default context is restored.
            if (!ImmAssociateContextEx(hwnd, 0, 0)) return;
            _imeOffIn = hwnd;
        }
        _imeOffFor = owner;
    }

    private void RestoreIme()
    {
        if (_imeOffIn == 0) return;
        ImmAssociateContextEx(_imeOffIn, 0, RestoreDefaultContext);
        _imeOffIn = 0;
        _imeOffFor = null;
    }

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
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmAssociateContextEx(nint window, nint context, uint flags);
}
