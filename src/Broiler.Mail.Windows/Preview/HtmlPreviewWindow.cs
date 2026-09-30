// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   48
// Annotated:        48/48
// Exempt:           35
// Human-reviewed:   0/48
// IP risk:          Low
// Security risk:    High
// Criteria:         39/20
// Resource impact:  8/10 max
// Unverified:       48
//
// GENERATED - DO NOT EDIT MANUALLY

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
// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=222A47
// Broiler-Falsified-If: a link click in the preview hands the external browser a URL that is not in the current document's ExternalLinks set
// Broiler-Human:        PENDING
internal sealed class HtmlPreviewWindow : Direct2DWindow
{
    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=75D67B
    // Broiler-Falsified-If: a remote image body still arriving 10 seconds after its request started keeps being read, because the client Timeout stops applying once response headers are in
    // Broiler-Human:        PENDING
    private static readonly HttpClient ImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=1A8942
    // Broiler-Falsified-If: an embedded message image in the preview is decoded by a codec other than those ManagedImageCodecs.CreateCodecs returns, because an earlier registration made Use throw and the exception was swallowed
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=8A70C4
    // Broiler-Human:        PENDING
    internal nint InputHandle => RenderNativeHandle;

    public bool ShowInTaskbar { get; set; }
    public double Opacity { get; set; } = 1.0;
    public TaskCompletionSource<bool> Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? FailureDiagnostic { get; private set; }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=4495BA
    // Broiler-Human:        PENDING
    public event EventHandler? Shown;

    public bool IsShowingPlainText => _isShowingPlainText;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=401F88
    // Broiler-Human:        PENDING
    public bool AreRemoteImagesAllowed => _allowRemoteImages;
    public HtmlPreviewDocument Document => _document;

    internal void Post(Action action) => PostToUiThread(action);
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=2829D7
    // Broiler-Falsified-If: a CloseWindow call from the preview host's thread runs Close on that thread instead of queuing it to the preview window's own thread
    // Broiler-Human:        PENDING
    internal void CloseWindow() => PostToUiThread(Close);

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=6; Fingerprint=12DA3F
    // Broiler-Falsified-If: an ArgumentException from HtmlPreviewPolicy.Create inside LoadRemoteImagesAsync escapes the async Clicked handler of the Load remote images button and terminates the process
    // Broiler-Human:        PENDING
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

        CloseRequested += (_, _) => Close();
        Closed += (_, _) =>
        {
            Loaded.TrySetResult(false);
            PostQuitMessage(0);
        };
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=A44345
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=Low; Resources=0; Fingerprint=E0745E
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=8; Fingerprint=E8D147
    // Broiler-Falsified-If: an exception thrown by LoadRemoteImagesAsync is discarded while the status line already says that remote images were loaded
    // Broiler-Human:        PENDING
    public void LoadRemoteImages() => _ = LoadRemoteImagesAsync();

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=F31BD1
    // Broiler-Falsified-If: a remote image response larger than 5,000,000 bytes is read fully into memory before the size check discards it
    // Broiler-Human:        PENDING
    public async Task LoadRemoteImagesAsync()
    {
        if (_allowRemoteImages || string.IsNullOrEmpty(_rawHtml)) return;
        _allowRemoteImages = true;
        _loadImagesButton.IsEnabled = false;
        _loadImagesButton.Text = "Remote images loaded";
        _status.Text = "Remote images loaded. Scripts and active content remain blocked.";

        // Download bounded remote images safely into inlined data URIs
        var downloadedImages = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase);
        if (_embeddedImages is not null)
        {
            foreach (var kvp in _embeddedImages) downloadedImages[kvp.Key] = kvp.Value;
        }

        foreach (string url in _document.RemoteImageUrls)
        {
            if (HtmlPreviewPolicy.TryExternalLink(url, out var uri))
            {
                try
                {
                    using var response = await ImageHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                            !mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
                        {
                            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            if (bytes.Length <= 5_000_000)
                            {
                                downloadedImages[url] = new MailEmbeddedImage(url, mediaType, bytes);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // Recreate document with remote images allowed and inline data URIs substituted
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
            _htmlView.UpdateHtml(_document.Html);
            Invalidate();
        });
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=DA6972
    // Broiler-Falsified-If: a URL that is absent from the current document's ExternalLinks set, or arrives with userInitiated false, reaches the open-external callback
    // Broiler-Human:        PENDING
    public void OpenLink(string target, bool userInitiated)
    {
        if (!userInitiated || !HtmlPreviewPolicy.TryExternalLink(target, out var uri) || !_document.ExternalLinks.Contains(uri!.AbsoluteUri)) return;
        try { _openExternal(uri); }
        catch (Exception) { _status.Text = "The external browser could not be opened. The preview remains isolated."; }
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=Low; Resources=0; Fingerprint=EDF9B0
    // Broiler-Falsified-If: after ShowFailure the HTML view is still visible or the Load remote images button is still enabled
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=A4CEFC
    // Broiler-Falsified-If: an exception thrown by a queued UI callback escapes DrainDispatcher and aborts the frame being built or the posted-callback handler
    // Broiler-Human:        PENDING
    private void DrainDispatcher()
    {
        try { _dispatcher.Drain(); } catch { }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=A6A9C3
    // Broiler-Falsified-If: a frame is rendered before the host has taken the new client size, so the HTML snapshot is laid out at the previous width
    // Broiler-Human:        PENDING
    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        return _session.RenderFrame();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=3E34D4
    // Broiler-Human:        PENDING
    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        _host.Update(clientSize, dpiScale);
        Invalidate();
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=626D97
    // Broiler-Falsified-If: pressing a mouse button over the preview delivers no pointer-down input to the UI session, or delivers it as a pointer move
    // Broiler-Human:        PENDING
    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=EF3419
    // Broiler-Falsified-If: a pointer-up reaches the UI session with a position or button other than the operating system reported, so a link other than the one under the cursor is followed
    // Broiler-Human:        PENDING
    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=780407
    // Broiler-Falsified-If: moving the pointer delivers a button event to the UI session instead of a move, following a link the user did not click
    // Broiler-Human:        PENDING
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_input.FromPointerMove(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=13EC08
    // Broiler-Falsified-If: a wheel notch over the preview delivers no wheel input to the UI session, so the HTML view does not scroll
    // Broiler-Human:        PENDING
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_input.FromMouseWheel(e));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=C5177F
    // Broiler-Falsified-If: a key press reaches the UI session as a key-up transition
    // Broiler-Human:        PENDING
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Down));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=C41524
    // Broiler-Falsified-If: a key release reaches the UI session as a key-down transition, repeating the action of that key
    // Broiler-Human:        PENDING
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Up));
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=7E8B06
    // Broiler-Falsified-If: a character committed through WM_CHAR or the IME reaches the UI session a number of times other than once
    // Broiler-Human:        PENDING
    protected override void OnTextInput(BTextInputEventArgs e) => Dispatch(_input.FromText(e));

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=17558C
    // Broiler-Falsified-If: an input event the UI session reports as handled leaves the window without an invalidation, so the frame on screen stays stale
    // Broiler-Human:        PENDING
    private void Dispatch(UiInputEvent input)
    {
        if (_session.DispatchInput(input))
            Invalidate();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=EDEB0C
    // Broiler-Falsified-If: a GetMessage return of -1 is passed to TranslateMessage and DispatchMessage instead of ending the loop with a Win32Exception
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=A71644
    // Broiler-Falsified-If: the HTML container behind the preview is left undisposed after the window is disposed
    // Broiler-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Dispose();
            _htmlView.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=35E864
// Broiler-Falsified-If: a document shown through this view has an http(s) image or stylesheet it names fetched over the network instead of denied
// Broiler-Human:        PENDING
internal sealed class ScrollableHtmlView : UiElement
{
    private readonly StandardScrollView _scroll = new();
    private readonly HtmlViewElement _content;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=F1BC57
    // Broiler-Falsified-If: an http(s) image named in the html passed to the constructor is fetched over the network when the view renders
    // Broiler-Human:        PENDING
    public ScrollableHtmlView(string html, Func<IBroilerRenderer?> rendererProvider, Action<string> onLinkClicked)
    {
        _content = new HtmlViewElement(html, rendererProvider, onLinkClicked);
        _scroll.AddChild(_content);
        AddChild(_scroll);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=2D6686
    // Broiler-Falsified-If: an http(s) image named in the html passed to UpdateHtml is fetched over the network when the view next renders
    // Broiler-Human:        PENDING
    public void UpdateHtml(string html)
    {
        _content.UpdateHtml(html);
        _scroll.ScrollToStart();
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=AB27B5
    // Broiler-Human:        PENDING
    public void ScrollToStart() => _scroll.ScrollToStart();
    internal HtmlViewElement Content => _content;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=588444
    // Broiler-Falsified-If: an infinite or NaN available width sets a content width other than 800, or a width under 13 sets one below 1
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.ContentWidth = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - 12) : 800;
        return _scroll.Measure(availableSize);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=00F201
    // Broiler-Human:        PENDING
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

// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=9D4E0E
// Broiler-Falsified-If: rendering a document whose img src is an http(s) or file: URL sends a request for it instead of the load being denied
// Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=6; Fingerprint=EA9C6B
    // Broiler-Falsified-If: an img whose src is not a data: URL, or a linked stylesheet, is loaded by the container instead of being blocked by the ImageLoad and StylesheetLoad handlers
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=E5E400
    // Broiler-Falsified-If: an http(s) image named in the html passed to the constructor is fetched over the network when the element renders
    // Broiler-Human:        PENDING
    public HtmlViewElement(string html, Func<IBroilerRenderer?> rendererProvider, Action<string> onLinkClicked)
    {
        _html = html;
        _rendererProvider = rendererProvider;
        _onLinkClicked = onLinkClicked;
        _container = CreateContainer(_html);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=591A3A
    // Broiler-Falsified-If: after UpdateHtml a paint or link hit-test runs against the container that UpdateHtml disposed
    // Broiler-Human:        PENDING
    public void UpdateHtml(string html)
    {
        _html = html;
        _container.Dispose();
        _container = CreateContainer(_html);
        InvalidateImage();
        Invalidate(UiInvalidationKind.Measure);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=6565FC
    // Broiler-Falsified-If: the same snapshot image handle is passed to ReleaseImage twice
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=8C1F1D
    // Broiler-Falsified-If: text that sits directly in body after the last element is left out of the height, so a message of bare text is measured at the 100-pixel minimum and its snapshot is cut off
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=F7F59D
    // Broiler-Falsified-If: a message of 10,000 nested div elements, inside the policy's 20,000-token cap, overflows the preview thread's stack while it is measured
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        float targetWidth = (float)(double.IsFinite(availableSize.Width) && availableSize.Width > 50
            ? availableSize.Width
            : ContentWidth);
        float height = CalculateContentHeight(targetWidth);
        return new BSize(targetWidth, height);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=Medium; Resources=8; Fingerprint=0ADDDF
    // Broiler-Falsified-If: a document laid out taller than 8192 pixels produces a snapshot bitmap taller than 8192 rows
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=98C950
    // Broiler-Falsified-If: a repaint at an unchanged size with a valid cached snapshot still lays out the whole document again
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=2248F9
    // Broiler-Falsified-If: the link callback fires for an input other than a left-button release, such as a pointer-down, a right click or a key press over a link
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=578CC2
    // Broiler-Human:        PENDING
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

// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=B984F4
// Broiler-Falsified-If: a request the renderer sends through this transport, including one for a file: or loopback URL, is dispatched to the network or the file system
// Broiler-Human:        PENDING
internal sealed class DenyingRequestTransport : IBrowserRequestTransport
{
    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=5A1C67
    // Broiler-Falsified-If: Deny returns a response whose status is anything other than 403 Forbidden or whose body is not empty
    // Broiler-Human:        PENDING
    private static TransportResponse Deny(HttpRequestMessage request)
    {
        var resp = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { Content = new StringContent(string.Empty) };
        var urls = request.RequestUri is null ? Array.Empty<Uri>() : new[] { request.RequestUri };
        return new TransportResponse(resp, urls, default);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=D8BAB5
    // Broiler-Falsified-If: SendAsync completes with a response other than the empty 403 that Deny builds
    // Broiler-Human:        PENDING
    public Task<TransportResponse> SendAsync(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(Deny(request));
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=EF07AC
    // Broiler-Falsified-If: Send returns a response other than the empty 403 that Deny builds
    // Broiler-Human:        PENDING
    public TransportResponse Send(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken)
    {
        return Deny(request);
    }
}
