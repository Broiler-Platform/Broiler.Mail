// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   11
// Annotated:        11/11
// Exempt:           4
// Human-reviewed:   0/11
// IP risk:          Low
// Security risk:    Critical
// Criteria:         5/4
// Resource impact:  3/10 max
// Unverified:       11
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Mail.Windows.Services;
using Broiler.UI;

namespace Broiler.Mail.Windows.Hosting;

// Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=3; Fingerprint=087FCE
// Broiler-Falsified-If: a paste reads clipboard memory past the size GlobalSize reports for a block another process placed there
// Broiler-Human:        PENDING
internal sealed class WindowsUiHost(Direct2DWindow window, Func<nint> inputHandle) : IUiHost, IUiClipboardHost, IUiTextInputHost, IUiSystemSettingsHost
{
    private readonly WindowsClipboard _clipboard = new(() => window.NativeHandle);
    private readonly WindowsTextInput _textInput = new(inputHandle, () => window.DpiScale);
    private UiSystemSettings _settings = MailSystemSettings.Query();
    private UiSession? _session;

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

    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=3; Fingerprint=E01786
    // Broiler-Falsified-If: PtrToStringUni reads more characters than half the GlobalSize of the locked clipboard block, reading past memory another process allocated
    // Broiler-Human:        PENDING
    public bool TryGetText(out string text) => _clipboard.TryGetText(out text);
    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=3; Fingerprint=A65525
    // Broiler-Falsified-If: the global block passed to SetClipboardData is freed afterwards by the host although the clipboard now owns it
    // Broiler-Human:        PENDING
    public void SetText(string text) => _clipboard.SetText(text);
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=80EB2A
    // Broiler-Falsified-If: an IMM input context obtained with ImmGetContext is left unreleased when positioning the composition window returns early or throws
    // Broiler-Human:        PENDING
    public void PublishCaret(UiTextCaretInfo caret) => _textInput.PublishCaret(caret);
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=582842
    // Broiler-Human:        PENDING
    public void ClearCaret(UiElement owner) => _textInput.ClearCaret(owner);
    public BSize ViewportSize { get; private set; } = new(1100, 720);
    public double Scale { get; private set; } = 1;

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=3; Fingerprint=516BFD
    // Broiler-Human:        PENDING
    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=4E9908
    // Broiler-Falsified-If: an invalidation raised by a control does not schedule a repaint of the window, so its change stays invisible until other input arrives
    // Broiler-Human:        PENDING
    public void Invalidate(UiInvalidation invalidation) => window.Invalidate();

    // Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=E9FEA9
    // Broiler-Human:        PENDING
    public void Present(BRenderList renderList)
    {
        // Direct2DWindow presents the list returned by BuildRenderList.
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=076F46
    // Broiler-Human:        PENDING
    public void Update(BSize viewportSize, double scale)
    {
        ViewportSize = viewportSize;
        Scale = scale;
    }
}
