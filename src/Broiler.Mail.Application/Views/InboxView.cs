using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

public sealed class InboxView(InboxViewModel model)
{
    public UiElement CreateContent()
    {
        var panel = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var toolbar = new StandardPanel { StackOrientation = UiStackOrientation.Horizontal, Spacing = 8 };
        var receive = new StandardButton { Text = "Receive mail" };
        var older = new StandardButton { Text = "Load older" };
        var read = new StandardButton { Text = "Read message" };
        var cancel = new StandardButton { Text = "Cancel" };
        foreach (var button in new[] { receive, older, read, cancel }) toolbar.AddChild(button);
        panel.AddChild(toolbar);
        panel.SetDock(toolbar, UiDock.Top);
        var list = new StandardListView { PreferredSize = new BSize(320, 420) };
        panel.AddChild(list);
        panel.SetDock(list, UiDock.Left);
        var reading = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        panel.AddChild(reading);
        var header = new StandardLabel { Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
        reading.AddChild(header);
        reading.SetDock(header, UiDock.Top);
        var text = new ScrollableMessageText();
        reading.AddChild(text);
        IReadOnlyList<MailMessageSummary>? shown = null;
        bool updating = false;
        MailMessageBody? shownBody = null;

        void Update()
        {
            if (panel.IsDisposed) return;
            updating = true;
            receive.IsEnabled = model.CanReceive;
            older.IsEnabled = model.CanLoadOlder;
            read.IsEnabled = !model.IsBusy && model.SelectedMessage is not null;
            cancel.IsEnabled = model.IsBusy;
            if (!ReferenceEquals(shown, model.Messages))
            {
                shown = model.Messages;
                list.SetItems(shown.Select(message => new UiListItem(Id(message),
                    $"{(message.IsRead ? "Read" : "Unread")} · {message.Subject} — {message.Sender}")));
            }
            list.SelectedItemId = model.SelectedMessage is { } selected ? Id(selected) : null;
            header.Text = (model.SelectedMessage is { } item
                ? $"{item.Subject}\nFrom: {item.Sender}\nReceived: {item.ReceivedAt?.ToLocalTime().ToString("g") ?? "Unknown"} · {(item.IsRead ? "Read" : "Unread")} on server"
                : "Select a message to read.").Replace("&", "&&", StringComparison.Ordinal);
            var body = model.Body;
            text.Text = body is null ? (model.SelectedMessage is null
                ? (model.Messages.Count == 0 ? "Receive mail to load your inbox." : "Choose a message from the inbox list.")
                : "Use Read message to retry if loading is canceled or fails.")
                : (body.IsHtmlFallback ? "Text extracted from HTML (formatting omitted).\n\n" : "") + body.PlainText +
                  (body.IsTruncated ? "\n\n[Preview limited to 32,000 characters.]" : "");
            if (!ReferenceEquals(shownBody, body)) text.ScrollToStart();
            shownBody = body;
            updating = false;
        }

        receive.Clicked += async (_, _) => await model.ReceiveAsync();
        older.Clicked += async (_, _) => await model.LoadOlderAsync();
        read.Clicked += async (_, _) => { if (model.SelectedMessage is { } item) await model.SelectAsync(item.Key); };
        cancel.Clicked += (_, _) => model.Cancel();
        list.SelectionChanged += async (_, _) =>
        {
            if (!updating && !model.CanSelect) { Update(); return; }
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == list.SelectedItemId) is { } item)
                await model.SelectAsync(item.Key);
        };
        model.Changed += (_, _) => Update();
        Update();
        return panel;
    }

    private static string Id(MailMessageSummary message) => $"{message.Key.UidValidity}:{message.Key.Uid}";
}
