using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A left-aligned reading column: margins that shrink before the content does, and a bounded
/// line length so prose stays readable in wide windows. It occupies the full available width,
/// so an enclosing scroll view still scrolls from anywhere in the pane.
/// </summary>
public sealed class ReadingColumn : UiElement
{
    /// <summary>About 80 characters of 17-DIP body text.</summary>
    public const double DefaultMaximumWidth = 720;
    public const double WideMargin = 24;
    public const double CompactMargin = 12;
    /// <summary>Below this width, compact margins leave more room for text.</summary>
    public const double CompactBelow = 480;

    private readonly UiElement _content;

    public ReadingColumn(UiElement content, double verticalMargin = 0)
    {
        _content = content;
        VerticalMargin = verticalMargin;
        AddChild(content);
    }

    public double MaximumWidth { get; init; } = DefaultMaximumWidth;
    public double VerticalMargin { get; }

    /// <summary>The horizontal margin used for a given available width, so siblings can align with the column.</summary>
    public static double MarginFor(double width) => width < CompactBelow ? CompactMargin : WideMargin;

    private double ContentWidth(double width) => Math.Max(0, Math.Min(width - 2 * MarginFor(width), MaximumWidth));

    protected override BSize MeasureCore(BSize availableSize)
    {
        var content = _content.Measure(new BSize(ContentWidth(availableSize.Width), Math.Max(0, availableSize.Height - 2 * VerticalMargin)));
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : content.Width + 2 * WideMargin;
        return new BSize(width, content.Height + 2 * VerticalMargin);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double width = ContentWidth(finalRect.Width);
        _content.Measure(new BSize(width, Math.Max(0, finalRect.Height - 2 * VerticalMargin)));
        _content.Arrange(new BRect(finalRect.X + MarginFor(finalRect.Width), finalRect.Y + VerticalMargin,
            width, Math.Max(0, finalRect.Height - 2 * VerticalMargin)));
    }
}
