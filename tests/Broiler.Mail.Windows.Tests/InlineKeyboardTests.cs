using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
public sealed class InlineKeyboardTests
{
    [Fact]
    public async Task ShellFindUsesActualHtmlTextAlternativeAndRestoresDefaultForNextMessage()
    {
        using var application = DemoApplication.Create(new(DemoScenario.LongHtml, Interactive: false)); await application.InitializeAsync(); var model = application.CreateViewModel();
        using var html = new HtmlHost(); using var shell = new MailShellView(model, html); using var session = new StandardUiSessionBuilder().Build(new Host()); shell.Attach(session);
        var keyboard = shell.CreateKeyboardNavigation(session);
        await model.Inbox.ReceiveAsync(); await model.Inbox.SelectAsync(model.Inbox.Messages.Single(message => message.Key.Uid == 54).Key); session.RenderFrame();
        var surface = Assert.IsType<ScrollableHtmlView>(html.Surface); session.SetFocus(surface.Content);
        bool Dispatch(UiInputEvent input) => keyboard.Handle(input) || session.DispatchInput(input);
        Assert.True(Dispatch(Key(0x46, KeyboardModifierState.Control))); session.RenderFrame(); Assert.Equal(UiVisibility.Collapsed, surface.Visibility);
        var content = shell.GetContent("inbox"); var find = Descendants(content).OfType<MessageTextFindBar>().Single();
        string needle = new(model.Inbox.Body!.PlainText.SkipWhile(c => !char.IsLetter(c)).TakeWhile(char.IsLetter).Take(20).ToArray()); Assert.NotEmpty(needle);
        var query = Descendants(find).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(); query.Text = needle;
        Assert.True(Dispatch(Key(0x0D))); session.RenderFrame(); Assert.Contains("Match", find.Status);
        var text = Descendants(content).OfType<ScrollableMessageText>().Single(); Assert.Equal(needle, text.Editor.Document.Slice(text.Editor.Selection).PlainText);
        Assert.True(Dispatch(Key(0x72))); Assert.True(Dispatch(Key(0x1B))); Assert.Equal(UiVisibility.Collapsed, find.Visibility); Assert.Same(text.Editor, session.FocusedElement);
        Assert.Empty(html.Opened);
    }

    [Fact]
    public void InlineShortcutsHandleMainAndKeypadKeysAndFractionalWheelButLeaveAltGrAlone()
    {
        using var view = new ScrollableHtmlView("<p>Readable</p>", () => null, _ => { });
        Assert.True(view.HandleReaderInput(Key(0xBB, KeyboardModifierState.Control))); Assert.Equal(1.1, view.Zoom);
        Assert.True(view.HandleReaderInput(Key(0x6B, KeyboardModifierState.Control))); Assert.Equal(1.25, view.Zoom);
        Assert.True(view.HandleReaderInput(Key(0x6D, KeyboardModifierState.Control))); Assert.Equal(1.1, view.Zoom);
        Assert.True(view.HandleReaderInput(Key(0x60, KeyboardModifierState.Control))); Assert.Equal(1, view.Zoom);
        Assert.False(view.HandleReaderInput(Key(0xBB))); Assert.False(view.HandleReaderInput(Key(0xBB, KeyboardModifierState.Control | KeyboardModifierState.RightAlt)));
        for (int i = 0; i < 3; i++) Assert.True(view.HandleReaderInput(Wheel(0, 0, 0.25, InputModifiers.Control)));
        Assert.Equal(1, view.Zoom);
        Assert.True(view.HandleReaderInput(Wheel(0, 0, 0.25, InputModifiers.Control))); Assert.Equal(1.1, view.Zoom);
        Assert.False(view.HandleReaderInput(Wheel(0, 0, 1, InputModifiers.Control | InputModifiers.Alt)));
        view.Visibility = UiVisibility.Collapsed; Assert.False(view.HandleReaderInput(Key(0xBB, KeyboardModifierState.Control)));
    }

    [Fact]
    public async Task ShellRoutesZoomOnlyToHtmlAndTabRevealsLinksThatEnterOpens()
    {
        using var application = DemoApplication.Create(new(DemoScenario.LongHtml, Interactive: false));
        await application.InitializeAsync();
        var model = application.CreateViewModel();
        using var htmlHost = new HtmlHost(); using var shell = new MailShellView(model, htmlHost);
        var uiHost = new Host();
        using var session = new StandardUiSessionBuilder().Build(uiHost); shell.Attach(session);
        var keyboard = shell.CreateKeyboardNavigation(session);
        await model.Inbox.ReceiveAsync(); await model.Inbox.SelectAsync(model.Inbox.Messages.Single(message => message.Key.Uid == 54).Key);
        session.RenderFrame();
        var view = Assert.IsType<ScrollableHtmlView>(htmlHost.Surface);
        bool Dispatch(UiInputEvent input) => keyboard.Handle(input) || session.DispatchInput(input);
        var list = Descendants(shell.GetContent("inbox")).OfType<Broiler.UI.ListView.Standard.StandardListView>().Single();
        session.SetFocus(list); Assert.False(keyboard.Handle(Key(0xBB, KeyboardModifierState.Control))); Assert.Equal(1, view.Zoom);
        session.SetFocus(view.Content); Assert.True(Dispatch(Key(0xBB, KeyboardModifierState.Control))); session.RenderFrame(); Assert.Equal(1.1, view.Zoom);
        Assert.True(Dispatch(Key(0x30, KeyboardModifierState.Control))); session.RenderFrame();
        keyboard.Execute(MailCommand.NextField);
        var link = Assert.Single(view.Content.LinkTargets);
        Assert.Same(link, session.FocusedElement); Assert.True(view.Scroll.VerticalOffset > 0);
        Assert.Equal(UiSemanticRole.Hyperlink, link.GetSemanticNode().Role); Assert.Equal("Closing link", link.GetSemanticNode().Name);
        Assert.InRange(link.Bounds.Top, view.Scroll.Bounds.Top - 1, view.Scroll.Bounds.Bottom + 1);
        Assert.True(Dispatch(Key(0x0D))); Assert.Equal("https://example.test/end", Assert.Single(htmlHost.Opened));
        session.SetFocus(list);
        Assert.True(keyboard.Handle(Wheel(view.Bounds.Left + 10, view.Bounds.Top + 10, 1, InputModifiers.Control))); Assert.Equal(1.1, view.Zoom);
        Assert.False(keyboard.Handle(Wheel(list.Bounds.Left + 10, list.Bounds.Top + 10, 1, InputModifiers.Control))); Assert.Equal(1.1, view.Zoom);
        shell.ShowView("settings"); Assert.False(keyboard.Handle(Key(0xBB, KeyboardModifierState.Control)));
        shell.CloseDialog(); session.SetFocus(view.Content);
        var toggle = Descendants(shell.GetContent("inbox")).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == "Plain text");
        for (int i = 0; i < 30 && session.FocusedElement != toggle; i++) keyboard.Execute(MailCommand.PreviousField);
        Assert.Same(toggle, session.FocusedElement);
        Assert.True(Dispatch(Key(0x0D))); Assert.False(keyboard.Handle(Key(0xBB, KeyboardModifierState.Control)));
        Assert.True(Dispatch(Key(0x0D))); // Restore HTML using the same focused toggle.
        uiHost.ViewportSize = new(640, 480); shell.Window.InvalidateMeasure(); session.RenderFrame();
        Assert.True(shell.Inbox.GoBackToList()); session.RenderFrame();
        Assert.False(keyboard.Handle(Wheel(view.Bounds.Left + 10, view.Bounds.Top + 10, 1, InputModifiers.Control)));
    }

    [Fact]
    public void SpacePagesOnlyWithoutControlOrAltModifiers()
    {
        using var view = new ScrollableHtmlView(string.Concat(Enumerable.Repeat("<p>Long readable message.</p>", 100)), () => null, _ => { });
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(view); session.RenderFrame(); session.SetFocus(view.Content);
        Assert.True(session.DispatchInput(Key(0x20))); Assert.True(view.Scroll.VerticalOffset > 0);
        Assert.False(view.Content.SendInput(Key(0x20, KeyboardModifierState.Control)));
        Assert.False(view.Content.SendInput(Key(0x20, KeyboardModifierState.LeftAlt)));
    }

    private sealed class HtmlHost : IInlineHtmlPreviewHost, IDisposable
    {
        public ScrollableHtmlView? Surface; public List<string> Opened = [];
        public UiElement CreateInlineContent(MailMessageBody message) => Surface = new ScrollableHtmlView(
            HtmlPreviewPolicy.Create(string.Concat(Enumerable.Repeat("<p>Readable long message.</p>", 50)) + "<p><a href='https://example.test/end'>Closing link</a></p>").Html, () => null, Opened.Add);
        public void Dispose() { }
    }
    private static IEnumerable<UiElement> Descendants(UiElement root)
    { foreach (var child in root.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static InputEventHeader Header() => new(InputDeviceId.FromOpaqueValue("inline-keyboard"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);
    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) => UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(Header(),
        KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false, Source: InputEventSource.Synthetic));
    private static UiInputEvent Wheel(double x, double y, double notches, InputModifiers modifiers) => UiInputEvent.FromMouseWheel(new MouseWheelEvent(Header(),
        InputPoint.ClientDeviceIndependentPixels(x, y), MouseButtons.None, MouseWheelAxis.Vertical, notches, InputEventSource.Synthetic, modifiers));
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize { get; set; } = new(1200, 800); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList list) { }
    }
}
