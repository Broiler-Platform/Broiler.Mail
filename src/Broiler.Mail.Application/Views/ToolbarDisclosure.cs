// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   10
// Annotated:        0/10
// Exempt:           2
// Human-reviewed:   0/10
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       10
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

internal sealed class ToolbarDisclosure : UiElement, IUiExpandable
{
    public ToolbarDisclosure(StandardButton button)
    {
        Button = button;
        Visibility = UiVisibility.Collapsed;
        AddChild(Content);
        button.Controls = this;
        button.Discloses = this;
        button.Clicked += (_, _) =>
        {
            if (IsExpanded) Collapse();
            else Expand();
        };
    }

    public StandardPanel Content { get; } = new() { Spacing = 4 };

    protected override BSize MeasureCore(BSize availableSize) => Content.Measure(availableSize);
    protected override void ArrangeCore(BRect finalRect) => Content.Arrange(finalRect);

    public StandardButton Button { get; }
    public bool IsExpanded => Visibility == UiVisibility.Visible;

    public bool Expand() => SetExpanded(true);
    public bool Collapse() => SetExpanded(false);

    private bool SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return false;
        if (!expanded && Session?.FocusedElement is { } focus && focus.IsDescendantOf(this))
            Session.SetFocus(Button);
        Visibility = expanded ? UiVisibility.Visible : UiVisibility.Collapsed;
        Button.Invalidate(UiInvalidationKind.Semantic);
        return true;
    }
}

internal static class MailToolbarButton
{
    public static StandardButton WithSymbol(string text, string symbol, string? shortcut = null)
    {
        var button = new StandardButton
        {
            Text = text,
            ToolTipText = shortcut is null ? text : $"{text} ({shortcut})",
            PreferredSize = new BSize(34, 34),
        };
        button.IconPainter = (render, box, color) =>
        {
            var font = button.Font;
            var size = BTextMeasurer.Measure(symbol, font).Size;
            var position = new BPoint(box.Left + (box.Width - size.Width) / 2, box.Top + (box.Height - size.Height) / 2);
            render.DrawText(StandardControlPaint.TextRun(symbol, font, color), position);
        };
        return button;
    }
}
