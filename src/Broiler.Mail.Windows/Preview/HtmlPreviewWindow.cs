using System.ComponentModel;
using System.Drawing;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.HTML.Image;
using Broiler.Net.Http;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Hosting;
using Broiler.Media;
using Broiler.Media.Image.Managed;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using static Broiler.Native.Windows.WindowNative;
using HtmlBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Broiler.HTML preview window. Fully isolated with no scripts, active content, or external process dependencies.</summary>
internal sealed class HtmlPreviewWindow : Direct2DWindow
{
    private static readonly HttpClient ImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    static HtmlPreviewWindow()
    {
        try { BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs())); }
        catch (InvalidOperationException) { }
    }

    private readonly StandardQueuedUiDispatcher _dispatcher;
    private readonly UiSession _session;
    private readonly WindowsUiHost _host;
#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _input = new("broiler-mail-preview");
#pragma warning restore CS0618

    private HtmlPreviewDocument _document;
    private readonly string _plainText;
    private readonly Action<Uri> _openExternal;
    private readonly string? _rawHtml;
    private readonly IReadOnlyDictionary<string, MailEmbeddedImage>? _embeddedImages;

    private readonly StandardLabel _status;
    private readonly StandardButton _toggleButton;
    private readonly StandardButton _loadImagesButton;
    private readonly ScrollableMessageText _plainTextView;
    private readonly ScrollableHtmlView _htmlView;

    private bool _isShowingPlainText;
    private bool _allowRemoteImages;
    private bool _initialShownRaised;
    private CancellationTokenSource? _imageLoadCts;

    internal nint InputHandle => RenderNativeHandle;

    public bool ShowInTaskbar { get; set; }
    public double Opacity { get; set; } = 1.0;
    public TaskCompletionSource<bool> Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? FailureDiagnostic { get; private set; }
    public event EventHandler? Shown;

    public bool IsShowingPlainText => _isShowingPlainText;
    public bool AreRemoteImagesAllowed => _allowRemoteImages;
    public HtmlPreviewDocument Document => _document;

    internal void Post(Action action) => PostToUiThread(action);
    internal void CloseWindow() => PostToUiThread(Close);

    public HtmlPreviewWindow(
        HtmlPreviewDocument document,
        string plainText,
        Action<Uri> openExternal,
        string? rawHtml = null,
        IReadOnlyDictionary<string, MailEmbeddedImage>? embeddedImages = null)
        : base(new BWindowOptions
        {
            Title = "Broiler.Mail — HTML preview",
            ClientWidth = 900,
            ClientHeight = 700,
            OwnsMessageLoop = false,
            RenderOptions = new BRenderOptions(
                Antialias: true,
                VSync: true,
                SubpixelText: true),
        })
    {
        _document = document;
        _plainText = plainText;
        _openExternal = openExternal;
        _rawHtml = rawHtml;
        _embeddedImages = embeddedImages;

        _host = new WindowsUiHost(this, () => InputHandle);
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);

        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };

        var header = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock, Spacing = 6 };
        _status = new StandardLabel
        {
            Text = "Simplified HTML. Images and active content are blocked. Selected HTTP(S) links open in your browser.",
            Wrapping = UiTextWrapping.Wrap,
            Foreground = StandardControlPaint.Text,
        };
        header.AddChild(_status);
        header.SetDock(_status, UiDock.Top);

        var toolbar = new StandardPanel { StackOrientation = UiStackOrientation.Horizontal, Spacing = 8 };
        _toggleButton = new StandardButton { Text = "Show plain text" };
        _toggleButton.Clicked += (_, _) => ToggleView();
        toolbar.AddChild(_toggleButton);

        _loadImagesButton = new StandardButton { Text = "Load remote images" };
        _loadImagesButton.Clicked += async (_, _) => await LoadRemoteImagesAsync();
        if (_document.RemoteImageUrls.Count > 0 && !string.IsNullOrEmpty(_rawHtml))
        {
            toolbar.AddChild(_loadImagesButton);
        }

        header.AddChild(toolbar);
        header.SetDock(toolbar, UiDock.Bottom);

        root.AddChild(header);
        root.SetDock(header, UiDock.Top);

        var content = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };

        _plainTextView = new ScrollableMessageText
        {
            Text = _plainText,
            Visibility = UiVisibility.Collapsed,
        };
        content.AddChild(_plainTextView);

        _htmlView = new ScrollableHtmlView(_document.Html, () => Renderer, target => OpenLink(target, userInitiated: true));
        content.AddChild(_htmlView);

        root.AddChild(content);
        _session.AddRoot(root);

        CloseRequested += (_, _) =>
        {
            _imageLoadCts?.Cancel();
            Close();
        };
        Closed += (_, _) =>
        {
            _imageLoadCts?.Cancel();
            Loaded.TrySetResult(false);
            PostQuitMessage(0);
        };
    }

    public Task InitializeAsync(string? profileDirectory = null)
    {
        try
        {
            Loaded.TrySetResult(true);
        }
        catch (Exception error)
        {
            FailureDiagnostic = error.ToString();
            ShowFailure();
        }
        return Task.CompletedTask;
    }

    public void ToggleView()
    {
        if (_isShowingPlainText)
        {
            _plainTextView.Visibility = UiVisibility.Collapsed;
            _htmlView.Visibility = UiVisibility.Visible;
            _isShowingPlainText = false;
            _toggleButton.Text = "Show plain text";
            _status.Text = _allowRemoteImages
                ? "Showing HTML preview. Remote images loaded; scripts and active content remain blocked."
                : "Simplified HTML. Images and active content are blocked. Selected HTTP(S) links open in your browser.";
        }
        else
        {
            _htmlView.Visibility = UiVisibility.Collapsed;
            _plainTextView.Visibility = UiVisibility.Visible;
            _isShowingPlainText = true;
            _toggleButton.Text = "Show HTML";
            _status.Text = "Showing plain text view.";
        }
        Invalidate();
    }

    public void LoadRemoteImages() => _ = LoadRemoteImagesAsync();

    public async Task LoadRemoteImagesAsync()
    {
        if (_allowRemoteImages || string.IsNullOrEmpty(_rawHtml)) return;

        _imageLoadCts?.Cancel();
        _imageLoadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _imageLoadCts = cts;
        var token = cts.Token;

        _loadImagesButton.IsEnabled = false;
        _loadImagesButton.Text = "Loading images…";
        _status.Text = "Loading remote images… Scripts and active content remain blocked.";
        Invalidate();

        var downloadedImages = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase);
        if (_embeddedImages is not null)
        {
            foreach (var kvp in _embeddedImages) downloadedImages[kvp.Key] = kvp.Value;
        }

        const int maxImageBytes = 5_000_000; // 5 MB per image limit
        const int maxTotalImageBytes = 20_000_000; // 20 MB total message budget
        long totalDownloadedBytes = 0;
        int totalRemote = _document.RemoteImageUrls.Count;
        int succeeded = 0;
        int failed = 0;

        try
        {
            foreach (string url in _document.RemoteImageUrls)
            {
                token.ThrowIfCancellationRequested();

                if (downloadedImages.ContainsKey(url))
                {
                    succeeded++;
                    continue;
                }

                if (!HtmlPreviewPolicy.TryExternalLink(url, out var uri) || uri is null)
                {
                    failed++;
                    continue;
                }

                try
                {
                    using var response = await ImageHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        failed++;
                        continue;
                    }

                    var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                        mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        continue;
                    }

                    if (response.Content.Headers.ContentLength is { } declaredLength && (declaredLength > maxImageBytes || totalDownloadedBytes + declaredLength > maxTotalImageBytes))
                    {
                        failed++;
                        continue;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    byte[] chunk = new byte[8192];
                    int bytesRead;
                    bool exceeded = false;
                    while ((bytesRead = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + bytesRead > maxImageBytes || totalDownloadedBytes + buffer.Length + bytesRead > maxTotalImageBytes)
                        {
                            exceeded = true;
                            break;
                        }
                        buffer.Write(chunk, 0, bytesRead);
                    }

                    if (exceeded || buffer.Length == 0)
                    {
                        failed++;
                        continue;
                    }

                    byte[] imageBytes = buffer.ToArray();
                    totalDownloadedBytes += imageBytes.Length;
                    downloadedImages[url] = new MailEmbeddedImage(url, mediaType, imageBytes);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    failed++;
                }
            }

            token.ThrowIfCancellationRequested();

            string updatedHtml = _rawHtml;
            foreach (var (url, img) in downloadedImages)
            {
                if (!url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    string dataUri = $"data:{img.ContentType};base64,{Convert.ToBase64String(img.Data)}";
                    updatedHtml = updatedHtml.Replace(url, dataUri, StringComparison.OrdinalIgnoreCase);
                }
            }

            _document = HtmlPreviewPolicy.Create(updatedHtml, downloadedImages, allowRemoteImages: true);

            PostToUiThread(() =>
            {
                if (IsDisposed || token.IsCancellationRequested) return;

                if (succeeded > 0 && failed == 0)
                {
                    _allowRemoteImages = true;
                    _loadImagesButton.IsEnabled = false;
                    _loadImagesButton.Text = "Remote images loaded";
                    _status.Text = "Remote images loaded. Scripts and active content remain blocked.";
                }
                else if (succeeded > 0)
                {
                    _allowRemoteImages = true;
                    _loadImagesButton.IsEnabled = true;
                    _loadImagesButton.Text = "Retry failed images";
                    _status.Text = $"Loaded {succeeded} of {totalRemote} remote images. Some images failed to load.";
                }
                else
                {
                    _loadImagesButton.IsEnabled = true;
                    _loadImagesButton.Text = "Retry remote images";
                    _status.Text = "Failed to load remote images. Check your network connection and retry.";
                }

                _htmlView.UpdateHtml(_document.Html);
                Invalidate();
            });
        }
        catch (OperationCanceledException)
        {
            PostToUiThread(() =>
            {
                if (IsDisposed) return;
                _loadImagesButton.IsEnabled = true;
                _loadImagesButton.Text = "Load remote images";
                _status.Text = "Remote image loading canceled.";
                Invalidate();
            });
        }
    }

    public void OpenLink(string target, bool userInitiated)
    {
        if (!userInitiated || !HtmlPreviewPolicy.TryExternalLink(target, out var uri) || !_document.ExternalLinks.Contains(uri!.AbsoluteUri)) return;
        try { _openExternal(uri); }
        catch (Exception) { _status.Text = "The external browser could not be opened. The preview remains isolated."; }
    }

    public void ShowFailure()
    {
        if (IsDisposed) return;
        Loaded.TrySetResult(false);
        _htmlView.Visibility = UiVisibility.Collapsed;
        _plainTextView.Visibility = UiVisibility.Visible;
        _isShowingPlainText = true;
        _toggleButton.IsEnabled = false;
        _loadImagesButton.IsEnabled = false;
        _status.Text = "HTML preview unavailable. Showing text. Close this window to continue reading.";
        Invalidate();
    }

    private void DrainDispatcher()
    {
        try { _dispatcher.Drain(); } catch { }
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        return _session.RenderFrame();
    }

    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        _host.Update(clientSize, dpiScale);
        Invalidate();
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
        if (_session.DispatchInput(input))
            Invalidate();
    }

    protected override int RunCore()
    {
        Show();
        if (!_initialShownRaised)
        {
            _initialShownRaised = true;
            Shown?.Invoke(this, EventArgs.Empty);
        }
        while (true)
        {
            int result = GetMessage(out MSG message, nint.Zero, 0, 0);
            if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (result == 0) return unchecked((int)message.WParam);
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            _session.Dispose();
            _htmlView.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ScrollableHtmlView : UiElement
{
    private readonly StandardScrollView _scroll = new();
    private readonly HtmlViewElement _content;

    public ScrollableHtmlView(string html, Func<IBroilerRenderer?> rendererProvider, Action<string> onLinkClicked)
    {
        _content = new HtmlViewElement(html, rendererProvider, onLinkClicked);
        _scroll.AddChild(_content);
        AddChild(_scroll);
    }

    public void UpdateHtml(string html)
    {
        _content.UpdateHtml(html);
        _scroll.ScrollToStart();
    }

    public void ScrollToStart() => _scroll.ScrollToStart();
    internal HtmlViewElement Content => _content;

    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.ContentWidth = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - 12) : 800;
        return _scroll.Measure(availableSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
            _scroll.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class HtmlViewElement : UiElement
{
    private readonly Func<IBroilerRenderer?> _rendererProvider;
    private readonly Action<string> _onLinkClicked;
    private HtmlContainer _container;
    private BImageHandle _imageHandle = BImageHandle.Invalid;
    private int _renderedWidth;
    private int _renderedHeight;
    private string _html;

    public double ContentWidth { get; set; } = 800;

    private static HtmlContainer CreateContainer(string html)
    {
        var container = new HtmlContainer();
        container.RequestTransport = new DenyingRequestTransport();
        container.StylesheetLoad += (_, e) => e.SetStyleSheet = string.Empty;
        container.ImageLoad += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Src) && !e.Src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                e.Handled = true;
            }
        };
        container.SetHtmlWithStyleSet(html, null, null);
        return container;
    }

    public HtmlViewElement(string html, Func<IBroilerRenderer?> rendererProvider, Action<string> onLinkClicked)
    {
        _html = html;
        _rendererProvider = rendererProvider;
        _onLinkClicked = onLinkClicked;
        _container = CreateContainer(_html);
    }

    public void UpdateHtml(string html)
    {
        _html = html;
        _container.Dispose();
        _container = CreateContainer(_html);
        InvalidateImage();
        Invalidate(UiInvalidationKind.Measure);
    }

    private void InvalidateImage()
    {
        if (_imageHandle.IsValid)
        {
            _rendererProvider()?.ReleaseImage(_imageHandle);
            _imageHandle = BImageHandle.Invalid;
        }
        _renderedWidth = 0;
        _renderedHeight = 0;
    }

    private float CalculateContentHeight(float width)
    {
        _container.MaxSize = new SizeF(width, 0);
        _container.PerformLayout();
        var geom = _container.GetLayoutGeometry(new SizeF(width, 0));
        float maxBottom = 0;
        foreach (var (k, v) in geom)
        {
            if (k.TagName != "html" && k.TagName != "body")
            {
                float bottom = v.BorderBox.Y + v.BorderBox.Height;
                if (bottom > maxBottom) maxBottom = bottom;
            }
        }
        return Math.Max(100f, maxBottom + 32f);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        float targetWidth = (float)(double.IsFinite(availableSize.Width) && availableSize.Width > 50
            ? availableSize.Width
            : ContentWidth);
        float height = CalculateContentHeight(targetWidth);
        return new BSize(targetWidth, height);
    }

    private void EnsureRendered(int width, int height)
    {
        int contentHeight = (int)Math.Ceiling(CalculateContentHeight(width));
        int renderHeight = Math.Clamp(Math.Max(contentHeight, height), 100, 8192);

        if (_imageHandle.IsValid && _renderedWidth == width && _renderedHeight == renderHeight)
            return;

        var renderer = _rendererProvider();
        if (renderer is null) return;

        InvalidateImage();

        _container.MaxSize = new SizeF(width, renderHeight);
        _container.PerformLayout();

        using var bitmap = new HtmlBitmap(width, renderHeight);
        _container.PerformPaint(bitmap, new RectangleF(0, 0, width, renderHeight));
        byte[] png = bitmap.Encode(Broiler.Media.Image.ImageEncodeFormat.Png);
        _imageHandle = renderer.CreateImage(png);
        _renderedWidth = width;
        _renderedHeight = renderHeight;
    }

    protected override void RenderCore(UiRenderContext context)
    {
        int w = (int)Math.Max(1, Bounds.Width);
        int h = (int)Math.Max(1, Bounds.Height);
        EnsureRendered(w, h);
        if (_imageHandle.IsValid)
        {
            var destRect = new BRect(Bounds.X, Bounds.Y, _renderedWidth, _renderedHeight);
            context.RenderList.DrawImage(_imageHandle, new BRect(0, 0, _renderedWidth, _renderedHeight), destRect, 1.0);
        }
    }

    protected override bool OnInput(UiInputEvent e)
    {
        if (e.Kind == UiInputEventKind.PointerButton && e.MouseButton == MouseButton.Left && e.MouseButtonTransition == MouseButtonTransition.Up)
        {
            float relX = (float)(e.Position.X - Bounds.X);
            float relY = (float)(e.Position.Y - Bounds.Y);
            string? link = _container.GetLinkAt(new PointF(relX, relY));
            if (!string.IsNullOrEmpty(link))
            {
                _onLinkClicked(link);
                return true;
            }
        }
        return base.OnInput(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            InvalidateImage();
            _container.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class DenyingRequestTransport : IBrowserRequestTransport
{
    private static TransportResponse Deny(HttpRequestMessage request)
    {
        var resp = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { Content = new StringContent(string.Empty) };
        var urls = request.RequestUri is null ? Array.Empty<Uri>() : new[] { request.RequestUri };
        return new TransportResponse(resp, urls, default);
    }

    public Task<TransportResponse> SendAsync(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(Deny(request));
    }

    public TransportResponse Send(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken)
    {
        return Deny(request);
    }
}
