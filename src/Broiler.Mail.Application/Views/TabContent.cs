using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>Broiler.UI's tab control measures using its preferred size. Remeasure content at its actual allocation.</summary>
internal sealed class TabContent : UiElement
{
    private readonly UiElement _content;
    public TabContent(UiElement content) { _content = content; AddChild(content); }
    protected override BSize MeasureCore(BSize availableSize) => _content.Measure(availableSize);
    protected override void ArrangeCore(BRect finalRect)
    {
        if (finalRect.IsEmpty) return;
        _content.Measure(finalRect.Size);
        _content.Arrange(finalRect);
    }
}
