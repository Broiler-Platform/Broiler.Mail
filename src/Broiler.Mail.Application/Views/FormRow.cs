// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           6
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Arranges two form controls side-by-side with a proportional split and fixed spacing.
/// </summary>
public sealed class FormRow : UiElement
{
    private readonly UiElement _left;
    private readonly UiElement _right;
    private readonly double _leftFraction;
    private readonly double _spacing;

    public FormRow(UiElement left, UiElement right, double leftFraction = 0.5, double spacing = 12)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _leftFraction = Math.Clamp(leftFraction, 0.05, 0.95);
        _spacing = Math.Max(0, spacing);
        AddChild(left);
        AddChild(right);
    }

    public UiElement Left => _left;
    public UiElement Right => _right;

    protected override BSize MeasureCore(BSize availableSize)
    {
        bool leftVisible = _left.Visibility != UiVisibility.Collapsed;
        bool rightVisible = _right.Visibility != UiVisibility.Collapsed;

        if (!leftVisible && !rightVisible)
            return BSize.Empty;
        if (!leftVisible)
            return _right.Measure(availableSize);
        if (!rightVisible)
            return _left.Measure(availableSize);

        double width = availableSize.Width;
        if (!double.IsFinite(width))
        {
            var leftSize = _left.Measure(availableSize);
            var rightSize = _right.Measure(availableSize);
            return new BSize(leftSize.Width + rightSize.Width + _spacing, Math.Max(leftSize.Height, rightSize.Height));
        }

        double availableWidth = Math.Max(0, width - _spacing);
        double leftWidth = Math.Round(availableWidth * _leftFraction);
        double rightWidth = Math.Max(0, availableWidth - leftWidth);

        var lSize = _left.Measure(new BSize(leftWidth, availableSize.Height));
        var rSize = _right.Measure(new BSize(rightWidth, availableSize.Height));

        return new BSize(width, Math.Max(lSize.Height, rSize.Height));
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        bool leftVisible = _left.Visibility != UiVisibility.Collapsed;
        bool rightVisible = _right.Visibility != UiVisibility.Collapsed;

        if (!leftVisible && !rightVisible)
        {
            _left.Arrange(BRect.Empty);
            _right.Arrange(BRect.Empty);
            return;
        }
        if (!leftVisible)
        {
            _left.Arrange(BRect.Empty);
            _right.Arrange(finalRect);
            return;
        }
        if (!rightVisible)
        {
            _right.Arrange(BRect.Empty);
            _left.Arrange(finalRect);
            return;
        }

        double availableWidth = Math.Max(0, finalRect.Width - _spacing);
        double leftWidth = Math.Round(availableWidth * _leftFraction);
        double rightWidth = Math.Max(0, availableWidth - leftWidth);

        _left.Arrange(new BRect(finalRect.Left, finalRect.Top, leftWidth, finalRect.Height));
        _right.Arrange(new BRect(finalRect.Left + leftWidth + _spacing, finalRect.Top, rightWidth, finalRect.Height));
    }
}
