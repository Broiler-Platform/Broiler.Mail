// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   109
// Annotated:        20/109
// Exempt:           70
// Human-reviewed:   0/109
// IP risk:          Low
// Security risk:    High
// Criteria:         18/12
// Resource impact:  8/10 max
// Unverified:       109
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Headers;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;
using Broiler.HTML.Image;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Layout.IR;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Diagnostics;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Measurement;
using Broiler.Net.Http;
using Broiler.UI;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using HtmlBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Mail.Windows.Preview;

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

// Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=7F007E
// Hairs-Falsified-If: a document shown through this view has an http(s) image or stylesheet it names fetched over the network instead of denied
// Hairs-Human:        PENDING
internal sealed class ScrollableHtmlView : UiElement, IInlineHtmlReader, IReaderViewport, IInlineRemoteImages, IInlineRemoteImageReset, IInlineReaderInput
{
    private string _sourceHtml;
    private StandardThemeTokens? _readerTheme;
    private BColor _pageBackground = new(255, 255, 255);
    internal void ApplyReaderTheme(StandardThemeTokens theme)
    {
        SetDefaultZoom(theme.TextScale);
        if (_readerTheme?.Surface == theme.Surface && _readerTheme.Text == theme.Text &&
            _readerTheme.TextMuted == theme.TextMuted && _readerTheme.Border == theme.Border &&
            _readerTheme.BorderStrong == theme.BorderStrong && _readerTheme.AccentText == theme.AccentText &&
            _readerTheme.IsHighContrast == theme.IsHighContrast) return;
        _readerTheme = theme;
        _pageBackground = theme.Surface;
        UpdateHtml(_sourceHtml, preserveReadingPosition: true);
    }
    private readonly PreviewZoomWheel _inlineZoomWheel = new();
    public bool HandleReaderInput(UiInputEvent input)
    {
        if (IsDisposed || Visibility != UiVisibility.Visible || !PreviewModifiers.Control(input.KeyModifiers) || PreviewModifiers.Alt(input.KeyModifiers)) return false;
        if (input.Kind == UiInputEventKind.PointerWheel && input.WheelAxis == MouseWheelAxis.Vertical)
        {
            int steps = _inlineZoomWheel.Add(input.WheelDeltaNotches);
            for (int i = 0; i < Math.Abs(steps); i++) ZoomBy(Math.Sign(steps));
            return true;
        }
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        switch (input.NativeKeyCode)
        {
            case 0xBB or 0x6B: ZoomBy(1); return true;
            case 0xBD or 0x6D: ZoomBy(-1); return true;
            case 0x30 or 0x60: ResetZoom(); return true;
            default: return false;
        }
    }
    private InlineRemoteImageSession? _remoteImages;
    public event EventHandler? RemoteImagesChanged;
    public RemoteImageState RemoteImages => _remoteImages?.State ?? new(0, 0, false, false, "");
    public Task LoadRemoteImagesAsync() => _remoteImages?.LoadAsync() ?? Task.CompletedTask;
    public void CancelRemoteImages() => _remoteImages?.Cancel();
    public void BlockRemoteImages() => _remoteImages?.Block();
    internal void HideAfterResourceResetFailure()
    {
        if (IsDisposed) return;
        _scroll.Visibility = UiVisibility.Collapsed;
        RenderingFailed?.Invoke(this, EventArgs.Empty);
    }
    internal void ConfigureRemoteImages(string rawHtml, IReadOnlyDictionary<string, MailEmbeddedImage> embedded, int total, Func<RemoteImageBroker> factory)
    {
        _remoteImages = new(this, rawHtml, embedded, total, factory);
        _remoteImages.Changed += (_, _) => RemoteImagesChanged?.Invoke(this, EventArgs.Empty);
    }
    private double _defaultZoom = 1;
    private bool _followDefaultZoom = true;
    public event EventHandler? ViewChanged;
    public event EventHandler? RenderingFailed;
    public bool CanZoomIn => !PreviewZoom.IsAtMaximum(Zoom);
    public bool CanZoomOut => !PreviewZoom.IsAtMinimum(Zoom);
    public void ZoomBy(int direction) { _followDefaultZoom = false; SetZoom(PreviewZoom.Next(Zoom, direction, _defaultZoom)); }
    public void ResetZoom() { _followDefaultZoom = true; SetZoom(_defaultZoom); }
    internal void SetDefaultZoom(double value)
    {
        _defaultZoom = PreviewZoom.Clamp(value);
        if (_followDefaultZoom) SetZoom(_defaultZoom);
    }
    private readonly StandardScrollView _scroll = new();
    private readonly HtmlViewElement _content;

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=EF8D9F
    // Hairs-Falsified-If: an http(s) image named in the html passed to the constructor is fetched over the network when the view renders
    // Hairs-Human:        PENDING
    public ScrollableHtmlView(
        string html,
        Func<IBroilerRenderer?> rendererProvider,
        Action<string> onLinkClicked,
        Func<double>? dpiScaleProvider = null,
        Func<string, bool>? canOpenLink = null,
        bool recoverManagedFailures = false,
        bool useBackgroundRaster = false)
    {
        _sourceHtml = html;
        _content = new HtmlViewElement(html, rendererProvider, onLinkClicked, dpiScaleProvider, canOpenLink, useBackgroundRaster);
        if (recoverManagedFailures) _content.RenderingFailed += (_, _) => RenderingFailed?.Invoke(this, EventArgs.Empty);
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

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=F2F519
    // Hairs-Falsified-If: an http(s) image named in the html passed to UpdateHtml is fetched over the network when the view next renders
    // Hairs-Human:        PENDING
    public void UpdateHtml(string html, bool preserveReadingPosition = false)
    {
        if (preserveReadingPosition) _readFraction = ReadingPosition;
        _sourceHtml = html;
        _content.UpdateHtml(_readerTheme is { } theme ? InlineReaderTheme.Apply(html, theme) : html);
        if (preserveReadingPosition) { _newDocument = false; _zoomAnchor = null; return; }
        _scroll.ScrollToStart();
        // A new document starts at its top, at the same zoom; that is the one deliberate reset of the
        // reading position.
        _newDocument = true;
        _restoreFraction = null; _readFraction = 0;
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
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=2BA274
    // Hairs-Human:        PENDING
    public void ScrollToStart()
    {
        _restoreFraction = null; _zoomAnchor = null; _readFraction = 0;
        _scroll.ScrollToStart();
    }
    public double ReadingPosition => _restoreFraction ?? _readFraction;
    public void RestoreReadingPosition(double fraction)
    {
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        _restoreFraction = _readFraction = Math.Clamp(fraction, 0, 1); _newDocument = false; _zoomAnchor = null;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Render);
    }
    internal HtmlViewElement Content => _content;
    internal StandardScrollView Scroll => _scroll;
    internal void SetLiveResizing(bool active) => _content.SetLiveResizing(active);

    private double _readFraction;
    private double? _restoreFraction;
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
        if (_restoreFraction is { } requested)
        {
            _restoreFraction = null; _newDocument = false; _zoomAnchor = null;
            if (_scroll.SetOffset(new BPoint(_scroll.HorizontalOffset, Math.Round(requested * Math.Max(0, range))))) ArrangeScroll(finalRect);
        }
        else if (_newDocument) { _newDocument = false; _readFraction = 0; _zoomAnchor = null; }
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
        _readFraction = ReadFraction();
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

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=82D3BC
    // Hairs-Falsified-If: an infinite or NaN available width sets a content width other than 800, or a width under 13 sets one below 1
    // Hairs-Human:        PENDING
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
        context.RenderList.FillRect(Bounds, _pageBackground);
        base.RenderCore(context);
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=24120C
    // Hairs-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _remoteImages?.Dispose();
            _content.Dispose();
            _scroll.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=9D4E0E
// Hairs-Falsified-If: rendering a document whose img src is an http(s) or file: URL sends a request for it instead of the load being denied
// Hairs-Human:        PENDING
internal sealed class HtmlViewElement : UiElement
{
    internal event EventHandler? RenderingFailed;
    private bool _renderingFailed;
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
    private IBroilerRenderer? _tileRenderer;
    private readonly Action<string> _onLinkClicked;
    private readonly Func<double>? _dpiScaleProvider;
    private readonly Func<string, bool>? _canOpenLink;
    private readonly bool _useBackgroundRaster;
    private readonly List<HtmlLinkTarget> _linkTargets = new();
    private IReadOnlyList<(string Href, string Text)>? _cachedLinkNames;
    private bool _selectingWithPointer;
    private bool _rightPressed;
    private double _selectionLastDocY;
    private double _selectionCurrentDocY;
    private readonly HashSet<(int Row, int Column)> _selectionTiles = new();
    private readonly HashSet<(int Row, int Column)> _dirtySelectionTiles = new();
    private readonly List<BRect> _selectionRects = new();
    private bool _selectionOverlayDirty;
    private bool? _hasActiveSelection;
    private bool _isLiveResizing;
    private bool _deferredResizeLayout;
    private (int First, int Last) _lastVisibleRows = (-1, -1);
    private (int First, int Last) _lastVisibleColumns = (-1, -1);
    private long _prefetchGeneration;
    private HtmlTilePrefetcher? _prefetcher;
    private readonly List<HtmlTilePrefetchRequest> _tileRequests = new();
    private readonly StandardMenu _htmlMenu = new()
    {
        PresentationMode = UiMenuPresentationMode.ContextMenu,
        IsTabStop = false,
        AccessibleName = "Message context menu",
    };
    private string? _menuLink;
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

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=6; Fingerprint=40D1B3
    // Hairs-Falsified-If: an img whose src is not a data: URL, or a linked stylesheet, is loaded by the container instead of being blocked by the ImageLoad and StylesheetLoad handlers
    // Hairs-Human:        PENDING
    internal static HtmlContainer CreateIsolatedContainer(string html)
    {
        HtmlRuntime.Initialize();
        var container = new HtmlContainer();
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;
        container.RequestTransport = new DenyingRequestTransport();
        container.StylesheetLoad += (_, e) => e.SetStyleSheet = string.Empty;
        container.ImageLoad += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Src) && !e.Src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                MailLogger.Warning("HtmlContainer", $"Blocked external image load in container: {e.Src}");
                e.Handled = true;
            }
            else
            {
                MailLogger.Debug("HtmlContainer", $"Inline data image accepted by container (Src length: {e.Src?.Length ?? 0})");
            }
        };
        container.SetHtmlWithStyleSet(html, null, null);
        return container;
    }

    private HtmlContainer CreateContainer(string html)
    {
        var container = CreateIsolatedContainer(html);
        container.SelectionInvalidated += InvalidateSelection;
        return container;
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=88D9BD
    // Hairs-Falsified-If: an http(s) image named in the html passed to the constructor is fetched over the network when the element renders
    // Hairs-Human:        PENDING
    public HtmlViewElement(
        string html,
        Func<IBroilerRenderer?> rendererProvider,
        Action<string> onLinkClicked,
        Func<double>? dpiScaleProvider = null,
        Func<string, bool>? canOpenLink = null,
        bool useBackgroundRaster = false)
    {
        _html = html;
        _rendererProvider = rendererProvider;
        _onLinkClicked = onLinkClicked;
        _dpiScaleProvider = dpiScaleProvider;
        _canOpenLink = canOpenLink;
        _useBackgroundRaster = useBackgroundRaster;
        AddChild(_htmlMenu);
        _htmlMenu.ItemInvoked += (_, e) => InvokeHtmlMenuItem(e.Item.Id);
        _container = CreateContainer(_html);
        // The document takes focus so the keyboard can scroll it; its links follow in Tab order.
        Focusable = true;
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=6; Fingerprint=9EFA01
    // Hairs-Falsified-If: after UpdateHtml a paint or link hit-test runs against the container that UpdateHtml disposed
    // Hairs-Human:        PENDING
    public void UpdateHtml(string html)
    {
        bool focusInside = _linkTargets.Any(target => target.IsFocused);
        _html = html;
        _cachedLinkNames = null;
        _deferredResizeLayout = false;
        _htmlMenu.ClosePopup();
        _rightPressed = false;
        EndSelectionGesture();
        _container.Dispose();
        _container = CreateContainer(_html);
        _selectionRects.Clear();
        _selectionOverlayDirty = false;
        _hasActiveSelection = null;
        _layoutSnapshot = null;
        ClearLinkTargets();
        if (focusInside) Session?.SetFocus(this);
        InvalidateTiles();
        Invalidate(UiInvalidationKind.Measure);
    }

    internal void SetLiveResizing(bool active)
    {
        if (_isLiveResizing == active) return;
        _isLiveResizing = active;
        if (active)
        {
            _prefetcher?.ClearQueue();
        }
        else
        {
            var invalidation = _deferredResizeLayout ? UiInvalidationKind.Measure | UiInvalidationKind.Render : UiInvalidationKind.Render;
            _deferredResizeLayout = false;
            Invalidate(invalidation);
        }
    }

    private void EnsureLayout(float width)
    {
        if (_layoutSnapshot is not null && Math.Abs(_layoutSnapshot.Width - width) <= 0.5f) return;
        InvalidateTiles();
        _layoutSnapshot = CalculateLayout(width);
        _selectionOverlayDirty |= _selectionRects.Count > 0;
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
        var names = _cachedLinkNames ??= HtmlLinkNames.From(_html);
        var used = new HashSet<int>();
        var resolved = new List<(string Href, string Name, BRect Bounds)>(merged.Count);
        foreach (var (href, _, bounds) in merged)
            resolved.Add((href, HtmlLinkNames.NameFor(href, names, used), bounds));

        bool canReuse = _linkTargets.Count == resolved.Count;
        if (canReuse)
        {
            for (int i = 0; i < resolved.Count; i++)
            {
                if (!string.Equals(_linkTargets[i].Href, resolved[i].Href, StringComparison.Ordinal) ||
                    !string.Equals(_linkTargets[i].Text, resolved[i].Name, StringComparison.Ordinal))
                {
                    canReuse = false;
                    break;
                }
            }
        }

        if (canReuse)
        {
            for (int i = 0; i < resolved.Count; i++)
            {
                var target = _linkTargets[i];
                target.UpdateGeometry(resolved[i].Bounds, _zoom);
                target.Measure(target.PreferredSize);
            }
            ArrangeLinkTargets(Bounds);
            return;
        }

        ClearLinkTargets();
        foreach (var (href, name, bounds) in resolved)
        {
            var target = new HtmlLinkTarget(href, name, bounds, _zoom);
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

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=417E8A
    // Hairs-Falsified-If: an allocated tile texture handle is leaked or passed to ReleaseImage after invalidation
    // Hairs-Human:        PENDING
    internal void InvalidateTiles()
    {
        _prefetchGeneration++;
        _prefetcher?.ClearQueue();
        _tileRequests.Clear();
        _dirtySelectionTiles.Clear();
        _selectionTiles.Clear();
        Statistics?.Discarded(_tiles.Count);
        var renderer = _tileRenderer;
        foreach (var entry in _tiles.Values)
        {
            if (entry.Handle.IsValid && renderer is not null)
            {
                try { renderer.ReleaseImage(entry.Handle); }
                catch (Exception error) when (_renderingFailed && error is not OutOfMemoryException) { }
            }
        }
        _tiles.Clear();
        _lruTiles.Clear();
        _cachedTileBytes = 0;
        _tileRenderer = null;
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

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=BC44AF
    // Hairs-Falsified-If: layout geometry calculation ignores trailing content, leaving bare text or tail elements outside measured height
    // Hairs-Human:        PENDING
    internal HtmlLayoutSnapshot CalculateLayout(float width)
    {
        long start = Stopwatch.GetTimestamp();
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

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=2FA422
    // Hairs-Falsified-If: repeated measures with unchanged width rerun the full layout instead of reusing the cached snapshot
    // Hairs-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        if (_renderingFailed) return new BSize(1, 1);
        try { return MeasureDocument(availableSize); }
        catch (Exception error) when (RenderingFailed is not null && error is not OutOfMemoryException)
        {
            _renderingFailed = true;
            RenderingFailed.Invoke(this, EventArgs.Empty);
            return new BSize(1, 1);
        }
    }

    private BSize MeasureDocument(BSize availableSize)
    {
        _viewportWidth = double.IsFinite(availableSize.Width) && availableSize.Width > 50 ? availableSize.Width : ContentWidth;
        if (_isLiveResizing && _layoutSnapshot is { } existing)
        {
            if (Math.Abs(existing.Width - LayoutWidth) > 0.5f)
                _deferredResizeLayout = true;
            double width = existing.ExtentWidth <= existing.Width + 0.5f
                ? _viewportWidth
                : Math.Max(_viewportWidth, existing.ExtentWidth * _zoom);
            return new BSize(width, existing.ContentHeight * _zoom);
        }
        _deferredResizeLayout = false;
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
        try
        {
            PaintTile(_container, bitmap, layoutTop, scale, layoutLeft);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    internal static void PaintTile(HtmlContainer container, HtmlBitmap bitmap, double layoutTop, double scale, double layoutLeft)
    {
        try
        {
            container.ViewportZoom = (float)scale;
            container.ScrollOffset = new PointF(-(float)layoutLeft, -(float)layoutTop);
            container.PerformPaint(bitmap, new RectangleF(0, 0, bitmap.Width, bitmap.Height));
        }
        finally
        {
            container.ScrollOffset = PointF.Empty;
            container.ViewportZoom = 1.0f;
        }
    }

    private void EnsureTile((int Row, int Column) key, double pageWidth, double columnWidth, double dpiScale, IBroilerRenderer renderer)
    {
        if (_tileRenderer is not null && !ReferenceEquals(_tileRenderer, renderer)) InvalidateTiles();
        _tileRenderer = renderer;
        if (_dirtySelectionTiles.Remove(key) && _tiles.Remove(key, out var stale))
        {
            _cachedTileBytes -= (long)stale.PixelWidth * stale.PixelHeight * BytesPerPixel;
            _lruTiles.Remove(key);
            _selectionTiles.Remove(key);
            if (stale.Handle.IsValid) renderer.ReleaseImage(stale.Handle);
        }
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
        EvictForTileBytes(tileBytes, renderer);

        // Timed only while measured.
        var statistics = Statistics;
        long started = statistics is null ? 0 : Stopwatch.GetTimestamp();
        using var bitmap = PaintTile(tileTop / _zoom, tileScale * _zoom, pixelW, pixelH, tileLeft / _zoom);
        long painted = statistics is null ? 0 : Stopwatch.GetTimestamp();

        // The renderer keeps RGBA pixels. Encoding a PNG for it to decode straight back cost far more
        // than painting the tile; the pixels are identical either way.
        BImageHandle handle = renderer.CreateImage(bitmap.ToPixelBuffer());
        StoreTileHandle(key, handle, pixelW, pixelH, tileW, tileH, tileBytes);
        if (_selectingWithPointer || HasActiveSelection())
            _selectionTiles.Add(key);
        statistics?.Drawn(key, painted - started, Stopwatch.GetTimestamp() - painted, _cachedTileBytes);
    }

    private void EvictForTileBytes(long tileBytes, IBroilerRenderer renderer)
    {
        while ((_tiles.Count >= MaxCachedTiles || _cachedTileBytes + tileBytes > MaxCachedTileBytes) && _lruTiles.Count > 0)
        {
            var lru = _lruTiles.Last!.Value;
            _lruTiles.RemoveLast();
            if (_tiles.Remove(lru, out var evicted))
            {
                _cachedTileBytes -= (long)evicted.PixelWidth * evicted.PixelHeight * BytesPerPixel;
                _selectionTiles.Remove(lru);
                _dirtySelectionTiles.Remove(lru);
                if (evicted.Handle.IsValid) renderer.ReleaseImage(evicted.Handle);
                Statistics?.Evicted();
            }
        }
    }

    private void StoreTileHandle((int Row, int Column) key, BImageHandle handle, int pixelW, int pixelH, double tileW, double tileH, long tileBytes)
    {
        _tiles[key] = (handle, pixelW, pixelH, tileW, tileH);
        _lruTiles.AddFirst(key);
        _cachedTileBytes += tileBytes;
    }

    private bool HasActiveSelection() => _hasActiveSelection ??= !string.IsNullOrEmpty(_container.GetSelectedText());
    private StandardQueuedUiDispatcher? BackgroundDispatcher =>
        _useBackgroundRaster ? Session?.Dispatcher as StandardQueuedUiDispatcher : null;

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=8; Fingerprint=9FBB3B
    // Hairs-Falsified-If: rendering tiles outside the visible viewport window allocates bitmaps for non-visible regions
    // Hairs-Human:        PENDING
    protected override void RenderCore(UiRenderContext context)
    {
        if (_renderingFailed) return;
        try
        {
            RenderDocument(context);
            _htmlMenu.Render(context);
        }
        catch (Exception error) when (RenderingFailed is not null && error is not OutOfMemoryException)
        {
            _renderingFailed = true;
            RenderingFailed.Invoke(this, EventArgs.Empty);
        }
    }

    private void RenderDocument(UiRenderContext context)
    {
        // The width measure laid the document out at; the arranged width also holds any overflow.
        if (_viewportWidth <= 0) _viewportWidth = Math.Max(1, Bounds.Width);
        bool liveResizing = _isLiveResizing && _layoutSnapshot is not null;
        if (!liveResizing) EnsureLayout(LayoutWidth);
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
        var backgroundDispatcher = BackgroundDispatcher;
        if (parentViewportHeight <= 0) parentViewportHeight = docHeight;
        if (backgroundDispatcher is not null)
        {
            // Leave space for every visible tile and its neighbours. At extreme DPI a cache
            // smaller than the viewport would otherwise keep evicting and rescheduling tiles in view.
            double rows = Math.Min(totalTiles, Math.Ceiling(parentViewportHeight / DefaultTileHeight) + 2);
            double slots = rows * Math.Min(totalColumns, 2);
            if (slots * MaxTilePixels * BytesPerPixel > MaxCachedTileBytes)
                dpiScale = Math.Min(dpiScale, Math.Sqrt(MaxCachedTileBytes / (slots * BytesPerPixel * columnWidth * DefaultTileHeight)));
        }

        // Tiles hold one page and column width, zoom, and display scale; a change to any of them discards them all.
        var tileKey = (Math.Round(pageWidth, 1), Math.Round(columnWidth, 1), _zoom, dpiScale);
        if (tileKey != _tileKey && !liveResizing)
        {
            InvalidateTiles();
            _tileKey = tileKey;
        }

        var renderer = _rendererProvider();
        if (renderer is null) return;
        if (_tileRenderer is not null && !ReferenceEquals(_tileRenderer, renderer)) InvalidateTiles();

        double visibleBottom = Math.Min(docHeight, visibleTop + parentViewportHeight);
        if (visibleBottom < visibleTop) visibleBottom = visibleTop;

        double bufferTop = Math.Max(0, visibleTop - 256);
        double bufferBottom = Math.Min(docHeight, visibleBottom + 256);

        int firstTile = Math.Clamp((int)(bufferTop / DefaultTileHeight), 0, totalTiles - 1);
        int lastTile = Math.Clamp((int)(bufferBottom / DefaultTileHeight), 0, totalTiles - 1);
        int visFirstTile = Math.Clamp((int)(visibleTop / DefaultTileHeight), 0, totalTiles - 1);
        int visLastTile = Math.Clamp((int)(Math.Max(visibleTop, visibleBottom - 0.5) / DefaultTileHeight), visFirstTile, totalTiles - 1);
        _lastVisibleRows = (visFirstTile, visLastTile);
        double scaleX = 1;

        if (liveResizing)
        {
            scaleX = _tileKey.PageWidth > 0 ? pageWidth / _tileKey.PageWidth : 1;
            double cachedColumnWidth = _tileKey.ColumnWidth > 0 ? _tileKey.ColumnWidth : columnWidth;
            int cachedColumns = Math.Max(1, (int)Math.Ceiling((_tileKey.PageWidth / cachedColumnWidth) - 0.001));
            for (int i = firstTile; i <= lastTile; i++)
            {
                for (int column = 0; column < cachedColumns; column++)
                {
                    if (_tiles.TryGetValue((i, column), out var tile) && tile.Handle.IsValid)
                    {
                        var srcRect = new BRect(0, 0, tile.PixelWidth, tile.PixelHeight);
                        var destRect = new BRect(Bounds.X + (column * cachedColumnWidth * scaleX), Bounds.Y + (i * DefaultTileHeight), tile.WidthDip * scaleX, tile.HeightDip);
                        context.RenderList.DrawImage(tile.Handle, srcRect, destRect, 1.0);
                    }
                }
            }
        }
        else
        {
            // Only the columns in view: at most two, when the page is scrolled part of a column sideways.
            int firstColumn = Math.Clamp((int)(visibleLeft / columnWidth), 0, totalColumns - 1);
            int lastColumn = Math.Clamp((int)((visibleLeft + viewportWidth - 0.5) / columnWidth), firstColumn, totalColumns - 1);
            _lastVisibleColumns = (firstColumn, lastColumn);
            var prefetchDispatcher = backgroundDispatcher ?? (Statistics is null && totalTiles > 1 &&
                !_selectingWithPointer && !HasActiveSelection() && Session?.Dispatcher is not ImmediateUiDispatcher ? Session?.Dispatcher : null);
            _tileRequests.Clear();

            for (int i = firstTile; i <= lastTile; i++)
            {
                bool isVisibleRow = i >= visFirstTile && i <= visLastTile;
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    var key = (i, column);
                    if (backgroundDispatcher is not null)
                    {
                        if (_tiles.ContainsKey(key))
                        {
                            _lruTiles.Remove(key);
                            _lruTiles.AddFirst(key);
                        }
                        else if (isVisibleRow)
                        {
                            RequestTile(key, pageWidth, columnWidth, dpiScale, backgroundDispatcher);
                        }
                    }
                    else if (prefetchDispatcher is null || isVisibleRow || (_tiles.ContainsKey(key) && !_dirtySelectionTiles.Contains(key)))
                    {
                        EnsureTile(key, pageWidth, columnWidth, dpiScale, renderer);
                    }
                    if (_tiles.TryGetValue(key, out var tile) && tile.Handle.IsValid)
                    {
                        var srcRect = new BRect(0, 0, tile.PixelWidth, tile.PixelHeight);
                        var destRect = new BRect(Bounds.X + (column * columnWidth), Bounds.Y + (i * DefaultTileHeight), tile.WidthDip, tile.HeightDip);
                        context.RenderList.DrawImage(tile.Handle, srcRect, destRect, 1.0);
                    }
                }
            }

            if (prefetchDispatcher is not null)
            {
                int prefetchFirst = Math.Clamp((int)(Math.Max(0, visibleTop - (DefaultTileHeight * 0.5)) / DefaultTileHeight), 0, totalTiles - 1);
                int prefetchLast = Math.Clamp((int)(Math.Min(docHeight, visibleBottom + (DefaultTileHeight * 0.5)) / DefaultTileHeight), 0, totalTiles - 1);
                for (int i = prefetchFirst; i <= prefetchLast; i++)
                {
                    if (i >= visFirstTile && i <= visLastTile) continue;
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        var key = (i, column);
                        if (_tiles.ContainsKey(key) || _dirtySelectionTiles.Contains(key)) continue;
                        RequestTile(key, pageWidth, columnWidth, dpiScale, prefetchDispatcher);
                    }
                }
            }
            if (_tileRequests.Count > 0) _prefetcher ??= new HtmlTilePrefetcher(this);
            _prefetcher?.SetRequests(_tileRequests);
        }

        if (backgroundDispatcher is not null)
            RenderSelection(context, visibleLeft, visibleTop, viewportWidth, visibleBottom, scaleX);

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

        var focused = Session?.FocusedElement;
        foreach (var target in _linkTargets)
        {
            if (ReferenceEquals(focused, target))
            {
                target.Render(context);
                continue;
            }
            var b = target.DocumentBounds;
            double top = b.Y * _zoom, bottom = b.Bottom * _zoom;
            double left = b.X * _zoom, right = b.Right * _zoom;
            if (bottom >= visibleTop - 8 && top <= visibleBottom + 8 && right >= visibleLeft - 8 && left <= visibleLeft + viewportWidth + 8)
                target.Render(context);
        }
        // The document's own focus ring frames the viewport, even below a short document's end.
        if (Session is { IsFocusVisible: true } session && session.FocusedElement == this)
            StandardControlPaint.DrawFocusRing(context.RenderList,
                new BRect(Bounds.X + visibleLeft + 2, Bounds.Y + visibleTop + 2, Math.Max(0, viewportWidth - 4), Math.Max(0, parentViewportHeight - 4)), 4);
    }

    private void RequestTile(
        (int Row, int Column) key,
        double pageWidth,
        double columnWidth,
        double dpiScale,
        IUiDispatcher dispatcher)
    {
        double tileTop = key.Row * DefaultTileHeight;
        double tileLeft = key.Column * columnWidth;
        double tileH = Math.Min(DefaultTileHeight, (_layoutSnapshot!.ContentHeight * _zoom) - tileTop);
        double tileW = Math.Min(columnWidth, pageWidth - tileLeft);
        if (tileH <= 0 || tileW <= 0) return;

        (double tileScale, int pixelW, int pixelH) = TilePixelSize(tileW, tileH, dpiScale);
        _tileRequests.Add(new HtmlTilePrefetchRequest(
            _prefetchGeneration,
            _html,
            _layoutSnapshot!.Width,
            key,
            tileTop / _zoom,
            tileLeft / _zoom,
            tileScale * _zoom,
            pixelW,
            pixelH,
            tileW,
            tileH,
            _tileKey,
            dispatcher));
    }

    internal void ApplyPrefetchedTile(HtmlTilePrefetchRequest request, BPixelBuffer? pixels, Exception? failure)
    {
        if (IsDisposed || _renderingFailed || _isLiveResizing)
            return;
        bool renderInBackground = BackgroundDispatcher is not null;
        if (!renderInBackground && (_selectingWithPointer || HasActiveSelection()))
            return;
        if (request.Generation != _prefetchGeneration || request.TileKey != _tileKey)
            return;
        if (_dirtySelectionTiles.Contains(request.Key) || _tiles.ContainsKey(request.Key))
            return;

        bool visible = request.Key.Row >= _lastVisibleRows.First && request.Key.Row <= _lastVisibleRows.Last
            && request.Key.Column >= _lastVisibleColumns.First && request.Key.Column <= _lastVisibleColumns.Last;
        try
        {
            if (failure is not null)
            {
                if (renderInBackground && visible) throw new InvalidOperationException("Could not paint the HTML tile.", failure);
                return;
            }
            var renderer = _rendererProvider();
            if (renderer is null || (_tileRenderer is not null && !ReferenceEquals(_tileRenderer, renderer)))
                return;
            _tileRenderer = renderer;

            long tileBytes = (long)request.PixelWidth * request.PixelHeight * BytesPerPixel;
            EvictForTileBytes(tileBytes, renderer);
            BImageHandle handle = renderer.CreateImage(pixels!);
            if (!handle.IsValid) return;
            StoreTileHandle(request.Key, handle, request.PixelWidth, request.PixelHeight, request.TileWidth, request.TileHeight, tileBytes);
            if (visible) Invalidate(UiInvalidationKind.Render);
        }
        catch (Exception error) when (RenderingFailed is not null && error is not OutOfMemoryException)
        {
            _renderingFailed = true;
            RenderingFailed.Invoke(this, EventArgs.Empty);
        }
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=6FEED3
    // Hairs-Falsified-If: the link callback fires for an input other than a left-button release, such as a pointer-down, a right click or a key press over a link
    // Hairs-Human:        PENDING
    protected override bool OnInput(UiInputEvent e)
    {
        if (_htmlMenu.IsOpen && e.Kind is UiInputEventKind.PointerButton or UiInputEventKind.PointerMove or UiInputEventKind.PointerWheel)
        {
            if (e.Kind == UiInputEventKind.PointerButton && e.MouseButtonTransition == MouseButtonTransition.Down)
                _htmlMenu.ClosePopup();
            return true;
        }

        if (e.Kind == UiInputEventKind.PointerButton && e.MouseButton == MouseButton.Right)
        {
            if (e.MouseButtonTransition == MouseButtonTransition.Down)
                _rightPressed = true;
            else if (e.MouseButtonTransition == MouseButtonTransition.Up)
            {
                bool open = _rightPressed;
                _rightPressed = false;
                if (open && !_selectingWithPointer)
                    OpenHtmlMenu(e.Position);
            }
            return true;
        }

        if (e.Kind == UiInputEventKind.KeyboardKey && e.KeyTransition == KeyboardKeyTransition.Down
            && PreviewModifiers.Control(e.KeyModifiers) && !PreviewModifiers.Alt(e.KeyModifiers))
        {
            if (e.NativeKeyCode == 0x41)
            {
                _container.HandleKeyDown(controlKey: true, aKeyCode: true, cKeyCode: false);
                return true;
            }
            if (e.NativeKeyCode == 0x43)
            {
                CopySelectedText();
                return true;
            }
        }

        // Space and Shift+Space page through the document, as in a browser; other keys reach the scroll view.
        if (e.Kind == UiInputEventKind.KeyboardKey && e.KeyTransition == KeyboardKeyTransition.Down && e.NativeKeyCode == 0x20
            && !PreviewModifiers.ControlOrAlt(e.KeyModifiers)
            && Parent is StandardScrollView pager)
        {
            double page = pager.ViewportSize.Height * pager.PageScrollFraction;
            pager.ScrollBy(0, PreviewModifiers.Shift(e.KeyModifiers) ? -page : page);
            return true;
        }
        if (e.Kind == UiInputEventKind.PointerMove && _selectingWithPointer)
        {
            var docPoint = ToDocumentPoint(e.Position);
            _selectionCurrentDocY = docPoint.Y;
            _container.HandleMouseMove(docPoint, leftButton: true);
            _selectionLastDocY = _selectionCurrentDocY;
            return true;
        }
        if (e.Kind == UiInputEventKind.PointerButton && e.MouseButton == MouseButton.Left
            && e.MouseButtonTransition == MouseButtonTransition.Down && e.TouchContactState is null)
        {
            Session?.SetFocus(this);
            Session?.CaptureInput(this);
            _selectingWithPointer = true;
            var docPoint = ToDocumentPoint(e.Position);
            _selectionLastDocY = _selectionCurrentDocY = docPoint.Y;
            _container.HandleMouseDown(docPoint);
            return true;
        }
        if (e.Kind == UiInputEventKind.PointerButton && e.MouseButton == MouseButton.Left && e.MouseButtonTransition == MouseButtonTransition.Up)
        {
            if (!_selectingWithPointer && e.TouchContactState != Broiler.Input.Touch.TouchContactState.Released)
                return false;

            PointF point = ToDocumentPoint(e.Position);
            if (_selectingWithPointer)
            {
                bool suppressLink = _container.IsSelecting || !Bounds.Contains(e.Position)
                    || (Parent is StandardScrollView scroll && !scroll.ContentBounds.Contains(e.Position));
                _container.HandleMouseUp(point, leftButton: true, rightButton: false, activateLink: false);
                EndSelectionGesture();
                if (suppressLink)
                    return true;
            }

            if (_layoutSnapshot is not null)
            {
                foreach (var link in _layoutSnapshot.Links)
                {
                    if (link.Bounds.Contains(new BPoint(point.X, point.Y)))
                    {
                        _onLinkClicked(link.Href);
                        return true;
                    }
                }
            }

            _container.ScrollOffset = PointF.Empty;
            string? linkUrl = _container.GetLinkAt(point);
            if (!string.IsNullOrEmpty(linkUrl))
            {
                _onLinkClicked(linkUrl);
                return true;
            }
        }
        return base.OnInput(e);
    }

    private void OpenHtmlMenu(BPoint position)
    {
        if (Session is not { } session)
            return;

        var point = ToDocumentPoint(position);
        _container.ClearSelectionOutside(point);
        _menuLink = _container.GetLinkAt(point);
        bool hasClipboard = session.Host is IUiClipboardHost;
        var items = new List<UiMenuItem>
        {
            new("copy", "Copy") { IsEnabled = hasClipboard && _container.GetSelectedText() is { Length: > 0 } },
            new("selectAll", "Select All"),
        };
        if (_menuLink is { Length: > 0 } link && (_canOpenLink?.Invoke(link) ?? true))
        {
            items.Add(new("openLink", "Open Link"));
            items.Add(new("copyLink", "Copy Link Address") { IsEnabled = hasClipboard });
        }

        _htmlMenu.SetItems(items);
        _htmlMenu.Arrange(new BRect(position.X, position.Y, 0, 0));
        session.SetFocus(this);
        _htmlMenu.OpenPopup();
    }

    private void InvokeHtmlMenuItem(string id)
    {
        switch (id)
        {
            case "copy": CopySelectedText(); break;
            case "selectAll": _container.HandleKeyDown(controlKey: true, aKeyCode: true, cKeyCode: false); break;
            case "openLink" when _menuLink is { } link: _onLinkClicked(link); break;
            case "copyLink" when _menuLink is { } link && Session?.Host is IUiClipboardHost clipboard:
                clipboard.SetText(link);
                break;
        }
    }

    private void CopySelectedText()
    {
        if (Session?.Host is IUiClipboardHost clipboard && _container.GetSelectedText() is { Length: > 0 } text)
            clipboard.SetText(text);
    }

    internal static bool HasSelectableTextAt(UiSession session, BPoint position)
    {
        for (var target = session.CapturedElement ?? session.HitTest(position); target is not null; target = target.Parent)
        {
            if (target is HtmlViewElement reader)
                return !reader._htmlMenu.IsOpen && reader.Bounds.Contains(position)
                    && (reader.Parent is not StandardScrollView scroll || scroll.ContentBounds.Contains(position))
                    && (reader._container.IsSelecting || reader._container.HasSelectableTextAt(reader.ToDocumentPoint(position)));
        }
        return false;
    }

    private PointF ToDocumentPoint(BPoint point) =>
        new((float)((point.X - Bounds.X) / _zoom), (float)((point.Y - Bounds.Y) / _zoom));

    private void EndSelectionGesture()
    {
        _selectingWithPointer = false;
        Session?.ReleaseInputCapture(this);
    }

    private void InvalidateSelection()
    {
        _hasActiveSelection = null;
        if (BackgroundDispatcher is not null)
        {
            _selectionOverlayDirty = true;
            Invalidate(UiInvalidationKind.Render);
            return;
        }

        _prefetchGeneration++;
        _prefetcher?.ClearQueue();
        foreach (var key in _selectionTiles)
            _dirtySelectionTiles.Add(key);

        if (_selectingWithPointer)
        {
            double topDip = Math.Min(_selectionLastDocY, _selectionCurrentDocY) * _zoom - 64;
            double bottomDip = Math.Max(_selectionLastDocY, _selectionCurrentDocY) * _zoom + 64;
            foreach (var key in _tiles.Keys)
            {
                double tileTop = key.Row * DefaultTileHeight;
                double tileBottom = tileTop + DefaultTileHeight;
                if (tileBottom >= topDip && tileTop <= bottomDip)
                    _dirtySelectionTiles.Add(key);
            }
        }
        else if (HasActiveSelection())
        {
            foreach (var key in _tiles.Keys)
                _dirtySelectionTiles.Add(key);
        }

        Invalidate(UiInvalidationKind.Render);
    }

    private void RenderSelection(UiRenderContext context, double left, double top, double viewportWidth, double bottom, double scaleX)
    {
        if (_selectionOverlayDirty)
        {
            _selectionOverlayDirty = false;
            _selectionRects.Clear();
            // Selection changes mark the laid-out fragment tree dirty. Building the display list
            // refreshes its character offsets without reflowing or rasterizing the message.
            _container.CreateDisplayList();
            if (_container.LatestFragmentTree is { } fragments) AddSelectionRects(fragments);
        }

        double zoomX = _zoom * scaleX;
        var highlight = BColor.FromArgb(0x60, 0x33, 0x99, 0xFF);
        foreach (var rect in _selectionRects)
        {
            if (rect.Bottom * _zoom <= top || rect.Top * _zoom >= bottom ||
                rect.Right * zoomX <= left || rect.Left * zoomX >= left + viewportWidth)
                continue;
            context.RenderList.FillRect(new BRect(
                Bounds.X + rect.X * zoomX, Bounds.Y + rect.Y * _zoom, rect.Width * zoomX, rect.Height * _zoom), highlight);
        }
    }

    private void AddSelectionRects(Fragment fragment)
    {
        if (fragment.Style.Display == "none") return;
        if (fragment.Style.Visibility == "visible" && fragment.Lines is { } lines)
        {
            foreach (var line in lines)
            {
                foreach (var inline in line.Inlines)
                {
                    if (!inline.Selected) continue;
                    double start = Math.Max(0, inline.SelectedStartOffset);
                    double end = inline.SelectedEndOffset < 0 ? inline.Width : inline.SelectedEndOffset;
                    if (end > start)
                        _selectionRects.Add(new BRect(inline.X + start, inline.Y, end - start, line.Height));
                }
            }
        }
        foreach (var child in fragment.Children) AddSelectionRects(child);
    }

    internal bool SendInput(UiInputEvent e) => OnInput(e);

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=22E3F6
    // Hairs-Falsified-If: disposing the element leaves cached tile image handles or the container undisposed
    // Hairs-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _prefetcher?.Dispose();
            _prefetcher = null;
            InvalidateTiles();
            _container.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed record HtmlTilePrefetchRequest(
    long Generation,
    string Html,
    float LayoutWidth,
    (int Row, int Column) Key,
    double LayoutTop,
    double LayoutLeft,
    double Scale,
    int PixelWidth,
    int PixelHeight,
    double TileWidth,
    double TileHeight,
    (double PageWidth, double ColumnWidth, double Zoom, double DpiScale) TileKey,
    IUiDispatcher Dispatcher);

internal sealed class HtmlTilePrefetcher : IDisposable
{
    private readonly HtmlViewElement _owner;
    private readonly object _gate = new();
    private readonly Queue<HtmlTilePrefetchRequest> _queue = new();
    private readonly HashSet<(long Generation, (int Row, int Column) Key)> _requested = new();
    private readonly AutoResetEvent _signal = new(false);
    private HtmlTilePrefetchRequest? _active;
    private bool _disposed;

    public HtmlTilePrefetcher(HtmlViewElement owner)
    {
        _owner = owner;
        new Thread(Run) { IsBackground = true, Name = "Broiler.HTML tile raster" }.Start();
    }

    public void SetRequests(IReadOnlyList<HtmlTilePrefetchRequest> requests)
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Each frame submits its visible tiles first. Replace speculative work when the
            // viewport moves, and keep at most one bitmap in flight through the dispatcher.
            _queue.Clear();
            _requested.Clear();
            foreach (var request in requests)
            {
                _requested.Add((request.Generation, request.Key));
                if (_active?.Generation != request.Generation || _active.Key != request.Key)
                    _queue.Enqueue(request);
            }
            if (_active is null && _queue.Count > 0) _signal.Set();
        }
    }

    public void ClearQueue()
    {
        lock (_gate)
        {
            _queue.Clear();
            _requested.Clear();
        }
    }

    private void Run()
    {
        string? html = null;
        HtmlContainer? container = null;
        float layoutWidth = 0;
        try
        {
            while (true)
            {
                HtmlTilePrefetchRequest? request = null;
                lock (_gate)
                {
                    if (_disposed) break;
                    if (_active is null && _queue.TryDequeue(out request))
                        _active = request;
                }
                if (request is null)
                {
                    _signal.WaitOne();
                    continue;
                }

                BPixelBuffer? pixels = null;
                Exception? failure = null;
                try
                {
                    if (container is null || !string.Equals(html, request.Html, StringComparison.Ordinal))
                    {
                        container?.Dispose();
                        container = null;
                        container = HtmlViewElement.CreateIsolatedContainer(request.Html);
                        html = request.Html;
                        layoutWidth = 0;
                    }
                    if (Math.Abs(layoutWidth - request.LayoutWidth) > 0.5f)
                    {
                        container.MaxSize = new SizeF(request.LayoutWidth, 0);
                        container.PerformLayout(new RectangleF(0, 0, request.LayoutWidth, 0));
                        layoutWidth = request.LayoutWidth;
                    }

                    if (!StillRequested(request))
                    {
                        CompleteRequest();
                        continue;
                    }
                    using var bitmap = new HtmlBitmap(request.PixelWidth, request.PixelHeight);
                    HtmlViewElement.PaintTile(container, bitmap, request.LayoutTop, request.Scale, request.LayoutLeft);
                    pixels = bitmap.ToPixelBuffer();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failure = ex;
                    layoutWidth = 0;
                }
                if (!StillRequested(request))
                {
                    CompleteRequest();
                    continue;
                }

                try
                {
                    request.Dispatcher.Post(() =>
                    {
                        try
                        {
                            if (StillRequested(request)) _owner.ApplyPrefetchedTile(request, pixels, failure);
                        }
                        finally { CompleteRequest(); }
                    });
                }
                catch (ObjectDisposedException)
                {
                    CompleteRequest();
                }
            }
        }
        finally
        {
            container?.Dispose();
            lock (_gate)
            {
                _disposed = true;
                _signal.Dispose();
            }
        }
    }

    private bool StillRequested(HtmlTilePrefetchRequest request)
    {
        lock (_gate) return !_disposed && _requested.Contains((request.Generation, request.Key));
    }

    private void CompleteRequest()
    {
        lock (_gate)
        {
            _active = null;
            if (!_disposed) _signal.Set();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Clear();
            _requested.Clear();
            _signal.Set();
        }
    }
}

// Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=B984F4
// Hairs-Falsified-If: a request the renderer sends through this transport, including one for a file: or loopback URL, is dispatched to the network or the file system
// Hairs-Human:        PENDING
internal sealed class DenyingRequestTransport : IBrowserRequestTransport
{
    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=1; Fingerprint=5A1C67
    // Hairs-Falsified-If: Deny returns a response whose status is anything other than 403 Forbidden or whose body is not empty
    // Hairs-Human:        PENDING
    private static TransportResponse Deny(HttpRequestMessage request)
    {
        var resp = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { Content = new StringContent(string.Empty) };
        var urls = request.RequestUri is null ? Array.Empty<Uri>() : new[] { request.RequestUri };
        return new TransportResponse(resp, urls, default);
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=D8BAB5
    // Hairs-Falsified-If: SendAsync completes with a response other than the empty 403 that Deny builds
    // Hairs-Human:        PENDING
    public Task<TransportResponse> SendAsync(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(Deny(request));
    }

    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=EF07AC
    // Hairs-Falsified-If: Send returns a response other than the empty 403 that Deny builds
    // Hairs-Human:        PENDING
    public TransportResponse Send(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken)
    {
        return Deny(request);
    }
}
