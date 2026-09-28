using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.UI;
using Broiler.Mail.Windows.Services;

namespace Broiler.Mail.Windows.Hosting;

internal sealed class WindowsUiHost(WindowsMailWindow window) : IUiHost, IUiClipboardHost, IUiTextInputHost
{
    private readonly WindowsClipboard _clipboard = new(() => window.NativeHandle);
    private readonly WindowsTextInput _textInput = new(() => window.InputHandle, () => window.DpiScale);
    public bool TryGetText(out string text) => _clipboard.TryGetText(out text);
    public void SetText(string text) => _clipboard.SetText(text);
    public void PublishCaret(UiTextCaretInfo caret) => _textInput.PublishCaret(caret);
    public void ClearCaret(UiElement owner) => _textInput.ClearCaret(owner);
    public BSize ViewportSize { get; private set; } = new(1100, 720);
    public double Scale { get; private set; } = 1;

    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
    public void Invalidate(UiInvalidation invalidation) => window.Invalidate();

    public void Present(BRenderList renderList)
    {
        // Direct2DWindow presents the list returned by BuildRenderList.
    }

    public void Update(BSize viewportSize, double scale)
    {
        ViewportSize = viewportSize;
        Scale = scale;
    }
}
