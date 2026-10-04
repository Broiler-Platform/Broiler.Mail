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
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.HTML.Image;
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
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
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

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=6; Fingerprint=12DA3F
    // Broiler-Falsified-If: an ArgumentException from HtmlPreviewPolicy.Create inside LoadRemoteImagesAsync escapes the async Clicked handler of the Load remote images button and terminates the process
    // Broiler-Human:        PENDING
    public HtmlPreviewWindow(
        HtmlPreviewDocument document,
        string plainText,
        Action<Uri> openExternal,
        string? rawHtml = null,
        IReadOnlyDictionary<string, MailEmbeddedImage>? embeddedImages = null,
        StandardThemeTokens? theme = null,
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

        _host = new WindowsUiHost(this, () => InputHandle);
        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);

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
        ApplyPendingTheme();
        // Match the caption to the shell's theme; the handle exists only from here on.
        WindowsTitleBar.ApplyDarkMode(NativeHandle, _dark);
        // Screen readers see the buttons, the document, and each link, as in the main window.
        _automationBridge ??= new WindowsAutomationBridge(RenderNativeHandle, _session, _root, () => DpiScale);
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        _host.Update(clientSize, DpiScale);
        DrainDispatcher();
        BRenderList? frame = _session.RenderFrame();
        SyncTruncationNotice();
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
            _automationBridge?.Dispose();
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
    float ExtentWidth,
    float ContentHeight,
    float UnclampedHeight,
    bool IsTruncated,
    IReadOnlyList<HtmlLinkGeometry> Links,
    IReadOnlyList<HtmlBoxDiagnostic> Diagnostics,
    long LayoutDurationTicks)
{
    public TimeSpan LayoutDuration => TimeSpan.FromTicks(LayoutDurationTicks);
}

/// <summary>
/// A place in a laid-out document that a relayout at another width can find again: a block (by its
/// position in the layout's element list and its tag), how far into it, and, for a place in the gap
/// above a block, the distance above it in CSS pixels.
/// </summary>
internal readonly record struct HtmlReadingAnchor(int Index, string TagName, double Fraction, double Gap);

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
        Func<double>? dpiScaleProvider = null,
        Func<string, bool>? canOpenLink = null)
    {
        _content = new HtmlViewElement(html, rendererProvider, onLinkClicked, dpiScaleProvider, canOpenLink);
        _scroll.AddChild(_content);
        AddChild(_scroll);
        _scroll.OffsetChanged += (_, change) =>
        {
            // The scroll view also clamps the offset while it measures a shorter document; that is not the
            // reader moving, and recording it lost the place (and sent the reader to the end) before
            // ArrangeCore could restore it.
            if (_inLayout) return;
            _readFraction = ReadFraction();
            // Scrolling after a zoom, before the document is laid out again, moves the place the zoom
            // keeps by the same distance in the document.
            if (_zoomAnchor is null) return;
            _zoomAnchorShift = new BPoint(
                _zoomAnchorShift.X + ((change.NewOffset.X - change.OldOffset.X) / _zoomAnchorZoom),
                _zoomAnchorShift.Y + ((change.NewOffset.Y - change.OldOffset.Y) / _zoomAnchorZoom));
        };
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=2D6686
    // Broiler-Falsified-If: an http(s) image named in the html passed to UpdateHtml is fetched over the network when the view next renders
    // Broiler-Human:        PENDING
    public void UpdateHtml(string html)
    {
        _content.UpdateHtml(html);
        _scroll.ScrollToStart();
        // A new document starts at its top, at the same zoom; that is the one deliberate reset of the
        // reading position.
        _newDocument = true;
        _zoomAnchor = null;
    }

    /// <summary>The document's zoom; see <see cref="HtmlViewElement.Zoom"/>.</summary>
    public double Zoom => _content.Zoom;

    /// <summary>
    /// Zooms the document and keeps the text at the top of the viewport there: the view notes which
    /// block that text is in and how far into it, and scrolls back to it once the document has been
    /// laid out again at the new width.
    /// </summary>
    public void SetZoom(double zoom)
    {
        double next = PreviewZoom.Clamp(zoom);
        if (next == _content.Zoom) return;
        // Several steps before the next layout keep the first place: only that one was measured.
        if (!_newDocument && _zoomAnchor is null && _content.AnchorAt(_scroll.VerticalOffset) is { } anchor)
        {
            _zoomAnchor = anchor;
            _zoomAnchorZoom = _content.Zoom;
            _zoomAnchorShift = new BPoint(_scroll.HorizontalOffset / _content.Zoom, 0);
        }
        _content.Zoom = next;
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=AB27B5
    // Broiler-Human:        PENDING
    public void ScrollToStart() => _scroll.ScrollToStart();
    internal HtmlViewElement Content => _content;
    internal StandardScrollView Scroll => _scroll;

    private double _readFraction;
    private double _arrangedExtent;
    private bool _newDocument = true;
    private bool _inLayout;
    private HtmlReadingAnchor? _zoomAnchor;
    private double _zoomAnchorZoom = 1;
    // In CSS pixels: the left edge, and any scrolling since the anchor was taken.
    private BPoint _zoomAnchorShift;

    /// <summary>Scrolls a focused link (or other element) inside the document into view.</summary>
    public void Reveal(UiElement element)
    {
        if (element != _content && element.IsDescendantOf(_content)) _scroll.MakeVisible(element.Bounds);
    }

    private double ReadFraction()
    {
        double range = _scroll.ExtentSize.Height - _scroll.ViewportSize.Height;
        return range > 0 ? _scroll.VerticalOffset / range : 0;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        ArrangeScroll(finalRect);
        // Hidden behind the plain text, the view is not laid out; a zoom's place waits until it is shown.
        if (finalRect.IsEmpty) return;
        // Reflowing at another width (a resize, or the header growing) changes the document's height.
        // Keep the reader at the same relative place rather than at the same, now unrelated, offset.
        double extent = _scroll.ExtentSize.Height;
        double range = extent - _scroll.ViewportSize.Height;
        if (_newDocument) { _newDocument = false; _readFraction = 0; _zoomAnchor = null; }
        else if (_zoomAnchor is { } anchor)
        {
            // A zoom puts the same text back at the top; a block the new layout lacks falls back to the
            // relative place.
            _zoomAnchor = null;
            double zoom = _content.Zoom;
            double top = _content.OffsetOf(anchor) is { } offset ? offset + (_zoomAnchorShift.Y * zoom) : (range > 0 ? _readFraction * range : 0);
            if (_scroll.SetOffset(new BPoint(_zoomAnchorShift.X * zoom, Math.Round(top)))) ArrangeScroll(finalRect);
        }
        else if (Math.Abs(extent - _arrangedExtent) > 0.5 && _arrangedExtent > 0 && range > 0)
        {
            if (_scroll.SetOffset(new BPoint(_scroll.HorizontalOffset, Math.Round(_readFraction * range)))) ArrangeScroll(finalRect);
        }
        _arrangedExtent = extent;
    }

    // Offsets the scroll view sets meanwhile are layout, not the reader scrolling. ArrangeCore arranges
    // again after correcting the offset, so this frame already shows the new place.
    private void ArrangeScroll(BRect finalRect)
    {
        _inLayout = true;
        try { base.ArrangeCore(finalRect); }
        finally { _inLayout = false; }
    }
    public HtmlLayoutSnapshot? Snapshot => _content.Snapshot;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=588444
    // Broiler-Falsified-If: an infinite or NaN available width sets a content width other than 800, or a width under 13 sets one below 1
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.ContentWidth = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - 12) : 800;
        _inLayout = true;
        try { return _scroll.Measure(availableSize); }
        finally { _inLayout = false; }
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
    // The height the document is cut at, in CSS pixels, at 100 % and below; see BudgetHeight.
    public const float MaxBudgetHeight = 32768f;
    // Content the renderer cannot wrap (a long address, say) may run past the page; it scrolls
    // sideways up to this width in CSS pixels and is cut beyond it (24,576 DIPs at the largest zoom).
    public const float MaxBudgetWidth = 8192f;
    public const int MaxCachedTiles = 16;
    // The count limit alone let memory grow with width and DPI: at 7680 DIPs and 300 % one tile is
    // about 280 MB, sixteen about 4.5 GB. A tile larger than this is rendered at a lower scale and
    // drawn stretched; the cache as a whole stays within the byte budget.
    public const long MaxTilePixels = 8L * 1024 * 1024;
    public const long MaxCachedTileBytes = 256L * 1024 * 1024;
    // The longest bitmap side Direct2D takes on older (feature level 10) hardware. A short last tile
    // of a page widened by zoomed overflow stayed under the pixel cap at more than twice this width.
    public const int MaxTileSide = 8192;
    private const int BytesPerPixel = 4;

    private readonly Func<IBroilerRenderer?> _rendererProvider;
    private readonly Action<string> _onLinkClicked;
    private readonly Func<double>? _dpiScaleProvider;
    private readonly Func<string, bool>? _canOpenLink;
    private readonly List<HtmlLinkTarget> _linkTargets = new();
    // Keyed by row and column; a page that fits the viewport has one column.
    private readonly Dictionary<(int Row, int Column), (BImageHandle Handle, int PixelWidth, int PixelHeight, double WidthDip, double HeightDip)> _tiles = new();
    private readonly LinkedList<(int Row, int Column)> _lruTiles = new();
    private long _cachedTileBytes;

    private HtmlContainer _container;
    private HtmlLayoutSnapshot? _layoutSnapshot;
    // What the cached tiles were drawn for: the page and column widths in DIPs, the zoom, and the display scale.
    private (double PageWidth, double ColumnWidth, double Zoom, double DpiScale) _tileKey;
    private string _html;

    private double _contentWidth = 800;
    private double _viewportWidth;
    private double _zoom = 1;

    /// <summary>
    /// How large the document is drawn, as a browser's page zoom: at 2 it is laid out at half the
    /// viewport's width in CSS pixels and drawn twice as large, so the text grows and still wraps to
    /// the window. A new zoom lays the document out again and discards every tile.
    /// </summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double zoom = PreviewZoom.Clamp(value);
            if (zoom == _zoom) return;
            _zoom = zoom;
            _layoutSnapshot = null;
            InvalidateTiles();
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Render);
        }
    }

    /// <summary>
    /// The height the document is cut at, in CSS pixels. Zoomed in, the document is laid out that much
    /// narrower and its text about that much taller, so the budget grows with the zoom: a preview that
    /// opens at a large system text size shows about as much of a long message as one at 100 % (the
    /// page margins, which do not shrink, take a little of it). The tiles stay cut in DIPs and bounded
    /// by the cache, and the whole document is laid out at any zoom.
    /// </summary>
    internal static float BudgetHeight(double zoom) => MaxBudgetHeight * (float)Math.Max(1, zoom);

    /// <summary>The width the document is laid out at, in CSS pixels: the viewport's width at this zoom.</summary>
    private float LayoutWidth => (float)Math.Max(1, _viewportWidth / _zoom);

    /// <summary>
    /// The width the document is laid out at when its parent measures without a width limit. A new
    /// value invalidates the measurement; otherwise the cached layout kept the old width after a
    /// resize, and a narrower window showed a horizontal scrollbar instead of reflowing the text.
    /// </summary>
    public double ContentWidth
    {
        get => _contentWidth;
        set
        {
            if (Math.Abs(_contentWidth - value) <= 0.5) return;
            _contentWidth = value;
            Invalidate(UiInvalidationKind.Measure);
        }
    }

    public HtmlLayoutSnapshot? Snapshot => _layoutSnapshot;
    /// <summary>UI-12: counts the tile cache's work while a --measure run watches it; null otherwise.</summary>
    internal HtmlTileStatistics? Statistics { get; set; }
    /// <summary>One focusable target per link the preview would open, in document order.</summary>
    internal IReadOnlyList<HtmlLinkTarget> LinkTargets => _linkTargets;
    public int CachedTileCount => _tiles.Count;
    public long CachedTileBytes => _cachedTileBytes;
    internal IEnumerable<(int PixelWidth, int PixelHeight, double WidthDip, double HeightDip)> CachedTileSizes =>
        _tiles.Values.Select(tile => (tile.PixelWidth, tile.PixelHeight, tile.WidthDip, tile.HeightDip)).ToArray();
    public bool IsTileCached(int row, int column = 0) => _tiles.ContainsKey((row, column));
    internal IReadOnlyCollection<(int Row, int Column)> CachedTileIndices => _tiles.Keys.ToArray();

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
        Func<double>? dpiScaleProvider = null,
        Func<string, bool>? canOpenLink = null)
    {
        _html = html;
        _rendererProvider = rendererProvider;
        _onLinkClicked = onLinkClicked;
        _dpiScaleProvider = dpiScaleProvider;
        _canOpenLink = canOpenLink;
        _container = CreateContainer(_html);
        // The document takes focus so the keyboard can scroll it; its links follow in Tab order.
        Focusable = true;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=591A3A
    // Broiler-Falsified-If: after UpdateHtml a paint or link hit-test runs against the container that UpdateHtml disposed
    // Broiler-Human:        PENDING
    public void UpdateHtml(string html)
    {
        bool focusInside = _linkTargets.Any(target => target.IsFocused);
        _html = html;
        _container.Dispose();
        _container = CreateContainer(_html);
        _layoutSnapshot = null;
        ClearLinkTargets();
        if (focusInside) Session?.SetFocus(this);
        InvalidateTiles();
        Invalidate(UiInvalidationKind.Measure);
    }

    private void EnsureLayout(float width)
    {
        if (_layoutSnapshot is not null && Math.Abs(_layoutSnapshot.Width - width) <= 0.5f) return;
        InvalidateTiles();
        _layoutSnapshot = CalculateLayout(width);
        Statistics?.LaidOut(_layoutSnapshot.LayoutDurationTicks);
        RebuildLinkTargets();
    }

    /// <summary>
    /// Replaces the link targets after a layout. Only links the preview would open get one, and a link
    /// broken across lines is one target. Focus on a link stays on the link at the same position.
    /// </summary>
    private void RebuildLinkTargets()
    {
        int focused = _linkTargets.FindIndex(target => target.IsFocused);
        ClearLinkTargets();
        var snapshot = _layoutSnapshot!;
        var merged = new List<(string Href, string? Id, BRect Bounds)>();
        foreach (var link in snapshot.Links)
        {
            if (link.Bounds.Width <= 0 || link.Bounds.Height <= 0 || link.Bounds.Bottom > snapshot.ContentHeight) continue;
            if (_canOpenLink is not null && !_canOpenLink(link.Href)) continue;
            if (merged.Count > 0 && merged[^1].Href == link.Href && merged[^1].Id == link.Id)
                merged[^1] = merged[^1] with { Bounds = Union(merged[^1].Bounds, link.Bounds) };
            else merged.Add((link.Href, link.Id, link.Bounds));
        }
        var names = HtmlLinkNames.From(_html);
        var used = new HashSet<int>();
        foreach (var (href, _, bounds) in merged)
        {
            var target = new HtmlLinkTarget(href, HtmlLinkNames.NameFor(href, names, used), bounds, _zoom);
            target.Clicked += (_, _) => _onLinkClicked(target.Href);
            AddChild(target);
            target.Measure(target.PreferredSize);
            _linkTargets.Add(target);
        }
        ArrangeLinkTargets(Bounds);
        if (focused >= 0) Session?.SetFocus(focused < _linkTargets.Count ? _linkTargets[focused] : this);
    }

    private void ClearLinkTargets()
    {
        foreach (var target in _linkTargets)
        {
            RemoveChild(target);
            target.Dispose();
        }
        _linkTargets.Clear();
    }

    private void ArrangeLinkTargets(BRect origin)
    {
        foreach (var target in _linkTargets)
        {
            var b = target.DocumentBounds;
            target.Arrange(new BRect(origin.X + (b.X * _zoom), origin.Y + (b.Y * _zoom), b.Width * _zoom, b.Height * _zoom));
        }
    }

    // Inline elements move within their lines when the text reflows; a reading position is kept by blocks.
    private static readonly HashSet<string> NotAnchors = new(StringComparer.Ordinal)
    { "html", "body", "a", "b", "strong", "em", "i", "u", "s", "small", "sub", "sup", "span", "code", "br", "img" };

    /// <summary>
    /// The place <paramref name="offset"/> DIPs from the document's top: the innermost block there and
    /// how far into it, or, in the gap between blocks, the next block and the distance above it.
    /// Null before the document is laid out.
    /// </summary>
    internal HtmlReadingAnchor? AnchorAt(double offset)
    {
        if (_layoutSnapshot is not { } snapshot) return null;
        double y = offset / _zoom;
        int inside = -1, below = -1;
        var boxes = snapshot.Diagnostics;
        for (int index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index].BorderBox;
            if (box.Height <= 0 || NotAnchors.Contains(boxes[index].TagName)) continue;
            if (box.Y <= y && y < box.Bottom)
            {
                if (inside < 0 || box.Height < boxes[inside].BorderBox.Height) inside = index;
            }
            else if (box.Y > y && (below < 0 || box.Y < boxes[below].BorderBox.Y)) below = index;
        }
        if (inside >= 0)
        {
            var box = boxes[inside].BorderBox;
            return new HtmlReadingAnchor(inside, boxes[inside].TagName, (y - box.Y) / box.Height, 0);
        }
        return below >= 0 ? new HtmlReadingAnchor(below, boxes[below].TagName, 0, y - boxes[below].BorderBox.Y) : null;
    }

    /// <summary>Where <paramref name="anchor"/> is in the current layout, in DIPs from the top; null if the layout lacks its block.</summary>
    internal double? OffsetOf(HtmlReadingAnchor anchor)
    {
        if (_layoutSnapshot is not { } snapshot || anchor.Index >= snapshot.Diagnostics.Count
            || snapshot.Diagnostics[anchor.Index].TagName != anchor.TagName) return null;
        var box = snapshot.Diagnostics[anchor.Index].BorderBox;
        return (box.Y + (anchor.Fraction * box.Height) + anchor.Gap) * _zoom;
    }

    private static BRect Union(BRect a, BRect b)
    {
        double left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        return new BRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    protected override void ArrangeCore(BRect finalRect) => ArrangeLinkTargets(finalRect);

    protected override UiSemanticNode GetSemanticNodeCore() =>
        base.GetSemanticNodeCore() with { Role = UiSemanticRole.Group, Name = "HTML message" };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=6565FC
    // Broiler-Falsified-If: an allocated tile texture handle is leaked or passed to ReleaseImage after invalidation
    // Broiler-Human:        PENDING
    private void InvalidateTiles()
    {
        Statistics?.Discarded(_tiles.Count);
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
    /// exceed <see cref="MaxTilePixels"/> or make a side longer than <see cref="MaxTileSide"/>. A capped
    /// size rounds down so it never passes a cap.
    /// </summary>
    internal static (double Scale, int Width, int Height) TilePixelSize(double widthDip, double heightDip, double dpiScale)
    {
        double limit = Math.Min(Math.Sqrt(MaxTilePixels / (widthDip * heightDip)), MaxTileSide / Math.Max(widthDip, heightDip));
        bool capped = limit < dpiScale;
        double scale = capped ? limit : dpiScale;
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
        float budget = BudgetHeight(_zoom);
        bool isTruncated = rawHeight > budget;
        float contentHeight = isTruncated ? budget : rawHeight;
        // The renderer's extent includes words and lines it could not wrap; the boxes do not.
        float actualWidth = _container.ActualSize.Width;
        float extentWidth = actualWidth > width + 0.5f ? Math.Min(actualWidth, Math.Max(width, MaxBudgetWidth)) : width;

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
        return new HtmlLayoutSnapshot(width, extentWidth, contentHeight, rawHeight, isTruncated, links, diagnostics, elapsedTicks);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=F7F59D
    // Broiler-Falsified-If: repeated measures with unchanged width rerun the full layout instead of reusing the cached snapshot
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        _viewportWidth = double.IsFinite(availableSize.Width) && availableSize.Width > 50 ? availableSize.Width : ContentWidth;
        EnsureLayout(LayoutWidth);
        var snapshot = _layoutSnapshot!;
        // As wide as the viewport, or wider where content the renderer could not wrap runs past it:
        // the scroll view then scrolls sideways.
        return new BSize(Math.Max(_viewportWidth, snapshot.ExtentWidth * _zoom), snapshot.ContentHeight * _zoom);
    }

    /// <summary>
    /// Paints the document from <paramref name="layoutTop"/> (in CSS pixels) down and from
    /// <paramref name="layoutLeft"/> right, each CSS pixel drawn <paramref name="scale"/> pixels large:
    /// the tile's own scale (the display scale unless the pixel cap lowered it) times the zoom.
    /// Broiler.HTML takes the scroll offset in layout units and applies the scale itself; multiplying it
    /// by the scale, as before, drew every tile after the first from too far down at any display scale
    /// other than 100 %, so at 150 % the text ran out two-thirds of the way through a long message and
    /// the rest of the preview was blank.
    /// </summary>
    internal HtmlBitmap PaintTile(double layoutTop, double scale, int pixelWidth, int pixelHeight, double layoutLeft = 0)
    {
        var bitmap = new HtmlBitmap(pixelWidth, pixelHeight);
        _container.ViewportZoom = (float)scale;
        _container.ScrollOffset = new PointF(-(float)layoutLeft, -(float)layoutTop);
        _container.PerformPaint(bitmap, new RectangleF(0, 0, pixelWidth, pixelHeight));
        _container.ScrollOffset = PointF.Empty;
        _container.ViewportZoom = 1.0f;
        return bitmap;
    }

    private void EnsureTile((int Row, int Column) key, double pageWidth, double columnWidth, double dpiScale, IBroilerRenderer renderer)
    {
        if (_tiles.ContainsKey(key))
        {
            _lruTiles.Remove(key);
            _lruTiles.AddFirst(key);
            Statistics?.Hit();
            return;
        }

        // Tiles are cut from the zoomed page in DIPs, so zooming does not enlarge them.
        double tileTop = key.Row * DefaultTileHeight;
        double tileLeft = key.Column * columnWidth;
        double tileH = Math.Min(DefaultTileHeight, (_layoutSnapshot!.ContentHeight * _zoom) - tileTop);
        double tileW = Math.Min(columnWidth, pageWidth - tileLeft);
        if (tileH <= 0 || tileW <= 0) return;

        (double tileScale, int pixelW, int pixelH) = TilePixelSize(tileW, tileH, dpiScale);
        long tileBytes = (long)pixelW * pixelH * BytesPerPixel;
        while ((_tiles.Count >= MaxCachedTiles || _cachedTileBytes + tileBytes > MaxCachedTileBytes) && _lruTiles.Count > 0)
        {
            var lru = _lruTiles.Last!.Value;
            _lruTiles.RemoveLast();
            if (_tiles.Remove(lru, out var evicted))
            {
                _cachedTileBytes -= (long)evicted.PixelWidth * evicted.PixelHeight * BytesPerPixel;
                if (evicted.Handle.IsValid) renderer.ReleaseImage(evicted.Handle);
                Statistics?.Evicted();
            }
        }

        // Timed only while measured.
        var statistics = Statistics;
        long started = statistics is null ? 0 : Stopwatch.GetTimestamp();
        using var bitmap = PaintTile(tileTop / _zoom, tileScale * _zoom, pixelW, pixelH, tileLeft / _zoom);
        long painted = statistics is null ? 0 : Stopwatch.GetTimestamp();

        // The renderer keeps RGBA pixels. Encoding a PNG for it to decode straight back cost far more
        // than painting the tile; the pixels are identical either way.
        BImageHandle handle = renderer.CreateImage(bitmap.ToPixelBuffer());

        _tiles[key] = (handle, pixelW, pixelH, tileW, tileH);
        _lruTiles.AddFirst(key);
        _cachedTileBytes += tileBytes;
        statistics?.Drawn(key, painted - started, Stopwatch.GetTimestamp() - painted, _cachedTileBytes);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=98C950
    // Broiler-Falsified-If: rendering tiles outside the visible viewport window allocates bitmaps for non-visible regions
    // Broiler-Human:        PENDING
    protected override void RenderCore(UiRenderContext context)
    {
        // The width measure laid the document out at; the arranged width also holds any overflow.
        if (_viewportWidth <= 0) _viewportWidth = Math.Max(1, Bounds.Width);
        EnsureLayout(LayoutWidth);
        if (_layoutSnapshot is null) return;

        double dpiScale = _dpiScaleProvider?.Invoke() ?? context.Host?.Scale ?? 1.0;
        if (dpiScale <= 0.1 || double.IsNaN(dpiScale)) dpiScale = 1.0;

        double pageWidth = Math.Max(1, Bounds.Width);
        double docHeight = _layoutSnapshot.ContentHeight * _zoom;
        int totalTiles = (int)Math.Ceiling(docHeight / DefaultTileHeight);
        if (totalTiles <= 0) totalTiles = 1;

        double parentViewportHeight;
        double visibleTop;
        double visibleLeft;
        double viewportWidth = pageWidth;
        if (Parent is StandardScrollView sv && sv.Bounds.Height > 0)
        {
            visibleTop = Math.Max(0, sv.VerticalOffset);
            parentViewportHeight = sv.ViewportSize.Height > 0 ? sv.ViewportSize.Height : sv.Bounds.Height;
            visibleLeft = Math.Max(0, sv.HorizontalOffset);
            if (sv.ViewportSize.Width > 0) viewportWidth = Math.Min(pageWidth, sv.ViewportSize.Width);
        }
        else
        {
            parentViewportHeight = context.Host?.ViewportSize.Height > 0 ? context.Host.ViewportSize.Height : docHeight;
            visibleTop = Math.Max(0, -Bounds.Y);
            visibleLeft = Math.Max(0, -Bounds.X);
            if (context.Host?.ViewportSize.Width > 0) viewportWidth = Math.Min(pageWidth, context.Host.ViewportSize.Width);
        }

        // A page wider than the viewport (content that cannot wrap) is cut into columns as wide as the
        // viewport. A tile of it is then no larger than one of an ordinary page and keeps the display
        // scale; drawn across the whole page, one long address made every tile of the message soft.
        double columnWidth = pageWidth > viewportWidth + 0.5 ? Math.Max(1, viewportWidth) : pageWidth;
        int totalColumns = Math.Max(1, (int)Math.Ceiling((pageWidth / columnWidth) - 0.001));

        // Tiles hold one page and column width, zoom, and display scale; a change to any of them discards them all.
        var tileKey = (Math.Round(pageWidth, 1), Math.Round(columnWidth, 1), _zoom, dpiScale);
        if (tileKey != _tileKey)
        {
            InvalidateTiles();
            _tileKey = tileKey;
        }

        var renderer = _rendererProvider();
        if (renderer is null) return;

        if (parentViewportHeight <= 0) parentViewportHeight = docHeight;
        double visibleBottom = Math.Min(docHeight, visibleTop + parentViewportHeight);
        if (visibleBottom < visibleTop) visibleBottom = visibleTop;

        double bufferTop = Math.Max(0, visibleTop - 256);
        double bufferBottom = Math.Min(docHeight, visibleBottom + 256);

        int firstTile = Math.Clamp((int)(bufferTop / DefaultTileHeight), 0, totalTiles - 1);
        int lastTile = Math.Clamp((int)(bufferBottom / DefaultTileHeight), 0, totalTiles - 1);
        // Only the columns in view: at most two, when the page is scrolled part of a column sideways.
        int firstColumn = Math.Clamp((int)(visibleLeft / columnWidth), 0, totalColumns - 1);
        int lastColumn = Math.Clamp((int)((visibleLeft + viewportWidth - 0.5) / columnWidth), firstColumn, totalColumns - 1);

        for (int i = firstTile; i <= lastTile; i++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                EnsureTile((i, column), pageWidth, columnWidth, dpiScale, renderer);
                if (_tiles.TryGetValue((i, column), out var tile) && tile.Handle.IsValid)
                {
                    var srcRect = new BRect(0, 0, tile.PixelWidth, tile.PixelHeight);
                    var destRect = new BRect(Bounds.X + (column * columnWidth), Bounds.Y + (i * DefaultTileHeight), tile.WidthDip, tile.HeightDip);
                    context.RenderList.DrawImage(tile.Handle, srcRect, destRect, 1.0);
                }
            }
        }

        if (_layoutSnapshot.IsTruncated)
        {
            double bannerHeight = 44;
            double bannerY = Bounds.Y + docHeight - bannerHeight;
            if (bannerY < (Bounds.Y + visibleBottom + 64) && (bannerY + bannerHeight) > (Bounds.Y + visibleTop - 64))
            {
                // Across the visible width, so it is readable however far the page is scrolled sideways.
                var bannerRect = new BRect(Bounds.X + visibleLeft + 8, bannerY - 4, Math.Max(100, viewportWidth - 16), bannerHeight);
                context.RenderList.FillRect(bannerRect, new BColor(255, 243, 205));
                context.RenderList.StrokeRect(bannerRect, new BColor(255, 220, 150), 1);
                context.RenderList.DrawText(
                    new BTextRun($"Content exceeds maximum render limit (truncated at {_layoutSnapshot.ContentHeight:N0}px).",
                        StandardControlPaint.Theme.FontCaption,
                        new BColor(133, 100, 4)),
                    new BPoint(bannerRect.Left + 12, bannerRect.Top + 14));
            }
        }

        foreach (var target in _linkTargets) target.Render(context);
        // The document's own focus ring frames the viewport, even below a short document's end.
        if (Session is { IsFocusVisible: true } session && session.FocusedElement == this)
            StandardControlPaint.DrawFocusRing(context.RenderList,
                new BRect(Bounds.X + visibleLeft + 2, Bounds.Y + visibleTop + 2, Math.Max(0, viewportWidth - 4), Math.Max(0, parentViewportHeight - 4)), 4);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=2248F9
    // Broiler-Falsified-If: the link callback fires for an input other than a left-button release, such as a pointer-down, a right click or a key press over a link
    // Broiler-Human:        PENDING
    protected override bool OnInput(UiInputEvent e)
    {
        // Space and Shift+Space page through the document, as in a browser; other keys reach the scroll view.
        if (e.Kind == UiInputEventKind.KeyboardKey && e.KeyTransition == KeyboardKeyTransition.Down && e.NativeKeyCode == 0x20
            && Parent is StandardScrollView pager)
        {
            double page = pager.ViewportSize.Height * pager.PageScrollFraction;
            pager.ScrollBy(0, PreviewModifiers.Shift(e.KeyModifiers) ? -page : page);
            return true;
        }
        if (e.Kind == UiInputEventKind.PointerButton && e.MouseButton == MouseButton.Left && e.MouseButtonTransition == MouseButtonTransition.Up)
        {
            // Links are found in document coordinates, CSS pixels before the zoom.
            float relX = (float)((e.Position.X - Bounds.X) / _zoom);
            float relY = (float)((e.Position.Y - Bounds.Y) / _zoom);

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
