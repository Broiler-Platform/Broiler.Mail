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
using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;
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
        HtmlRuntime.Initialize();
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
    private readonly StandardLabel _truncationNotice;
    private readonly bool _dark;
    private bool _truncationShown;
    private readonly StandardButton _toggleButton;
    private readonly StandardButton _loadImagesButton;
    private readonly ScrollableMessageText _plainTextView;
    private readonly ScrollableHtmlView _htmlView;

    private bool _isShowingPlainText;
    private bool _allowRemoteImages;
    private bool _initialShownRaised;
    private CancellationTokenSource? _imageLoadCts;

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
        IReadOnlyDictionary<string, MailEmbeddedImage>? embeddedImages = null,
        bool dark = false,
        string? title = null)
        : base(new BWindowOptions
        {
            // The title names the message; it must be set here because the native window does not exist yet.
            Title = title ?? "Broiler.Mail — HTML preview",
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

        _dark = dark;
        _host = new WindowsUiHost(this, () => InputHandle);
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);

        // The shell's theme surface behind the header; without it, dark-theme text sat on the white window.
        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock, Background = StandardControlPaint.Surface };

        var header = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock, Spacing = 6 };
        _status = new StandardLabel
        {
            Text = "Simplified HTML. Images and active content are blocked. Selected HTTP(S) links open in your browser.",
            Wrapping = UiTextWrapping.Wrap,
            Foreground = StandardControlPaint.Text,
        };
        header.AddChild(_status);
        header.SetDock(_status, UiDock.Top);
        // Shown once layout finds the document longer than the render budget; the banner at the cut is far below.
        _truncationNotice = new StandardLabel
        {
            Text = "This long message is shortened in the HTML preview. Show plain text has all of it.",
            Wrapping = UiTextWrapping.Wrap, UseMnemonic = false, Role = StandardLabelRole.Muted,
            Visibility = UiVisibility.Collapsed,
        };
        header.AddChild(_truncationNotice);
        header.SetDock(_truncationNotice, UiDock.Top);

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

        _htmlView = new ScrollableHtmlView(_document.Html, () => Renderer, target => OpenLink(target, userInitiated: true), () => DpiScale);
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

    /// <summary>
    /// Last structured batch outcome from loading remote resources, providing detailed
    /// per-resource and overall status for diagnostics and testing.
    /// </summary>
    public HtmlResourceBatchResult? LastResourceBatchResult { get; private set; }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=F31BD1
    // Broiler-Falsified-If: a remote image response larger than 5,000,000 bytes is read fully into memory before the size check discards it
    // Broiler-Human:        PENDING
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

        try
        {
            var (batchResult, downloadedImages) = await HtmlResourceLoader.LoadBatchAsync(
                ImageHttpClient,
                _document.RemoteImageUrls,
                _embeddedImages,
                cancellationToken: token).ConfigureAwait(false);

            LastResourceBatchResult = batchResult;

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

                switch (batchResult.OverallOutcome)
                {
                    case HtmlResourceOutcomeKind.Rendered:
                        _allowRemoteImages = true;
                        _loadImagesButton.IsEnabled = false;
                        _loadImagesButton.Text = "Remote images loaded";
                        _status.Text = "Remote images loaded. Scripts and active content remain blocked.";
                        break;

                    case HtmlResourceOutcomeKind.Partial:
                        _allowRemoteImages = true;
                        _loadImagesButton.IsEnabled = true;
                        _loadImagesButton.Text = "Retry failed images";
                        _status.Text = $"Loaded {batchResult.TotalSucceeded} of {batchResult.TotalRequested} remote images. Some images failed or exceeded limits.";
                        break;

                    case HtmlResourceOutcomeKind.BudgetExceeded:
                        _loadImagesButton.IsEnabled = true;
                        _loadImagesButton.Text = "Retry remote images";
                        _status.Text = "Remote images exceeded size or dimension limits.";
                        break;

                    default:
                        _loadImagesButton.IsEnabled = true;
                        _loadImagesButton.Text = "Retry remote images";
                        _status.Text = "Failed to load remote images. Check your network connection and retry.";
                        break;
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
    protected override void OnCreated()
    {
        base.OnCreated();
        // Match the caption to the shell's theme; the handle exists only from here on.
        WindowsTitleBar.Apply(NativeHandle, _dark);
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        BRenderList? frame = _session.RenderFrame();
        if (!_truncationShown && _htmlView.Snapshot is { IsTruncated: true })
        {
            _truncationShown = true;
            _truncationNotice.Visibility = UiVisibility.Visible;
            Invalidate();
        }
        return frame;
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
        // Escape closes the preview unless a control used it first, for example to dismiss a menu.
        else if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down
            && input.NativeKeyCode == 0x1B)
            Close();
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
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            _session.Dispose();
            _htmlView.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed record HtmlBoxDiagnostic(
    string TagName,
    string? Id,
    string? ClassName,
    BRect BorderBox,
    BRect ContentBox);

internal sealed record HtmlLinkGeometry(
    string? Id,
    string Href,
    BRect Bounds);

internal sealed record HtmlLayoutSnapshot(
    float Width,
    float ContentHeight,
    float UnclampedHeight,
    bool IsTruncated,
    IReadOnlyList<HtmlLinkGeometry> Links,
    IReadOnlyList<HtmlBoxDiagnostic> Diagnostics,
    long LayoutDurationTicks)
{
    public TimeSpan LayoutDuration => TimeSpan.FromTicks(LayoutDurationTicks);
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
    public ScrollableHtmlView(
        string html,
        Func<IBroilerRenderer?> rendererProvider,
        Action<string> onLinkClicked,
        Func<double>? dpiScaleProvider = null)
    {
        _content = new HtmlViewElement(html, rendererProvider, onLinkClicked, dpiScaleProvider);
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
    public HtmlLayoutSnapshot? Snapshot => _content.Snapshot;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=588444
    // Broiler-Falsified-If: an infinite or NaN available width sets a content width other than 800, or a width under 13 sets one below 1
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.ContentWidth = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - 12) : 800;
        return _scroll.Measure(availableSize);
    }

    // Mail HTML is authored for a white page, and the renderer paints the document white; the canvas
    // stays white below a short document instead of showing the shell's (possibly dark) surface.
    protected override void RenderCore(UiRenderContext context)
    {
        context.RenderList.FillRect(Bounds, new BColor(255, 255, 255));
        base.RenderCore(context);
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
    public const int DefaultTileHeight = 1024;
    public const float MaxBudgetHeight = 32768f;
    public const int MaxCachedTiles = 16;
    // The count limit alone let memory grow with width and DPI: at 7680 DIPs and 300 % one tile is
    // about 280 MB, sixteen about 4.5 GB. A tile larger than this is rendered at a lower scale and
    // drawn stretched; the cache as a whole stays within the byte budget.
    public const long MaxTilePixels = 8L * 1024 * 1024;
    public const long MaxCachedTileBytes = 256L * 1024 * 1024;
    private const int BytesPerPixel = 4;

    private readonly Func<IBroilerRenderer?> _rendererProvider;
    private readonly Action<string> _onLinkClicked;
    private readonly Func<double>? _dpiScaleProvider;
    private readonly Dictionary<int, (BImageHandle Handle, int PixelWidth, int PixelHeight, double HeightDip)> _tiles = new();
    private readonly LinkedList<int> _lruTiles = new();
    private long _cachedTileBytes;

    private HtmlContainer _container;
    private HtmlLayoutSnapshot? _layoutSnapshot;
    private double _cachedDpiScale = 1.0;
    private string _html;

    public double ContentWidth { get; set; } = 800;

    public HtmlLayoutSnapshot? Snapshot => _layoutSnapshot;
    public int CachedTileCount => _tiles.Count;
    public long CachedTileBytes => _cachedTileBytes;
    internal IEnumerable<(int PixelWidth, int PixelHeight)> CachedTileSizes => _tiles.Values.Select(tile => (tile.PixelWidth, tile.PixelHeight)).ToArray();
    public bool IsTileCached(int tileIndex) => _tiles.ContainsKey(tileIndex);
    internal IReadOnlyCollection<int> CachedTileIndices => _tiles.Keys.ToArray();

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=6; Fingerprint=EA9C6B
    // Broiler-Falsified-If: an img whose src is not a data: URL, or a linked stylesheet, is loaded by the container instead of being blocked by the ImageLoad and StylesheetLoad handlers
    // Broiler-Human:        PENDING
    private static HtmlContainer CreateContainer(string html)
    {
        HtmlRuntime.Initialize();
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
    public HtmlViewElement(
        string html,
        Func<IBroilerRenderer?> rendererProvider,
        Action<string> onLinkClicked,
        Func<double>? dpiScaleProvider = null)
    {
        _html = html;
        _rendererProvider = rendererProvider;
        _onLinkClicked = onLinkClicked;
        _dpiScaleProvider = dpiScaleProvider;
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
        _layoutSnapshot = null;
        InvalidateTiles();
        Invalidate(UiInvalidationKind.Measure);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=6565FC
    // Broiler-Falsified-If: an allocated tile texture handle is leaked or passed to ReleaseImage after invalidation
    // Broiler-Human:        PENDING
    private void InvalidateTiles()
    {
        var renderer = _rendererProvider();
        foreach (var entry in _tiles.Values)
        {
            if (entry.Handle.IsValid && renderer is not null)
            {
                renderer.ReleaseImage(entry.Handle);
            }
        }
        _tiles.Clear();
        _lruTiles.Clear();
        _cachedTileBytes = 0;
    }

    /// <summary>
    /// The scale and pixel size a tile is rendered at: the display scale, lowered only when that would
    /// exceed <see cref="MaxTilePixels"/>. A capped size rounds down so it never passes the cap.
    /// </summary>
    internal static (double Scale, int Width, int Height) TilePixelSize(double widthDip, double heightDip, double dpiScale)
    {
        bool capped = widthDip * heightDip * dpiScale * dpiScale > MaxTilePixels;
        double scale = capped ? Math.Sqrt(MaxTilePixels / (widthDip * heightDip)) : dpiScale;
        Func<double, double> round = capped ? Math.Floor : Math.Ceiling;
        return (scale, (int)Math.Max(1, round(widthDip * scale)), (int)Math.Max(1, round(heightDip * scale)));
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=8C1F1D
    // Broiler-Falsified-If: layout geometry calculation ignores trailing content, leaving bare text or tail elements outside measured height
    // Broiler-Human:        PENDING
    internal HtmlLayoutSnapshot CalculateLayout(float width)
    {
        long start = Stopwatch.GetTimestamp();
        _container.MaxSize = new SizeF(width, 0);
        _container.PerformLayout();
        var geom = _container.GetLayoutGeometry(new SizeF(width, 0));

        float maxBottom = 0;
        var diagnostics = new List<HtmlBoxDiagnostic>(geom.Count);
        foreach (var (k, v) in geom)
        {
            diagnostics.Add(new HtmlBoxDiagnostic(
                k.TagName,
                k.Id,
                k.ClassName,
                new BRect(v.BorderBox.X, v.BorderBox.Y, v.BorderBox.Width, v.BorderBox.Height),
                new BRect(v.ContentBox.X, v.ContentBox.Y, v.ContentBox.Width, v.ContentBox.Height)));

            if (k.TagName != "html" && k.TagName != "body")
            {
                float bottom = v.BorderBox.Y + v.BorderBox.Height;
                if (bottom > maxBottom) maxBottom = bottom;
            }
        }

        float rawHeight = Math.Max(100f, maxBottom + 32f);
        bool isTruncated = rawHeight > MaxBudgetHeight;
        float contentHeight = isTruncated ? MaxBudgetHeight : rawHeight;

        var linksRaw = _container.GetLinks();
        var links = new List<HtmlLinkGeometry>(linksRaw.Count);
        foreach (var l in linksRaw)
        {
            links.Add(new HtmlLinkGeometry(
                l.Id,
                l.Href,
                new BRect(l.Rectangle.X, l.Rectangle.Y, l.Rectangle.Width, l.Rectangle.Height)));
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - start;
        return new HtmlLayoutSnapshot(width, contentHeight, rawHeight, isTruncated, links, diagnostics, elapsedTicks);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=F7F59D
    // Broiler-Falsified-If: repeated measures with unchanged width rerun the full layout instead of reusing the cached snapshot
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        float targetWidth = (float)(double.IsFinite(availableSize.Width) && availableSize.Width > 50
            ? availableSize.Width
            : ContentWidth);

        if (_layoutSnapshot is null || Math.Abs(_layoutSnapshot.Width - targetWidth) > 0.5f)
        {
            InvalidateTiles();
            _layoutSnapshot = CalculateLayout(targetWidth);
        }

        return new BSize(targetWidth, _layoutSnapshot.ContentHeight);
    }

    private void EnsureTile(int tileIndex, float targetWidth, double dpiScale, IBroilerRenderer renderer)
    {
        if (_tiles.ContainsKey(tileIndex))
        {
            _lruTiles.Remove(tileIndex);
            _lruTiles.AddFirst(tileIndex);
            return;
        }

        double tileTop = tileIndex * DefaultTileHeight;
        double tileH = Math.Min(DefaultTileHeight, _layoutSnapshot!.ContentHeight - tileTop);
        if (tileH <= 0) return;

        (double tileScale, int pixelW, int pixelH) = TilePixelSize(targetWidth, tileH, dpiScale);
        long tileBytes = (long)pixelW * pixelH * BytesPerPixel;
        while ((_tiles.Count >= MaxCachedTiles || _cachedTileBytes + tileBytes > MaxCachedTileBytes) && _lruTiles.Count > 0)
        {
            int lru = _lruTiles.Last!.Value;
            _lruTiles.RemoveLast();
            if (_tiles.Remove(lru, out var evicted))
            {
                _cachedTileBytes -= (long)evicted.PixelWidth * evicted.PixelHeight * BytesPerPixel;
                if (evicted.Handle.IsValid) renderer.ReleaseImage(evicted.Handle);
            }
        }

        // Rendered at the tile's own scale, which is the display scale unless the pixel cap lowered it.
        using var bitmap = new HtmlBitmap(pixelW, pixelH);
        _container.ViewportZoom = (float)tileScale;
        _container.ScrollOffset = new PointF(0, -(float)(tileTop * tileScale));
        _container.PerformPaint(bitmap, new RectangleF(0, 0, pixelW, pixelH));
        _container.ScrollOffset = PointF.Empty;
        _container.ViewportZoom = 1.0f;

        // The renderer keeps RGBA pixels. Encoding a PNG for it to decode straight back cost far more
        // than painting the tile; the pixels are identical either way.
        BImageHandle handle = renderer.CreateImage(bitmap.ToPixelBuffer());

        _tiles[tileIndex] = (handle, pixelW, pixelH, tileH);
        _lruTiles.AddFirst(tileIndex);
        _cachedTileBytes += tileBytes;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=98C950
    // Broiler-Falsified-If: rendering tiles outside the visible viewport window allocates bitmaps for non-visible regions
    // Broiler-Human:        PENDING
    protected override void RenderCore(UiRenderContext context)
    {
        float w = (float)Math.Max(1, Bounds.Width);
        if (_layoutSnapshot is null || Math.Abs(_layoutSnapshot.Width - w) > 0.5f)
        {
            InvalidateTiles();
            _layoutSnapshot = CalculateLayout(w);
        }

        double dpiScale = _dpiScaleProvider?.Invoke() ?? context.Host?.Scale ?? 1.0;
        if (dpiScale <= 0.1 || double.IsNaN(dpiScale)) dpiScale = 1.0;

        if (Math.Abs(_cachedDpiScale - dpiScale) > 0.001)
        {
            InvalidateTiles();
            _cachedDpiScale = dpiScale;
        }

        var renderer = _rendererProvider();
        if (renderer is null) return;

        double docHeight = _layoutSnapshot.ContentHeight;
        int totalTiles = (int)Math.Ceiling(docHeight / DefaultTileHeight);
        if (totalTiles <= 0) totalTiles = 1;

        double parentViewportHeight;
        double visibleTop;
        if (Parent is StandardScrollView sv && sv.Bounds.Height > 0)
        {
            visibleTop = Math.Max(0, sv.VerticalOffset);
            parentViewportHeight = sv.Bounds.Height;
        }
        else
        {
            parentViewportHeight = context.Host?.ViewportSize.Height > 0 ? context.Host.ViewportSize.Height : docHeight;
            visibleTop = Math.Max(0, -Bounds.Y);
        }

        if (parentViewportHeight <= 0) parentViewportHeight = docHeight;
        double visibleBottom = Math.Min(docHeight, visibleTop + parentViewportHeight);
        if (visibleBottom < visibleTop) visibleBottom = visibleTop;

        double bufferTop = Math.Max(0, visibleTop - 256);
        double bufferBottom = Math.Min(docHeight, visibleBottom + 256);

        int firstTile = Math.Clamp((int)(bufferTop / DefaultTileHeight), 0, totalTiles - 1);
        int lastTile = Math.Clamp((int)(bufferBottom / DefaultTileHeight), 0, totalTiles - 1);

        for (int i = firstTile; i <= lastTile; i++)
        {
            EnsureTile(i, w, dpiScale, renderer);
            if (_tiles.TryGetValue(i, out var tile) && tile.Handle.IsValid)
            {
                var srcRect = new BRect(0, 0, tile.PixelWidth, tile.PixelHeight);
                var destRect = new BRect(Bounds.X, Bounds.Y + (i * DefaultTileHeight), Bounds.Width, tile.HeightDip);
                context.RenderList.DrawImage(tile.Handle, srcRect, destRect, 1.0);
            }
        }

        if (_layoutSnapshot.IsTruncated)
        {
            double bannerHeight = 44;
            double bannerY = Bounds.Y + docHeight - bannerHeight;
            if (bannerY < (Bounds.Y + visibleBottom + 64) && (bannerY + bannerHeight) > (Bounds.Y + visibleTop - 64))
            {
                var bannerRect = new BRect(Bounds.X + 8, bannerY - 4, Math.Max(100, Bounds.Width - 16), bannerHeight);
                context.RenderList.FillRect(bannerRect, new BColor(255, 243, 205));
                context.RenderList.StrokeRect(bannerRect, new BColor(255, 220, 150), 1);
                context.RenderList.DrawText(
                    new BTextRun($"Content exceeds maximum render limit (truncated at {_layoutSnapshot.ContentHeight:N0}px).",
                        new BFontStyle("Segoe UI", 11),
                        new BColor(133, 100, 4)),
                    new BPoint(bannerRect.Left + 12, bannerRect.Top + 14));
            }
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

            if (_layoutSnapshot is not null)
            {
                foreach (var link in _layoutSnapshot.Links)
                {
                    if (link.Bounds.Contains(new BPoint(relX, relY)))
                    {
                        _onLinkClicked(link.Href);
                        return true;
                    }
                }
            }

            _container.ScrollOffset = PointF.Empty;
            string? linkUrl = _container.GetLinkAt(new PointF(relX, relY));
            if (!string.IsNullOrEmpty(linkUrl))
            {
                _onLinkClicked(linkUrl);
                return true;
            }
        }
        return base.OnInput(e);
    }

    internal bool SendInput(UiInputEvent e) => OnInput(e);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=578CC2
    // Broiler-Falsified-If: disposing the element leaves cached tile image handles or the container undisposed
    // Broiler-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            InvalidateTiles();
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
