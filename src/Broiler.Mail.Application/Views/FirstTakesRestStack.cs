using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A vertical stack whose later children keep their natural height, and whose first child is measured
/// in the height they leave, so a first child that caps itself and scrolls, such as the reader's header,
/// cannot push the rows below it out of view. Children keep their order, which is the keyboard and
/// reading order.
/// </summary>
public sealed class FirstTakesRestStack : UiElement
{
    public void Add(UiElement child) => AddChild(child);

    private IEnumerable<UiElement> Shown => Children.Where(child => child.Visibility != UiVisibility.Collapsed);

    protected override BSize MeasureCore(BSize availableSize)
    {
        var shown = Shown.ToArray();
        if (shown.Length == 0) return BSize.Empty;
        double width = availableSize.Width, below = 0, desiredWidth = 0;
        foreach (var child in shown.Skip(1))
        {
            var size = child.Measure(new BSize(width, double.PositiveInfinity));
            below += size.Height;
            desiredWidth = Math.Max(desiredWidth, size.Width);
        }
        double rest = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height - below) : double.PositiveInfinity;
        var first = shown[0].Measure(new BSize(width, rest));
        desiredWidth = Math.Max(desiredWidth, first.Width);
        return new BSize(double.IsFinite(width) ? width : desiredWidth, first.Height + below);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double y = finalRect.Y;
        foreach (var child in Children)
        {
            if (child.Visibility == UiVisibility.Collapsed)
            {
                child.Arrange(BRect.Empty);
                continue;
            }
            double height = Math.Max(0, Math.Min(child.DesiredSize.Height, finalRect.Bottom - y));
            child.Arrange(new BRect(finalRect.X, y, finalRect.Width, height));
            y += height;
        }
    }
}
