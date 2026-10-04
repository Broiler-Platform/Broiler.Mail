// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           0
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    Medium
// Criteria:         3/0
// Resource impact:  7/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Splitter.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=721087
// Broiler-Falsified-If: selecting another message leaves the HTML preview window of the previous body open
// Broiler-Human:        PENDING
public sealed class InboxView(InboxViewModel model, IHtmlPreviewHost? htmlPreview = null, MessageDateFormatter? dates = null, CompositionCommands? commands = null, SettingsViewModel? settings = null)
{
    private Func<bool>? _back;
    private Func<bool>? _open;

    /// <summary>In compact mode, returns from the reader to the list. False when there is nothing to go back from.</summary>
    public bool GoBackToList() => _back?.Invoke() == true;
    /// <summary>Reloads the selected message and, in compact mode, shows it in place of the list.</summary>
    public bool OpenSelected() => _open?.Invoke() == true;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=A38929
    // Broiler-Falsified-If: selecting another message leaves the HTML preview window of the previous body open
    // Broiler-Human:        PENDING
    public UiElement CreateContent()
    {
        var dateFormat = dates ?? MessageDateFormatter.Default;
        var panel = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var toolbar = new StandardToolbar
        {
            Overflow = UiToolbarOverflow.Wrap,
            Padding = 6,
            Spacing = 8,
            PreferredSize = new BSize(0, 36),
        };
        var receive = new StandardButton { Text = "Receive mail" };
        var older = new StandardButton { Text = "Load older" };
        var read = new StandardButton { Text = "Read message" };
        var cancel = new StandardButton { Text = "Cancel" };
        foreach (var button in new[] { receive, older, read, cancel }) toolbar.AddChild(button);
        panel.AddChild(toolbar);
        panel.SetDock(toolbar, UiDock.Top);
        var list = new StandardListView
        {
            PreferredSize = new BSize(320, 420),
            ItemPresenter = dates is null ? MailMessageItemPresenter.Instance : new MailMessageItemPresenter(dateFormat),
            Density = settings?.Settings.InboxDensity == InboxDensity.Compact ? UiDensity.Compact : UiDensity.Comfortable,
            // Otherwise the list is announced by its item count.
            AccessibleName = "Messages",
            // Refreshes keep the first visible row in place; at the top, new mail stays visible.
            EnableScrollAnchoring = true,
        };
        if (settings is not null)
            settings.Changed += (_, _) =>
            {
                if (list.IsDisposed) return;
                // Only committed settings change the view. Keep the previous first row visible;
                // changing spacing must not select/reload a message or recreate the reader.
                var density = settings.Settings.InboxDensity == InboxDensity.Compact ? UiDensity.Compact : UiDensity.Comfortable;
                if (density == list.Density) return;
                int first = list.FirstVisibleIndex;
                list.Density = density;
                list.ScrollIntoView(first);
            };
        // The list pane: a notice row (empty inbox, receiving, or a problem with Retry) above the list.
        var listPane = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var listNotice = new StandardPanel { Spacing = 4 };
        var listFeedback = new InlineFeedback();
        var listRetry = new StandardButton { Text = "Retry receiving" };
        var listRetryRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 0, Spacing = 8, PreferredSize = new BSize(0, 36) };
        listRetryRow.AddChild(listRetry);
        listNotice.AddChild(listFeedback);
        listNotice.AddChild(listRetryRow);
        // A long notice at a large text size scrolls instead of leaving the list no room.
        var noticeArea = new BoundedScrollArea(listNotice, 0.4, "Inbox notice");
        listPane.AddChild(noticeArea);
        listPane.SetDock(noticeArea, UiDock.Top);
        listPane.AddChild(list);
        listPane.SetDock(list, UiDock.Fill);
        var reading = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        bool updating = false;
        var split = new StandardSplitContainer
        {
            Orientation = UiSplitterOrientation.Vertical,
            FirstPane = listPane,
            SecondPane = reading,
            // Side by side, neither pane is dragged below the width the compact switch keeps readable;
            // the saved ratio is only clamped for display, so a wider window shows it again.
            FirstPaneMinimumSize = AdaptiveInboxLayout.ListReadableWidth,
            SecondPaneMinimumSize = AdaptiveInboxLayout.ReaderReadableWidth,
            SplitterFraction = model.SplitterFraction,
        };
        // Whether the reader is the pane being looked at. In compact mode only one pane shows, and
        // moving the list selection with the keyboard must not leave the list.
        bool readerOpen = false;
        MailMessageKey? selectedKey = null;
        var layout = new AdaptiveInboxLayout(split, () => readerOpen && model.SelectedMessage is not null, () => model.SplitterFraction);
        split.SplitterPositionChanged += (_, e) =>
        {
            // Collapsing a pane for compact mode, or a narrow width clamping the split to the panes'
            // minimum widths, must not overwrite the user's wide split ratio.
            if (!updating && !layout.IsAdapting && !layout.IsCompact)
                model.SplitterFraction = e.NewFraction;
        };
        panel.AddChild(layout);
        panel.SetDock(layout, UiDock.Fill);
        // Reader header: subject heading, selectable sender/recipient details, then a quieter date line.
        // The title style follows theme changes, including the system text size.
        var subjectLine = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false, TextStyle = StandardTextStyle.Title };
        var details = new StandardRichEdit
        {
            IsReadOnly = true, Wrapping = RichEditWrapping.Wrap, BorderThickness = 0, FocusRingThickness = 0, PaddingX = 0, PaddingY = 0,
            VerticalScrollPolicy = RichEditScrollPolicy.Never, HorizontalScrollPolicy = RichEditScrollPolicy.Never,
            AccessibleName = "Sender and recipients",
            Background = StandardControlPaint.Surface, Foreground = StandardControlPaint.Text,
        };
        var meta = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false, Role = StandardLabelRole.Muted };
        var replyActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 0, Spacing = 8, PreferredSize = new BSize(0, 36) };
        var reply = new StandardButton { Text = "Reply" };
        var replyAll = new StandardButton { Text = "Reply all" };
        var forward = new StandardButton { Text = "Forward" };
        foreach (var button in new[] { reply, replyAll, forward }) replyActions.AddChild(button);
        var previewActions = new StandardPanel();
        // A message that could not be loaded explains why beside its header and offers Retry there.
        var messageFeedback = new InlineFeedback();
        var messageRetry = new StandardButton { Text = "Retry loading" };
        var messageRetryRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 0, Spacing = 8, PreferredSize = new BSize(0, 36) };
        messageRetryRow.AddChild(messageRetry);
        var back = new StandardButton { Text = "Back to inbox" };
        var backRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 0, Spacing = 8, PreferredSize = new BSize(0, 36), Visibility = UiVisibility.Collapsed };
        backRow.AddChild(back);
        var headerStack = new StandardPanel { Spacing = 4 };
        foreach (var element in new UiElement[] { backRow, subjectLine, details, meta, messageFeedback, messageRetryRow, replyActions, previewActions }) headerStack.AddChild(element);
        // A long subject or many recipients at a large text size scroll within the header, so the
        // message text keeps most of the pane.
        var headerColumn = new BoundedScrollArea(new ReadingColumn(headerStack, verticalMargin: 8), 0.45, "Message header");
        reading.AddChild(headerColumn);
        reading.SetDock(headerColumn, UiDock.Top);
        var text = new ScrollableMessageText();
        reading.AddChild(text);
        IReadOnlyList<MailMessageSummary>? shown = null;
        MailMessageBody? shownBody = null;
        string? shownText = null;

        void Update()
        {
            if (panel.IsDisposed) return;
            updating = true;
            receive.IsEnabled = model.CanReceive;
            older.IsEnabled = model.CanLoadOlder;
            read.IsEnabled = !model.IsBusy && model.SelectedMessage is not null;
            cancel.IsEnabled = model.IsBusy;
            if (!layout.IsCompact && Math.Abs(split.SplitterFraction - model.SplitterFraction) > 0.001)
                split.SplitterFraction = model.SplitterFraction;
            if (!ReferenceEquals(shown, model.Messages))
            {
                shown = model.Messages;
                list.SetItems(shown.Select(message => new UiListItem(
                    Id(message),
                    $"{(message.IsRead ? "Read" : "Unread")} · {message.Subject} — {message.Sender}",
                    message.Subject,
                    dateFormat.Detail(message.ReceivedAt),
                    message.IsRead,
                    message)));
            }
            list.SelectedItemId = model.SelectedMessage is { } selected ? Id(selected) : null;
            var item = model.SelectedMessage;
            if (item?.Key != selectedKey)
            {
                selectedKey = item?.Key;
                // Side by side, choosing a message is reading it; resizing to compact keeps showing it.
                // A message that disappears returns a compact layout to the list.
                if (item is null) readerOpen = false;
                else if (!layout.IsCompact) readerOpen = true;
            }
            layout.Refresh();
            subjectLine.Text = item is null ? "Select a message to read." : item.Subject.Length > 0 ? item.Subject : "(No subject)";
            string detailText = item is null ? "" : string.Join("\n", HeaderDetails(item, model.Body is { } loaded && loaded.Key == item.Key ? loaded.Composition : null));
            if (detailText != details.GetPlainText()) details.SetPlainText(detailText);
            details.Visibility = item is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            meta.Text = item is null ? "" : $"Received {dateFormat.Detail(item.ReceivedAt)} · {(item.IsRead ? "Read" : "Unread")} on server";
            replyActions.Visibility = item is null || commands is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            reply.IsEnabled = replyAll.IsEnabled = forward.IsEnabled = commands?.CanRespond == true;
            ShowStates();
            var body = model.Body;
            string reader = body is null ? (model.SelectedMessage is null
                ? (model.Messages.Count == 0 ? "Receive mail to load your inbox." : "Choose a message from the inbox list.")
                : (model.IsLoadingMessage ? "Loading message body…" : ""))
                : (body.IsHtmlFallback ? "Text extracted from HTML (formatting omitted).\n\n" : "") + body.PlainText +
                  (body.IsTruncated ? "\n\n[Preview limited to 32,000 characters.]" : "");
            // Rewriting unchanged text would reset the reader's selection during unrelated updates.
            if (reader != shownText) text.Text = shownText = reader;
            if (!ReferenceEquals(shownBody, body))
            {
                htmlPreview?.Close();
                text.ScrollToStart();
                foreach (var child in previewActions.Children.ToArray()) { previewActions.RemoveChild(child); child.Dispose(); }
                if (htmlPreview is not null && body is not null && (body.HtmlText is not null || body.HtmlUnavailableReason is not null))
                    previewActions.AddChild(new HtmlMessagePreview(htmlPreview).CreateContent(body));
            }
            shownBody = body;
            updating = false;
        }

        receive.Clicked += async (_, _) => await model.ReceiveAsync();
        older.Clicked += async (_, _) => await model.LoadOlderAsync();
        bool listWasLoading = false;
        void ShowStates()
        {
            // List notice: a problem with Retry, progress while receiving, or why the list is empty.
            bool listProblem = model.ProblemScope == InboxProblemScope.List;
            (string text, FeedbackKind kind) notice =
                listProblem ? (model.Problem!, model.ProblemIsCancellation ? FeedbackKind.Information : FeedbackKind.Error)
                : model.IsLoadingList ? (model.Status, FeedbackKind.Progress)
                : model.Messages.Count == 0 ? (model.HasLoaded ? "The inbox is empty." : model.CanReceive ? "Receive mail to load your inbox." : "", FeedbackKind.Information)
                : ("", FeedbackKind.Information);
            listFeedback.Set(notice.text, notice.kind);
            // The notice announces progress and problems itself, then disappears once rows arrive;
            // say that receiving finished, or a screen reader user hears nothing after the progress.
            if (listWasLoading && !model.IsLoadingList && !listProblem && model.Messages.Count > 0)
                panel.Session?.AnnounceStatus(list, model.Messages.Count == 1 ? "1 message loaded." : $"{model.Messages.Count} messages loaded.");
            listWasLoading = model.IsLoadingList;
            listRetryRow.Visibility = listProblem ? UiVisibility.Visible : UiVisibility.Collapsed;
            listRetry.IsEnabled = model.CanRetry;
            bool messageProblem = model.ProblemScope == InboxProblemScope.Message && model.SelectedMessage is not null;
            messageFeedback.Set(messageProblem ? model.Problem! : "", model.ProblemIsCancellation ? FeedbackKind.Information : FeedbackKind.Error);
            messageRetryRow.Visibility = messageProblem ? UiVisibility.Visible : UiVisibility.Collapsed;
            messageRetry.IsEnabled = model.CanRetry;
            KeepFocusUsable();
        }
        void KeepFocusUsable()
        {
            // A Retry that disappears or a Cancel that is disabled must not keep keyboard focus.
            if (panel.Session is not { } session || session.FocusedElement is not { } focused) return;
            if (focused == listRetry && listRetryRow.Visibility != UiVisibility.Visible) session.SetFocus(list);
            else if (focused == messageRetry && messageRetryRow.Visibility != UiVisibility.Visible) session.SetFocus(text.Editor);
            else if (focused == cancel && !model.IsBusy) session.SetFocus(receive.IsEnabled ? receive : list);
            // Any other command that stays disabled once its work is done hands focus on; Load older
            // on the last page goes to the list it extended or, while the reader is shown alone, to
            // Back to inbox, which leads there.
            else if (!model.IsBusy) FocusNavigation.KeepFocusUsable(session, panel, focused != older ? null : layout.ShowsReaderOnly ? back : list);
        }
        listRetry.Clicked += async (_, _) => await model.RetryAsync();
        messageRetry.Clicked += async (_, _) => await model.RetryAsync();
        void FocusVisiblePane()
        {
            if (panel.Session is not { } session || !layout.IsCompact) return;
            UiElement hidden = layout.ShowsReaderOnly ? listPane : reading;
            if (session.FocusedElement is { } focused && (focused == hidden || focused.IsDescendantOf(hidden)))
                session.SetFocus(layout.ShowsReaderOnly ? text.Editor : list);
        }
        layout.ModeChanged += (_, _) =>
        {
            backRow.Visibility = layout.ShowsReaderOnly ? UiVisibility.Visible : UiVisibility.Collapsed;
            FocusVisiblePane();
        };
        bool OpenReader()
        {
            if (model.SelectedMessage is null) return false;
            readerOpen = true;
            layout.Refresh();
            FocusVisiblePane();
            return true;
        }
        _open = () =>
        {
            if (model.SelectedMessage is not { } item) return false;
            _ = model.SelectAsync(item.Key);
            return OpenReader();
        };
        _back = () =>
        {
            if (!layout.ShowsReaderOnly) return false;
            readerOpen = false;
            layout.Refresh();
            // The collapsed list kept its scroll offset, so the selected row is where the user left it.
            panel.Session?.SetFocus(list);
            return true;
        };
        back.Clicked += (_, _) => _back();
        layout.ListClicked += (_, _) => OpenReader();
        read.Clicked += async (_, _) =>
        {
            if (model.SelectedMessage is not { } item) return;
            var loading = model.SelectAsync(item.Key);
            OpenReader();
            await loading;
        };
        cancel.Clicked += (_, _) => model.Cancel();
        list.SelectionChanged += async (_, _) =>
        {
            if (!updating && !model.CanSelect) { Update(); return; }
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == list.SelectedItemId) is { } item)
                await model.SelectAsync(item.Key);
        };
        list.ItemActivated += async (_, e) =>
        {
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == e.Item.Id) is { } item)
            {
                var loading = model.SelectAsync(item.Key);
                OpenReader();
                await loading;
            }
        };
        if (commands is not null)
        {
            reply.Clicked += (_, _) => commands.Respond(CompositionKind.Reply);
            replyAll.Clicked += (_, _) => commands.Respond(CompositionKind.ReplyAll);
            forward.Clicked += (_, _) => commands.Respond(CompositionKind.Forward);
            // Reply availability also depends on the composer's draft state.
            commands.Changed += (_, _) => Update();
        }
        else model.Changed += (_, _) => Update();
        Update();
        return panel;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=C55E43
    // Broiler-Falsified-If: two loaded messages with different keys get the same Id, so choosing one row opens the other
    // Broiler-Human:        PENDING
    private static IEnumerable<string> HeaderDetails(MailMessageSummary message, MailCompositionSource? headers)
    {
        yield return $"From: {message.Sender}";
        if (headers is null) yield break;
        if (headers.To.Count > 0) yield return $"To: {string.Join(", ", headers.To)}";
        if (headers.Cc.Count > 0) yield return $"Cc: {string.Join(", ", headers.Cc)}";
    }

    private static string Id(MailMessageSummary message) => $"{message.Key.UidValidity}:{message.Key.Uid}";
}
