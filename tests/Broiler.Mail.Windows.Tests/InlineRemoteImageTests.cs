using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
public sealed class InlineRemoteImageTests
{
    [Fact]
    public async Task ImagesCompletingAfterAThemeChangeUseTheCurrentPalette()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler(async (request, token) =>
        { entered.TrySetResult(); await release.Task.WaitAsync(token); return Response(request); })) };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var queue = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(surface);
        var loading = surface.LoadRemoteImagesAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.ApplyTheme(StandardThemeTokens.Dark); queue.Drain();
        release.SetResult(); await loading; queue.Drain();
        Assert.Equal(2, surface.RemoteImages.Loaded);
        surface.Content.CalculateLayout(640); using var bitmap = surface.Content.PaintTile(0, 1, 640, 256);
        var pixels = bitmap.ToPixelBuffer().Rgba; var color = StandardThemeTokens.Dark.Surface;
        Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] == color.R && pixels[i * 4 + 1] == color.G && pixels[i * 4 + 2] == color.B && pixels[i * 4 + 3] > 200) > 1000);
    }

    [Fact]
    public async Task DisposingWhileLoadingCancelsTheOutstandingRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
            throw new InvalidOperationException();
        })) };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var loading = surface.LoadRemoteImagesAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        surface.Dispose(); await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)); await loading;
        Assert.Equal(0, surface.RemoteImages.Loaded);
    }

    private static MailMessageBody Message() => new(new(new(Guid.NewGuid()), "INBOX", 7, 1), "Fallback",
        "<p>Images</p><img src='https://images.example.test/a'><img src='https://images.example.test/b'>");

    [Fact]
    public async Task ConsentAndPartialRetryAreScopedToTheSurfaceAndKeepApprovedImages()
    {
        var calls = new ConcurrentDictionary<string, int>();
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath; int count = calls.AddOrUpdate(path, 1, (_, n) => n + 1);
            return Task.FromResult(Response(request, path == "/b" && count == 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        })) };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var queue = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(surface);
        Assert.Empty(calls); Assert.Equal(0, surface.RemoteImages.Loaded);
        surface.ZoomBy(1); double zoom = surface.Zoom;
        await surface.LoadRemoteImagesAsync(); queue.Drain();
        Assert.Equal(1, surface.RemoteImages.Loaded); Assert.True(surface.RemoteImages.CanLoad); Assert.Equal(zoom, surface.Zoom);
        host.ApplyTheme(StandardThemeTokens.Dark); queue.Drain();
        Assert.Equal(1, calls["/a"]); Assert.Equal(1, calls["/b"]); // Appearance never grants another fetch.
        await surface.LoadRemoteImagesAsync(); queue.Drain();
        Assert.Equal(2, surface.RemoteImages.Loaded); Assert.False(surface.RemoteImages.CanLoad);
        Assert.Equal(1, calls["/a"]); Assert.Equal(2, calls["/b"]);
        Assert.Contains(surface.Content.CalculateLayout(640).Diagnostics, box => box.TagName == "img");
        using var next = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        Assert.Equal(0, next.RemoteImages.Loaded); Assert.True(next.RemoteImages.CanLoad);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrDisposeRejectsAQueuedCompletion(bool dispose)
    {
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler((request, _) => Task.FromResult(Response(request)))) };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var queue = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(surface);
        await surface.LoadRemoteImagesAsync(); // Result queued; the renderer has not received it.
        if (dispose) surface.Dispose(); else surface.CancelRemoteImages();
        queue.Drain(); Assert.Equal(0, surface.RemoteImages.Loaded);
        if (!dispose) { Assert.False(surface.RemoteImages.IsLoading); Assert.True(surface.RemoteImages.CanLoad); }
    }

    [Fact]
    public async Task BlockingDropsApprovalsKeepsEmbeddedImagesAndRequiresFreshConsent()
    {
        int calls = 0;
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler((request, _) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(Response(request)); })) };
        var message = Message() with
        {
            HtmlText = Message().HtmlText + "<img src='cid:local'>" + string.Concat(Enumerable.Range(1, 100).Select(i => $"<p>Paragraph {i}: reader position fixture.</p>")),
            EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["local"] = new("local", "image/png", Convert.FromBase64String(RemoteImageBrokerSmokeCheck.Pixel)) }
        };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(message));
        using var other = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var queue = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(surface);
        surface.ZoomBy(1); double zoom = surface.Zoom;
        await surface.LoadRemoteImagesAsync(); queue.Drain();
        await other.LoadRemoteImagesAsync();
        Assert.Equal(3, surface.Content.CalculateLayout(640).Diagnostics.Count(box => box.TagName == "img"));
        surface.ApplyReaderTheme(StandardThemeTokens.Dark); queue.Drain();
        surface.RestoreReadingPosition(0.6); session.RenderFrame();
        int before = calls;
        surface.BlockRemoteImages(); queue.Drain(); session.RenderFrame();
        Assert.InRange(surface.ReadingPosition, 0.59, 0.61);
        Assert.Equal(before, calls); Assert.Equal(0, surface.RemoteImages.Loaded);
        Assert.True(surface.RemoteImages.CanLoad); Assert.False(surface.RemoteImages.IsLoading);
        Assert.Contains("cannot be undone", surface.RemoteImages.Status);
        Assert.Equal(2, other.RemoteImages.Loaded); Assert.Equal(zoom, surface.Zoom);
        Assert.Single(surface.Content.CalculateLayout(640).Diagnostics, box => box.TagName == "img");
        using var bitmap = surface.Content.PaintTile(0, 1, 640, 256);
        var pixels = bitmap.ToPixelBuffer().Rgba; var color = StandardThemeTokens.Dark.Surface;
        Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] == color.R && pixels[i * 4 + 1] == color.G && pixels[i * 4 + 2] == color.B && pixels[i * 4 + 3] > 200) > 1000);
        await surface.LoadRemoteImagesAsync(); queue.Drain();
        Assert.Equal(before + 2, calls); Assert.Equal(2, surface.RemoteImages.Loaded);
    }

    [Fact]
    public async Task BlockingRejectsAQueuedCompletion()
    {
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler((request, _) => Task.FromResult(Response(request)))) };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var queue = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(surface);
        await surface.LoadRemoteImagesAsync();
        surface.BlockRemoteImages(); queue.Drain();
        Assert.Equal(0, surface.RemoteImages.Loaded); Assert.True(surface.RemoteImages.CanLoad);
        Assert.DoesNotContain(surface.Content.CalculateLayout(640).Diagnostics, box => box.TagName == "img");
    }

    [Fact]
    public async Task BlockingCancelsPendingRequestAndRejectsItsLateResult()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken pending = default; int calls = 0;
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler(async (request, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { pending = token; entered.SetResult(); await release.Task; }
            return Response(request);
        })) };
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        var queue = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(surface);
        var loading = surface.LoadRemoteImagesAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        surface.BlockRemoteImages(); Assert.True(pending.IsCancellationRequested);
        Assert.Equal(0, surface.RemoteImages.Loaded); Assert.True(surface.RemoteImages.CanLoad);
        await surface.LoadRemoteImagesAsync(); queue.Drain(); Assert.Equal(2, surface.RemoteImages.Loaded);
        release.SetResult(); await loading; queue.Drain();
        Assert.Equal(2, surface.RemoteImages.Loaded); Assert.False(surface.RemoteImages.CanLoad);
    }

    [Fact]
    public async Task InboxBannerDrivesTheRealSessionWithoutADialog()
    {
        using var application = DemoApplication.Create(new(DemoScenario.LongHtml, Interactive: false));
        await application.InitializeAsync();
        var model = application.CreateViewModel();
        int calls = 0;
        using var inner = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new Handler((request, _) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(Response(request)); })) };
        using var htmlHost = new ImageMessageHost(inner);
        using var shell = new Broiler.Mail.Application.Views.MailShellView(model, htmlHost);
        var queue = new Queue();
        using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new WideHost()); shell.Attach(session);
        var keyboard = shell.CreateKeyboardNavigation(session);
        await model.Inbox.ReceiveAsync(); await model.Inbox.SelectAsync(model.Inbox.Messages.Single(message => message.Key.Uid == 54).Key);
        queue.Drain(); session.RenderFrame();
        var view = Assert.IsType<ScrollableHtmlView>(htmlHost.Surface);
        var inbox = shell.GetContent("inbox");
        var banner = Descendants(inbox).OfType<Broiler.Mail.Application.Preview.RemoteImageActions>().Single();
        Broiler.UI.Button.Standard.StandardButton Button(string text) => Descendants(banner).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == text);
        string Summary() => Descendants(banner).OfType<Broiler.UI.Label.Standard.StandardLabel>().First().Text;
        Assert.Equal(UiVisibility.Visible, banner.Visibility); Assert.Equal(0, calls);
        Assert.Equal("Remote images (2) are blocked to protect your privacy.", Summary());
        Assert.True(view.Bounds.Top >= banner.Bounds.Bottom - 1, $"{view.Bounds} overlaps the banner {banner.Bounds}.");
        // Zoom keys reach the HTML reader while focus is on the banner, as from the reader itself.
        var load = Button("Load images"); session.SetFocus(load);
        Assert.True(keyboard.Handle(Key(0xBB, Broiler.Input.Keyboard.KeyboardModifierState.Control))); Assert.Equal(1.1, view.Zoom);
        load.Click();
        for (int i = 0; i < 250 && view.RemoteImages.Loaded < 2; i++) { queue.Drain(); if (view.RemoteImages.Loaded < 2) await Task.Delay(20); }
        session.RenderFrame();
        Assert.Equal(2, view.RemoteImages.Loaded); Assert.Equal(2, calls);
        Assert.Null(shell.ActiveDialog); Assert.Equal("inbox", shell.ActiveViewId);
        var block = Button("Block again");
        Assert.Equal(UiVisibility.Visible, block.Visibility); Assert.Same(block, session.FocusedElement);
        block.Click(); queue.Drain(); session.RenderFrame();
        Assert.Contains("cannot be undone", Summary()); Assert.Equal(0, view.RemoteImages.Loaded); Assert.Equal(2, calls);
        Assert.Same(load, session.FocusedElement); Assert.Equal("Load images", load.Text);
        Assert.Null(shell.ActiveDialog); Assert.Equal("inbox", shell.ActiveViewId);
    }

    private static UiInputEvent Key(int code, Broiler.Input.Keyboard.KeyboardModifierState modifiers) => UiInputEvent.FromKeyboardKey(new Broiler.Input.Keyboard.KeyboardKeyEvent(
        new Broiler.Input.InputEventHeader(Broiler.Input.InputDeviceId.FromOpaqueValue("remote-image-banner"), new Broiler.Input.InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
        Broiler.Input.Keyboard.KeyboardKey.FromName("VirtualKey:" + code), Broiler.Input.Keyboard.KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
        Source: Broiler.Input.InputEventSource.Synthetic));
    private static IEnumerable<UiElement> Descendants(UiElement root)
    { foreach (var child in root.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    /// <summary>Shows every message as the two-remote-image fixture, through the real Windows preview host.</summary>
    private sealed class ImageMessageHost(WindowsHtmlPreviewHost inner) : Broiler.Mail.Application.Preview.IInlineHtmlPreviewHost, IDisposable
    {
        public UiElement? Surface;
        public UiElement CreateInlineContent(MailMessageBody message) => Surface = inner.CreateInlineContent(Message() with { Key = message.Key });
        public void Dispose() { }
    }
    private sealed class WideHost : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(1200, 800); public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(Broiler.Graphics.RenderList.BRenderList list) { }
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(Convert.FromBase64String(RemoteImageBrokerSmokeCheck.Pixel)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png"); return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class Queue : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public bool CheckAccess() => true; public void Post(Action action) => _queue.Enqueue(action);
        public void Drain() { while (_queue.TryDequeue(out var action)) action(); }
    }
    private sealed class Host : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(640, 480); public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(Broiler.Graphics.RenderList.BRenderList list) { }
    }
}
