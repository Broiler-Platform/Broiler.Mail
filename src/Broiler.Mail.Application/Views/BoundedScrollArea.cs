using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Shows its content at its natural height up to a fraction of the space available, and scrolls the
/// rest. Docked areas such as the reader header then cannot push the main content off screen when
/// text is large (for example, at a 200 % system text size).
/// </summary>
public sealed class BoundedScrollArea : UiElement
{
    private readonly UiElement _content;
    private readonly StandardScrollView _scroll = new() { Constraint = UiScrollConstraint.ConstrainWidth };
    private double _maximumFraction;

    /// <param name="name">What a screen reader announces when the area scrolls and so takes focus.</param>
    public BoundedScrollArea(UiElement content, double maximumFraction, string name)
    {
        _content = content;
        MaximumFraction = maximumFraction;
        _scroll.AccessibleName = name;
        _scroll.AddChild(content);
        AddChild(_scroll);
    }

    /// <summary>The largest share of the available height the area takes.</summary>
    public double MaximumFraction
    {
        get => _maximumFraction;
        set
        {
            if (!(value > 0 && value <= 1)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _maximumFraction) return;
            _maximumFraction = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// The rows of the content, top to bottom, as it was last measured: where each starts and ends, in
    /// DIP from the content's top, and whether the area may take more than its share to show it whole.
    /// The area does not end inside a row: when the share would cut one, it ends below the row (below
    /// the last one, at the end of the content) if the row may grow the area and that leaves
    /// <see cref="MinimumRemaining"/> of the space, and otherwise above the row. A row that starts in
    /// the upper half of the share, such as a long subject at a large text size, is cut at the share
    /// instead, since ending above it would leave the area nearly empty.
    /// </summary>
    public Func<IEnumerable<(double Start, double End, bool Grows)>>? Rows { get; init; }

    /// <summary>
    /// The height the area leaves of the space when it takes more than its share to show a row whole.
    /// Without it, the area takes no more than its share.
    /// </summary>
    public Func<double>? MinimumRemaining { get; init; }

    /// <summary>The scroll view that scrolls the content once it passes the cap.</summary>
    public StandardScrollView Scroll => _scroll;

    protected override BSize MeasureCore(BSize availableSize)
    {
        double natural = _content.Measure(new BSize(availableSize.Width, double.PositiveInfinity)).Height;
        double cap = double.IsFinite(availableSize.Height) ? availableSize.Height * MaximumFraction : double.PositiveInfinity;
        double height = Math.Min(natural, cap);
        _scroll.Measure(new BSize(availableSize.Width, height));
        if (natural > cap && Rows is not null)
        {
            // Measured at the cap, the content is laid out beside the scroll bar, as it is shown when it scrolls.
            double limit = MinimumRemaining is null ? cap : Math.Max(cap, availableSize.Height - MinimumRemaining());
            var rows = Rows().ToArray();
            for (int index = 0; index < rows.Length; index++)
            {
                var (start, end, grows) = rows[index];
                if (!(start < cap && cap < end)) continue;
                // Below the last row the whole content fits, at its natural height without the scroll bar.
                double below = index == rows.Length - 1 ? natural : end;
                height = grows && below <= limit ? below : start > cap / 2 ? start : cap;
                break;
            }
            if (height != cap) _scroll.Measure(new BSize(availableSize.Width, height));
        }
        return new BSize(double.IsFinite(availableSize.Width) ? availableSize.Width : _content.DesiredSize.Width, height);
    }

    protected override void ArrangeCore(BRect finalRect) => _scroll.Arrange(finalRect);
}
