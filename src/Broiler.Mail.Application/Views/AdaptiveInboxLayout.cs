using Broiler.Graphics.Geometry;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Splitter;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Shows the inbox list and reader side by side when both fit at a readable width, and one at a
/// time otherwise. Switching only collapses or restores panes of the existing split container,
/// so the list, the reader, their scroll positions, and the user's split ratio all survive.
/// </summary>
public sealed class AdaptiveInboxLayout : UiElement
{
    /// <summary>Sender, subject, and a trailing date remain readable at this list width.</summary>
    public const double ListReadableWidth = 280;
    /// <summary>The subject heading, the three reply actions with reading margins, and short lines of text fit at this reader width.</summary>
    public const double ReaderReadableWidth = 400;

    private readonly UiSplitContainer _split;
    private readonly Func<bool> _readerOpen;
    private readonly Func<double>? _preferredFraction;
    private bool? _compact;
    private bool _readerOnly;

    /// <param name="readerOpen">Whether the reader, rather than the list, is the single pane in compact mode.</param>
    /// <param name="preferredFraction">
    /// The user's split ratio. A narrow window clamps the split to the panes' minimum widths; a wider
    /// one shows this ratio again.
    /// </param>
    public AdaptiveInboxLayout(UiSplitContainer split, Func<bool> readerOpen, Func<double>? preferredFraction = null)
    {
        _split = split;
        _readerOpen = readerOpen;
        _preferredFraction = preferredFraction;
        AddChild(split);
    }

    /// <summary>Raised during layout when the mode or the visible compact pane changes, before panes are arranged.</summary>
    public event EventHandler? ModeChanged;
    /// <summary>Raised when a pointer is released over the list in compact list mode, which completes a row click.</summary>
    public event EventHandler? ListClicked;

    public bool IsCompact => _compact == true;
    /// <summary>Compact mode currently shows the reader alone.</summary>
    public bool ShowsReaderOnly => IsCompact && _readerOnly;
    /// <summary>
    /// True while panes are collapsed or restored, or the split is laid out, so splitter events can be
    /// ignored: at a narrow width the panes' minimum widths clamp the ratio, and that must not replace
    /// the user's own.
    /// </summary>
    public bool IsAdapting { get; private set; }

    /// <summary>Compact when the two panes cannot both be readable; not a device or fixed window breakpoint.</summary>
    public static bool NeedsCompact(double width) => width < ListReadableWidth + ReaderReadableWidth;

    /// <summary>Re-evaluates the compact pane after the reader was opened or closed.</summary>
    public void Refresh()
    {
        if (_compact is { } compact) Apply(compact);
    }

    private void Apply(bool compact)
    {
        bool readerOnly = compact && _readerOpen();
        if (_compact == compact && _readerOnly == readerOnly) return;
        Adapt(() =>
        {
            if (!compact) _split.RestorePanes();
            else if (readerOnly) _split.CollapseFirstPane();
            else _split.CollapseSecondPane();
        });
        _compact = compact;
        _readerOnly = readerOnly;
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Adapt(Action change)
    {
        bool outer = IsAdapting;
        IsAdapting = true;
        try { change(); }
        finally { IsAdapting = outer; }
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        var size = BSize.Empty;
        Adapt(() => size = _split.Measure(availableSize));
        return size;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (finalRect.IsEmpty) return;
        Apply(NeedsCompact(finalRect.Width));
        Adapt(() =>
        {
            _split.Measure(finalRect.Size);
            // Measuring clamps the split to the panes' minimum widths at this width, and the clamped
            // ratio would otherwise stay after the window widens again.
            if (!IsCompact && _preferredFraction?.Invoke() is { } preferred && Math.Abs(_split.SplitterFraction - preferred) > 0.001)
            {
                double shown = _split.SplitterFraction;
                _split.SplitterFraction = preferred;
                if (!_split.SplitterFraction.Equals(shown)) _split.Measure(finalRect.Size);
            }
            _split.Arrange(finalRect);
        });
    }

    protected override bool OnInput(UiInputEvent input)
    {
        // The list selects on pointer down and leaves the release unhandled, so it reaches this
        // ancestor. Keyboard selection never does, so arrow keys can browse without opening mail.
        if (input.Kind == UiInputEventKind.PointerButton && input.MouseButtonTransition == MouseButtonTransition.Up
            && IsCompact && !_readerOnly && _split.FirstPane is { } list && list.Bounds.Contains(input.Position))
            ListClicked?.Invoke(this, EventArgs.Empty);
        return false;
    }
}
