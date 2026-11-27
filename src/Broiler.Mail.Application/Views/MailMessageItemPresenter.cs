// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   25
// Annotated:        0/25
// Exempt:           5
// Human-reviewed:   0/25
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       25
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using System.Globalization;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Specialized list item presenter for Mail messages displaying sender, subject, received time, and read/unread status.
/// A narrow row drops the sender's address before shortening the name, and shortens or leaves out the
/// date rather than crowd the sender out; the row's semantic name always carries the full sender and
/// received time.
/// </summary>
public sealed class MailMessageItemPresenter(MessageDateFormatter? dates = null, bool showMailbox = false) : IUiListItemPresenter
{
    private readonly MessageDateFormatter _dates = dates ?? MessageDateFormatter.Default;
    public static readonly MailMessageItemPresenter Instance = new();
    private readonly StandardTwoLineListItemPresenter _twoLinePresenter = StandardTwoLineListItemPresenter.Instance;

    public double GetItemHeight(UiListItem? item, UiDensity density, double availableWidth) =>
        _twoLinePresenter.GetItemHeight(item, density, availableWidth);

    // Rows grow with the list's font, for example at a larger system text size.
    public double GetItemHeight(UiListItem? item, UiDensity density, double availableWidth, Broiler.Graphics.Text.BFontStyle font) =>
        _twoLinePresenter.GetItemHeight(item, density, availableWidth, font);

    public void Render(UiListItemRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Item.Tag is MailMessageSummary message)
        {
            bool unread = !context.State.IsRead;
            var row = DisplayItem(context.Item.Id, message, unread, context.Bounds.Width, context.Font);

            // Every color, font and state carries over, including the selected text colors a theme gives the
            // selection; a member-by-member copy dropped them, and a selected row then drew its text in Text.
            _twoLinePresenter.Render(context.WithItem(row.Item));
            if (message.HasAttachments)
                PaintAttachmentBadge(context, row, unread);
            return;
        }

        _twoLinePresenter.Render(context);
    }

    // The paperclip sits on line 1 in the room the sender left for it: DateGap before the date, or 8 DIP
    // before the edge when the row drew no date, and AttachmentGap after the (already shortened) sender.
    // The line's top mirrors StandardTwoLineListItemPresenter's private TopPadding (4 / 6 / 10 DIP).
    private static void PaintAttachmentBadge(UiListItemRenderContext context, Row row, bool unread)
    {
        BRect bounds = context.Bounds;
        BFontStyle font = context.Font;
        double width = AttachmentGlyph.Width(font);
        double height = AttachmentGlyph.Height(font);
        double right = bounds.Right - 8 - (row.DateAdvance > 0 ? row.DateAdvance + DateGap : 0);
        double left = right - width;
        if (left < bounds.Left + 10 + (unread ? 12 : 0)) return;
        double top = bounds.Top + TopPadding(context.State.Density);
        top += Math.Max(0, (BTextMeasurer.GetLineHeight(font) - height) / 2);
        bool selected = context.State.IsSelected;
        BColor color = context.IsHighContrast
            ? (selected ? context.SelectedForeground : context.Foreground)
            : selected ? context.SelectedSecondaryForeground : context.SecondaryForeground;
        AttachmentGlyph.Paint(context.RenderList, new BRect(left, top, width, height), color);
    }

    private static double TopPadding(UiDensity density) => density switch
    {
        UiDensity.Compact => 4,
        UiDensity.Spacious => 10,
        _ => 6,
    };

    // Every visible row is drawn on every frame, and its texts only change with the message, its read
    // state, the row's width or font, the day, or the culture. Formatting the date and measuring the
    // sender and date for each row again took a fifth of a selection's frame.
    private const int RowCacheLimit = 512;
    private readonly Dictionary<RowKey, Row> _rows = [];

    // The drawn item and its date's advance (0 without a date), which places the attachment badge.
    private sealed record Row(UiListItem Item, double DateAdvance);
    private readonly Lock _rowsLock = new();

    private readonly record struct RowKey(MailMessageSummary Message, string Id, bool Unread, double Width, BFontStyle Font, DateTime Today, CultureInfo Culture)
    {
        // A message is a record whose equality compares every member; the row only needs this instance.
        public bool Equals(RowKey other) => ReferenceEquals(Message, other.Message) && Id == other.Id && Unread == other.Unread
            && Width.Equals(other.Width) && Font.Equals(other.Font) && Today == other.Today && ReferenceEquals(Culture, other.Culture);
        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Message), Id, Unread, Width, Font, Today);
    }

    private Row DisplayItem(string id, MailMessageSummary message, bool unread, double width, BFontStyle font)
    {
        var (today, culture) = _dates.ListContext;
        var key = new RowKey(message, id, unread, width, font, today, culture);
        lock (_rowsLock)
            if (_rows.TryGetValue(key, out var cached)) return cached;
        bool attachments = message.HasAttachments;
        string date = RowDate(_dates.ListForms(message.ReceivedAt), message.Sender, unread, width, font, attachments);
        double dateAdvance = string.IsNullOrEmpty(date) ? 0 : BTextMeasurer.MeasureAdvance(date, DateFont(font));
        string sender = RowSender(message.Sender, date, unread, width, font, attachments);
        // The two-line row only leaves the date room, so the sender is shortened here to leave the badge
        // its own room as well; the row then draws it unchanged.
        if (attachments)
            sender = DefaultListItemPresenter.TruncateWithEllipsis(sender, SenderFont(unread, font),
                Math.Max(0, SenderSpace(unread, width) - (dateAdvance > 0 ? dateAdvance + DateGap : 0) - BadgeSpace(font)));
        var item = new UiListItem(
            id,
            sender,
            (showMailbox ? "[" + message.Key.MailboxId + "] " : "") + (message.IsStarred ? "★ " : "") + message.Subject,
            date,
            message.IsRead,
            message);
        var row = new Row(item, dateAdvance);
        lock (_rowsLock)
        {
            // Bounded: a resize or a new page starts again rather than keeping every width ever drawn.
            if (_rows.Count >= RowCacheLimit) _rows.Clear();
            _rows[key] = row;
        }
        return row;
    }

    public UiSemanticNode CreateSemanticNode(UiListItemSemanticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Item.Tag is MailMessageSummary message)
        {
            UiSemanticState state = UiSemanticState.Visible | UiSemanticState.Enabled;
            if (context.State.IsSelected)
                state |= UiSemanticState.Selected;
            if (context.State.IsFocused)
                state |= UiSemanticState.Focused;

            string unread = message.IsRead ? string.Empty : "Unread, ";
            string attachments = message.HasAttachments ? "Has attachments, " : string.Empty;
            string label = (showMailbox ? $"Folder: {message.Key.MailboxId}, " : "") + $"{(message.IsStarred ? "Starred, " : "")}{unread}{attachments}From: {message.Sender}, Subject: {message.Subject}, Received: {_dates.Detail(message.ReceivedAt)}";

            return new UiSemanticNode(
                UiSemanticRole.ListItem,
                label,
                context.Bounds,
                state,
                []);
        }

        return _twoLinePresenter.CreateSemanticNode(context);
    }

    /// <summary>
    /// The sender as the row shows it: in full while it fits beside <paramref name="date"/>, otherwise
    /// only the display name (which the row then shortens if needed), since the name identifies the
    /// sender and the reader shows the address. With <paramref name="attachments"/> the attachment badge
    /// takes its room first.
    /// </summary>
    public static string RowSender(string sender, string date, bool unread, double rowWidth, BFontStyle font, bool attachments = false)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(font);
        string name = SenderName(sender);
        if (name.Length == sender.Length)
            return sender;
        double available = SenderSpace(unread, rowWidth) - (attachments ? BadgeSpace(font) : 0);
        if (!string.IsNullOrEmpty(date))
            available -= BTextMeasurer.MeasureAdvance(date, DateFont(font)) + DateGap;
        return BTextMeasurer.MeasureAdvance(sender, SenderFont(unread, font)) <= available ? sender : name;
    }

    /// <summary>
    /// The longest of <paramref name="forms"/> (<see cref="MessageDateFormatter.ListForms"/>) that still
    /// leaves the sender's name its first few characters, or an empty string when even the shortest
    /// would crowd the sender out. The full date stays in the row's semantic name
    /// (<see cref="CreateSemanticNode"/>) and in the reader. With <paramref name="attachments"/> the
    /// attachment badge keeps its room, so the date shortens or drops before the sender does.
    /// </summary>
    public static string RowDate(IReadOnlyList<string> forms, string sender, bool unread, double rowWidth, BFontStyle font, bool attachments = false)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(font);
        // The name's first few characters (whole text elements), as the row shortens it.
        string name = SenderName(sender);
        int end = 0;
        for (int count = 0; count < MinimumSenderCharacters && end < name.Length; count++)
            end += StringInfo.GetNextTextElementLength(name, end);
        string shortest = end >= name.Length ? name : string.Concat(name.AsSpan(0, end), "...");
        // The two-line presenter draws a date whole or not at all: only when it leaves the sender its first
        // three characters and an ellipsis, and at least 10 DIP. Four characters ask more, so a form chosen
        // here is always drawn, and an empty one leaves the line to the sender, as the presenter would.
        double needed = Math.Max(BTextMeasurer.MeasureAdvance(shortest, SenderFont(unread, font)), DateMinimumOffset - DateGap);
        double space = SenderSpace(unread, rowWidth) - (attachments ? BadgeSpace(font) : 0);
        BFontStyle dateFont = DateFont(font);
        foreach (string form in forms)
            if (space - BTextMeasurer.MeasureAdvance(form, dateFont) - DateGap >= needed)
                return form;
        return string.Empty;
    }

    // Line 1 of StandardTwoLineListItemPresenter: 10 DIP inset, the unread dot (6 + 6), and the date
    // right-aligned in a font 2 DIP smaller with 8 DIP to the edge and 10 DIP before it.
    private const double DateGap = 10;
    private const double DateMinimumOffset = 20;
    private const int MinimumSenderCharacters = 4;

    // The attachment badge and the gap between it and the sender.
    private const double AttachmentGap = 6;

    private static double SenderSpace(bool unread, double rowWidth) => rowWidth - 10 - (unread ? 12 : 0) - 8;

    private static double BadgeSpace(BFontStyle font) => AttachmentGlyph.Width(font) + AttachmentGap;

    private static BFontStyle DateFont(BFontStyle font) => font with { Size = Math.Max(9, font.Size - 2) };

    private static BFontStyle SenderFont(bool unread, BFontStyle font) => unread ? font with { Weight = BFontWeight.Bold } : font;

    /// <summary>
    /// The display name of a single <c>Name &lt;address&gt;</c> sender, unquoted; otherwise the sender
    /// unchanged (a bare address, a group, or several addresses).
    /// </summary>
    public static string SenderName(string sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        string trimmed = sender.Trim();
        int open = trimmed.LastIndexOf('<');
        if (open <= 0 || !trimmed.EndsWith('>') || trimmed.IndexOf('>') != trimmed.Length - 1)
            return sender;
        string name = trimmed[..open].Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            name = name[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal).Trim();
        else if (name.Contains('"', StringComparison.Ordinal) || name.Contains(',', StringComparison.Ordinal))
            return sender;
        return name.Length == 0 ? sender : name;
    }

    public static string FormatTimestamp(DateTimeOffset? timestamp) => MessageDateFormatter.Default.List(timestamp);
}
