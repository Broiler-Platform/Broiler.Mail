using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Broiler.Mail.Application;
using Broiler.Mail.Windows.Hosting;
using Broiler.UI;
using Broiler.UI.Standard;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// A real <see cref="WindowsMailWindow"/> on its own STA thread, shown once so its native windows exist and then
/// hidden. Tests post native messages to the render window, as Windows would deliver them, and read or change
/// the UI through <see cref="Ui{T}"/>, which runs on the window thread.
/// </summary>
internal sealed class HiddenMailWindow : IDisposable
{
    private const uint WmClose = 0x0010;
    // WM_APP + 0x2A: the render window ignores it; the message loop below reports it back.
    private const uint Marker = 0x802A;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly TaskCompletionSource<WindowsMailWindow> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _markers = new();
    // The window sets the process-wide palette, as do measurement themes applied to it; closing puts this back.
    private readonly StandardThemeTokens _palette = StandardControlPaint.Theme;
    private long _marker;

    private HiddenMailWindow(Func<MailApplication> create, DemoOptions? demo)
    {
        var thread = new Thread(() =>
        {
            try
            {
                var app = create();
                app.InitializeAsync().GetAwaiter().GetResult();
                using var window = new WindowsMailWindow(app, demo);
                // Direct2DWindow creates its native windows only by showing, and so activating, them. Until the
                // line below hides it, the window may hold the focus, and keys typed then would reach it; Graphics
                // has no option to create a window without activating it.
                window.Show();
                ShowWindow(window.NativeHandle, 0); // Keep the native fixture hidden.
                _ready.SetResult(window);
                while (GetMessage(out MSG message, nint.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                    // Posted messages arrive in order, so everything posted before the marker has been handled.
                    if (message.Message == Marker && _markers.TryRemove((long)message.WParam, out var marker)) marker.TrySetResult();
                }
                _closed.TrySetResult();
            }
            catch (Exception error) { _ready.TrySetException(error); _closed.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Window = Wait(_ready.Task);
    }

    public static HiddenMailWindow Start(Func<MailApplication>? create = null, DemoOptions? demo = null) =>
        new(create ?? (() => DemoApplication.Create()), demo);

    public WindowsMailWindow Window { get; }
    public nint Frame => Window.NativeHandleForTests;
    /// <summary>The render child, which holds keyboard focus and receives input in a real session.</summary>
    public nint Render => Window.RenderNativeHandleForTests;

    /// <summary>Posts a message to the render window without waiting for it.</summary>
    public void Post(uint message, nint wParam, nint lParam) => Assert.True(PostMessage(Render, message, wParam, lParam));

    /// <summary>Waits until every message posted so far has been dispatched.</summary>
    public void Settle()
    {
        long id = Interlocked.Increment(ref _marker);
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _markers[id] = marker;
        Post(Marker, (nint)id, 0);
        Wait(marker.Task);
    }

    /// <summary>Posts and settles each character of <paramref name="text"/> as WM_CHAR, one UTF-16 unit at a time.</summary>
    public void Type(string text)
    {
        foreach (char unit in text) Post(0x0102, unit, 1);
        Settle();
    }

    public T Ui<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(Window.RunOnUiThread(() =>
        {
            try { result.SetResult(action()); }
            catch (Exception error) { result.SetException(error); }
        }));
        return Wait(result.Task);
    }

    public void Ui(Action action) => Ui(() => { action(); return true; });

    /// <summary>Lays the window out at its current size, as the next paint would.</summary>
    public void Layout() => Ui(() => Window.Session.RenderFrame());

    /// <summary>Polls on the window thread until <paramref name="condition"/> holds.</summary>
    public void WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!Ui(condition))
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"Timed out waiting until {because}.");
            Thread.Sleep(10);
        }
    }

    /// <summary>A point in device-independent client coordinates, as the screen-coordinate lParam of a wheel message.</summary>
    public nint ScreenLParam(double x, double y)
    {
        double scale = Ui(() => Window.DpiScale);
        var point = new Point { X = (int)Math.Round(x * scale), Y = (int)Math.Round(y * scale) };
        Assert.True(ClientToScreen(Render, ref point));
        return (nint)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
    }

    public static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    public void Dispose()
    {
        if (_ready.Task.IsCompletedSuccessfully && !_closed.Task.IsCompleted)
        {
            // Closing waits for the draft to be saved, as a real close does. A failure here must not hide the
            // test's own failure, so the outcome is only waited for.
            PostMessage(Frame, WmClose, 0, 0);
            _closed.Task.ContinueWith(_ => { }, TaskScheduler.Default).Wait(Timeout);
        }
        StandardControlPaint.ApplyTheme(_palette);
    }

    // Rethrows what failed on the window thread itself rather than an AggregateException around it.
    private static T Wait<T>(Task<T> task) => task.WaitAsync(Timeout).GetAwaiter().GetResult();

    private static void Wait(Task task) => task.WaitAsync(Timeout).GetAwaiter().GetResult();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point point);
}
