using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class MessageTextFindViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void FindWrapsSelectsRevealsAndCopiesOnlyCurrentMatch(double scale)
    {
        using var text = new ScrollableMessageText { Text = string.Join("\n", Enumerable.Range(1, 100).Select(i => "Line " + i)) + "\nAlpha end Alpha" };
        using var root = new Broiler.UI.Panel.Standard.StandardPanel { LayoutMode = Broiler.UI.Panel.UiPanelLayoutMode.Dock };
        bool current = true; using var bar = new MessageTextFindBar(text, text.Text, 0, () => { }, () => current, () => { });
        root.AddChild(bar); root.SetDock(bar, Broiler.UI.Panel.UiDock.Top); root.AddChild(text);
        var host = new Host(); using var session = new StandardUiSessionBuilder().Build(host); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        bar.ShowFind(); session.RenderFrame(); Query(bar).Text = "ALPHA"; bar.Search(); session.RenderFrame();
        Assert.Contains("Match 1 of 2", bar.Status); Assert.Equal("Alpha", Selected(text));
        Assert.True(((StandardScrollView)text.Children[0]).VerticalOffset > 0);
        Button(bar, "Next match").Click(); Assert.Contains("Match 2 of 2", bar.Status); Button(bar, "Next match").Click(); Assert.Contains("Wrapped", bar.Status);
        Button(bar, "Previous match").Click(); Assert.Contains("Match 2 of 2", bar.Status);
        Button(bar, "Copy match").Click(); Assert.Equal("Alpha", host.Clipboard); Assert.Equal(0, host.Reads);
        host.FailCopy = true; Button(bar, "Copy match").Click(); Assert.Contains("failed", bar.Status); Assert.Equal("Alpha", Selected(text));
        current = false; host.FailCopy = false; host.Clipboard = "Keep"; Button(bar, "Copy match").Click(); bar.Search(); Assert.Equal("Keep", host.Clipboard);
    }

    [Fact]
    public async Task ReaderShortcutsUseTextAlternativeResetPerMessageAndDoNotHijackListFocus()
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7); int fetches = 0;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
            Body = (key, _) => { fetches++; return Task.FromResult(new MailMessageBody(key, "First\r\nAlpha marker Alpha", "<p>HTML Alpha</p>") { IsHtmlFallback = true }); },
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher()); model.SetAccount(account); await model.ReceiveAsync();
        using var html = new HtmlHost(); var view = new InboxView(model, html); using var root = view.CreateContent(); var host = new Host();
        using var session = new StandardUiSessionBuilder().Build(host); session.AddRoot(root); await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1)); session.RenderFrame();
        var list = All(root).OfType<Broiler.UI.ListView.Standard.StandardListView>().Single(); session.SetFocus(list); Assert.False(view.HandleReaderInput(Key(0x46, KeyboardModifierState.Control)));
        session.SetFocus(html.Surface!); Assert.False(view.HandleReaderInput(Key(0x46, KeyboardModifierState.Control | KeyboardModifierState.Alt)));
        Assert.True(view.HandleReaderInput(Key(0x46, KeyboardModifierState.Control))); session.RenderFrame();
        var bar = All(root).OfType<MessageTextFindBar>().Single(); var text = All(root).OfType<ScrollableMessageText>().Single(); Assert.Equal(UiVisibility.Visible, text.Visibility);
        Query(bar).Text = "alpha"; bar.Search(); session.RenderFrame(); Assert.Equal("Alpha", Selected(text)); Assert.Equal(1, fetches);
        Assert.True(view.HandleReaderInput(Key(0x72))); Assert.Contains("Match 2 of 2", bar.Status); Assert.True(view.HandleReaderInput(Key(0x72, KeyboardModifierState.Shift))); Assert.Contains("Match 1 of 2", bar.Status);
        Assert.True(view.HandleReaderInput(Key(0x1B))); Assert.Equal(UiVisibility.Collapsed, bar.Visibility); Assert.Same(text.Editor, session.FocusedElement);
        var oldCopy = Button(bar, "Copy match"); bar.ShowFind(); await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2)); session.RenderFrame();
        Assert.True(bar.IsDisposed); Assert.Equal(UiVisibility.Visible, html.Surface!.Visibility); Assert.Equal("", Query(All(root).OfType<MessageTextFindBar>().Single()).Text);
        host.Clipboard = "Keep"; Assert.True(oldCopy.IsDisposed); bar.Search(); Assert.False(bar.HandleFindInput(Key(0x46, KeyboardModifierState.Control))); Assert.Equal("Keep", host.Clipboard); Assert.Equal(2, fetches);
    }

    [Fact]
    public void EditedOrInvalidQueriesDisableStaleNavigationAndClipboard()
    {
        using var text = new ScrollableMessageText { Text = "Alpha" }; using var bar = new MessageTextFindBar(text, "Alpha", 0, () => { }, () => true, () => { });
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(bar); bar.ShowFind(); Query(bar).Text = "Alpha"; bar.Search();
        Query(bar).Text = "Missing"; Assert.False(Button(bar, "Next match").IsEnabled); Assert.False(Button(bar, "Copy match").IsEnabled); bar.Search(); Assert.Contains("No matches", bar.Status);
        Query(bar).Text = ""; bar.Search(); Assert.Contains("1–256", bar.Status);
    }

    private static string Selected(ScrollableMessageText text) => text.Editor.Document.Slice(text.Editor.Selection).PlainText;
    private static StandardEdit Query(UiElement root) => All(root).OfType<StandardEdit>().Single(item => item.AccessibleName == "Find in message text");
    private static StandardButton Button(UiElement root, string value) => All(root).OfType<StandardButton>().Single(item => item.Text == value);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var value in All(child)) yield return value; }
    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None)
    {
#pragma warning disable CS0618
        return new StandardLegacyGraphicsInputAdapter("message-find").FromKey(new Broiler.Graphics.Windowing.BKeyEventArgs(code, modifiers.HasFlag(KeyboardModifierState.Control), modifiers.HasFlag(KeyboardModifierState.Shift), modifiers.HasFlag(KeyboardModifierState.Alt)), KeyboardKeyTransition.Down);
#pragma warning restore CS0618
    }
    private sealed class Host : IUiHost, IUiClipboardHost
    {
        public string Clipboard = ""; public int Reads; public bool FailCopy;
        public bool TryGetText(out string value) { Reads++; value = Clipboard; return true; }
        public void SetText(string value) { if (FailCopy) throw new InvalidOperationException("Synthetic clipboard busy"); Clipboard = value; }
        public BSize ViewportSize => new(1100, 720); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList list) { }
    }
    private sealed class HtmlHost : IInlineHtmlPreviewHost, IDisposable
    {
        public UiElement? Surface;
        public UiElement CreateInlineContent(MailMessageBody body) => Surface = new StandardButton { Text = "Passive HTML surface" };
        public void Dispose() { }
    }
}
