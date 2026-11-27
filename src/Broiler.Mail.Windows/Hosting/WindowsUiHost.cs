// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   18
// Annotated:        9/18
// Exempt:           11
// Human-reviewed:   0/18
// IP risk:          Low
// Security risk:    Critical
// Criteria:         5/4
// Resource impact:  3/10 max
// Unverified:       18
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Mail.Windows.Services;
using Broiler.UI;

namespace Broiler.Mail.Windows.Hosting;

// Hairs-AI:           Origin=AI; IP=Low; Security=Critical; Resources=3; Fingerprint=1C4409
// Hairs-Falsified-If: a paste reads clipboard memory past the size GlobalSize reports for a block another process placed there
// Hairs-Human:        PENDING
internal sealed class WindowsUiHost(Direct2DWindow window, Func<nint> inputHandle) : IUiHost, IUiClipboardHost, IUiTextInputHost, IUiSystemSettingsHost, IUiWindowHost, IUiImageHost
{
    private readonly WindowsClipboard _clipboard = new(() => window.NativeHandle);
    private readonly WindowsTextInput _textInput = new(inputHandle, () => window.DpiScale);
    private readonly List<WindowsHostWindow> _hostWindows = [];
    private UiSystemSettings _settings = MailSystemSettings.Query();
    private UiSession? _session;

    internal WindowsHostWindow? ActiveHostWindow { get; private set; }

    /// <summary>
    /// The main window's keyboard navigation, applied in broken-out windows too, so Tab moves between
    /// fields, Enter never accepts a form by accident, and the shell's shortcuts work there as well.
    /// </summary>
    internal Func<UiInputEvent, bool>? KeyboardFilter { get; set; }
    internal IReadOnlyList<WindowsHostWindow> HostWindows => _hostWindows;

    public IUiHostWindow CreateHostWindow(UiHostWindowRequest request)
    {
        var hostWindow = new WindowsHostWindow(window, request, KeyboardFilter);
        _hostWindows.Add(hostWindow);
        ActiveHostWindow = hostWindow;
        hostWindow.Closed += (_, _) =>
        {
            _hostWindows.Remove(hostWindow);
            if (ActiveHostWindow == hostWindow)
                ActiveHostWindow = _hostWindows.LastOrDefault();
        };
        hostWindow.Show();
        return hostWindow;
    }

    public void DisposeHostWindows()
    {
        foreach (var hostWindow in _hostWindows.ToArray())
        {
            try { hostWindow.Dispose(); }
            catch (Exception) { }
        }
        _hostWindows.Clear();
        ActiveHostWindow = null;
    }

    public UiSystemSettings Settings => _settings;

    /// <summary>
    /// Lets the IME follow <paramref name="session"/>'s focus: off while the focus draws no composition, such as
    /// a list or a button, which publish no caret. The native window may not exist yet; <see cref="FollowFocus"/>
    /// applies the focus set before it did.
    /// </summary>
    public void TrackFocus(UiSession session)
    {
        _session = session;
        session.SemanticChanged += (_, e) =>
        {
            if (e.Change == UiSemanticChangeKind.FocusChanged) FollowFocus();
        };
    }

    /// <summary>Turns the IME on or off for the tracked session's current focus.</summary>
    public void FollowFocus() => _textInput.FollowFocus(_session?.FocusedElement);

    public event EventHandler<UiSystemSettingsChangedEventArgs>? SettingsChanged;

    public void RefreshSettings()
    {
        var newSettings = MailSystemSettings.Query();
        if (_settings != newSettings)
        {
            _settings = newSettings;
            SettingsChanged?.Invoke(this, new UiSystemSettingsChangedEventArgs(_settings));
            window.Invalidate();
        }
    }

    // Hairs-AI:           Origin=AI; IP=None; Security=Critical; Resources=3; Fingerprint=E01786
    // Hairs-Falsified-If: PtrToStringUni reads more characters than half the GlobalSize of the locked clipboard block, reading past memory another process allocated
    // Hairs-Human:        PENDING
    public bool TryGetText(out string text) => _clipboard.TryGetText(out text);
    // Hairs-AI:           Origin=AI; IP=None; Security=Critical; Resources=3; Fingerprint=A65525
    // Hairs-Falsified-If: the global block passed to SetClipboardData is freed afterwards by the host although the clipboard now owns it
    // Hairs-Human:        PENDING
    public void SetText(string text) => _clipboard.SetText(text);
    // Hairs-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=80EB2A
    // Hairs-Falsified-If: an IMM input context obtained with ImmGetContext is left unreleased when positioning the composition window returns early or throws
    // Hairs-Human:        PENDING
    public void PublishCaret(UiTextCaretInfo caret) => _textInput.PublishCaret(caret);
    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=582842
    // Hairs-Human:        PENDING
    public void ClearCaret(UiElement owner) => _textInput.ClearCaret(owner);

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        if (encodedImage.IsEmpty || window.Renderer is not { } renderer)
            return BImageHandle.Invalid;
        try { return renderer.CreateImage(encodedImage); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return BImageHandle.Invalid; }
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        if (window.Renderer is not { } renderer)
            return BImageHandle.Invalid;
        try { return renderer.CreateImage(pixels); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return BImageHandle.Invalid; }
    }

    public void ReleaseImage(BImageHandle image)
    {
        if (image.IsValid)
            window.Renderer?.ReleaseImage(image);
    }

    public BSize ViewportSize { get; private set; } = new(1100, 720);
    public double Scale { get; private set; } = 1;

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=3; Fingerprint=516BFD
    // Hairs-Human:        PENDING
    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=4E9908
    // Hairs-Falsified-If: an invalidation raised by a control does not schedule a repaint of the window, so its change stays invisible until other input arrives
    // Hairs-Human:        PENDING
    public void Invalidate(UiInvalidation invalidation) => window.Invalidate();

    // Hairs-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=E9FEA9
    // Hairs-Human:        PENDING
    public void Present(BRenderList renderList)
    {
        // Direct2DWindow presents the list returned by BuildRenderList.
    }

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=076F46
    // Hairs-Human:        PENDING
    public void Update(BSize viewportSize, double scale)
    {
        ViewportSize = viewportSize;
        Scale = scale;
    }
}
