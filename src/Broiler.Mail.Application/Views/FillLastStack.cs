using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A vertical stack whose last child takes the remaining height, but never less than
/// <see cref="MinimumFillHeight"/>. Inside a scroll viewport that arranges content at least as tall
/// as the viewport, the last child (the composer body) fills the window, and outer scrolling only
/// starts when the other children genuinely need more room.
/// </summary>
public sealed class FillLastStack : UiElement
{
    public double Spacing { get; init; } = 8;
    public double MinimumFillHeight { get; init; } = 160;

    public void Add(UiElement child) => AddChild(child);

    private IEnumerable<UiElement> Shown => Children.Where(child => child.Visibility != UiVisibility.Collapsed);

    protected override BSize MeasureCore(BSize availableSize)
    {
        var shown = Shown.ToArray();
        double width = availableSize.Width, height = 0, desiredWidth = 0;
        for (int index = 0; index < shown.Length; index++)
        {
            bool fill = index == shown.Length - 1;
            var size = shown[index].Measure(new BSize(width, fill ? MinimumFillHeight : double.PositiveInfinity));
            // The filling child asks for its minimum here; arrangement gives it whatever is left.
            height += (fill ? MinimumFillHeight : size.Height) + (index > 0 ? Spacing : 0);
            desiredWidth = Math.Max(desiredWidth, size.Width);
        }
        return new BSize(double.IsFinite(width) ? width : desiredWidth, height);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        var shown = Shown.ToArray();
        double y = finalRect.Y;
        for (int index = 0; index < shown.Length; index++)
        {
            var child = shown[index];
            if (index > 0) y += Spacing;
            double height = index == shown.Length - 1
                ? Math.Max(MinimumFillHeight, finalRect.Bottom - y)
                : child.Measure(new BSize(finalRect.Width, double.PositiveInfinity)).Height;
            child.Arrange(new BRect(finalRect.X, y, finalRect.Width, height));
            y += height;
        }
    }
}
