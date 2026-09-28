using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Constrains text measurement to the pane width inside Broiler.UI's unconstrained scroll content.</summary>
public sealed class ScrollableMessageText : UiElement
{
    private readonly StandardLabel _label = new() { Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
    private readonly StandardScrollView _scroll = new();
    private readonly WrappedContent _content;

    public ScrollableMessageText()
    {
        _content = new WrappedContent(_label);
        _scroll.AddChild(_content);
        AddChild(_scroll);
    }

    public string Text { get => _label.DisplayText; set => _label.Text = value.Replace("&", "&&", StringComparison.Ordinal); }
    public void ScrollToStart() => _scroll.ScrollToStart();

    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.Width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - 12) : 520;
        return _scroll.Measure(availableSize);
    }

    private sealed class WrappedContent : UiElement
    {
        private readonly StandardLabel _label;
        public WrappedContent(StandardLabel label) { _label = label; AddChild(label); }
        public double Width { get; set; } = 520;
        protected override BSize MeasureCore(BSize availableSize) => _label.Measure(new BSize(Width, double.PositiveInfinity));
        protected override void ArrangeCore(BRect finalRect) => _label.Arrange(new BRect(finalRect.X, finalRect.Y, Width, finalRect.Height));
    }
}
