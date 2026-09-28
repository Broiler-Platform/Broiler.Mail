using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Wraps scroll content to the viewport width instead of measuring long labels on one line.</summary>
public sealed class ViewportScrollView : UiElement
{
    public StandardScrollView Scroll { get; } = new();
    private readonly Content _content;

    public ViewportScrollView(UiElement content)
    {
        _content = new Content(content);
        Scroll.AddChild(_content);
        AddChild(Scroll);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.Width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - Scroll.ScrollbarThickness) : 520;
        return Scroll.Measure(availableSize);
    }

    private sealed class Content : UiElement
    {
        private readonly UiElement _child;
        public Content(UiElement child) { _child = child; AddChild(child); }
        public double Width { get; set; } = 520;
        protected override BSize MeasureCore(BSize availableSize) => _child.Measure(new BSize(Width, double.PositiveInfinity));
        protected override void ArrangeCore(BRect finalRect) => _child.Arrange(new BRect(finalRect.X, finalRect.Y, Width, finalRect.Height));
    }
}
