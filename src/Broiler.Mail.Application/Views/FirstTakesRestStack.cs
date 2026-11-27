// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   6
// Annotated:        0/6
// Exempt:           4
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

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A vertical stack whose later children keep their natural height, and whose first child is measured
/// in the height they leave, so a first child that caps itself and scrolls, such as the reader's header,
/// cannot push the rows below it out of view. Children keep their order, which is the keyboard and
/// reading order.
/// </summary>
public sealed class FirstTakesRestStack : UiElement
{
    private readonly HashSet<UiElement> _bounded = [];

    public void Add(UiElement child) => AddChild(child);

    /// <summary>
    /// Adds a later child that is measured in the height the other later children leave, rather than at its
    /// natural height, so a child that caps itself and scrolls, such as the reader's banners, takes its share of
    /// that height and the first child is measured in what it leaves in turn.
    /// </summary>
    public void AddBounded(UiElement child)
    {
        AddChild(child);
        _bounded.Add(child);
    }

    /// <summary>
    /// Called with the space the stack is measured in, once its children are measured there. It may move
    /// an element within the stack, such as the reader's commands from below its header into it where they
    /// do not fit, and returns true when it did, so the children are measured again. It depends only on
    /// the space and on what the stack shows, so measuring again in the same space moves nothing.
    /// </summary>
    public Func<BSize, bool>? Fit { get; init; }

    /// <summary>
    /// The height the stack leaves below itself for what follows it, such as the rows a list keeps below the
    /// search header: the first child is measured in the height the later children and this leave, but never in
    /// less than half the height the later children leave, so a space too short for both still shows some of it.
    /// Without it, the first child has all the height the later children leave.
    /// </summary>
    public Func<double>? Reserve { get; init; }

    private IEnumerable<UiElement> Shown => Children.Where(child => child.Visibility != UiVisibility.Collapsed);

    protected override BSize MeasureCore(BSize availableSize)
    {
        var size = MeasureChildren(availableSize);
        return Fit?.Invoke(availableSize) == true ? MeasureChildren(availableSize) : size;
    }

    private BSize MeasureChildren(BSize availableSize)
    {
        var shown = Shown.ToArray();
        if (shown.Length == 0) return BSize.Empty;
        double width = availableSize.Width, below = 0, desiredWidth = 0;
        // The later children of natural height first, then the bounded ones in the height those leave.
        foreach (var child in shown.Skip(1).Where(child => !_bounded.Contains(child)).Concat(shown.Skip(1).Where(_bounded.Contains)))
        {
            double height = _bounded.Contains(child) && double.IsFinite(availableSize.Height)
                ? Math.Max(0, availableSize.Height - below) : double.PositiveInfinity;
            var size = child.Measure(new BSize(width, height));
            below += size.Height;
            desiredWidth = Math.Max(desiredWidth, size.Width);
        }
        double rest = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height - below) : double.PositiveInfinity;
        if (double.IsFinite(rest) && Reserve is not null) rest = Math.Max(rest / 2, rest - Math.Max(0, Reserve()));
        var first = shown[0].Measure(new BSize(width, rest));
        desiredWidth = Math.Max(desiredWidth, first.Width);
        return new BSize(double.IsFinite(width) ? width : desiredWidth, first.Height + below);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double y = finalRect.Y;
        foreach (var child in Children)
        {
            if (child.Visibility == UiVisibility.Collapsed)
            {
                child.Arrange(BRect.Empty);
                continue;
            }
            double height = Math.Max(0, Math.Min(child.DesiredSize.Height, finalRect.Bottom - y));
            child.Arrange(new BRect(finalRect.X, y, finalRect.Width, height));
            y += height;
        }
    }
}
