// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   16
// Annotated:        0/16
// Exempt:           2
// Human-reviewed:   0/16
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       16
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using System.Globalization;
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
public sealed class MailMessageItemPresenter(MessageDateFormatter? dates = null) : IUiListItemPresenter
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
            string date = RowDate(_dates.ListForms(message.ReceivedAt), message.Sender, unread, context.Bounds.Width, context.Font);
            var displayItem = new UiListItem(
                context.Item.Id,
                RowSender(message.Sender, date, unread, context.Bounds.Width, context.Font),
                message.Subject,
                date,
                message.IsRead,
                message);

            // Every color, font and state carries over, including the selected text colors a theme gives the
            // selection; a member-by-member copy dropped them, and a selected row then drew its text in Text.
            _twoLinePresenter.Render(context.WithItem(displayItem));
            return;
        }

        _twoLinePresenter.Render(context);
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
            string label = $"{unread}From: {message.Sender}, Subject: {message.Subject}, Received: {_dates.Detail(message.ReceivedAt)}";

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
    /// sender and the reader shows the address.
    /// </summary>
    public static string RowSender(string sender, string date, bool unread, double rowWidth, BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(font);
        string name = SenderName(sender);
        if (name.Length == sender.Length)
            return sender;
        double available = SenderSpace(unread, rowWidth);
        if (!string.IsNullOrEmpty(date))
            available -= BTextMeasurer.MeasureAdvance(date, DateFont(font)) + DateGap;
        return BTextMeasurer.MeasureAdvance(sender, SenderFont(unread, font)) <= available ? sender : name;
    }

    /// <summary>
    /// The longest of <paramref name="forms"/> (<see cref="MessageDateFormatter.ListForms"/>) that still
    /// leaves the sender's name its first few characters, or an empty string when even the shortest
    /// would crowd the sender out. The full date stays in the row's semantic name
    /// (<see cref="CreateSemanticNode"/>) and in the reader.
    /// </summary>
    public static string RowDate(IReadOnlyList<string> forms, string sender, bool unread, double rowWidth, BFontStyle font)
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
        double space = SenderSpace(unread, rowWidth);
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

    private static double SenderSpace(bool unread, double rowWidth) => rowWidth - 10 - (unread ? 12 : 0) - 8;

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
