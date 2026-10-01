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
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Wraps scroll content to the viewport width using standard viewport-constrained scrolling.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=D1F2BA
// Broiler-Falsified-If: a long wrapped label inside the scroll view is measured on one line wider than the viewport, forcing horizontal scrolling
// Broiler-Human:        PENDING
public sealed class ViewportScrollView : UiElement
{
    public StandardScrollView Scroll { get; } = new() { Constraint = UiScrollConstraint.ConstrainWidth };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=CABF7B
    // Broiler-Human:        PENDING
    public ViewportScrollView(UiElement content)
    {
        Scroll.AddChild(content);
        AddChild(Scroll);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=325701
    // Broiler-Falsified-If: a long wrapped label inside the scroll view is measured on one line wider than the viewport, forcing horizontal scrolling
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize) => Scroll.Measure(availableSize);

    protected override void ArrangeCore(BRect finalRect) => Scroll.Arrange(finalRect);
}
