using System.Runtime.InteropServices;
using Broiler.Mail.Windows.Hosting;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// UI-09 regression: the automation and input bridges must subclass the real native windows. They were
/// constructed before the window existed, received zero handles, attached to nothing, and external UI
/// Automation clients saw an empty render pane.
/// </summary>
[Collection("UI theme")]
public sealed class NativeBridgeAttachmentTests
{
    private const uint WmGetObject = 0x003D;
    private const int UiaRootObjectId = -25;

    [Fact]
    public async Task BridgesAttachToTheNativeWindowsAndAnswerAutomationAndTextInput()
    {
        var ready = new TaskCompletionSource<(nint Frame, nint Render, nint BridgeWindow, bool Input)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var typed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = DemoApplication.Create();
                app.InitializeAsync().GetAwaiter().GetResult();
                using var window = new WindowsMailWindow(app);
                window.Show();
                ShowWindow(window.NativeHandle, 0); // Keep the native fixture hidden.
                // A new message focuses To; posted characters must arrive there exactly once.
                var compose = window.Shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!;
                Descendants(compose).OfType<StandardButton>().Single(button => button.Text == "New message").Click();
                var to = (StandardEdit)Descendants(compose).OfType<StandardLabel>().Single(label => label.Text == "To").Target!;
                ready.SetResult((window.NativeHandle, window.RenderNativeHandleForTests, window.AutomationBridge?.Hwnd ?? 0, window.InputBridge is not null));
                while (GetMessage(out MSG message, nint.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                    if (message.Message == 0x0102 /* WM_CHAR */ && to.Text.Length > 0) typed.TrySetResult(to.Text);
                }
                typed.TrySetResult(to.Text);
            }
            catch (Exception error) { ready.TrySetException(error); typed.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        nint frame = 0;
        try
        {
            var handles = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            frame = handles.Frame;
            Assert.NotEqual(0, handles.Render);
            Assert.Equal(handles.Render, handles.BridgeWindow);
            Assert.True(handles.Input);

            // An external client asks the render window for its provider; an unattached bridge returned 0.
            nint provider = SendMessage(handles.Render, WmGetObject, 0, UiaRootObjectId);
            Assert.NotEqual(0, provider);

            Assert.True(PostMessage(handles.Render, 0x0102, 'x', 1));
            Assert.Equal("x", await typed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            if (frame != 0) PostMessage(frame, 0x0010, 0, 0);
        }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
