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
        // The HTML row with zoom is in the reader's header, below its commands, without opening anything.
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
        // Top and End of message are in the strip's More message actions popup; no button of their own remains.
        Assert.DoesNotContain(Descendants(root).OfType<StandardButton>(), item => item.AccessibleName is "End of message" or "Top of message");
        ReaderMenus.Invoke(session, root, "More message actions", "End of message"); Assert.Equal(1, old.ReadingPosition);
        ReaderMenus.Invoke(session, root, "More message actions", "Top of message"); Assert.Equal(0, old.ReadingPosition);
        old.RestoreReadingPosition(0.7); old.Fail();
        if (stale) await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 2));
        dispatcher.Drain(); session.RenderFrame();
        if (stale) { Assert.NotSame(old, host.Surface); Assert.Equal(0, ((IReaderViewport)host.Surface!).ReadingPosition); Assert.Equal(0, text.ReadingPosition); }
        else { Assert.Equal(UiVisibility.Visible, text.Visibility); Assert.InRange(text.ReadingPosition, 0.69, 0.71); }
    }

    private sealed class ImageReader : IDisposable
    {
        public required InboxViewModel Model; public required InlineHost Host; public required UiElement Root;
        public required UiSession Session; public required TestQueueDispatcher Dispatcher; public required MailboxKey Mailbox;
        public required UiHost UiHost;
        public ImageSurface Surface => Assert.IsAssignableFrom<ImageSurface>(Host.Surface);
        public RemoteImageActions Banner => Descendants(Root).OfType<RemoteImageActions>().Single();
        public UiElement Notices => Descendants(Root).Single(element => element.AccessibleName == "Message notices");
        public ScrollableMessageText Text => Descendants(Root).OfType<ScrollableMessageText>().Single();
        public StandardButton Button(string name) => Descendants(Root).OfType<StandardButton>().Single(b => b.Text == name);
        public bool Shown(UiElement element)
        {
            for (UiElement? at = element; at is not null; at = at.Parent) if (at.Visibility != UiVisibility.Visible) return false;
            return element.IsDescendantOf(Root);
        }
        public Task SelectAsync(uint uid) => Model.SelectAsync(SqliteMailCacheTests.Key(Mailbox, uid));
        public void Render() { Dispatcher.Drain(); Session.RenderFrame(); }
        public void Dispose() { Session.Dispose(); Root.Dispose(); Host.Dispose(); Model.Dispose(); }

        public static async Task<ImageReader> OpenAsync(double width = 1200, double height = 800, bool resettable = true)
        {
            var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
            var receiver = new TestMailReceiver
            {
                Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
                Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join("\n\n", Enumerable.Range(1, 60).Select(i => "Paragraph " + i)), "<p>HTML</p><img src='https://images.example.test/a'>")),
            };
            var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
            model.SetAccount(account); await model.ReceiveAsync();
            var host = new InlineHost { RemoteImages = true, Resettable = resettable };
            var root = new InboxView(model, host).CreateContent();
            var dispatcher = new TestQueueDispatcher(); var uiHost = new UiHost { ViewportSize = new(width, height) };
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(uiHost);
            session.AddRoot(root); session.RenderFrame();
            await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1)); session.RenderFrame();
            return new ImageReader { Model = model, Host = host, Root = root, Session = session, Dispatcher = dispatcher, Mailbox = mailbox, UiHost = uiHost };
        }
    }

    [Theory]
    [InlineData(1200, 800)]
    [InlineData(1100, 720)]
    public async Task RemoteImagesBannerIsInlineBetweenTheLineAndTheHtmlAndKeepsTheTextsLines(double width, double height)
    {
        using var reader = await ImageReader.OpenAsync(width, height);
        var surface = reader.Surface; var banner = reader.Banner;
        // Shown without opening anything, and nothing is fetched before Load images.
        Assert.True(reader.Shown(banner)); Assert.Equal(0, surface.Loads);
        Assert.Contains(Descendants(banner).OfType<Broiler.UI.Label.Standard.StandardLabel>(), l => l.Text == "Remote images (3) are blocked to protect your privacy.");
        Assert.DoesNotContain(Descendants(reader.Root).OfType<MailMenuButton>(), button => button.IsExpanded);
        var divider = Descendants(reader.Root).OfType<Divider>().Single();
        Assert.True(reader.Notices.Bounds.Top >= divider.Bounds.Bottom - 1);
        Assert.True(banner.Bounds.Top >= divider.Bounds.Bottom - 1);
        Assert.True(surface.Bounds.Top >= banner.Bounds.Bottom - 1, $"{surface.Bounds} overlaps the banner {banner.Bounds}.");
        // The banner is one row of the sentence and its buttons.
        var load = reader.Button("Load images");
        Assert.InRange(load.Bounds.Top, banner.Bounds.Top - 1, banner.Bounds.Top + banner.Bounds.Height / 2);
        Assert.True(banner.Bounds.Height < load.Bounds.Height * 2, $"The banner is {banner.Bounds.Height} high.");
        // The header's share is of the height the banner leaves, so the message keeps its lines.
        Assert.True(surface.Bounds.Height >= reader.Text.HeightOfLines(6) - 1, $"{surface.Bounds.Height} < six lines.");
        reader.Button("Plain text").Click(); reader.Render();
        Assert.False(reader.Shown(banner));
    }

    [Fact]
    public async Task RemoteImagesConsentCancelRetryAndBlockAgainNeedNoDialog()
    {
        using var reader = await ImageReader.OpenAsync();
        var surface = reader.Surface; var load = reader.Button("Load images");
        reader.Session.SetFocus(load); load.Click(); reader.Render();
        Assert.Equal(1, surface.Loads); Assert.Same(reader.Button("Cancel loading"), reader.Session.FocusedElement);
        // While loading, More message actions offers Block remote images again too.
        Assert.Contains(ReaderMenus.Items(reader.Root, "More message actions"), item => item.Text == "Block remote images again");
        reader.Session.SetFocus(reader.Button("Cancel loading")); reader.Button("Cancel loading").Click(); reader.Render();
        Assert.Equal(1, surface.Cancellations); Assert.Same(load, reader.Session.FocusedElement); Assert.Equal("Retry", load.Text);
        load.Click(); surface.Complete(2); reader.Render();
        Assert.Equal("Retry failed", load.Text); Assert.Same(load, reader.Session.FocusedElement);
        load.Click(); surface.Complete(3); reader.Render();
        // Loaded: the right-edge Block again replaces Load, and focus stays usable on it.
        Assert.False(reader.Shown(load)); var block = reader.Button("Block again");
        Assert.True(reader.Shown(block)); Assert.Same(block, reader.Session.FocusedElement);
        ReaderMenus.Invoke(reader.Session, reader.Root, "More message actions", "Block remote images again"); reader.Render();
        Assert.Equal(1, Assert.IsType<ResettableImageSurface>(surface).Resets); Assert.Equal(3, surface.Loads);
        Assert.True(reader.Shown(load)); Assert.Equal("Load images", load.Text); Assert.False(reader.Shown(block));
        Assert.DoesNotContain(ReaderMenus.Items(reader.Root, "More message actions"), item => item.Text == "Block remote images again");
    }

    [Fact]
    public async Task WithoutResetTheMenuNeverOffersBlockAgain()
    {
        using var reader = await ImageReader.OpenAsync(resettable: false);
        reader.Button("Load images").Click(); reader.Surface.Complete(3); reader.Render();
        Assert.False(reader.Shown(reader.Button("Block again")));
        Assert.DoesNotContain(ReaderMenus.Items(reader.Root, "More message actions"), item => item.Text == "Block remote images again");
    }

    [Fact]
    public async Task PlainTextHidesTheBannerAndCancelsLoading()
    {
        using var reader = await ImageReader.OpenAsync();
        var surface = reader.Surface; var banner = reader.Banner;
        reader.Button("Load images").Click(); reader.Render();
        reader.Button("Plain text").Click(); reader.Render();
        Assert.Equal(1, surface.Cancellations); Assert.False(reader.Shown(banner));
        Assert.Equal(UiVisibility.Collapsed, Descendants(reader.Root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Reader notices").Visibility);
        reader.Button("HTML").Click(); reader.Render();
        Assert.True(reader.Shown(banner)); Assert.Equal("Retry", reader.Button("Retry").Text);
    }

    [Fact]
    public async Task AnotherMessageDisposesTheBannerAndMovesItsFocusToTheReader()
    {
        using var reader = await ImageReader.OpenAsync();
        var old = reader.Surface; var oldBanner = reader.Banner;
        reader.Button("Load images").Click(); reader.Render();
        reader.Session.SetFocus(reader.Button("Cancel loading"));
        await reader.SelectAsync(2); reader.Render();
        Assert.True(oldBanner.IsDisposed); Assert.False(oldBanner.IsDescendantOf(reader.Root));
        Assert.Equal(0, old.Subscribers); Assert.Equal(1, old.Cancellations);
        var next = reader.Surface; Assert.NotSame(old, next); Assert.Equal(0, next.Loads);
        Assert.Equal("Load images", reader.Button("Load images").Text);
        Assert.Same(next.Children.Single(), reader.Session.FocusedElement);
    }

    [Fact]
    public async Task RenderingFailureCollapsesTheBannerAndFocusesTheText()
    {
        using var reader = await ImageReader.OpenAsync();
        var surface = reader.Surface; var banner = reader.Banner;
        reader.Session.SetFocus(reader.Button("Load images"));
        surface.Fail(); reader.Render();
        Assert.False(reader.Shown(banner)); Assert.Same(reader.Text.Editor, reader.Session.FocusedElement);
        Assert.Equal(0, surface.Loads);
    }

    [Fact]
    public async Task LoadingAllImagesWithFocusOnLoadAndNoResetFallsBackToTheReader()
    {
        using var reader = await ImageReader.OpenAsync(resettable: false);
        var surface = reader.Surface; var load = reader.Button("Load images");
        reader.Session.SetFocus(load); load.Click(); reader.Render();
        surface.Complete(3); reader.Render();
        Assert.Same(surface.Children.Single(), reader.Session.FocusedElement);
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        foreach (var child in element.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private sealed class InlineHost : IInlineHtmlPreviewHost, IDisposable
    {
        public bool Fail; public int Created; public UiElement? Surface;
        public bool RemoteImages, Resettable = true;
        public UiElement CreateInlineContent(MailMessageBody message)
        {
            Created++;
            if (Fail) throw new ArgumentException("Fixture renderer failure");
            var panel = RemoteImages ? Resettable ? new ResettableImageSurface() : new ImageSurface() : new ControlledSurface();
            panel.AddChild(new StandardButton { Text = "Fixture HTML link" });
            return Surface = panel;
        }
        public void Dispose() { }
    }

    private class ImageSurface : ControlledSurface, IInlineRemoteImages
    {
        private EventHandler? _changed;
        public int Loads, Cancellations, Subscribers;
        public event EventHandler? RemoteImagesChanged { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public RemoteImageState RemoteImages { get; private set; } = new(3, 0, false, true, "Remote images are blocked.");
        public Task LoadRemoteImagesAsync() { Loads++; Set(RemoteImages with { IsLoading = true, CanLoad = false, Status = "Loading remote images for this message…" }); return Task.CompletedTask; }
        public void CancelRemoteImages() { if (!RemoteImages.IsLoading) return; Cancellations++; Set(RemoteImages with { IsLoading = false, CanLoad = true, Status = "Remote image loading canceled." }); }
        public void Complete(int loaded) => Set(new(3, loaded, false, loaded < 3, loaded == 3 ? "Loaded 3 remote images for this message." : $"Loaded {loaded} of 3 remote images."));
        protected void Set(RemoteImageState state) { RemoteImages = state; _changed?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class ResettableImageSurface : ImageSurface, IInlineRemoteImageReset
    {
        public int Resets;
        public void BlockRemoteImages() { Resets++; CancelRemoteImages(); Set(new(3, 0, false, true, "Remote images are blocked again. Previously sent requests cannot be undone.")); }
    }

    private class ControlledSurface : UiElement, IInlineHtmlReader, IReaderViewport
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
