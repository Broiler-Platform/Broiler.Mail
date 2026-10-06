// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   6
// Annotated:        0/6
// Exempt:           2
// Human-reviewed:   0/6
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Draws the theme's focus ring around a frameless control while it has focus. The reader's read-only
/// editors draw no border, so their text lines up with the labels beside them, and a ring of their own
/// would sit on the text; this one sits the theme's ring offset outside the control instead.
/// </summary>
public sealed class FocusRingFrame : UiElement
{
    private readonly UiElement _content;

    public FocusRingFrame(UiElement content)
    {
        _content = content;
        AddChild(content);
    }

    /// <summary>The control the frame rings.</summary>
    public UiElement Content => _content;

    protected override BSize MeasureCore(BSize availableSize) => _content.Measure(availableSize);

    protected override void ArrangeCore(BRect finalRect) => _content.Arrange(finalRect);

    protected override void RenderCore(UiRenderContext context)
    {
        base.RenderCore(context);
        if (Session?.FocusedElement != _content || _content.Visibility != UiVisibility.Visible) return;
        double offset = StandardControlPaint.GetTheme(this).FocusRingOffset;
        BRect bounds = _content.Bounds;
        Draw(context, this, new BRect(bounds.X - offset, bounds.Y - offset, bounds.Width + (2 * offset), bounds.Height + (2 * offset)));
    }

    /// <summary>Strokes the focus ring of <paramref name="owner"/>'s theme along <paramref name="ring"/>.</summary>
    internal static void Draw(UiRenderContext context, UiElement owner, BRect ring)
    {
        var theme = StandardControlPaint.GetTheme(owner);
        if (ring.IsEmpty || theme.FocusRingThickness <= 0) return;
        context.RenderList.StrokeRect(ring, theme.FocusRing, theme.FocusRingThickness);
    }
}
