// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           3
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>Measures dialog content at its actual client size, including after owner resizes.</summary>
internal sealed class MeasuredContent : UiElement
{
    private readonly UiElement _content;
    private readonly Action<BSize>? _arranging;
    private readonly Func<BSize?>? _measureLimit;

    public MeasuredContent(UiElement content, Action<BSize>? arranging = null, Func<BSize?>? measureLimit = null)
    {
        _content = content;
        _arranging = arranging;
        _measureLimit = measureLimit;
        AddChild(content);
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        // Owned windows are initially measured against their owner's whole viewport. Measuring
        // scrollable forms larger than the dialog would clamp their offsets before arrangement.
        if (_measureLimit?.Invoke() is { } limit)
            availableSize = new BSize(Math.Min(availableSize.Width, limit.Width), Math.Min(availableSize.Height, limit.Height));
        return _content.Measure(availableSize);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        _arranging?.Invoke(finalRect.Size);
        _content.Measure(finalRect.Size);
        _content.Arrange(finalRect);
    }
}
