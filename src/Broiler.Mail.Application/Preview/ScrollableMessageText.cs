// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   9
// Annotated:        9/9
// Exempt:           5
// Human-reviewed:   0/9
// IP risk:          Low
// Security risk:    Medium
// Criteria:         6/0
// Resource impact:  4/10 max
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Constrains text measurement to the pane width inside Broiler.UI's unconstrained scroll content.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=7CBBCE
// Broiler-Falsified-If: message text containing an ampersand is displayed with it dropped or turned into an access-key underline
// Broiler-Human:        PENDING
public sealed class ScrollableMessageText : UiElement
{
    private readonly StandardLabel _label = new() { Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
    private readonly StandardScrollView _scroll = new();
    private readonly WrappedContent _content;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=A24DF7
    // Broiler-Human:        PENDING
    public ScrollableMessageText()
    {
        _content = new WrappedContent(_label);
        _scroll.AddChild(_content);
        AddChild(_scroll);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=050DA5
    // Broiler-Falsified-If: message text containing an ampersand is displayed with it dropped or turned into an access-key underline
    // Broiler-Human:        PENDING
    public string Text { get => _label.DisplayText; set => _label.Text = value.Replace("&", "&&", StringComparison.Ordinal); }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=AB27B5
    // Broiler-Human:        PENDING
    public void ScrollToStart() => _scroll.ScrollToStart();

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=884914
    // Broiler-Falsified-If: an infinite available width reaches the label as its wrap width, so long message lines never wrap
    // Broiler-Human:        PENDING
    protected override BSize MeasureCore(BSize availableSize)
    {
        _content.Width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - 12) : 520;
        return _scroll.Measure(availableSize);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=3083D9
    // Broiler-Falsified-If: the label is measured against the scroll view's unconstrained width instead of Width, so message text wider than the pane does not wrap
    // Broiler-Human:        PENDING
    private sealed class WrappedContent : UiElement
    {
        private readonly StandardLabel _label;
        // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=13A113
        // Broiler-Human:        PENDING
        public WrappedContent(StandardLabel label) { _label = label; AddChild(label); }
        public double Width { get; set; } = 520;
        // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=33FF25
        // Broiler-Falsified-If: the label is measured against the scroll view's unconstrained width instead of Width, so message text wider than the pane does not wrap
        // Broiler-Human:        PENDING
        protected override BSize MeasureCore(BSize availableSize) => _label.Measure(new BSize(Width, double.PositiveInfinity));
        // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=6B727E
        // Broiler-Falsified-If: the label is arranged wider than Width, so wrapped lines extend past the right edge of the pane
        // Broiler-Human:        PENDING
        protected override void ArrangeCore(BRect finalRect) => _label.Arrange(new BRect(finalRect.X, finalRect.Y, Width, finalRect.Height));
    }
}
