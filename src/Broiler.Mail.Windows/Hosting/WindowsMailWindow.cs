using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Views;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Minimal native host. OS chrome handles moving, resizing, and closing.</summary>
internal sealed class WindowsMailWindow : Direct2DWindow
{
    private readonly WindowsUiHost _host;
    private readonly StandardQueuedUiDispatcher _dispatcher;
    private readonly UiSession _session;
    private readonly MailShellView _shell;
    private readonly MailKeyboardNavigation _keyboard;
    internal nint InputHandle => RenderNativeHandle;

    // Broiler.Graphics currently exposes legacy events at this host boundary.
    // TODO: Replace this adapter when the backend exposes the neutral input source directly.
#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _input = new("broiler-mail-windows");
#pragma warning restore CS0618

    public WindowsMailWindow(MailApplication application, bool demo = false)
        : base(new BWindowOptions
        {
            Title = demo ? "Broiler.Mail — Demo (no network or saved data)" : "Broiler.Mail",
            ClientWidth = application.LoadedSettings.WindowWidth,
            ClientHeight = application.LoadedSettings.WindowHeight,
        })
    {
        _host = new WindowsUiHost(this);
        // Results posted from any thread, this one included, wait until the window drains them:
        // on the message the wake-up posts, or before the next frame if no window existed yet.
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);
        _shell = application.CreateShell(_dispatcher);
        _session.AddRoot(_shell.Window);
        _keyboard = _shell.CreateKeyboardNavigation(_session);
        _session.SetFocus(_shell.Navigation);
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        return _session.RenderFrame();
    }

    private void DrainDispatcher()
    {
        // Log a failing callback as Direct2DWindow logs its own posted callbacks. The callbacks
        // after it stay queued, and the dispatcher has already asked for another drain.
        try { _dispatcher.Drain(); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        _host.Update(clientSize, dpiScale);
        Invalidate();
    }

    protected override void OnNativeWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (hwnd == NativeHandle) WindowsWindowSizing.OnMessage(hwnd, message, lParam, DpiScale);
    }

    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_input.FromPointerMove(e));
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_input.FromMouseWheel(e));
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Down));
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Up));
    protected override void OnTextInput(BTextInputEventArgs e) => Dispatch(_input.FromText(e));

    private void Dispatch(UiInputEvent input)
    {
        // Let the session record keyboard input for visible focus rings before applying global navigation.
        bool handled = _session.DispatchInput(input);
        if (_keyboard.Handle(input) || handled)
            Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shell.Dispose();
            _session.Dispose();
        }
        base.Dispose(disposing);
    }
}
