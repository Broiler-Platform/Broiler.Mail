// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           1
// Human-reviewed:   0/4
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  4/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>Broiler.UI's tab control measures using its preferred size. Remeasure content at its actual allocation.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=14962F
// Broiler-Falsified-If: tab content is arranged at the tab control's preferred size instead of the rectangle it was given
// Broiler-Human:        PENDING
internal sealed class TabContent : UiElement
{
    private readonly UiElement _content;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=3DFDCF
    // Broiler-Human:        PENDING
    public TabContent(UiElement content) { _content = content; AddChild(content); }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=4; Fingerprint=64E638
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize) => _content.Measure(availableSize);
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=4; Fingerprint=D96FFC
    // Broiler-Falsified-If: tab content is arranged at the tab control's preferred size instead of the rectangle it was given
    // Broiler-Human:        PENDING
    protected override void ArrangeCore(BRect finalRect)
    {
        if (finalRect.IsEmpty) return;
        _content.Measure(finalRect.Size);
        _content.Arrange(finalRect);
    }
}
