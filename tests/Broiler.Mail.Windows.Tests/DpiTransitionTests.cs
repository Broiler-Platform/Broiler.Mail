using System.Runtime.InteropServices;
using Broiler.Documents.Model;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Windows.Hosting;
using Broiler.UI;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// UI-04: a display scale change keeps the reading and writing context. The change is simulated on one
/// monitor (WindowsMailWindow.SimulateDpiChange): Mail sees a new scale and the suggested rectangle
/// through the same WM_DPICHANGED handlers as a monitor move, but Windows' own DPI is unchanged.
/// </summary>
public sealed class DpiTransitionTests
{
    // Above the 640x480 minimum and below the 680-DIP width at which the inbox shows two panes.
    private const int Width = 660, Height = 500;

    [Fact]
    public async Task Scale_Changes_Keep_Focus_Selection_Scroll_And_Draft_And_The_Layout_Follows_The_Dip_Width()
    {
        await using var window = await HiddenMailWindow.StartAsync(new DemoOptions(DemoScenario.Inbox, AppTheme.Light, Width, Height, ScalePercent: 150));
        await window.WaitUntilAsync(w => !w.Model.Inbox.IsBusy && w.Model.Inbox.Body?.Key.Uid == 55);
        var controls = await window.InvokeAsync(w =>
        {
            var found = Controls.Find(w);
            // A draft with a selection in its body.
            Assert.True(w.Model.Composer.StartNew());
            found.DraftBody.SetPlainText("Dear team,\n\nthis draft stays as it is while the display scale changes.");
            Assert.True(found.DraftBody.SetEditorSelection(6, 10));
            w.Shell.Navigation.SelectTab("inbox");
            w.Session.RenderFrame();
            Assert.True(found.Layout.IsCompact);
            Assert.False(found.Layout.ShowsReaderOnly);
            // A scrolled list with a message chosen in it.
            found.List.ScrollIntoView(20);
            w.Session.RenderFrame();
            Assert.True(found.List.VerticalOffset > 0);
            found.List.SelectedItemId = "1:40";
            return found;
        });
        await window.WaitUntilAsync(w => !w.Model.Inbox.IsBusy && w.Model.Inbox.Body?.Key.Uid == 40);
        Assert.True(await window.InvokeAsync(w => w.Shell.Inbox.OpenSelected()));
        await window.WaitUntilAsync(w => !w.Model.Inbox.IsBusy && w.Model.Inbox.Body is not null);
        var start = await window.InvokeAsync(w =>
        {
            // The compact reader, scrolled, with text selected and focus in it.
            w.Session.RenderFrame();
            Assert.True(controls.Layout.ShowsReaderOnly);
            w.Session.SetFocus(controls.Reader.Editor);
            Assert.True(controls.ReaderScroll.ScrollBy(0, 300));
            Assert.True(controls.Reader.Editor.SetEditorSelection(100, 160));
            w.Session.RenderFrame();
            return State.Capture(w, controls);
        });
        Assert.Equal(new BSize(Width, Height), start.Viewport);
        Assert.Equal(1.5, start.Scale);
        Assert.True(start.Compact && start.ReaderOnly && start.FocusInReader);
        Assert.Equal(("1:40", 40u), (start.ListSelection, start.Selected?.Uid));
        Assert.True(start.ListOffset > 0 && start.ReaderOffset > 0);
        Assert.False(start.ReaderSelection.IsEmpty || start.DraftSelection.IsEmpty);

        // A move to a 200 % monitor: Windows suggests a rectangle that keeps the DIP size.
        var doubled = await window.InvokeAsync(w => ChangeScale(w, controls, 2.0, Width * 2, Height * 2));
        Assert.Equal(start with { Scale = 2.0 }, doubled);

        // A 100 % scale at the same pixel size makes the window twice as wide in DIPs: both panes show,
        // and the reader keeps focus, the selections, and the draft.
        var wide = await window.InvokeAsync(w => ChangeScale(w, controls, 1.0, Width * 2, Height * 2));
        Assert.False(wide.Compact);
        Assert.Equal(new BSize(Width * 2, Height * 2), wide.Viewport);
        Assert.Equal(start with { Scale = 1.0, Compact = false, ReaderOnly = false, Viewport = wide.Viewport, FirstVisible = wide.FirstVisible, ListOffset = wide.ListOffset, ReaderOffset = wide.ReaderOffset }, wide);

        // Back to 200 % at the same pixels: compact again with the reader alone, exactly as before.
        Assert.Equal(doubled, await window.InvokeAsync(w => ChangeScale(w, controls, 2.0, Width * 2, Height * 2)));
        // And to 100 % keeping the DIP size.
        Assert.Equal(start with { Scale = 1.0 }, await window.InvokeAsync(w => ChangeScale(w, controls, 1.0, Width, Height)));
    }

    /// <summary>
    /// A simulated scale starts at the requested DIP size: at the minimum size, where the minimum track size
    /// must add Windows' real frame rather than one for the simulated DPI, and taller than the real desktop,
    /// to which Windows otherwise limits a window. Tests run on desktops of any size, CI's included.
    /// </summary>
    [Theory]
    [InlineData(200, false)]
    [InlineData(300, true)]
    public async Task A_Simulated_Scale_Starts_At_The_Requested_Dip_Size_On_Any_Desktop(int percent, bool tallerThanTheDesktop)
    {
        const int SmCyMaxTrack = 60;
        int height = tallerThanTheDesktop ? Math.Max(560, GetSystemMetrics(SmCyMaxTrack) * 100 / percent + 20) : 480;
        await using var window = await HiddenMailWindow.StartAsync(new DemoOptions(DemoScenario.Inbox, AppTheme.Light, 640, height, ScalePercent: percent));
        var (client, viewport) = await window.InvokeAsync(w =>
        {
            Assert.True(GetClientRect(w.NativeHandle, out RECT rect));
            return ((rect.Right - rect.Left, rect.Bottom - rect.Top), w.Host.ViewportSize);
        });
        Assert.Equal((640 * percent / 100, height * percent / 100), client);
        Assert.Equal(new BSize(640, height), viewport);
    }

    /// <summary>Simulates the change with a suggested client size in pixels, checks that the window took it, and captures the state.</summary>
    private static State ChangeScale(WindowsMailWindow window, Controls controls, double scale, int clientWidth, int clientHeight)
    {
        Assert.True(GetWindowRect(window.NativeHandle, out RECT outer));
        Assert.True(GetClientRect(window.NativeHandle, out RECT client));
        // Windows' frame is unchanged by a simulated scale, so the suggested outer size is the client plus that frame.
        var suggested = new PixelRect(outer.Left, outer.Top,
            outer.Left + clientWidth + (outer.Right - outer.Left) - (client.Right - client.Left),
            outer.Top + clientHeight + (outer.Bottom - outer.Top) - (client.Bottom - client.Top));
        window.SimulateDpiChange(scale, suggested);
        Assert.Equal(suggested, WindowsScreen.OuterBounds(window.NativeHandle));
        Assert.True(GetClientRect(window.NativeHandle, out client));
        Assert.Equal((clientWidth, clientHeight), (client.Right - client.Left, client.Bottom - client.Top));
        Assert.Equal(scale, window.DpiScale);
        window.Session.RenderFrame();
        // The same controls, not recreated ones.
        Assert.Equal(controls, Controls.Find(window));
        return State.Capture(window, controls);
    }

    private sealed record Controls(AdaptiveInboxLayout Layout, StandardListView List, ScrollableMessageText Reader, StandardScrollView ReaderScroll, StandardRichEdit DraftBody)
    {
        public static Controls Find(WindowsMailWindow window)
        {
            var inbox = window.Shell.Navigation.Tabs.Single(tab => tab.Id == "inbox").Content!;
            var compose = window.Shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!;
            var reader = Descendants(inbox).OfType<ScrollableMessageText>().Single();
            return new(Descendants(inbox).OfType<AdaptiveInboxLayout>().Single(), Descendants(inbox).OfType<StandardListView>().Single(),
                reader, Descendants(reader).OfType<StandardScrollView>().Single(), Descendants(compose).OfType<StandardRichEdit>().Single());
        }
    }

    private sealed record State(double Scale, BSize Viewport, bool Compact, bool ReaderOnly, bool FocusInReader, MailMessageKey? Selected, string? ListSelection,
        int FirstVisible, double ListOffset, double ReaderOffset, RichTextRange ReaderSelection, string Draft, RichTextRange DraftSelection)
    {
        public static State Capture(WindowsMailWindow window, Controls controls) => new(
            window.Host.Scale, window.Host.ViewportSize, controls.Layout.IsCompact, controls.Layout.ShowsReaderOnly,
            ReferenceEquals(window.Session.FocusedElement, controls.Reader.Editor), window.Model.Inbox.SelectedMessage?.Key, controls.List.SelectedItemId,
            controls.List.FirstVisibleIndex, controls.List.VerticalOffset, controls.ReaderScroll.VerticalOffset, controls.Reader.Editor.Selection,
            controls.DraftBody.GetPlainText(), controls.DraftBody.Selection);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>A real, hidden Mail window on its own STA thread; test code runs on that thread through its posted callbacks.</summary>
    private sealed class HiddenMailWindow : IAsyncDisposable
    {
        private readonly TaskCompletionSource<WindowsMailWindow> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private WindowsMailWindow? _window;
        private nint _frame;

        private HiddenMailWindow(DemoOptions options)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    var application = DemoApplication.Create(options);
                    application.InitializeAsync().GetAwaiter().GetResult();
                    using var window = new WindowsMailWindow(application, options);
                    window.Show();
                    ShowWindow(window.NativeHandle, 0); // Keep the native fixture hidden.
                    _frame = window.NativeHandle;
                    _ready.SetResult(window);
                    while (GetMessage(out MSG message, nint.Zero, 0, 0) > 0)
                    {
                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
                    }
                }
                catch (Exception error) { _ready.TrySetException(error); }
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public static async Task<HiddenMailWindow> StartAsync(DemoOptions options)
        {
            var host = new HiddenMailWindow(options);
            host._window = await host._ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return host;
        }

        public Task<T> InvokeAsync<T>(Func<WindowsMailWindow, T> action)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(_window!.RunOnUiThread(() =>
            {
                try { result.SetResult(action(_window)); }
                catch (Exception error) { result.SetException(error); }
            }));
            return result.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        public async Task WaitUntilAsync(Func<WindowsMailWindow, bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!await InvokeAsync(condition))
            {
                Assert.True(DateTime.UtcNow < deadline, "The window did not settle.");
                await Task.Delay(20);
            }
        }

        public async ValueTask DisposeAsync()
        {
            // The window saves the draft, closes, and ends its message loop.
            if (_frame != 0) PostMessage(_frame, 0x0010, 0, 0);
            await Task.Run(() => _thread.Join(TimeSpan.FromSeconds(10)));
        }
    }
}
