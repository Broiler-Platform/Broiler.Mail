using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class InlineInboxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZoomControlsStayBoundedAndQueuedFailureOnlyFallsBackForItsOwnSurface(bool stale)
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Fallback", "<p>HTML</p>")),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account); await model.ReceiveAsync();
        using var host = new InlineHost(); using var root = new InboxView(model, host).CreateContent();
        var dispatcher = new TestQueueDispatcher();
        var uiHost = new UiHost();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(uiHost);
        session.AddRoot(root); session.RenderFrame();
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1)); session.RenderFrame();
        var surface = Assert.IsType<ControlledSurface>(host.Surface);
        StandardButton Button(string name) => Descendants(root).OfType<StandardButton>().Single(b => b.Text == name);
        var zoomIn = Button("Zoom in"); var zoomOut = Button("Zoom out");
        var reset = Button("100 %");
        session.SetFocus(zoomIn);
        for (int i = 0; i < 10 && zoomIn.IsEnabled; i++) zoomIn.Click();
        Assert.Equal(3, surface.Zoom); Assert.False(zoomIn.IsEnabled); Assert.Same(reset, session.FocusedElement);
        reset.Click(); Assert.Equal(1, surface.Zoom);
        Button("Plain text").Click(); Assert.False(zoomIn.IsEnabled); Assert.False(zoomOut.IsEnabled);
        Assert.Same(Button("HTML"), session.FocusedElement);
        Button("HTML").Click(); Assert.True(zoomIn.IsEnabled);
        uiHost.ViewportSize = new(640, 480); session.RenderFrame();
        foreach (var button in new[] { zoomIn, zoomOut, reset, Button("Plain text") })
        {
            Assert.True(button.Bounds.Width > 0);
            Assert.InRange(button.Bounds.Left, button.Parent!.Bounds.Left - 1, button.Parent.Bounds.Right + 1);
            Assert.InRange(button.Bounds.Right, button.Parent.Bounds.Left - 1, button.Parent.Bounds.Right + 1);
        }
        session.SetFocus(surface.Children.Single()); surface.Fail();
        if (stale) await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
        dispatcher.Drain(); session.RenderFrame();
        if (stale)
        {
            Assert.NotSame(surface, host.Surface); Assert.Equal(UiVisibility.Visible, host.Surface!.Visibility);
            Assert.True(Button("Plain text").IsEnabled);
            Assert.DoesNotContain(Descendants(root).OfType<Broiler.UI.Label.Standard.StandardLabel>(), l => l.Text.Contains("rendering failed"));
        }
        else
        {
            Assert.True(surface.IsDisposed);
            Assert.False(Button("Plain text").IsEnabled); Assert.False(zoomIn.IsEnabled);
            var text = Descendants(root).OfType<ScrollableMessageText>().Single();
            Assert.Equal(UiVisibility.Visible, text.Visibility); Assert.Same(text.Editor, session.FocusedElement);
            Assert.Contains(Descendants(root).OfType<Broiler.UI.Label.Standard.StandardLabel>(), l => l.Text.Contains("rendering failed"));
        }
    }

    [Fact]
    public async Task ReaderModeSurvivesRestartAndGeometryWrites()
    {
        using var directory = new TestDirectory();
        var store = new Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore(directory.File("settings.json"));
        Assert.False((await store.LoadAsync()).PreferPlainText);
        var settings = new SettingsViewModel(store, new ImmediateUiDispatcher(), await store.LoadAsync(), null);
        await settings.RememberReaderModeAsync(true);
        await settings.RememberLayoutAsync(null, 0.6);
        var reopened = await new Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore(directory.File("settings.json")).LoadAsync();
        Assert.True(reopened.PreferPlainText); Assert.Equal(0.6, reopened.InboxSplitterFraction);
        await settings.RememberReaderModeAsync(false);
        Assert.False((await store.LoadAsync()).PreferPlainText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HtmlUsesReaderSurfaceAndSelectionDisposesItWhileFailuresKeepText(bool fail)
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Readable fallback", key.Uid == 1 ? "<p>Formatted</p>" : null)),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account); await model.ReceiveAsync();
        using var host = new InlineHost { Fail = fail };
        using var root = new InboxView(model, host).CreateContent();
        using var session = new StandardUiSessionBuilder().Build(new UiHost());
        session.AddRoot(root); session.RenderFrame();
        var text = Descendants(root).OfType<ScrollableMessageText>().Single();
        session.SetFocus(text.Editor);
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1));
        session.RenderFrame();
        Assert.Equal(1, host.Created);
        Assert.DoesNotContain(Descendants(root).OfType<StandardButton>(), b => b.Text == "Open HTML preview");
        if (!fail)
        {
            var surface = Assert.IsType<ControlledSurface>(host.Surface);
            Assert.Equal(UiVisibility.Visible, surface.Visibility);
            Assert.Same(surface.Children.Single(), session.FocusedElement);
            var toggle = Descendants(root).OfType<StandardButton>().Single(b => b.Text == "Plain text");
            toggle.Click(); Assert.Equal(UiVisibility.Collapsed, surface.Visibility); Assert.Equal("HTML", toggle.Text);
            toggle.Click(); Assert.Equal(UiVisibility.Visible, surface.Visibility);
            session.SetFocus(surface.Children.Single());
            await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
            session.RenderFrame();
            Assert.True(surface.IsDisposed); Assert.DoesNotContain(surface, Descendants(root));
            Assert.Same(text.Editor, session.FocusedElement);
            Assert.DoesNotContain(Descendants(root).OfType<StandardButton>(), b => b.Text is "HTML" or "Plain text");
        }
        else
        {
            Assert.Contains(Descendants(root).OfType<Broiler.UI.Label.Standard.StandardLabel>(), label => label.Text.Contains("HTML unavailable"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModeSwitchAndManagedFailureTransferOnlyThisMessagesReadingPosition(bool stale)
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Range(1, 100).Select(i => "Paragraph " + i)), "<p>HTML</p>")),
        };
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher()); model.SetAccount(account); await model.ReceiveAsync();
        using var host = new InlineHost(); using var root = new InboxView(model, host).CreateContent(); var dispatcher = new TestQueueDispatcher();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new UiHost()); session.AddRoot(root); await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1)); session.RenderFrame();
        StandardButton Button(string value) => Descendants(root).OfType<StandardButton>().Single(item => item.Text == value);
        var old = Assert.IsType<ControlledSurface>(host.Surface); var text = Descendants(root).OfType<ScrollableMessageText>().Single();
        old.RestoreReadingPosition(0.6); Button("Plain text").Click(); session.RenderFrame(); Assert.InRange(text.ReadingPosition, 0.59, 0.61);
        text.RestoreReadingPosition(0.35); session.RenderFrame(); Button("HTML").Click(); Assert.InRange(old.ReadingPosition, 0.34, 0.36);
        var endButton = Descendants(root).OfType<StandardButton>().Single(item => item.AccessibleName == "End of message"); session.SetFocus(endButton);
#pragma warning disable CS0618
        var enter = new StandardLegacyGraphicsInputAdapter("reader-viewport").FromKey(new Broiler.Graphics.Windowing.BKeyEventArgs(0x0D, false, false, false), Broiler.Input.Keyboard.KeyboardKeyTransition.Down);
#pragma warning restore CS0618
        Assert.True(session.DispatchInput(enter)); Assert.Equal(1, old.ReadingPosition); Descendants(root).OfType<StandardButton>().Single(item => item.AccessibleName == "Top of message").Click(); Assert.Equal(0, old.ReadingPosition);
        old.RestoreReadingPosition(0.7); old.Fail();
        if (stale) await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
        dispatcher.Drain(); session.RenderFrame();
        if (stale) { Assert.NotSame(old, host.Surface); Assert.Equal(0, ((IReaderViewport)host.Surface!).ReadingPosition); Assert.Equal(0, text.ReadingPosition); }
        else { Assert.Equal(UiVisibility.Visible, text.Visibility); Assert.InRange(text.ReadingPosition, 0.69, 0.71); }
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        foreach (var child in element.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private sealed class InlineHost : IInlineHtmlPreviewHost, IDisposable
    {
        public bool Fail; public int Created; public UiElement? Surface;
        public UiElement CreateInlineContent(MailMessageBody message)
        {
            Created++;
            if (Fail) throw new ArgumentException("Fixture renderer failure");
            var panel = new ControlledSurface();
            panel.AddChild(new StandardButton { Text = "Fixture HTML link" });
            return Surface = panel;
        }
        public void Dispose() { }
    }

    private sealed class ControlledSurface : UiElement, IInlineHtmlReader, IReaderViewport
    {
        protected override Broiler.Graphics.Geometry.BSize MeasureCore(Broiler.Graphics.Geometry.BSize availableSize) => Children.Single().Measure(availableSize);
        public event EventHandler? ViewChanged;
        public event EventHandler? RenderingFailed;
        public double ReadingPosition { get; private set; }
        public void RestoreReadingPosition(double value) => ReadingPosition = Math.Clamp(value, 0, 1);
        public double Zoom { get; private set; } = 1;
        public bool CanZoomIn => Zoom < 3; public bool CanZoomOut => Zoom > 0.5;
        public void ZoomBy(int direction) { Zoom = Math.Clamp(Zoom + direction * 0.5, 0.5, 3); ViewChanged?.Invoke(this, EventArgs.Empty); }
        public void ResetZoom() { Zoom = 1; ViewChanged?.Invoke(this, EventArgs.Empty); }
        public void Fail() => RenderingFailed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class UiHost : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize { get; set; } = new(1200, 800);
        public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(Broiler.Graphics.RenderList.BRenderList renderList) { }
    }
}
