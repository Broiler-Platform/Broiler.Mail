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
/// date rather than crowd the sender out; the accessible name always carries the full sender and
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

            var adapted = new UiListItemRenderContext
            {
                RenderList = context.RenderList,
                Bounds = context.Bounds,
                Item = displayItem,
                State = context.State,
                Font = context.Font,
                Foreground = context.Foreground,
                SecondaryForeground = context.SecondaryForeground,
                Background = context.Background,
                SelectedBackground = context.SelectedBackground,
                FocusRing = context.FocusRing,
                Accent = context.Accent,
                IsHighContrast = context.IsHighContrast,
            };

            _twoLinePresenter.Render(adapted);
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
    /// would crowd the sender out. The full date stays in the row's accessible name and in the reader.
    /// </summary>
    public static string RowDate(IReadOnlyList<string> forms, string sender, bool unread, double rowWidth, BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(font);
        var name = new StringInfo(SenderName(sender));
        string shortest = name.LengthInTextElements <= MinimumSenderCharacters ? name.String
            : name.SubstringByTextElements(0, MinimumSenderCharacters) + "...";
        // The presenter never starts the date closer than 20 DIP to the sender's left edge, so a date
        // that leaves less is pushed past the row's edge instead.
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
