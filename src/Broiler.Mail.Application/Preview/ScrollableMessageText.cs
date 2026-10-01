using System;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Readable and selectable message reader.</summary>
public sealed class ScrollableMessageText : UiElement
{
    private readonly StandardRichEdit _editor = new()
    {
        IsReadOnly = true,
        VerticalScrollPolicy = RichEditScrollPolicy.Never,
        HorizontalScrollPolicy = RichEditScrollPolicy.Never,
        Wrapping = RichEditWrapping.Wrap,
        BorderThickness = 0,
        FocusRingThickness = 0,
        Background = StandardControlPaint.Surface,
        Foreground = StandardControlPaint.Text,
    };
    private readonly StandardScrollView _scroll = new()
    {
        Constraint = UiScrollConstraint.ConstrainWidth,
    };

    public ScrollableMessageText()
    {
        _scroll.AddChild(_editor);
        AddChild(_scroll);
    }

    public StandardRichEdit Editor => _editor;

    public string Text
    {
        get => _editor.GetPlainText();
        set => _editor.SetPlainText(value ?? string.Empty);
    }

    public void ScrollToStart() => _scroll.ScrollToStart();

    protected override BSize MeasureCore(BSize availableSize) => _scroll.Measure(availableSize);

    protected override void ArrangeCore(BRect finalRect) => _scroll.Arrange(finalRect);
}
