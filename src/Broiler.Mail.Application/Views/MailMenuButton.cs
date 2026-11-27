// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   20
// Annotated:        0/20
// Exempt:           5
// Human-reviewed:   0/20
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       20
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A toolbar button that opens a popup menu anchored below it (above it near the bottom of the window), as the
/// reader's More message actions and Move do. The items are built each time it opens, so they follow the open
/// message. Escape or a click outside closes the popup and focus returns to the button; Tab closes it and moves on.
/// </summary>
internal sealed class MailMenuButton : UiElement, IUiExpandable
{
    private readonly Func<IEnumerable<UiMenuItem>> _items;

    public MailMenuButton(StandardButton trigger, Func<IEnumerable<UiMenuItem>> items, string menuName)
    {
        Trigger = trigger;
        _items = items;
        Menu = new StandardMenu { PresentationMode = UiMenuPresentationMode.ContextMenu, AccessibleName = menuName, PopupWidth = 220 };
        AddChild(Trigger);
        AddChild(Menu);
        trigger.Discloses = this;
        trigger.Controls = Menu;
        trigger.Clicked += (_, _) =>
        {
            if (IsExpanded) Collapse();
            else Expand();
        };
        // The menu gives focus back to the button after it raises ItemInvoked, so a command that moves focus,
        // such as Find's query or Keep message, runs after that.
        Menu.ItemInvoked += (_, e) =>
        {
            string id = e.Item.Id;
            if (Session is { } session) session.Dispatcher.Post(() => { if (!IsDisposed) Invoked?.Invoke(id); });
            else Invoked?.Invoke(id);
        };
        Menu.OpenStateChanged += (_, _) => Trigger.Invalidate(UiInvalidationKind.Semantic);
    }

    public StandardButton Trigger { get; }
    public StandardMenu Menu { get; }

    /// <summary>Raised with the chosen item's id, after the popup has closed and focus has returned.</summary>
    public event Action<string>? Invoked;

    public bool IsExpanded => !Menu.IsDisposed && Menu.IsOpen;

    public bool Expand()
    {
        if (IsDisposed || IsExpanded || !Trigger.IsEnabled || Visibility != UiVisibility.Visible) return false;
        var items = _items().ToArray();
        if (items.Length == 0) return false;
        // Focus returns to the button when the popup closes, also when it was opened without a click.
        Session?.SetFocus(Trigger);
        FollowFont();
        Menu.SetItems(items);
        return Menu.OpenPopup();
    }

    public bool Collapse() => IsExpanded && Menu.ClosePopup();

    /// <summary>
    /// Rows as tall as the menu's text needs, as the shell's menu bar has: StandardMenu keeps a fixed height
    /// otherwise, and larger text would run into the row below. Measuring follows a theme or text-size change.
    /// </summary>
    private void FollowFont()
    {
        double height = Math.Max(26, BTextMeasurer.GetLineHeight(Menu.Font) + 6);
        if (Menu.ItemHeight == height) return;
        Menu.ItemHeight = height;
        Menu.InvalidateMeasure();
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        FollowFont();
        Trigger.Measure(availableSize);
        Menu.Measure(availableSize);
        return Trigger.DesiredSize;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        Trigger.Arrange(finalRect);
        // A context menu paints no trigger of its own; its bounds place the popup below the button.
        Menu.Arrange(new BRect(finalRect.Left, finalRect.Bottom, finalRect.Width, 0));
        Menu.PopupAnchorBounds = finalRect;
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down)
            return false;
        if (IsExpanded && (input.NativeKeyCode == 0x09 || string.Equals(input.KeyName, "Tab", StringComparison.OrdinalIgnoreCase)))
        {
            // Mail's keyboard navigation closes the popup and moves on before the key gets here; where it is not in
            // place, Tab still closes the popup and is left for whatever moves focus.
            Collapse();
            return false;
        }
        bool f4 = input.KeyModifiers == KeyboardModifierState.None &&
            (input.NativeKeyCode == 0x73 || string.Equals(input.KeyName, "F4", StringComparison.OrdinalIgnoreCase));
        bool altDown = input.KeyModifiers == KeyboardModifierState.Alt &&
            (input.NativeKeyCode == 0x28 || string.Equals(input.KeyName, "Down", StringComparison.OrdinalIgnoreCase));
        if (!f4 && !altDown) return false;
        return IsExpanded ? Collapse() : Expand();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !Menu.IsDisposed && Menu.IsOpen) Menu.ClosePopup();
        base.Dispose(disposing);
    }

    /// <summary>A separator row, which <see cref="Tidy"/> drops at either end and where two would meet.</summary>
    public static UiMenuItem Separator(string id) => new(id, "") { IsSeparator = true, IsEnabled = false };

    /// <summary>The items without a separator at either end or next to another.</summary>
    public static IEnumerable<UiMenuItem> Tidy(IEnumerable<UiMenuItem> items)
    {
        UiMenuItem? pending = null;
        bool any = false;
        foreach (var item in items)
        {
            if (item.IsSeparator) { if (any) pending ??= item; continue; }
            if (pending is not null) { yield return pending; pending = null; }
            any = true;
            yield return item;
        }
    }
}

/// <summary>
/// The reader's subject with the star toggle in the gutter of its first line. The row is as tall as the subject's
/// lines, so the header can still end between them.
/// </summary>
internal sealed class SubjectRow : UiElement
{
    public const double Gap = 8;

    public SubjectRow(StandardLabel label, StandardButton star)
    {
        Label = label;
        Star = star;
        AddChild(label);
        AddChild(star);
    }

    public StandardLabel Label { get; }
    public StandardButton Star { get; }

    private double StarSize => Star.Visibility == UiVisibility.Collapsed ? 0 : BTextMeasurer.GetLineHeight(Label.Font);

    protected override BSize MeasureCore(BSize availableSize)
    {
        double star = StarSize;
        double reserved = star > 0 ? star + Gap : 0;
        Star.Measure(new BSize(star, star));
        Label.Measure(new BSize(Math.Max(0, availableSize.Width - reserved), availableSize.Height));
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : Label.DesiredSize.Width + reserved;
        return new BSize(width, Label.DesiredSize.Height);
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        double star = StarSize;
        double reserved = star > 0 ? star + Gap : 0;
        Label.Arrange(new BRect(finalRect.Left, finalRect.Top, Math.Max(0, finalRect.Width - reserved), Label.DesiredSize.Height));
        Star.Arrange(new BRect(Math.Max(finalRect.Left, finalRect.Right - star), finalRect.Top, star, star));
    }

    /// <summary>A star button whose glyph shows whether the message is starred; its text names what it does.</summary>
    public static StandardButton CreateStar(Func<bool> starred)
    {
        var button = new StandardButton { Text = "Star message", AccessibleName = "Star message", ToolTipText = "Star message", PaddingX = 0 };
        button.IconPainter = (render, box, color) =>
        {
            string glyph = starred() ? "★" : "☆";
            var font = button.Font;
            var size = BTextMeasurer.Measure(glyph, font).Size;
            var position = new BPoint(box.Left + (box.Width - size.Width) / 2, box.Top + (box.Height - size.Height) / 2);
            render.DrawText(StandardControlPaint.TextRun(glyph, font, color), position);
        };
        return button;
    }
}
