// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   42
// Annotated:        0/42
// Exempt:           16
// Human-reviewed:   0/42
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       42
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.Hosting.Windows.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Windows.Services;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>
/// Native top-level host window for broken-out dialogs and secondary windows.
/// Serviced by the main message loop with owner-drawn chrome.
/// </summary>
[SupportedOSPlatform("windows7.0")]
internal sealed class WindowsHostWindow : Direct2DWindow, IUiHostWindow, IUiWindowChromeHost, IUiClipboardHost, IUiTextInputHost, IUiSystemSettingsHost
{
    private readonly Direct2DWindow _owner;
    private readonly WindowsClipboard _clipboard;
    private readonly WindowsTextInput _textInput;
    private UiSystemSettings _settings = MailSystemSettings.Query();
    private UiSession? _session;
    private WindowsAutomationBridge? _automationBridge;
    private WindowsInputBridge? _inputBridge;

#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _legacyInput = new("broiler-mail-breakout-window");
#pragma warning restore CS0618

    public WindowsHostWindow(Direct2DWindow owner, UiHostWindowRequest request)
        : base(new BWindowOptions
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Broiler.Mail" : request.Title,
            ClientWidth = ToClientExtent(request.Placement.Width, 940),
            ClientHeight = ToClientExtent(request.Placement.Height, 680),
            Left = request.Placement.IsEmpty ? null : request.Placement.X,
            Top = request.Placement.IsEmpty ? null : request.Placement.Y,
            ClearColor = StandardControlPaint.Theme.SurfaceAlt,
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
            OwnsMessageLoop = false,
            Chrome = request.Chrome == UiHostWindowChrome.Owner ? BWindowChrome.Owner : BWindowChrome.System,
            Resizable = request.Resizable,
        })
    {
        _owner = owner;
        _clipboard = new WindowsClipboard(() => NativeHandle);
        _textInput = new WindowsTextInput(() => RenderNativeHandle, () => DpiScale);
        StateChanged += (_, _) => WindowStateChanged?.Invoke(this, EventArgs.Empty);
        CloseRequested += (_, _) => PostToUiThread(Close);
    }

    internal WindowsAutomationBridge? AutomationBridge => _automationBridge;
    internal WindowsInputBridge? InputBridge => _inputBridge;
    internal UiSession? Session => _session;
    internal nint RenderHandle => RenderNativeHandle;

    protected override void OnCreated()
    {
        base.OnCreated();
        WindowsTitleBar.ApplyDarkMode(NativeHandle, StandardControlPaint.Theme.IsDark);
        EnsureBridges();
    }

    private void EnsureBridges()
    {
        if (_session is null || RenderNativeHandle == 0) return;
        _automationBridge ??= new WindowsAutomationBridge(RenderNativeHandle, _session, _session.Roots.Count > 0 ? _session.Roots[0] : null!, () => DpiScale);
        _inputBridge ??= new WindowsInputBridge(NativeHandle, RenderNativeHandle, _session, null, () => DpiScale, InvalidateIfAlive);
        _textInput.FollowFocus(_session.FocusedElement);
    }

    public void ApplyTheme(StandardThemeTokens theme)
    {
        if (NativeHandle != 0) WindowsTitleBar.ApplyDarkMode(NativeHandle, theme.IsDark);
        if (_session is { IsDisposed: false } session)
        {
            StandardControlPaint.SetSessionTheme(session, theme);
            if (session.Roots.Count > 0)
                StandardThemeController.ApplyToSubtree(session.Roots[0], theme);
        }
        InvalidateIfAlive();
    }

    // IUiHost
    BSize IUiHost.ViewportSize => ClientSize;

    double IUiHost.Scale => DpiScale;

    BRenderList IUiHost.CreateRenderList(int capacity) => new(capacity);

    void IUiHost.Invalidate(UiInvalidation invalidation) => InvalidateIfAlive();

    void IUiHost.Present(BRenderList renderList) { }

    // IUiHostWindow
    public void Bind(UiSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.SemanticChanged += (_, e) =>
        {
            if (e.Change == UiSemanticChangeKind.FocusChanged)
                _textInput.FollowFocus(_session.FocusedElement);
        };
        EnsureBridges();
        InvalidateIfAlive();
    }

    public void Activate()
    {
        if (IsDisposed || NativeHandle == 0) return;
        SetForegroundWindow(NativeHandle);
        InvalidateIfAlive();
    }

    // IUiWindowChromeHost
    public event EventHandler? WindowStateChanged;

    UiHostWindowChrome IUiWindowChromeHost.Chrome =>
        Options.Chrome == BWindowChrome.Owner ? UiHostWindowChrome.Owner : UiHostWindowChrome.System;

    bool IUiWindowChromeHost.IsResizable => Options.Resizable;

    UiHostWindowState IUiWindowChromeHost.WindowState => ToHostState(WindowState);

    void IUiWindowChromeHost.SetWindowState(UiHostWindowState state) => SetWindowState(state switch
    {
        UiHostWindowState.Minimized => BWindowState.Minimized,
        UiHostWindowState.Maximized => BWindowState.Maximized,
        _ => BWindowState.Normal,
    });

    void IUiWindowChromeHost.SetIcon(BPixelBuffer? icon) => SetIcon(icon);

    void IUiWindowChromeHost.RequestClose() => PostToUiThread(Close);

    void IUiWindowChromeHost.BeginMoveDrag() => BeginMoveDrag();

    void IUiWindowChromeHost.BeginResizeDrag(UiWindowEdge edge) => BeginResizeDrag(ToWindowEdge(edge));

    // IUiClipboardHost
    public bool TryGetText(out string text) => _clipboard.TryGetText(out text);

    public void SetText(string text) => _clipboard.SetText(text);

    // IUiTextInputHost
    public void PublishCaret(UiTextCaretInfo caret) => _textInput.PublishCaret(caret);

    public void ClearCaret(UiElement owner) => _textInput.ClearCaret(owner);

    // IUiSystemSettingsHost
    public UiSystemSettings Settings => _settings;

    public event EventHandler<UiSystemSettingsChangedEventArgs>? SettingsChanged;

    public void RefreshSettings()
    {
        var newSettings = MailSystemSettings.Query();
        if (_settings != newSettings)
        {
            _settings = newSettings;
            SettingsChanged?.Invoke(this, new UiSystemSettingsChangedEventArgs(_settings));
            InvalidateIfAlive();
        }
    }

    protected override BRenderList? BuildRenderList(BSize clientSize) =>
        _session is { IsDisposed: false } session ? session.RenderFrame() : null;

    protected override void OnResized(BSize clientSize, double dpiScale) => InvalidateIfAlive();

    protected override void OnNativeWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (hwnd == NativeHandle)
        {
            WindowsWindowSizing.OnMessage(hwnd, message, lParam, DpiScale);
            _inputBridge?.OnTopLevelMessage(message, wParam, lParam);
            if (message is 0x001A or 0x031A or 0x0015)
            {
                if (_owner is WindowsMailWindow mailWindow)
                    mailWindow.OnSystemThemeOrColorChanged();
                else
                    RefreshSettings();
            }
        }
    }

    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerButton(e));

    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerButton(e));

    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerMove(e));

    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_legacyInput.FromMouseWheel(e));

    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Down));

    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Up));

    protected override void OnTextInput(BTextInputEventArgs e)
    {
        if (_inputBridge is not null) _inputBridge.ProcessTextInput(e.Character);
        else Dispatch(_legacyInput.FromText(e));
    }

    private void Dispatch(UiInputEvent input)
    {
        if (_session is null || _session.IsDisposed) return;
        if (_session.DispatchInput(input))
            InvalidateIfAlive();
    }

    private void InvalidateIfAlive()
    {
        if (!IsDisposed && NativeHandle != 0)
            Invalidate();
    }

    protected override void CloseCore()
    {
        try { _session?.SetFocus(null); } catch (Exception) { }
        try
        {
            base.CloseCore();
        }
        catch (COMException)
        {
        }
        catch (Exception)
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _automationBridge?.Dispose(); } catch (Exception) { }
            _automationBridge = null;
            try { _inputBridge?.Dispose(); } catch (Exception) { }
            _inputBridge = null;
            try
            {
                if (!IsDisposed) Close();
            }
            catch (Exception)
            {
            }
        }
        base.Dispose(disposing);
    }

    internal static UiHostWindowState ToHostState(BWindowState state) => state switch
    {
        BWindowState.Minimized => UiHostWindowState.Minimized,
        BWindowState.Maximized => UiHostWindowState.Maximized,
        _ => UiHostWindowState.Normal,
    };

    internal static BWindowEdge ToWindowEdge(UiWindowEdge edge) => edge switch
    {
        UiWindowEdge.Left => BWindowEdge.Left,
        UiWindowEdge.Top => BWindowEdge.Top,
        UiWindowEdge.Right => BWindowEdge.Right,
        UiWindowEdge.Bottom => BWindowEdge.Bottom,
        UiWindowEdge.TopLeft => BWindowEdge.TopLeft,
        UiWindowEdge.TopRight => BWindowEdge.TopRight,
        UiWindowEdge.BottomLeft => BWindowEdge.BottomLeft,
        UiWindowEdge.BottomRight => BWindowEdge.BottomRight,
        _ => BWindowEdge.None,
    };

    private static int ToClientExtent(double requested, int fallback) =>
        requested > 1 ? (int)Math.Round(requested) : fallback;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);
}
