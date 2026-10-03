// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   18
// Annotated:        18/18
// Exempt:           8
// Human-reviewed:   0/18
// IP risk:          Low
// Security risk:    Critical
// Criteria:         18/3
// Resource impact:  7/10 max
// Unverified:       18
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Application.Views;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.Hosting.Windows.Input;
using Broiler.Mail.Windows.Measurement;
using Broiler.Mail.Windows.Preview;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Minimal native host. OS chrome handles moving, resizing, and closing.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=7; Fingerprint=0A6203
// Broiler-Falsified-If: a WM_GETMINMAXINFO or WM_DPICHANGED for the render child window reaches WindowsWindowSizing.OnMessage, whose Marshal reads and writes through lParam then act on the wrong window
// Broiler-Human:        PENDING
internal sealed class WindowsMailWindow : Direct2DWindow
{
    private readonly WindowsUiHost _host;
    private readonly StandardQueuedUiDispatcher _dispatcher;
    private readonly UiSession _session;
    private readonly MailShellView _shell;
    private readonly MailKeyboardNavigation _keyboard;
    private readonly AppearanceController _appearance;
    private readonly MailShellViewModel _model;
    private readonly WindowRestorePlan _restore;
    private WindowPlacement? _normal;
    private bool _maximized;
    private bool _closePending;
    private readonly WindowsHtmlPreviewHost _htmlPreview;
    private readonly FrameRecorder? _recorder;
    private int _exitCode;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=8A70C4
    // Broiler-Falsified-If: IME positioning calls receive the top-level frame handle instead of the render child window that holds keyboard focus, so the composition window is placed against the wrong client origin
    // Broiler-Human:        PENDING
    // Both bridges subclass native windows, which exist only from WM_CREATE on; see OnCreated.
    private WindowsAutomationBridge? _automationBridge;
    private WindowsInputBridge? _inputBridge;
    internal WindowsAutomationBridge? AutomationBridge => _automationBridge;
    internal WindowsInputBridge? InputBridge => _inputBridge;
    internal MailShellView Shell => _shell;
    internal MailShellViewModel Model => _model;
    internal UiSession Session => _session;
    internal nint RenderNativeHandleForTests => RenderNativeHandle;
    internal nint InputHandle => RenderNativeHandle;

    // Broiler.Graphics currently exposes legacy events at this host boundary.
    // TODO: Replace this adapter when the backend exposes the neutral input source directly.
#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _input = new("broiler-mail-windows");
#pragma warning restore CS0618

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=Medium; Resources=3; Fingerprint=6D2AE8
    // Broiler-Falsified-If: a result posted by a background mail operation runs its callback on the posting thread instead of waiting for DrainDispatcher on the window thread
    // Broiler-Human:        PENDING
    public WindowsMailWindow(MailApplication application, DemoOptions? demo = null)
        : this(application, demo, WindowRestorePlan.For(application.LoadedSettings, WindowsScreen.WorkAreas(), WindowsScreen.SystemScale()))
    {
    }

    private WindowsMailWindow(MailApplication application, DemoOptions? demo, WindowRestorePlan restore)
        : base(new BWindowOptions
        {
            Title = demo is null ? "Broiler.Mail" : demo.Interactive
                ? "Broiler.Mail — Demo (no network or saved data)" : $"Broiler.Mail — Demo: {demo.Name} (no network or saved data)",
            // Remembered geometry, already clamped to the current monitors; otherwise the initial size, centered.
            ClientWidth = restore.ClientWidth,
            ClientHeight = restore.ClientHeight,
            Left = restore.Left,
            Top = restore.Top,
            // Application-managed close must be able to keep the window open after a failed draft save.
            OwnsMessageLoop = false,
            RenderOptions = new BRenderOptions(
                Antialias: true,
                VSync: true,
                SubpixelText: true),
        })
    {
        _host = new WindowsUiHost(this, () => InputHandle);
        // Results posted from any thread, this one included, wait until the window drains them:
        // on the message the wake-up posts, or before the next frame if no window existed yet.
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);
        var model = application.CreateViewModel(_dispatcher);
        _model = model;
        _restore = restore;
        _normal = restore.Normal;
        _maximized = restore.Maximized;
        // A preview opens with the shell's current theme, so its caption matches.
        _htmlPreview = new WindowsHtmlPreviewHost(() => _appearance?.Current?.IsDark == true);
        _shell = new MailShellView(model, _htmlPreview, demo is null ? null : DemoApplication.CreateDateFormatter());
        _session.AddRoot(_shell.Window);
        _keyboard = _shell.CreateKeyboardNavigation(_session);
        _session.SetFocus(_shell.Navigation);
        // Saved theme and OS appearance changes re-theme the live controls; no restart is needed.
        _appearance = new AppearanceController(_session, model.Settings, _host);
        _appearance.Applied += (_, _) =>
        {
            WindowsTitleBar.Apply(NativeHandle, _appearance.Current!.IsDark);
            // An open HTML preview follows too; it runs its own session on its own thread.
            _htmlPreview.ApplyTheme(_appearance.Current);
            Invalidate();
        };
        if (demo is { Interactive: false })
        {
            var driver = DemoScenarioDriver.Start(demo, model, _shell, _dispatcher);
            if (demo.Measure is not null)
            {
                _recorder = new FrameRecorder();
                MeasurementRun.Start(this, _recorder, demo, driver.Completion);
            }
        }
        StateChanged += (_, _) => _ = RememberLayout();
        CloseRequested += (_, _) => RequestClose();
        Closed += (_, _) => PostQuitMessage(_exitCode);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=7386EC
    // Broiler-Falsified-If: a GetMessage return of -1 is passed to TranslateMessage and DispatchMessage instead of ending the loop with a Win32Exception
    // Broiler-Human:        PENDING
    protected override int RunCore()
    {
        // Broiler's secondary-window mode delegates close requests. Own its outer loop here so
        // a close can wait for autosave without blocking dispatch or silently dropping edits.
        Show();
        if (_restore.MoveAfterShow is { } position) WindowsScreen.MoveTo(NativeHandle, position.Left, position.Top);
        if (_restore.Maximized) SetWindowState(BWindowState.Maximized);
        while (true)
        {
            int result = GetMessage(out MSG message, nint.Zero, 0, 0);
            if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (result == 0) return unchecked((int)message.WParam);
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    /// <summary>
    /// Records the normal bounds (kept unchanged while maximized or minimized), the maximized state,
    /// and the inbox split, and saves them quietly through the settings view model.
    /// </summary>
    private Task RememberLayout()
    {
        if (WindowState != BWindowState.Minimized) _maximized = WindowState == BWindowState.Maximized;
        if (WindowState == BWindowState.Normal && WindowsScreen.OuterBounds(NativeHandle) is { } outer)
        {
            _normal = new WindowPlacement
            {
                Left = outer.Left, Top = outer.Top, Width = outer.Width, Height = outer.Height,
                // The minimum-size handling can leave a fraction of a DIP below the limit at some scales.
                ClientWidth = Math.Clamp((int)Math.Round(ClientSize.Width), 640, 7680),
                ClientHeight = Math.Clamp((int)Math.Round(ClientSize.Height), 480, 4320),
            };
        }
        var placement = _normal is null ? null : _normal with { Maximized = _maximized };
        return _model.Settings.RememberLayoutAsync(placement, _model.Inbox.SplitterFraction);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=F830F3
    // Broiler-Falsified-If: the window closes although PrepareCloseAsync returned false or threw, discarding an unsaved draft
    // Broiler-Human:        PENDING
    private async void RequestClose()
    {
        if (_closePending) return;
        _closePending = true;
        // Capture geometry while the window still exists; the write finishes before the window closes.
        var layout = RememberLayout();
        bool saved;
        try { saved = await _shell.PrepareCloseAsync().ConfigureAwait(false); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); saved = false; }
        try { await layout.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        PostToUiThread(() =>
        {
            _closePending = false;
            if (saved) Close();
            else { _shell.Navigation.SelectTab("compose"); Invalidate(); }
        });
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=A6A9C3
    // Broiler-Falsified-If: a mail result queued before a paint is not applied until a later frame, so the painted frame shows the state from before that result
    // Broiler-Human:        PENDING
    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        var frame = _recorder?.BeginFrame();
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        var renderList = _session.RenderFrame();
        if (frame is { } begin) _recorder!.EndFrame(begin);
        return renderList;
    }

    // UI-12 measurement hooks; only a --measure demo run uses them.
    internal bool RunOnUiThread(Action action) => PostToUiThread(action);

    internal void DispatchMeasured(UiInputEvent input)
    {
        _recorder?.MarkInput();
        Dispatch(input);
    }

    internal void DispatchUnmeasured(UiInputEvent input) => Dispatch(input);

    internal void ApplyThemeForMeasurement(StandardThemeTokens tokens)
    {
        _recorder?.MarkInput();
        StandardThemeController.Apply(_session, tokens);
        WindowsTitleBar.Apply(NativeHandle, tokens.IsDark);
        Invalidate();
    }

    internal (int Width, int Height) OuterSize() => WindowsScreen.OuterSize(NativeHandle);

    internal void ResizeForMeasurement(int width, int height)
    {
        // The resize draws its frame before SetWindowPos returns, so the mark comes first.
        _recorder?.MarkInput();
        WindowsScreen.Resize(NativeHandle, width, height);
    }

    internal void CloseAfterMeasurement(int exitCode)
    {
        _exitCode = exitCode;
        Close();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=C533CD
    // Broiler-Falsified-If: an exception thrown by one queued UI callback escapes Drain and ends the message loop or aborts the frame being built
    // Broiler-Human:        PENDING
    private void DrainDispatcher()
    {
        // Log a failing callback as Direct2DWindow logs its own posted callbacks. The callbacks
        // after it stay queued, and the dispatcher has already asked for another drain.
        try { _dispatcher.Drain(); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=3E34D4
    // Broiler-Falsified-If: after a resize or DPI change the UI session lays out against the old viewport size or scale until another resize arrives
    // Broiler-Human:        PENDING
    /// <summary>
    /// Runs during WM_CREATE, once both the frame and the render child exist and before the window is
    /// shown. Constructing the bridges earlier passed zero handles, so neither subclassed anything: UI
    /// Automation clients saw an empty render pane, and native IME and wheel handling never ran.
    /// </summary>
    protected override void OnCreated()
    {
        base.OnCreated();
        _automationBridge ??= new WindowsAutomationBridge(RenderNativeHandle, _session, _shell.Window, () => DpiScale);
        _inputBridge ??= new WindowsInputBridge(NativeHandle, RenderNativeHandle, _session, _keyboard.Handle, () => DpiScale, Invalidate);
        // Before the first paint, so a dark caption never flashes light.
        WindowsTitleBar.Apply(NativeHandle, _appearance.Current!.IsDark);
    }

    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        _host.Update(clientSize, dpiScale);
        Invalidate();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=1; Fingerprint=09320F
    // Broiler-Falsified-If: a WM_GETMINMAXINFO or WM_DPICHANGED addressed to a window other than NativeHandle is handed to WindowsWindowSizing.OnMessage, which then writes through that message's lParam
    // Broiler-Human:        PENDING
    protected override void OnNativeWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (hwnd == NativeHandle)
        {
            WindowsWindowSizing.OnMessage(hwnd, message, lParam, DpiScale);
            _inputBridge?.OnTopLevelMessage(message, wParam, lParam);
            // WM_EXITSIZEMOVE: one write when a move or resize ends, never one per pixel.
            if (message == 0x0232) _ = RememberLayout();
            if (message is 0x001A or 0x031A)
            {
                _host.RefreshSettings();
            }
        }
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=626D97
    // Broiler-Falsified-If: pressing a mouse button over a control delivers no pointer-down input to the UI session, or delivers it as a pointer move
    // Broiler-Human:        PENDING
    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=EF3419
    // Broiler-Falsified-If: releasing a mouse button delivers no pointer-up input to the UI session, so a pressed button never completes its click
    // Broiler-Human:        PENDING
    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=780407
    // Broiler-Falsified-If: moving the pointer delivers a button event to the UI session instead of a move, starting a click or drag the user did not make
    // Broiler-Human:        PENDING
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_input.FromPointerMove(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=13EC08
    // Broiler-Falsified-If: a wheel notch over the message list delivers no wheel input to the UI session, so the list does not scroll
    // Broiler-Human:        PENDING
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_input.FromMouseWheel(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=C5177F
    // Broiler-Falsified-If: a key press reaches MailKeyboardNavigation or the UI session as a key-up transition
    // Broiler-Human:        PENDING
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Down));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=C41524
    // Broiler-Falsified-If: a key release reaches MailKeyboardNavigation or the UI session as a key-down transition, repeating the action of that key
    // Broiler-Human:        PENDING
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Up));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=7E8B06
    // Broiler-Falsified-If: a character committed through WM_CHAR or the IME is inserted into the focused editor a number of times other than once
    // Broiler-Human:        PENDING
    protected override void OnTextInput(BTextInputEventArgs e) => _inputBridge?.ProcessTextInput(e.Character);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=7ABD26
    // Broiler-Falsified-If: an input that MailKeyboardNavigation handles, such as Tab, is also dispatched to the UI session and inserts text into the focused editor
    // Broiler-Human:        PENDING
    private void Dispatch(UiInputEvent input)
    {
        // Application navigation owns Tab; dispatching it to RichEdit first would also insert text.
        if (_keyboard.Handle(input) || _session.DispatchInput(input))
            Invalidate();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=008B02
    // Broiler-Falsified-If: an HTML preview opened from this mail window stays open after the mail window is disposed
    // Broiler-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _automationBridge?.Dispose();
            _inputBridge?.Dispose();
            _htmlPreview.Dispose();
            _shell.Dispose();
            _appearance.Dispose();
            _session.Dispose();
        }
        base.Dispose(disposing);
    }
}
