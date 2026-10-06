// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        0/3
// Exempt:           1
// Human-reviewed:   0/3
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A line in the theme's border color across the width it is given, which separates two areas such as
/// the reader's header and the message text. It follows theme changes.
/// </summary>
public sealed class Divider : UiElement
{
    public double Thickness { get; init; } = 1;

    protected override BSize MeasureCore(BSize availableSize) =>
        new(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, Thickness);

    protected override void RenderCore(UiRenderContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.RenderList.FillRect(Bounds, StandardControlPaint.GetTheme(this).Border);
    }
}
