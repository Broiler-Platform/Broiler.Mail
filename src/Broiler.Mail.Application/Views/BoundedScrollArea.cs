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

    /// <summary>The scroll view that scrolls the content once it passes the cap.</summary>
    public StandardScrollView Scroll => _scroll;

    protected override BSize MeasureCore(BSize availableSize)
    {
        double natural = _content.Measure(new BSize(availableSize.Width, double.PositiveInfinity)).Height;
        double cap = double.IsFinite(availableSize.Height) ? availableSize.Height * MaximumFraction : double.PositiveInfinity;
        double height = Math.Min(natural, cap);
        _scroll.Measure(new BSize(availableSize.Width, height));
        return new BSize(double.IsFinite(availableSize.Width) ? availableSize.Width : _content.DesiredSize.Width, height);
    }

    protected override void ArrangeCore(BRect finalRect) => _scroll.Arrange(finalRect);
}
