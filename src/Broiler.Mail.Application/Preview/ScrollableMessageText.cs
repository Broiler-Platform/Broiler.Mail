// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   9
// Annotated:        9/9
// Exempt:           5
// Human-reviewed:   0/9
// IP risk:          Low
// Security risk:    Medium
// Criteria:         6/0
// Resource impact:  4/10 max
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Broiler.Mail.Application.Views;

namespace Broiler.Mail.Application.Preview;

/// <summary>Readable and selectable message reader.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=7CBBCE
// Broiler-Falsified-If: message text containing an ampersand is displayed with it dropped or turned into an access-key underline
// Broiler-Human:        PENDING
public sealed class ScrollableMessageText : UiElement
{
    private readonly StandardRichEdit _editor = new()
    {
        IsReadOnly = true,
        // Without a name a read-only editor would be announced by its placeholder, or not at all.
        AccessibleName = "Message text",
        VerticalScrollPolicy = RichEditScrollPolicy.Never,
        HorizontalScrollPolicy = RichEditScrollPolicy.Never,
        Wrapping = RichEditWrapping.Wrap,
        BorderThickness = 0,
        FocusRingThickness = 0,
        PaddingX = 0,
        PaddingY = 0,
        Background = StandardControlPaint.Surface,
        Foreground = StandardControlPaint.Text,
    };
    private readonly StandardScrollView _scroll = new()
    {
        Constraint = UiScrollConstraint.ConstrainWidth,
    };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=A24DF7
    // Broiler-Human:        PENDING
    public ScrollableMessageText()
    {
        // Margins and a bounded line length come from the column; the scroll view still spans the pane.
        _scroll.AddChild(new ReadingColumn(_editor, verticalMargin: 8));
        AddChild(_scroll);
    }

    public StandardRichEdit Editor => _editor;

    private double? _zoomFraction;

    /// <summary>
    /// How large the text is drawn on top of its font's size; 1 is the font's size. The reader stays
    /// at the same relative place in the text.
    /// </summary>
    public double Zoom
    {
        get => _editor.Zoom;
        set
        {
            double before = _editor.Zoom;
            _editor.Zoom = value;
            if (_editor.Zoom == before) return;
            double range = _scroll.ExtentSize.Height - _scroll.ViewportSize.Height;
            _zoomFraction ??= range > 0 ? _scroll.VerticalOffset / range : 0;
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=050DA5
    // Broiler-Falsified-If: message text containing an ampersand is displayed with it dropped or turned into an access-key underline
    // Broiler-Human:        PENDING
    public string Text
    {
        get => _editor.GetPlainText();
        set => _editor.SetPlainText(value ?? string.Empty);
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=AB27B5
    // Broiler-Human:        PENDING
    public void ScrollToStart() => _scroll.ScrollToStart();

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=884914
    // Broiler-Falsified-If: an infinite available width reaches the label as its wrap width, so long message lines never wrap
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize) => _scroll.Measure(availableSize);

    protected override void ArrangeCore(BRect finalRect)
    {
        _scroll.Arrange(finalRect);
        // A hidden view keeps the place until it is shown and laid out at the new zoom.
        if (_zoomFraction is not { } fraction || finalRect.IsEmpty) return;
        _zoomFraction = null;
        double range = _scroll.ExtentSize.Height - _scroll.ViewportSize.Height;
        if (range > 0 && _scroll.SetOffset(new BPoint(_scroll.HorizontalOffset, Math.Round(fraction * range))))
            _scroll.Arrange(finalRect);
    }
}
