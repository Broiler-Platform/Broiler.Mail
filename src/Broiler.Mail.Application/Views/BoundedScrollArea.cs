// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   6
// Annotated:        0/6
// Exempt:           8
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
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Shows its content at its natural height up to a fraction of the space available, and scrolls the
/// rest. Docked areas such as the reader header then cannot push the main content off screen when
/// text is large (for example, at a 200 % system text size).
/// </summary>
public sealed class BoundedScrollArea : UiElement
{
    private readonly UiElement _content;
    private readonly StandardScrollView _scroll = new() { Constraint = UiScrollConstraint.ConstrainWidth };
    private double _maximumFraction;

    /// <param name="name">What a screen reader announces when the area scrolls and so takes focus.</param>
    public BoundedScrollArea(UiElement content, double maximumFraction, string name)
    {
        _content = content;
        MaximumFraction = maximumFraction;
        _scroll.AccessibleName = name;
        // A keyboard stop of its own, with a focus ring, while it scrolls and nothing inside can take focus.
        _scroll.FocusWhenScrollable = true;
        _scroll.AddChild(content);
        AddChild(_scroll);
    }

    /// <summary>The largest share of the available height the area takes.</summary>
    public double MaximumFraction
    {
        get => _maximumFraction;
        set
        {
            if (!(value > 0 && value <= 1)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _maximumFraction) return;
            _maximumFraction = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// The rows of the content, top to bottom, as it was last measured: where each starts and ends, in
    /// DIP from the content's top, whether the area may take more than its share to show it whole, and,
    /// for a row of text lines of one height, that height (0 for any other row). With them, the area
    /// does not end inside a row or a line. When its share would hide part of the content, the area
    /// shows the whole content if its rows leave <see cref="MinimumRemaining"/> of the space, or else
    /// ends below the first row from the share's end that may grow it, if that leaves the minimum, and
    /// below the gap between that row and the next, which may take from the minimum.
    /// Otherwise it ends above the row the share would cut, or below that row's last whole line, unless
    /// that leaves less than half its share: a row so near the top, such as Back to inbox in a very short
    /// window, is cut at the share instead. When the share is the whole space, nothing below would take
    /// what the area gives back, so the area takes its share and scrolls.
    /// </summary>
    public Func<IEnumerable<(double Start, double End, bool Grows, double LineHeight)>>? Rows { get; init; }

    /// <summary>
    /// The height the area leaves of the space when it takes more than its share, less the gap below a row
    /// it ends below to show that row whole (see <see cref="Rows"/>). Without it, the area takes no more
    /// than its share.
    /// </summary>
    public Func<double>? MinimumRemaining { get; init; }

    /// <summary>
    /// With <see cref="Rows"/>, the area never ends inside a line of text: where its share would cut the
    /// first line of the row it ends in, or end above its first row, it takes more to show that line whole,
    /// so a short notice still says something. Without it, such a row is cut at the share, as described there.
    /// </summary>
    public bool KeepsLinesWhole { get; init; }

    /// <summary>The scroll view that scrolls the content once it passes the cap.</summary>
    public StandardScrollView Scroll => _scroll;

    /// <summary>The height the area was last measured in, of which it takes its share.</summary>
    public double AvailableHeight { get; private set; }

    protected override BSize MeasureCore(BSize availableSize)
    {
        AvailableHeight = availableSize.Height;
        double natural = _content.Measure(new BSize(availableSize.Width, double.PositiveInfinity)).Height;
        double cap = double.IsFinite(availableSize.Height) ? availableSize.Height * MaximumFraction : double.PositiveInfinity;
        double height = Math.Min(natural, cap);
        _scroll.Measure(new BSize(availableSize.Width, height));
        if (natural > cap && Rows is not null && MaximumFraction < 1)
        {
            double limit = MinimumRemaining is null ? cap : Math.Max(cap, availableSize.Height - MinimumRemaining());
            height = EndBetweenRows(natural, cap, limit);
            if (height != cap) _scroll.Measure(new BSize(availableSize.Width, height));
        }
        return new BSize(double.IsFinite(availableSize.Width) ? availableSize.Width : _content.DesiredSize.Width, height);
    }

    // Measured at the cap, the content is laid out beside the scroll bar, as it is shown when it scrolls,
    // and the rows are where they are shown.
    private double EndBetweenRows(double natural, double cap, double limit)
    {
        var rows = Rows!().ToArray();
        // The whole content, at its natural height without the scroll bar. The margin below the last row
        // counts as part of that row: a scroll bar that only scrolls the margin would narrow every row.
        if (natural <= limit || (rows.Length > 0 && rows[^1].End <= limit)) return natural;
        int next = Array.FindIndex(rows, row => row.End > cap);
        if (next < 0) return cap;
        int grows = Array.FindIndex(rows, next, row => row.Grows);
        // Below the gap that separates the row from the next too, so the row is not flush with what follows
        // the area, such as the line above the message text.
        if (grows >= 0 && rows[grows].End <= limit) return grows + 1 < rows.Length ? rows[grows + 1].Start : rows[grows].End;
        var (start, _, _, lineHeight) = rows[next];
        bool keepsLine = KeepsLinesWhole && lineHeight > 0;
        // Between two rows the share cuts neither, unless it would show nothing of the first.
        if (start >= cap && !(keepsLine && next == 0)) return cap;
        double end = lineHeight > 0 ? start + (Math.Floor(Math.Max(0, cap - start) / lineHeight) * lineHeight) : start;
        if (keepsLine) return Math.Max(end, start + lineHeight);
        return end > cap / 2 ? end : cap;
    }

    protected override void ArrangeCore(BRect finalRect) => _scroll.Arrange(finalRect);
}
