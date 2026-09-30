// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   7
// Annotated:        7/7
// Exempt:           4
// Human-reviewed:   0/7
// IP risk:          Low
// Security risk:    Low
// Criteria:         4/0
// Resource impact:  4/10 max
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Wraps scroll content to the viewport width instead of measuring long labels on one line.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=D1F2BA
// Broiler-Falsified-If: a long wrapped label inside the scroll view is measured on one line wider than the viewport, forcing horizontal scrolling
// Broiler-Human:        PENDING
public sealed class ViewportScrollView : UiElement
{
    public StandardScrollView Scroll { get; } = new();
    private readonly Content _content;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=CABF7B
    // Broiler-Human:        PENDING
    public ViewportScrollView(UiElement content)
    {
        _content = new Content(content);
        Scroll.AddChild(_content);
        AddChild(Scroll);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=325701
    // Broiler-Falsified-If: a long wrapped label inside the scroll view is measured on one line wider than the viewport, forcing horizontal scrolling
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.Width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - Scroll.ScrollbarThickness) : 520;
        return Scroll.Measure(availableSize);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=BA76DB
    // Broiler-Falsified-If: the child is measured or arranged at a width other than the viewport width set by MeasureCore
    // Broiler-Human:        PENDING
    private sealed class Content : UiElement
    {
        private readonly UiElement _child;
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=6B55C5
        // Broiler-Human:        PENDING
        public Content(UiElement child) { _child = child; AddChild(child); }
        public double Width { get; set; } = 520;
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=4; Fingerprint=D766FA
        // Broiler-Falsified-If: the child is measured with an unbounded width, so its labels do not wrap
        // Broiler-Human:        PENDING
        protected override BSize MeasureCore(BSize availableSize) => _child.Measure(new BSize(Width, double.PositiveInfinity));
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=4; Fingerprint=081C07
        // Broiler-Human:        PENDING
        protected override void ArrangeCore(BRect finalRect) => _child.Arrange(new BRect(finalRect.X, finalRect.Y, Width, finalRect.Height));
    }
}
