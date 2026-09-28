using System.Runtime.InteropServices;
using Broiler.UI;

namespace Broiler.Mail.Windows.Services;

/// <summary>Positions the default Windows IME composition UI; committed text arrives through WM_CHAR.</summary>
internal sealed class WindowsTextInput(Func<nint> window, Func<double> scale) : IUiTextInputHost
{
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

    public void ClearCaret(UiElement owner) { }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm { public uint Style; public int X, Y, Left, Top, Right, Bottom; }
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmSetCompositionWindow(nint context, ref CompositionForm form);
}
