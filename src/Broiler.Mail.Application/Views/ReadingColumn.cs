// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   14
// Annotated:        0/14
// Exempt:           4
// Human-reviewed:   0/14
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       14
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A left-aligned reading column: margins that shrink before the content does, and a bounded
/// line length so prose stays readable in wide windows. It occupies the full available width,
/// so an enclosing scroll view still scrolls from anywhere in the pane.
/// </summary>
public sealed class ReadingColumn : UiElement
{
    /// <summary>About 80 characters of 17-DIP body text.</summary>
    public const double DefaultMaximumWidth = 720;
    public const double WideMargin = 24;
    public const double CompactMargin = 12;
    /// <summary>Below this width, compact margins leave more room for text.</summary>
    public const double CompactBelow = 480;

    private readonly UiElement _content;

    public ReadingColumn(UiElement content, double verticalMargin = 0)
    {
        _content = content;
        VerticalMargin = verticalMargin;
        AddChild(content);
    }

    public double MaximumWidth { get; init; } = DefaultMaximumWidth;
    public double VerticalMargin { get; }

    /// <summary>The least room kept between the text and a scroll bar that sits in the right margin.</summary>
    public const double ScrollBarGap = 8;

    /// <summary>
    /// The width of the whole pane the column is shown in, when a scroll view between them may take
    /// some of it for its scroll bar. The margins and the line length then follow the pane, and the scroll
    /// bar sits in the right margin where it fits, so the text keeps one width whether the bar is shown or
    /// not. A scroll view measures its content without and then with its bar, and text laid out again at
    /// each width costs a full rewrap of every line, on every selection and every frame of a resize. Where
    /// the margin cannot hold the bar, the column narrows as it does without a pane width.
    /// </summary>
    public Func<double>? PaneWidth { get; set; }

    /// <summary>The horizontal margin used for a given available width, so siblings can align with the column.</summary>
    public static double MarginFor(double width) => width < CompactBelow ? CompactMargin : WideMargin;

    private static double ContentWidth(double width, double maximumWidth) => Math.Max(0, Math.Min(width - 2 * MarginFor(width), maximumWidth));

    /// <summary>
    /// The text's width in a column given <paramref name="viewportWidth"/> inside a pane of <paramref name="paneWidth"/>,
    /// such as beside a scroll bar, so siblings can tell what fits on one line in it. Equal widths give the
    /// width without a bar.
    /// </summary>
    public static double ContentWidthIn(double paneWidth, double viewportWidth, double maximumWidth = DefaultMaximumWidth) =>
        Column(paneWidth, viewportWidth, maximumWidth).Width;

    /// <summary>The left margin and the text's width in an available width, following the pane where it can.</summary>
    private (double Margin, double Width) Column(double width) =>
        Column(PaneWidth?.Invoke() ?? double.PositiveInfinity, width, MaximumWidth);

    private static (double Margin, double Width) Column(double pane, double width, double maximumWidth)
    {
        if (double.IsFinite(pane) && double.IsFinite(width) && pane > width)
        {
            double margin = MarginFor(pane), content = ContentWidth(pane, maximumWidth);
            if (margin + content <= width - ScrollBarGap) return (margin, content);
        }
        return (MarginFor(width), ContentWidth(width, maximumWidth));
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        var content = _content.Measure(new BSize(Column(availableSize.Width).Width, Math.Max(0, availableSize.Height - 2 * VerticalMargin)));
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : content.Width + 2 * WideMargin;
        return new BSize(width, content.Height + 2 * VerticalMargin);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        var (margin, width) = Column(finalRect.Width);
        _content.Measure(new BSize(width, Math.Max(0, finalRect.Height - 2 * VerticalMargin)));
        _content.Arrange(new BRect(finalRect.X + margin, finalRect.Y + VerticalMargin,
            width, Math.Max(0, finalRect.Height - 2 * VerticalMargin)));
    }
}
