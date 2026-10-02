using System;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Specialized list item presenter for Mail messages displaying sender, subject, received time, and read/unread status.
/// </summary>
public sealed class MailMessageItemPresenter(MessageDateFormatter? dates = null) : IUiListItemPresenter
{
    private readonly MessageDateFormatter _dates = dates ?? MessageDateFormatter.Default;
    public static readonly MailMessageItemPresenter Instance = new();
    private readonly StandardTwoLineListItemPresenter _twoLinePresenter = StandardTwoLineListItemPresenter.Instance;

    public double GetItemHeight(UiListItem? item, UiDensity density, double availableWidth) =>
        _twoLinePresenter.GetItemHeight(item, density, availableWidth);

    public void Render(UiListItemRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Item.Tag is MailMessageSummary message)
        {
            var displayItem = new UiListItem(
                context.Item.Id,
                message.Sender,
                message.Subject,
                _dates.List(message.ReceivedAt),
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
            string label = $"{unread}From: {message.Sender}, Subject: {message.Subject}, Received: {_dates.List(message.ReceivedAt)}";

            return new UiSemanticNode(
                UiSemanticRole.ListItem,
                label,
                context.Bounds,
                state,
                []);
        }

        return _twoLinePresenter.CreateSemanticNode(context);
    }

    public static string FormatTimestamp(DateTimeOffset? timestamp) => MessageDateFormatter.Default.List(timestamp);
}
