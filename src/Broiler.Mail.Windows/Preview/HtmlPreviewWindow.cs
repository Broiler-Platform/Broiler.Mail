// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   160
// Annotated:        48/160
// Exempt:           125
// Human-reviewed:   0/160
// IP risk:          Low
// Security risk:    High
// Criteria:         39/20
// Resource impact:  8/10 max
// Unverified:       160
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
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.HTML.Image;
using Broiler.Layout.IR;
using Broiler.Mail.Core.Diagnostics;
using Broiler.Net.Http;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Measurement;
using Broiler.Media;
using Broiler.Media.Image.Managed;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using static Broiler.Native.Windows.WindowNative;
using HtmlBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Broiler.HTML preview window. Fully isolated with no scripts, active content, or external process dependencies.</summary>
// Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=222A47
// Hairs-Falsified-If: a link click in the preview hands the external browser a URL that is not in the current document's ExternalLinks set
// Hairs-Human:        PENDING
internal sealed class HtmlPreviewWindow : Direct2DWindow
{
    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=75D67B
    // Hairs-Falsified-If: a remote image body still arriving 10 seconds after its request started keeps being read, because the client Timeout stops applying once response headers are in
    // Hairs-Human:        PENDING
    private static readonly HttpClient ImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=AD9575
    // Hairs-Falsified-If: an embedded message image in the preview is decoded by a codec other than those ManagedImageCodecs.CreateCodecs returns, because an earlier registration made Use throw and the exception was swallowed
    // Hairs-Human:        PENDING
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
    private bool _dark;
    private bool _truncationShown;
    private readonly StandardButton _toggleButton;
    private readonly StandardButton _loadImagesButton;
    private readonly StandardButton _zoomOutButton;
    private readonly StandardButton _zoomResetButton;
    private readonly StandardButton _zoomInButton;
    private readonly PreviewZoomWheel _zoomWheel = new();
    // The system text size: a preview opens at it, and Reset returns to it.
    private double _defaultZoom = 1;
    // Until the reader zooms, the preview follows the system text size as it changes.
    private bool _zoomFollowsSystem = true;
    private string? _zoomAnnouncement;
    private readonly object _themeGate = new();
    private StandardThemeTokens? _pendingTheme;
    private readonly ScrollableMessageText _plainTextView;
    private readonly ScrollableHtmlView _htmlView;
    private readonly StandardPanel _root;
    private WindowsAutomationBridge? _automationBridge;
    internal WindowsAutomationBridge? AutomationBridge => _automationBridge;

    private bool _isShowingPlainText;
    private bool _allowRemoteImages;
    private bool _initialShownRaised;
    private CancellationTokenSource? _imageLoadCts;
    // UI-12: a --measure run's recorder and tile counters, and the simulated scale it renders the preview at.
    private readonly PreviewMeasurement? _measurement;
    private const int DefaultClientWidth = 900, DefaultClientHeight = 700;

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=8A70C4
    // Hairs-Human:        PENDING
    internal nint InputHandle => RenderNativeHandle;

    public bool ShowInTaskbar { get; set; }
    public double Opacity { get; set; } = 1.0;
    public TaskCompletionSource<bool> Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? FailureDiagnostic { get; private set; }
    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=4495BA
    // Hairs-Human:        PENDING
    public event EventHandler? Shown;

    public bool IsShowingPlainText => _isShowingPlainText;
    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=401F88
    // Hairs-Human:        PENDING
    public bool AreRemoteImagesAllowed => _allowRemoteImages;
    public HtmlPreviewDocument Document => _document;

    internal void Post(Action action) => PostToUiThread(action);
    /// <summary>UI-12: queues <paramref name="action"/> to the preview's thread; false once the window is closing.</summary>
    internal bool RunOnUiThread(Action action) => PostToUiThread(action);
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=36E190
    // Hairs-Falsified-If: a CloseWindow call from the preview host's thread runs Close on that thread instead of queuing it to the preview window's own thread
    // Hairs-Human:        PENDING
    internal void CloseWindow()
    {
        _imageLoadCts?.Cancel();
        PostToUiThread(() =>
        {
            _session.SetFocus(null);
            Close();
        });
    }

    /// <summary>
    /// Re-themes the open preview, from any thread: the header, buttons, plain text, and caption follow
    /// the shell's new palette and text size, as the main window does. The HTML page keeps its own
    /// white canvas; its zoom follows the new text size unless the reader has zoomed.
    /// </summary>
    internal void ApplyTheme(StandardThemeTokens theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        // Only the newest theme is applied. Nothing can be posted before the native window exists; a
        // theme that arrives then waits for OnCreated instead of being dropped.
        lock (_themeGate) _pendingTheme = theme;
        PostToUiThread(ApplyPendingTheme);
    }

    private void ApplyPendingTheme()
    {
        StandardThemeTokens? theme;
        lock (_themeGate) (theme, _pendingTheme) = (_pendingTheme, null);
        if (theme is null || IsDisposed) return;
        ApplyThemeToSession(theme);
        if (NativeHandle != 0) WindowsTitleBar.ApplyDarkMode(NativeHandle, _dark);
        Invalidate();
    }

    private void ApplyThemeToSession(StandardThemeTokens theme)
    {
        // The session and its controls only: the main window has already set the process-wide
        // palette, and a theme queued here earlier must not overwrite a newer one there.
        StandardControlPaint.SetSessionTheme(_session, theme);
        StandardThemeController.ApplyToSubtree(_root, theme);
        // The panel is not a themed control; its surface was taken from the theme at creation.
        _root.Background = theme.Surface;
        _dark = theme.IsDark;
        FollowTextSize(theme.TextScale);
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=6; Fingerprint=6E7FBD
    // Hairs-Falsified-If: an ArgumentException from HtmlPreviewPolicy.Create inside LoadRemoteImagesAsync escapes the async Clicked handler of the Load remote images button and terminates the process
    // Hairs-Human:        PENDING
    public HtmlPreviewWindow(
        HtmlPreviewDocument document,
        string plainText,
        Action<Uri> openExternal,
        string? rawHtml = null,
        IReadOnlyDictionary<string, MailEmbeddedImage>? embeddedImages = null,
        StandardThemeTokens? theme = null,
        string? title = null,
        PreviewMeasurement? measurement = null)
        : base(new BWindowOptions
        {
            // The title names the message; it must be set here because the native window does not exist yet.
            Title = TitleAtScale(title ?? "Broiler.Mail — HTML preview", measurement),
            ClientWidth = DefaultClientWidth,
            ClientHeight = DefaultClientHeight,
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
        _measurement = measurement;
        MailLogger.Info("HtmlPreview", $"HtmlPreviewWindow opened. Document: Embedded={_embeddedImages?.Count ?? 0}, RemoteUrls={_document.RemoteImageUrls.Count}, HasEmbeddedImages={_document.HasEmbeddedImages}.");

        _host = new WindowsUiHost(this, () => InputHandle);
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);
        // Nothing in the preview draws a composition, so its window takes no IME while it has focus.
        _host.TrackFocus(_session);

        // The shell's theme surface behind the header; without it, dark-theme text sat on the white window.
        var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock, Background = StandardControlPaint.Surface };
        _root = root;

        var header = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock, Spacing = 6 };
        _status = new StandardLabel
        {
            Text = "Simplified HTML. Images and active content are blocked. Selected HTTP(S) links open in your browser.",
            Wrapping = UiTextWrapping.Wrap,
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

        // Wraps onto another row in a narrow window or at a large text size instead of clipping its buttons.
        var toolbar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 0, Spacing = 8, PreferredSize = new BSize(0, 36) };
        _toggleButton = new StandardButton { Text = "Show plain text" };
        _toggleButton.Clicked += (_, _) => ToggleView();
        toolbar.AddChild(_toggleButton);

        // One zoom for the HTML and the plain text: Zoom out, the current level (which resets it), Zoom in.
        _zoomOutButton = new StandardButton { Text = "Zoom out" };
        _zoomOutButton.Clicked += (_, _) => StepZoom(-1);
        _zoomResetButton = new StandardButton();
        _zoomResetButton.Clicked += (_, _) => ResetZoom();
        _zoomInButton = new StandardButton { Text = "Zoom in" };
        _zoomInButton.Clicked += (_, _) => StepZoom(1);
        foreach (var button in new[] { _zoomOutButton, _zoomResetButton, _zoomInButton }) toolbar.AddChild(button);

        _loadImagesButton = new StandardButton { Text = "Load remote images" };
        _loadImagesButton.Clicked += async (_, _) => await LoadRemoteImagesAsync();
        if (_document.RemoteImageUrls.Count > 0 && !string.IsNullOrEmpty(_rawHtml))
        {
            toolbar.AddChild(_loadImagesButton);
        }

        header.AddChild(toolbar);
        header.SetDock(toolbar, UiDock.Bottom);

        // The same margins and line length as the reader header in the main window.
        var headerColumn = new ReadingColumn(header, verticalMargin: 8);
        root.AddChild(headerColumn);
        root.SetDock(headerColumn, UiDock.Top);

        var content = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };

        _plainTextView = new ScrollableMessageText
        {
            Text = _plainText,
            Visibility = UiVisibility.Collapsed,
        };
        content.AddChild(_plainTextView);

        // Only links the policy would open get a keyboard target; the others do nothing when clicked either.
        _htmlView = new ScrollableHtmlView(_document.Html, () => Renderer, target => OpenLink(target, userInitiated: true), () => DpiScale,
            target => HtmlPreviewPolicy.TryExternalLink(target, out var uri) && _document.ExternalLinks.Contains(uri!.AbsoluteUri));
        _htmlView.Content.Statistics = measurement?.Tiles;
        content.AddChild(_htmlView);

        root.AddChild(content);
        _session.AddRoot(root);

        // The preview opens at the system text size, so the document's text grows with the shell's.
        // Without a theme, the session's own (the process-wide palette) supplies it.
        if (theme is not null) ApplyThemeToSession(theme);
        var initialTheme = StandardControlPaint.GetTheme(_session);
        _dark = initialTheme.IsDark;
        _defaultZoom = PreviewZoom.Clamp(initialTheme.TextScale);
        ApplyZoom(_defaultZoom);

        // The document starts focused, so the keyboard scrolls it at once; Tab reaches the buttons and links.
        _session.SetFocus(_htmlView.Content);

        CloseRequested += (_, _) =>
        {
            _imageLoadCts?.Cancel();
            _session.SetFocus(null);
            PostToUiThread(Close);
        };
        Closed += (_, _) =>
        {
            _imageLoadCts?.Cancel();
            Loaded.TrySetResult(false);
            PostQuitMessage(0);
        };
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=A44345
    // Hairs-Human:        PENDING
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

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=Low; Resources=0; Fingerprint=4A1377
    // Hairs-Human:        PENDING
    public void ToggleView()
    {
        if (_isShowingPlainText)
        {
            _plainTextView.Visibility = UiVisibility.Collapsed;
            _htmlView.Visibility = UiVisibility.Visible;
            _isShowingPlainText = false;
            _toggleButton.Text = "Show plain text";
            KeepFocusOutOf(_plainTextView, _htmlView.Content);
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
            KeepFocusOutOf(_htmlView, _plainTextView.Editor);
            _status.Text = "Showing plain text view.";
        }
        Invalidate();
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=8; Fingerprint=E8D147
    // Hairs-Falsified-If: an exception thrown by LoadRemoteImagesAsync is discarded while the status line already says that remote images were loaded
    // Hairs-Human:        PENDING
    public void LoadRemoteImages() => _ = LoadRemoteImagesAsync();

    /// <summary>
    /// Last structured batch outcome from loading remote resources, providing detailed
    /// per-resource and overall status for diagnostics and testing.
    /// </summary>
    public HtmlResourceBatchResult? LastResourceBatchResult { get; private set; }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=C2B077
    // Hairs-Falsified-If: a remote image response larger than 5,000,000 bytes is read fully into memory before the size check discards it
    // Hairs-Human:        PENDING
    public async Task LoadRemoteImagesAsync()
    {
        if (_allowRemoteImages || string.IsNullOrEmpty(_rawHtml)) return;

        MailLogger.Info("HtmlPreview", $"LoadRemoteImagesAsync initiated for {_document.RemoteImageUrls.Count} remote images.");

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

            MailLogger.Info("HtmlPreview", $"LoadBatchAsync finished: Outcome={batchResult.OverallOutcome}, Succeeded={batchResult.TotalSucceeded}/{batchResult.TotalRequested}, Bytes={batchResult.TotalDownloadedBytes:N0}.");

            string updatedHtml = _rawHtml;
            foreach (var (url, img) in downloadedImages)
            {
                if (!url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    string dataUri = $"data:{img.ContentType};base64,{Convert.ToBase64String(img.Data)}";
                    updatedHtml = updatedHtml.Replace(url, dataUri, StringComparison.OrdinalIgnoreCase);
                }
            }

            var loaded = HtmlPreviewPolicy.Create(updatedHtml, downloadedImages, allowRemoteImages: true);

            PostToUiThread(() =>
            {
                if (IsDisposed || token.IsCancellationRequested) return;
                // The document is swapped here, on the UI thread and together with the HTML it describes:
                // OpenLink reads it there, and a load that was cancelled or outlived the window must not replace it.
                _document = loaded;

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
            MailLogger.Info("HtmlPreview", "Remote image loading was canceled.");
            PostToUiThread(() =>
            {
                if (IsDisposed) return;
                _loadImagesButton.IsEnabled = true;
                _loadImagesButton.Text = "Load remote images";
                _status.Text = "Remote image loading canceled.";
                Invalidate();
            });
        }
        catch (Exception ex)
        {
            MailLogger.Error("HtmlPreview", "Failed to load remote images due to unexpected error.", ex);
            PostToUiThread(() =>
            {
                if (IsDisposed) return;
                _loadImagesButton.IsEnabled = true;
                _loadImagesButton.Text = "Retry remote images";
                _status.Text = "Failed to load remote images. Check your network connection and retry.";
                Invalidate();
            });
        }
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=DA6972
    // Hairs-Falsified-If: a URL that is absent from the current document's ExternalLinks set, or arrives with userInitiated false, reaches the open-external callback
    // Hairs-Human:        PENDING
    public void OpenLink(string target, bool userInitiated)
    {
        if (!userInitiated || !HtmlPreviewPolicy.TryExternalLink(target, out var uri) || !_document.ExternalLinks.Contains(uri!.AbsoluteUri)) return;
        try { _openExternal(uri); }
        catch (Exception) { _status.Text = "The external browser could not be opened. The preview remains isolated."; }
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=Low; Resources=0; Fingerprint=534967
    // Hairs-Falsified-If: after ShowFailure the HTML view is still visible or the Load remote images button is still enabled
    // Hairs-Human:        PENDING
    public void ShowFailure()
    {
        if (IsDisposed) return;
        Loaded.TrySetResult(false);
        _htmlView.Visibility = UiVisibility.Collapsed;
        _plainTextView.Visibility = UiVisibility.Visible;
        _isShowingPlainText = true;
        _toggleButton.IsEnabled = false;
        _loadImagesButton.IsEnabled = false;
        KeepFocusOutOf(_htmlView, _plainTextView.Editor);
        if (_session.FocusedElement is { CanFocus: false }) _session.SetFocus(_plainTextView.Editor);
        _status.Text = "HTML preview unavailable. Showing text. Close this window to continue reading.";
        Invalidate();
    }

    /// <summary>Focus inside a view that was just hidden moves to its replacement; focus elsewhere stays.</summary>
    private void KeepFocusOutOf(UiElement hidden, UiElement replacement)
    {
        if (_session.FocusedElement is { } focused && (focused == hidden || focused.IsDescendantOf(hidden)))
            _session.SetFocus(replacement);
    }

    /// <summary>
    /// Tab and Shift+Tab cycle through the header buttons, the document, and its links (or the plain
    /// text), in the same order the shell uses for its own controls.
    /// </summary>
    internal void MoveFocus(int direction)
    {
        _session.RenderFrame();
        var stops = MailKeyboardNavigation.TabStops(_root);
        if (stops.Count == 0) return;
        int current = _session.FocusedElement is { } focused ? IndexOf(stops, focused) : -1;
        int next = current < 0 ? (direction > 0 ? 0 : stops.Count - 1) : (current + direction + stops.Count) % stops.Count;
        _session.SetFocus(stops[next]);
        _htmlView.Reveal(stops[next]);
    }

    private static int IndexOf(IReadOnlyList<UiElement> stops, UiElement element)
    {
        for (int index = 0; index < stops.Count; index++)
            if (stops[index] == element) return index;
        return -1;
    }

    /// <summary>The zoom both views are drawn at; 1 is the document's own size.</summary>
    internal double Zoom => _htmlView.Zoom;

    /// <summary>The zoom a preview opens at and Reset returns to: the system text size.</summary>
    internal double DefaultZoom => _defaultZoom;

    /// <summary>Zooms one level in (a positive direction) or out, as Zoom in, Zoom out, Ctrl+Plus and Ctrl+Minus do.</summary>
    internal void StepZoom(int direction) => ZoomTo(PreviewZoom.Next(Zoom, direction, _defaultZoom));

    /// <summary>Returns to the system text size, as the reset button and Ctrl+0 do, and follows it again.</summary>
    internal void ResetZoom()
    {
        _zoomFollowsSystem = true;
        ZoomTo(_defaultZoom);
    }

    /// <summary>The reader's own zoom. Back at the default, the preview follows the system text size again.</summary>
    private void ZoomTo(double zoom)
    {
        if (PreviewZoom.AreSame(PreviewZoom.Clamp(zoom), Zoom)) return;
        ApplyZoom(zoom);
        // Only the reader decides this: a text size that moves onto their zoom leaves it theirs.
        _zoomFollowsSystem = PreviewZoom.AreSame(Zoom, _defaultZoom);
        AnnounceZoom();
        Invalidate();
    }

    /// <summary>
    /// Says the new level, from the control that shows it, once the current input has been handled: once
    /// per change, and once for a burst of changes (a held key, a fast wheel) at the level it ends on,
    /// so a screen reader is not handed a queue of levels already passed.
    /// </summary>
    private void AnnounceZoom()
    {
        bool queued = _zoomAnnouncement is not null;
        _zoomAnnouncement = "Zoom " + PreviewZoom.Format(Zoom) + ".";
        if (!queued && !PostToUiThread(SayZoom)) _zoomAnnouncement = null;
    }

    private void SayZoom()
    {
        string? message = _zoomAnnouncement;
        _zoomAnnouncement = null;
        // The reset button's name holds the level, and a screen reader reads the focused control's new
        // name (or the control focus has just moved to) itself; a notification as well would read it twice.
        if (message is not null && !IsDisposed && _session.FocusedElement != _zoomResetButton)
            _session.AnnounceStatus(_zoomResetButton, message);
    }

    /// <summary>
    /// A new system text size moves the default zoom. A preview the reader has not zoomed follows it;
    /// an explicit zoom stays, and only Reset's target moves.
    /// </summary>
    private void FollowTextSize(double textScale)
    {
        double next = PreviewZoom.Clamp(textScale);
        if (PreviewZoom.AreSame(next, _defaultZoom)) return;
        _defaultZoom = next;
        ApplyZoom(_zoomFollowsSystem ? next : Zoom);
    }

    private void ApplyZoom(double zoom)
    {
        zoom = PreviewZoom.Clamp(zoom);
        _htmlView.SetZoom(zoom);
        // The text view's font already has the system text size; it adds only the reader's own zoom.
        _plainTextView.Zoom = zoom / _defaultZoom;
        UpdateZoomControls();
    }

    private void UpdateZoomControls()
    {
        double zoom = Zoom;
        bool atDefault = PreviewZoom.AreSame(zoom, _defaultZoom);
        _zoomOutButton.IsEnabled = !PreviewZoom.IsAtMinimum(zoom);
        _zoomInButton.IsEnabled = !PreviewZoom.IsAtMaximum(zoom);
        _zoomResetButton.IsEnabled = !atDefault;
        _zoomResetButton.Text = PreviewZoom.Format(zoom);
        // The button shows the current level; its name starts with that text, as speech input and a
        // reader comparing the two expect, and then says where it goes.
        _zoomResetButton.AccessibleName = atDefault
            ? $"{PreviewZoom.Format(zoom)}, the default zoom"
            : $"{PreviewZoom.Format(zoom)}, reset zoom to {PreviewZoom.Format(_defaultZoom)}";
        // A zoom button that has just become unavailable passes focus to one that still works.
        if (_session.FocusedElement is StandardButton { IsEnabled: false } focused
            && (focused == _zoomInButton || focused == _zoomOutButton || focused == _zoomResetButton)
            && new[] { _zoomResetButton, _zoomInButton, _zoomOutButton }.FirstOrDefault(button => button.IsEnabled) is { } next)
            _session.SetFocus(next);
    }

    /// <summary>
    /// Ctrl+Plus and Ctrl+Minus, on the main keys or the number pad, zoom in and out; Ctrl+0 resets;
    /// Ctrl+wheel zooms. They are taken before any control sees them: the scroll view would scroll on
    /// Ctrl+wheel. Ctrl+Alt is AltGr on many keyboard layouts, where it types characters, so it never zooms.
    /// </summary>
    private bool TryZoom(UiInputEvent input)
    {
        if (!PreviewModifiers.Control(input.KeyModifiers) || PreviewModifiers.Alt(input.KeyModifiers)) return false;
        if (input.Kind == UiInputEventKind.PointerWheel && input.WheelAxis == MouseWheelAxis.Vertical)
        {
            int steps = _zoomWheel.Add(input.WheelDeltaNotches);
            double zoom = Zoom;
            for (int step = 0; step < Math.Abs(steps); step++) zoom = PreviewZoom.Next(zoom, Math.Sign(steps), _defaultZoom);
            ZoomTo(zoom);
            return true;
        }
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        switch (input.NativeKeyCode)
        {
            case 0xBB or 0x6B: StepZoom(1); return true; // "=" (with Shift "+") and the number pad's "+"
            case 0xBD or 0x6D: StepZoom(-1); return true; // "-" and the number pad's "-"
            case 0x30 or 0x60: ResetZoom(); return true; // "0" and the number pad's "0"
            default: return false;
        }
    }

    internal UiSession Session => _session;
    internal ScrollableHtmlView HtmlView => _htmlView;
    internal ScrollableMessageText PlainTextView => _plainTextView;
    internal StandardButton ToggleButton => _toggleButton;
    internal StandardButton ZoomOutButton => _zoomOutButton;
    internal StandardButton ZoomResetButton => _zoomResetButton;
    internal StandardButton ZoomInButton => _zoomInButton;
    internal StandardPanel Root => _root;
    internal StandardLabel Status => _status;
    internal StandardLabel TruncationNotice => _truncationNotice;

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=9A3595
    // Hairs-Falsified-If: an exception thrown by a queued UI callback escapes DrainDispatcher and aborts the frame being built or the posted-callback handler
    // Hairs-Human:        PENDING
    private void DrainDispatcher()
    {
        try { _dispatcher.Drain(); }
        catch (Exception exception)
        {
            GlobalExceptionHandler.LogException(exception, "HtmlPreviewWindow.DrainDispatcher");
        }
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=695F24
    // Hairs-Falsified-If: a frame is rendered before the host has taken the new client size, so the HTML snapshot is laid out at the previous width
    // Hairs-Human:        PENDING
    protected override void OnCreated()
    {
        base.OnCreated();
        ApplyPendingTheme();
        // Match the caption to the shell's theme; the handle exists only from here on.
        WindowsTitleBar.ApplyDarkMode(NativeHandle, _dark);
        // Screen readers see the buttons, the document, and each link, as in the main window.
        _automationBridge ??= new WindowsAutomationBridge(RenderNativeHandle, _session, _root, () => DpiScale);
        _host.FollowFocus();
        // A measured preview at a simulated scale keeps its DIP size in that scale's pixels, as the main window does.
        if (_measurement?.SimulatedScale is { } scale)
            WindowsScreen.FitClient(NativeHandle, (int)Math.Round(DefaultClientWidth * scale), (int)Math.Round(DefaultClientHeight * scale), centered: true);
    }

    /// <summary>Windows' scale for the window, or the simulated one a --measure run at --scale renders the preview at.</summary>
    public override double DpiScale => _measurement?.SimulatedScale ?? base.DpiScale;

    /// <summary>Names a simulated scale in the title, as the main window does, so a capture cannot be mistaken for a real display.</summary>
    private static string TitleAtScale(string title, PreviewMeasurement? measurement) => measurement?.SimulatedScale is { } scale
        ? $"{title}, simulated {Math.Round(scale * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}% scale"
        : title;

    protected override void OnNativeWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        // WM_GETMINMAXINFO: a simulated scale gets its pixels even beyond the real desktop, as the main window does.
        if (message == 0x0024 && hwnd == NativeHandle && _measurement?.SimulatedScale is not null)
            WindowsScreen.LiftMaximumTrackSize(lParam);
        if (hwnd == NativeHandle)
        {
            if (message == 0x0214) _htmlView.SetLiveResizing(true);
            else if (message == 0x0232) _htmlView.SetLiveResizing(false);
        }
    }

    protected override void OnGraphicsResourcesReleasing() => _htmlView?.Content.InvalidateTiles();

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        var recorder = _measurement?.Frames;
        var started = recorder?.BeginFrame();
        int tilesBefore = recorder is null ? 0 : _measurement!.Tiles.Misses;
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        FramePhases? phases = null;
        BRenderList? frame;
        if (recorder is { Detail: true })
        {
            frame = FramePhaseTimer.Render(_session, Stopwatch.GetTimestamp(), out var timed);
            phases = timed;
        }
        else frame = _session.RenderFrame();
        SyncTruncationNotice();
        if (started is { } begin) recorder!.EndFrame(begin, phases, _measurement!.Tiles.Misses - tilesBefore);
        return frame;
    }

    /// <summary>
    /// The header notice follows the current layout: zooming in can take a document past the render
    /// budget, and zooming out can bring it back within it.
    /// </summary>
    internal void SyncTruncationNotice()
    {
        if (_htmlView.Snapshot is not { } snapshot || snapshot.IsTruncated == _truncationShown) return;
        _truncationShown = snapshot.IsTruncated;
        _truncationNotice.Visibility = _truncationShown ? UiVisibility.Visible : UiVisibility.Collapsed;
        Invalidate();
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=3E34D4
    // Hairs-Human:        PENDING
    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        _host.Update(clientSize, dpiScale);
        Invalidate();
    }

    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=626D97
    // Hairs-Falsified-If: pressing a mouse button over the preview delivers no pointer-down input to the UI session, or delivers it as a pointer move
    // Hairs-Human:        PENDING
    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=EF3419
    // Hairs-Falsified-If: a pointer-up reaches the UI session with a position or button other than the operating system reported, so a link other than the one under the cursor is followed
    // Hairs-Human:        PENDING
    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_input.FromPointerButton(e));
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=780407
    // Hairs-Falsified-If: moving the pointer delivers a button event to the UI session instead of a move, following a link the user did not click
    // Hairs-Human:        PENDING
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_input.FromPointerMove(e));
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=13EC08
    // Hairs-Falsified-If: a wheel notch over the preview delivers no wheel input to the UI session, so the HTML view does not scroll
    // Hairs-Human:        PENDING
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_input.FromMouseWheel(e));
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=C5177F
    // Hairs-Falsified-If: a key press reaches the UI session as a key-up transition
    // Hairs-Human:        PENDING
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Down));
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=C41524
    // Hairs-Falsified-If: a key release reaches the UI session as a key-down transition, repeating the action of that key
    // Hairs-Human:        PENDING
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_input.FromKey(e, KeyboardKeyTransition.Up));
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=7E8B06
    // Hairs-Falsified-If: a character committed through WM_CHAR or the IME reaches the UI session a number of times other than once
    // Hairs-Human:        PENDING
    protected override void OnTextInput(BTextInputEventArgs e) => Dispatch(_input.FromText(e));

    /// <summary>UI-12: dispatches an input a --measure run synthesized, marked for the frame that shows it.</summary>
    internal void DispatchMeasured(UiInputEvent input)
    {
        var recorder = _measurement?.Frames;
        recorder?.MarkInput();
        long started = Stopwatch.GetTimestamp();
        Dispatch(input);
        recorder?.EndDispatch(started);
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=CD5455
    // Hairs-Falsified-If: an input event the UI session reports as handled leaves the window without an invalidation, so the frame on screen stays stale
    // Hairs-Human:        PENDING
    internal void Dispatch(UiInputEvent input)
    {
        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down
            && input.NativeKeyCode == 0x09 && !PreviewModifiers.ControlOrAlt(input.KeyModifiers))
        {
            MoveFocus(PreviewModifiers.Shift(input.KeyModifiers) ? -1 : 1);
            Invalidate();
            return;
        }
        if (TryZoom(input))
        {
            Invalidate();
            return;
        }
        if (input.Kind == UiInputEventKind.PointerMove)
            WindowsPointerCursor.SetTextCursor(HtmlViewElement.HasSelectableTextAt(_session, input.Position));

        if (_session.DispatchInput(input))
            Invalidate();
        // Escape closes the preview unless a control used it first, for example to dismiss a menu.
        else if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down
            && input.NativeKeyCode == 0x1B)
            Close();
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=BCB653
    // Hairs-Falsified-If: a GetMessage return of -1 is passed to TranslateMessage and DispatchMessage instead of ending the loop with a Win32Exception
    // Hairs-Human:        PENDING
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
            // --measure --detail: the render window's WM_PAINT has rendered and presented the frame it built; the
            // frame window's own WM_PAINT draws nothing.
            if (message.Message == 0x000F && message.Hwnd == RenderNativeHandle && _measurement?.Frames is { Detail: true } recorder) recorder.EndPaint();
        }
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=6D7D24
    // Hairs-Human:        PENDING
    protected override void CloseCore()
    {
        try { _imageLoadCts?.Cancel(); } catch (Exception) { }
        try { _session?.SetFocus(null); } catch (Exception) { }
        try
        {
            base.CloseCore();
        }
        catch (COMException)
        {
            // UI Automation or accessibility teardown during native window destruction
            // may report UIA_E_ELEMENTNOTAVAILABLE (0x80040201: The element is no longer available).
        }
        catch (Exception)
        {
        }
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=B68F46
    // Hairs-Falsified-If: the HTML container behind the preview is left undisposed after the window is disposed
    // Hairs-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            try { _automationBridge?.Dispose(); } catch (Exception) { }
            _automationBridge = null;
            _session.Dispose();
            _htmlView.Dispose();
        }
        base.Dispose(disposing);
    }
}
