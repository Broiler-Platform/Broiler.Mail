// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// The paperclip a message row shows when the message has attachments: two nested rounded outlines, drawn
/// as vectors rather than taken from a font, so it looks the same with every platform font and text size.
/// </summary>
public static class AttachmentGlyph
{
    /// <summary>The glyph's width beside text in <paramref name="font"/>; it grows with the text size.</summary>
    public static double Width(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return Math.Max(5, Math.Round(font.Size * 0.5));
    }

    /// <summary>The glyph's height beside text in <paramref name="font"/>, never taller than its line.</summary>
    public static double Height(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return Math.Min(BTextMeasurer.GetLineHeight(font), Math.Round(Width(font) * 1.8));
    }

    /// <summary>Draws the paperclip inside <paramref name="bounds"/>: an outer loop and a shorter inner one.</summary>
    public static void Paint(BRenderList list, BRect bounds, BColor color)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double thickness = Math.Max(1, Math.Round(bounds.Width / 5));
        double outerRadius = Math.Min(bounds.Width, bounds.Height) / 2;
        list.StrokeRoundedRect(bounds, color, outerRadius, outerRadius, thickness);
        var inner = new BRect(bounds.Left + bounds.Width * 0.3, bounds.Top + bounds.Height * 0.25, bounds.Width * 0.4, bounds.Height * 0.55);
        double innerRadius = Math.Min(inner.Width, inner.Height) / 2;
        list.StrokeRoundedRect(inner, color, innerRadius, innerRadius, thickness);
    }
}
