using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Keeps its content a fixed distance inside the rectangle it is given, as padding would. While the
/// content takes no height, such as a notice with nothing to say, the inset takes no room either.
/// </summary>
public sealed class Inset : UiElement
{
    private readonly UiElement _content;

    public Inset(UiElement content, double horizontal, double vertical)
    {
        if (!(horizontal >= 0) || double.IsInfinity(horizontal)) throw new ArgumentOutOfRangeException(nameof(horizontal));
        if (!(vertical >= 0) || double.IsInfinity(vertical)) throw new ArgumentOutOfRangeException(nameof(vertical));
        _content = content;
        Horizontal = horizontal;
        Vertical = vertical;
        AddChild(content);
    }

    /// <summary>The distance on the left and on the right.</summary>
    public double Horizontal { get; }
    /// <summary>The distance at the top and at the bottom.</summary>
    public double Vertical { get; }

    protected override BSize MeasureCore(BSize availableSize)
    {
        var content = _content.Measure(new BSize(Math.Max(0, availableSize.Width - 2 * Horizontal), Math.Max(0, availableSize.Height - 2 * Vertical)));
        return content.Height <= 0 ? BSize.Empty : new BSize(content.Width + 2 * Horizontal, content.Height + 2 * Vertical);
    }

    protected override void ArrangeCore(BRect finalRect) =>
        _content.Arrange(new BRect(finalRect.X + Horizontal, finalRect.Y + Vertical,
            Math.Max(0, finalRect.Width - 2 * Horizontal), Math.Max(0, finalRect.Height - 2 * Vertical)));
}
