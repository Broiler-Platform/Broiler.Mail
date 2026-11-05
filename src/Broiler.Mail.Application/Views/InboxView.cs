// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   9
// Annotated:        3/9
// Exempt:           3
// Human-reviewed:   0/9
// IP risk:          Low
// Security risk:    Medium
// Criteria:         3/0
// Resource impact:  7/10 max
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
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

// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=AC6BE6
// Broiler-Falsified-If: selecting another message leaves the HTML preview window of the previous body open
// Broiler-Human:        PENDING
public sealed class InboxView(InboxViewModel model, IInlineHtmlPreviewHost? htmlPreview = null, MessageDateFormatter? dates = null, CompositionCommands? commands = null, SettingsViewModel? settings = null)
{
    private Func<bool>? _back;
    private Func<bool>? _open;
    private Func<UiInputEvent, bool>? _readerInput;
    public bool HandleReaderInput(UiInputEvent input) => _readerInput?.Invoke(input) == true;
    private AdaptiveInboxLayout? _layout;

    /// <summary>The label of the compact reader's way back to the list, which the footer names.</summary>
    public const string BackText = "Back to inbox";

    /// <summary>In compact mode, returns from the reader to the list. False when there is nothing to go back from.</summary>
    public bool GoBackToList() => _back?.Invoke() == true;
    /// <summary>Reloads the selected message and, in compact mode, shows it in place of the list.</summary>
    public bool OpenSelected() => _open?.Invoke() == true;

    /// <summary>
    /// Raised when compact mode hides the list or the reader, or shows it again. A window resize
    /// raises it during layout.
    /// </summary>
    public event EventHandler? PanesChanged;

    /// <summary>
    /// Whether the pane that explains a problem of <paramref name="scope"/> is on screen: the list
    /// pane for the list, the reader for the message. Compact mode shows only one of them.
    /// </summary>
    public bool ShowsPaneOf(InboxProblemScope scope) => _layout is not { IsCompact: true } layout
        || (scope == InboxProblemScope.Message ? layout.ShowsReaderOnly : !layout.ShowsReaderOnly);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=146B5E
    // Broiler-Falsified-If: selecting another message leaves the HTML preview window of the previous body open
    // Broiler-Human:        PENDING
    public UiElement CreateContent()
    {
        var dateFormat = dates ?? MessageDateFormatter.Default;
        var panel = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        bool updating = false;
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
        var download = new StandardButton { Text = "Download loaded messages" };
        if (model.HasPersistentCache) toolbar.AddChild(download);
        var folders = new StandardComboBox { AccessibleName = "Mail folder", PreferredSize = new BSize(240, 32) };
        var refreshFolders = new StandardButton { Text = "Refresh folders" };
        IReadOnlyList<MailFolder>? shownFolders = null;
        if (model.SupportsFolders) { toolbar.AddChild(folders); toolbar.AddChild(refreshFolders); }
        var offlineToggle = new StandardButton { Text = "Select this folder for offline batches" };
        var offlineDownload = new StandardButton { Text = "Download selected offline folders" };
        var offlineRestart = new StandardButton { Text = "Restart offline batches from newest" };
        var offlineHeaders = new StandardButton { Text = "Receive selected-folder headers" };
        var offlineShowHeaders = new StandardButton { Text = "Show saved headers" };
        var offlineHeaderFeedback = new InlineFeedback();
        var offlineHeadersRestart = new StandardButton { Text = "Restart selected-folder headers" };
        var offlineReload = new StandardButton { Text = "Reload offline-folder selections" };
        var offlineSection = new FormSection("Offline folders", "Selections are local to the current account. Receive header pages explicitly, then download saved-header body batches separately.", collapsible: true, expanded: false)
            { ShowText = "Show offline folders", HideText = "Hide offline folders" };
        offlineSection.Content.AccessibleName = "Offline-folder batch controls";
        var offlineActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var captureSnapshot = new StandardButton { Text = "Snapshot all headers in current folder" };
        var inspectSnapshot = new StandardButton { Text = "Inspect saved header snapshot" };
        var snapshotFeedback = new InlineFeedback();
        if (model.SupportsMailboxSnapshots) { offlineActions.AddChild(captureSnapshot); offlineActions.AddChild(inspectSnapshot); }
        captureSnapshot.Clicked += async (_, _) => await model.CaptureMailboxSnapshotAsync();
        inspectSnapshot.Clicked += async (_, _) => await model.InspectMailboxSnapshotAsync();
        offlineActions.AddChild(offlineToggle); offlineActions.AddChild(offlineDownload); offlineActions.AddChild(offlineRestart); offlineActions.AddChild(offlineReload); offlineActions.AddChild(offlineHeaders); offlineActions.AddChild(offlineHeadersRestart); offlineActions.AddChild(offlineShowHeaders);
        offlineSection.Content.AddChild(offlineActions); offlineSection.Content.AddChild(offlineHeaderFeedback);
        if (model.SupportsMailboxSnapshots) offlineSection.Content.AddChild(snapshotFeedback);
        var offlineFrame = new BoundedScrollArea(offlineSection, 0.35, "Offline folder controls");
        var navigationSection = new FormSection("Account folders", "Choose a local start folder and up to eight pinned shortcuts for this account.", collapsible: true, expanded: false)
            { ShowText = "Show account folders", HideText = "Hide account folders" };
        navigationSection.Content.AccessibleName = "Account folder navigation controls";
        var navigationActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var startFolder = new StandardButton { Text = "Use current folder at startup" };
        var pinFolder = new StandardButton { Text = "Pin current folder" };
        var pinnedFolders = new StandardComboBox { AccessibleName = "Pinned folders for current account", PreferredSize = new BSize(220, 32) };
        var openPinned = new StandardButton { Text = "Open pinned folder" };
        var reloadNavigation = new StandardButton { Text = "Reload saved navigation" };
        var resetNavigation = new StandardButton { Text = "Reset account navigation" };
        var navigationNotice = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap };
        IReadOnlyList<string>? shownPinnedFolders = null;
        foreach (var element in new UiElement[] { startFolder, pinFolder, pinnedFolders, openPinned, reloadNavigation, resetNavigation }) navigationActions.AddChild(element);
        navigationSection.Content.AddChild(navigationActions); navigationSection.Content.AddChild(navigationNotice);
        var navigationFrame = new BoundedScrollArea(navigationSection, 0.35, "Account folders");
        var undoMove = new StandardButton { Text = "Undo last move", Visibility = UiVisibility.Collapsed };
        if (model.SupportsMoves) toolbar.AddChild(undoMove);
        undoMove.Clicked += async (_, _) => await model.UndoLastMoveAsync();
        bool searchExpanded = false;
        var showSearch = new StandardButton { Text = "Search saved mail" };
        if (model.SupportsLocalSearch) toolbar.AddChild(showSearch);
        var searchText = new StandardEdit { AccessibleName = "Search saved sender or subject", MaxLength = 256, PreferredSize = new BSize(220, 32) };
        var searchSender = new StandardEdit { AccessibleName = "Saved sender header contains (optional)", PlaceholderText = "Sender contains (optional)", MaxLength = 256, PreferredSize = new BSize(220, 32) };
        var searchScope = new StandardComboBox { AccessibleName = "Saved search folder scope", PreferredSize = new BSize(220,32) };
        searchScope.SetItems(new[] { new UiComboBoxItem("folder","Current folder"),new UiComboBoxItem("account","All saved folders for this account") }); searchScope.SelectedIndex = 0;
        var accountSearchPresenter = new MailMessageItemPresenter(dateFormat,showMailbox:true);
        var folderSearchPresenter = dates is null ? MailMessageItemPresenter.Instance : new MailMessageItemPresenter(dateFormat);
        var searchField = new StandardComboBox { AccessibleName = "Saved search header field", PreferredSize = new BSize(160, 32) };
        searchField.SetItems(new[] { "Sender or subject", "Sender only", "Subject only", "Saved body preview", "Headers or saved body", "Indexed headers", "Indexed sender", "Indexed subject", "Indexed body preview", "Indexed headers/body preview", "Indexed complete retained body", "Indexed headers/complete retained body" }.Select((text, i) => new UiComboBoxItem(i.ToString(), text)));
        searchField.SelectedIndex = 0;
        var searchFilter = new StandardComboBox { AccessibleName = "Saved search filter", PreferredSize = new BSize(170, 32) };
        (bool? Read, bool? Star)[] stateChoices = [(null,null), (false,null), (null,true), (false,true), (true,null), (null,false), (true,true), (false,false), (true,false)];
        string[] stateLabels = ["All saved headers", "Unread only", "Starred only", "Unread and starred", "Read only", "Unstarred only", "Read and starred", "Unread and unstarred", "Read and unstarred"];
        bool?[] bodyChoices = [null, true, false];
        var flagChoices = bodyChoices.SelectMany(cached => stateChoices.Select(flags => (flags.Read, flags.Star, Cached: cached))).ToArray();
        searchFilter.SetItems(flagChoices.Select((choice, index) => new UiComboBoxItem(index.ToString(), stateLabels[index % stateLabels.Length] +
            (choice.Cached is { } cached ? (cached ? " · downloaded body" : " · body not downloaded") : ""))));
        searchFilter.SelectedIndex = 0;
        var searchDate = new StandardComboBox { AccessibleName = "Saved search received date", PreferredSize = new BSize(160, 32) };
        int?[] searchDays = [null, 7, 30, 365];
        searchDate.SetItems(new[] { "All received dates", "Last 7 days (UTC)", "Last 30 days (UTC)", "Last 365 days (UTC)", "Custom UTC dates" }.Select((text, i) => new UiComboBoxItem(i.ToString(), text)));
        searchDate.SelectedIndex = 0;
        var customDates = new StandardEdit { AccessibleName = "UTC date range (YYYY-MM-DD..YYYY-MM-DD, end exclusive)", PlaceholderText = "YYYY-MM-DD..YYYY-MM-DD", MaxLength = 24, PreferredSize = new BSize(240, 32), Visibility = UiVisibility.Collapsed };
        var search = new StandardButton { Text = "Find saved headers" };
        var previousSearch = new StandardButton { Text = "Previous saved results", Visibility = UiVisibility.Collapsed };
        var nextSearch = new StandardButton { Text = "Next saved results", Visibility = UiVisibility.Collapsed };
        var clearSearch = new StandardButton { Text = "Close search" };
        var searchRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8, Visibility = UiVisibility.Collapsed };
        foreach (var control in new UiElement[] { searchText, searchSender, searchScope, searchField, searchFilter, searchDate, customDates, search, previousSearch, nextSearch, clearSearch }) searchRow.AddChild(control);
        var searchFrame = new BoundedScrollArea(searchRow, 0.35, "Saved search controls");
        panel.AddChild(toolbar); panel.SetDock(toolbar, UiDock.Top);
        if (model.SupportsOfflineFolders || model.SupportsMailboxSnapshots) { panel.AddChild(offlineFrame); panel.SetDock(offlineFrame, UiDock.Top); }
        if (model.SupportsAccountNavigation) { panel.AddChild(navigationFrame); panel.SetDock(navigationFrame, UiDock.Top); }
        if (model.SupportsLocalSearch) { panel.AddChild(searchFrame); panel.SetDock(searchFrame, UiDock.Top); }
        searchText.TextChanged += (_, _) => { if (!updating) model.SetSearchText(searchText.Text); };
        searchSender.TextChanged += (_, _) => { if (!updating) model.SetSearchSenderText(searchSender.Text); };
        searchFilter.SelectionChanged += (_, _) =>
        {
            if (!updating && searchFilter.SelectedIndex >= 0 && searchFilter.SelectedIndex < flagChoices.Length)
            { var flags = flagChoices[searchFilter.SelectedIndex]; model.SetSearchFilters(flags.Read, flags.Star, flags.Cached); }
        };
        searchScope.SelectionChanged += (_,_) => { if (!updating && searchScope.SelectedIndex is 0 or 1) model.SetSearchAllSavedFolders(searchScope.SelectedIndex == 1); };
        searchField.SelectionChanged += (_, _) => { if (!updating && searchField.SelectedIndex is >= 0 and < 12) model.SetSearchField((MailHeaderSearchField)searchField.SelectedIndex); };
        searchDate.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            if (searchDate.SelectedIndex is >= 0 and < 4) model.SetSearchReceivedWithinDays(searchDays[searchDate.SelectedIndex]);
            else if (searchDate.SelectedIndex == 4) model.SetSearchCustomDateRange(customDates.Text);
        };
        customDates.TextChanged += (_, _) => { if (!updating && model.SearchUsesCustomDates) model.SetSearchCustomDateRange(customDates.Text); };
        previousSearch.Clicked += async (_, _) => await model.LoadPreviousSearchPageAsync();
        nextSearch.Clicked += async (_, _) => await model.LoadNextSearchPageAsync();
        search.Clicked += async (_, _) => await model.SearchSavedHeadersAsync();
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
        var listFeedback = new InlineFeedback();
        var listRetry = new StandardButton { Text = "Retry receiving" };
        // Like the toolbar above it, the Retry row spans the pane and insets its button, so Retry
        // lines up with Receive mail.
        var listRetryRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36) };
        listRetryRow.AddChild(listRetry);
        // The notice keeps the toolbar's inset, so its accent stays clear of the frame around the pane.
        var retryMoveCache = new StandardButton { Text = "Retry local move update", Visibility = UiVisibility.Collapsed };
        if (model.SupportsMoves) listRetryRow.AddChild(retryMoveCache);
        retryMoveCache.Clicked += async (_, _) => await model.RetryMoveCacheAsync();
        var retryDeleteCache = new StandardButton { Text = "Retry local deletion cleanup", Visibility = UiVisibility.Collapsed };
        if (model.SupportsPermanentDelete) listRetryRow.AddChild(retryDeleteCache);
        retryDeleteCache.Clicked += async (_, _) => await model.RetryDeleteCacheAsync();
        var listExplanation = new Inset(listFeedback, toolbar.Padding, toolbar.Padding);
        // A long explanation at a large text size scrolls instead of leaving the list no room. Like the
        // reader's header, it ends between its lines, never inside one, even its first in a short pane, and
        // shows them all while the list keeps two rows. Retry stays in view below it, so the footer can
        // point to it.
        const int listRowsKept = 2;
        var noticeArea = new BoundedScrollArea(listExplanation, 0.4, "Inbox notice")
        {
            Rows = NoticeRows,
            MinimumRemaining = () => listRowsKept * list.EffectiveItemHeight,
            KeepsLinesWhole = true,
        };
        var listNotice = new FirstTakesRestStack();
        listNotice.Add(noticeArea);
        listNotice.Add(listRetryRow);
        listPane.AddChild(listNotice);
        listPane.SetDock(listNotice, UiDock.Top);
        listPane.AddChild(list);
        listPane.SetDock(list, UiDock.Fill);
        var reading = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var split = new StandardSplitContainer
        {
            Orientation = UiSplitterOrientation.Vertical,
            FirstPane = listPane,
            SecondPane = reading,
            // Side by side, neither pane is dragged below the width the compact switch keeps readable
            // (just above the switch the two share the splitter's width; see NeedsCompact). The saved
            // ratio is only clamped for display, so a wider window shows it again.
            FirstPaneMinimumSize = AdaptiveInboxLayout.ListReadableWidth,
            SecondPaneMinimumSize = AdaptiveInboxLayout.ReaderReadableWidth,
            SplitterFraction = model.SplitterFraction,
        };
        // Whether the reader is the pane being looked at. In compact mode only one pane shows, and
        // moving the list selection with the keyboard must not leave the list.
        bool readerOpen = false;
        MailMessageKey? selectedKey = null;
        var layout = _layout = new AdaptiveInboxLayout(split, () => readerOpen && model.SelectedMessage is not null, () => model.SplitterFraction);
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
        // Frameless so it lines up with the subject; the frame rings it outside the text while it has focus.
        var detailsFrame = new FocusRingFrame(details);
        bool extendedHeaders = false; MailMessageKey? headersKey = null;
        var inspectHeaders = new StandardButton { Text = "Show parsed headers", Visibility = UiVisibility.Collapsed };
        var meta = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false, Role = StandardLabelRole.Muted };
        // The reader's rows of commands are framed strips like the toolbar, and inset their buttons as it does.
        var replyActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36) };
        var reply = new StandardButton { Text = "Reply" };
        var replyAll = new StandardButton { Text = "Reply all" };
        var forward = new StandardButton { Text = "Forward" };
        foreach (var button in new[] { reply, replyAll, forward }) replyActions.AddChild(button);
        // The HTML preview's row is collapsed while empty, so it adds no gap below the row above it.
        var previewActions = new StandardPanel { Visibility = UiVisibility.Collapsed };
        var readerNavigation = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8, AccessibleName = "Message reading position", Visibility = UiVisibility.Collapsed };
        var readerTop = new StandardButton { Text = "Top", AccessibleName = "Top of message" };
        var readerEnd = new StandardButton { Text = "End", AccessibleName = "End of message" };
        readerNavigation.AddChild(readerTop); readerNavigation.AddChild(readerEnd);
        var findMessage = new StandardButton { Text = "Find text", AccessibleName = "Find in selected message text" }; readerNavigation.AddChild(findMessage);
        var findArea = new StandardPanel { Visibility = UiVisibility.Collapsed, AccessibleName = "Message text find" };
        // A message that could not be loaded explains why below its subject, sender and date, and offers
        // Retry there, in the compact reader as beside the list.
        var messageFeedback = new InlineFeedback();
        var messageRetry = new StandardButton { Text = "Retry loading" };
        var messageRetryRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36) };
        messageRetryRow.AddChild(messageRetry);
        var back = new StandardButton { Text = BackText };
        var backRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36), Visibility = UiVisibility.Collapsed };
        backRow.AddChild(back); backRow.AddChild(inspectHeaders);
        var changeRead = new StandardButton { Text = "Mark read" };
        var changeStar = new StandardButton { Text = "Star message" };
        var readStateActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, Visibility = UiVisibility.Collapsed };
        readStateActions.AddChild(changeRead); readStateActions.AddChild(changeStar);
        var readStateFeedback = new InlineFeedback();
        changeStar.Clicked += async (_, _) => await model.SetSelectedStarStateAsync(model.SelectedMessage?.IsStarred != true);
        changeRead.Clicked += async (_, _) => await model.SetSelectedReadStateAsync(model.SelectedMessage?.IsRead != true);
        var moveDestination = new StandardComboBox { AccessibleName = "Move destination", PreferredSize = new BSize(240, 32) };
        var moveMessage = new StandardButton { Text = "Move message" };
        var archiveMessage = new StandardButton { Text = "Archive" };
        var trashMessage = new StandardButton { Text = "Move to trash" };
        var moveFeedback = new InlineFeedback();
        var moveActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, Visibility = UiVisibility.Collapsed };
        foreach (var control in new UiElement[] { moveDestination, moveMessage, archiveMessage, trashMessage }) moveActions.AddChild(control);
        IReadOnlyList<MailFolder>? shownMoveDestinations = null;
        moveDestination.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            int index = moveDestination.SelectedIndex;
            if (index >= 0 && index < model.MoveDestinations.Count) model.ChooseMoveDestination(model.MoveDestinations[index].MailboxId);
        };
        moveMessage.Clicked += async (_, _) => await model.MoveSelectedAsync(model.MoveDestinationMailboxId);
        archiveMessage.Clicked += async (_, _) => await model.ArchiveSelectedAsync();
        trashMessage.Clicked += async (_, _) => await model.TrashSelectedAsync();
        var requestDelete = new StandardButton { Text = "Delete permanently…" };
        var deleteActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, Visibility = UiVisibility.Collapsed };
        deleteActions.AddChild(requestDelete);
        var deleteConfirmation = new StandardPanel { Spacing = 4, Visibility = UiVisibility.Collapsed, AccessibleName = "Permanent deletion confirmation" };
        var deleteConfirmationText = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap };
        var deleteConfirmationActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8 };
        var cancelDelete = new StandardButton { Text = "Keep message" };
        var confirmDelete = new StandardButton { Text = "Confirm permanent deletion" };
        deleteConfirmationActions.AddChild(cancelDelete); deleteConfirmationActions.AddChild(confirmDelete);
        deleteConfirmation.AddChild(deleteConfirmationText); deleteConfirmation.AddChild(deleteConfirmationActions);
        var deleteFeedback = new InlineFeedback();
        requestDelete.Clicked += (_, _) =>
        { if (model.RequestPermanentDelete() && panel.Session is { } session) FocusNavigation.FocusAndReveal(session, cancelDelete); };
        cancelDelete.Clicked += (_, _) => model.CancelPermanentDeleteConfirmation();
        confirmDelete.Clicked += async (_, _) => await model.ConfirmPermanentDeleteAsync();
        var sourceArea = new StandardPanel { Visibility = UiVisibility.Collapsed };
        MessageSourceExportActions? sourceExportActions = null;
        var headerStack = new StandardPanel { Spacing = 4 };
        foreach (var element in new UiElement[] { backRow, subjectLine, detailsFrame, meta, messageFeedback, messageRetryRow, replyActions, readStateActions, readStateFeedback, previewActions, readerNavigation, findArea, sourceArea })
            if (model.SupportsReadStateChanges || model.SupportsStarStateChanges || element != readStateActions && element != readStateFeedback) headerStack.AddChild(element);
        if (model.SupportsMoves) { headerStack.AddChild(moveActions); headerStack.AddChild(moveFeedback); }
        if (model.SupportsPermanentDelete) { headerStack.AddChild(deleteActions); headerStack.AddChild(deleteConfirmation); headerStack.AddChild(deleteFeedback); }
        // A long subject or many recipients at a large text size scroll within the header, so the
        // message text keeps most of the pane. The header ends between its rows or lines of text, not
        // inside one: while the text keeps a few lines it grows to show the whole header, or the next
        // row of buttons whole with the gap below it, which may narrow the text's margin below its lines,
        // and otherwise ends above the row or line its share would cut.
        const double headerShare = 0.45;
        const int textLinesKept = 6;
        var text = new ScrollableMessageText();
        var headerContent = new ReadingColumn(headerStack, verticalMargin: 8);
        UiElement[] buttonRows = [deleteActions, deleteConfirmationActions, moveActions, backRow, messageRetryRow, replyActions, readStateActions, previewActions, readerNavigation, findArea, sourceArea];
        var headerColumn = new BoundedScrollArea(headerContent, headerShare, "Message header")
        {
            Rows = HeaderRows,
            MinimumRemaining = () => text.HeightOfLines(textLinesKept),
        };
        // Beside the list, Reply, Reply all and Forward stay in view below the header even while it
        // scrolls, where they fit, and the header has the height they leave (see PlaceCommands). A line
        // across the reader separates the header and its commands from the message text.
        const double commandsInset = 4;
        var pinnedCommands = new StandardPanel();
        var divider = new Divider();
        var headerArea = new FirstTakesRestStack { Fit = PlaceCommands };
        headerArea.Add(headerColumn);
        headerArea.Add(new ReadingColumn(new Inset(pinnedCommands, 0, commandsInset)));
        headerArea.Add(divider);
        reading.AddChild(headerArea);
        reading.SetDock(headerArea, UiDock.Top);
        var bodyArea = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        bodyArea.AddChild(text);
        reading.AddChild(bodyArea);
        UiElement? inlineContent = null;
        MessageTextFindBar? messageFind = null;
        Action? showTextForFind = null;
        bool preferHtml = settings?.Settings.PreferPlainText != true;
        bool findChangedMode = false;
        _readerInput = input =>
        {
            if (panel.IsDisposed || (layout.IsCompact && !layout.ShowsReaderOnly)) return false;
            var focused = panel.Session?.FocusedElement;
            bool inReader = focused is not null && (focused == text || focused.IsDescendantOf(text) ||
                (inlineContent is not null && (focused == inlineContent || focused.IsDescendantOf(inlineContent))) ||
                focused.IsDescendantOf(previewActions) || focused.IsDescendantOf(readerNavigation) || focused.IsDescendantOf(findArea));
            bool findScope = focused is not null && (focused == reading || focused.IsDescendantOf(reading));
            if (findScope && messageFind?.HandleFindInput(input) == true) return true;
            if (inlineContent is not IInlineReaderInput handler || inlineContent.Visibility != UiVisibility.Visible) return false;
            if (input.Kind == UiInputEventKind.PointerWheel) return inlineContent.Bounds.Contains(input.Position) && handler.HandleReaderInput(input);
            return inReader && handler.HandleReaderInput(input);
        };
        void FocusReader()
        {
            UiElement? FindFocus(UiElement element) => element.CanFocus ? element : element.Children.Select(FindFocus).FirstOrDefault(child => child is not null);
            panel.Session?.SetFocus(inlineContent is { Visibility: UiVisibility.Visible } surface ? FindFocus(surface) ?? text.Editor : text.Editor);
        }

        // Where the explanation's lines are, as the notice last measured them: inside the inset and the
        // feedback's own padding, which below the last line is a margin, not a row.
        IEnumerable<(double Start, double End, bool Grows, double LineHeight)> NoticeRows()
        {
            double height = listExplanation.DesiredSize.Height;
            if (height > 0 && listFeedback.Children.OfType<StandardLabel>().FirstOrDefault() is { } label)
            {
                double start = (height - label.DesiredSize.Height) / 2;
                yield return (start, start + label.DesiredSize.Height, false, BTextMeasurer.GetLineHeight(label.Font));
            }
        }

        // Where the header's rows are, as the stack last measured them, which are rows of buttons, and the
        // line height of the subject, the sender and recipients, and the date, so the header may end between lines.
        IEnumerable<(double Start, double End, bool Grows, double LineHeight)> HeaderRows()
        {
            double top = headerContent.VerticalMargin;
            foreach (var child in headerStack.Children.Where(child => child.Visibility != UiVisibility.Collapsed))
            {
                double height = child.DesiredSize.Height;
                double lineHeight = child switch
                {
                    StandardLabel label => BTextMeasurer.GetLineHeight(label.Font),
                    FocusRingFrame { Content: StandardRichEdit edit } => BTextMeasurer.GetLineHeight(edit.Font),
                    _ => 0,
                };
                if (height > 0) yield return (top, top + height, buttonRows.Contains(child), lineHeight);
                top += height + headerStack.Spacing;
            }
        }
        IReadOnlyList<MailMessageSummary>? shown = null;
        MailMessageBody? shownBody = null;
        IReaderViewport? ActiveViewport() => text.Visibility == UiVisibility.Visible ? text : inlineContent as IReaderViewport;
        void UpdateReaderNavigation()
        {
            readerNavigation.Visibility = model.Body?.HtmlText is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            readerTop.IsEnabled = readerEnd.IsEnabled = model.Body is not null && ActiveViewport() is not null;
        }
        void MoveReader(double fraction)
        {
            if (panel.IsDisposed || model.Body is null || !ReferenceEquals(shownBody, model.Body)) return;
            ActiveViewport()?.RestoreReadingPosition(fraction);
        }
        readerTop.Clicked += (_, _) => MoveReader(0); readerEnd.Clicked += (_, _) => MoveReader(1);
        findMessage.Clicked += (_, _) => messageFind?.ShowFind();
        string? shownText = null;

        bool listWasLoading = false;
        void Update()
        {
            if (panel.IsDisposed) return;
            updating = true;
            if (!ReferenceEquals(shownFolders, model.Folders))
            {
                shownFolders = model.Folders;
                folders.SetItems(shownFolders.Select(folder => new UiComboBoxItem(folder.MailboxId,
                    folder.MailboxId + (model.OfflineFolders.Contains(folder.MailboxId, StringComparer.Ordinal) ? " — offline selected" : "") + (model.FolderRoleLabel(folder.MailboxId) is { Length: > 0 } roles ? " — " + roles : "") + (!folder.IsSelectable ? " — not selectable" : model.IsSavedOnlyFolder(folder.MailboxId) ? " — saved only" : ""))));
            }
            folders.SelectedIndex = model.Folders.ToList().FindIndex(folder => folder.MailboxId == model.SelectedMailboxId);
            folders.IsEnabled = model.CanSwitchFolder;
            refreshFolders.IsEnabled = model.CanRefreshFolders;
            navigationFrame.Visibility = searchExpanded ? UiVisibility.Collapsed : UiVisibility.Visible;
            navigationNotice.Text = model.NavigationNotice;
            startFolder.IsEnabled = model.CanSetStartFolder; pinFolder.IsEnabled = model.CanPinCurrentFolder;
            pinFolder.Text = model.IsCurrentFolderPinned ? "Unpin current folder" : "Pin current folder";
            reloadNavigation.IsEnabled = model.CanReloadNavigation; resetNavigation.IsEnabled = model.CanResetNavigation;
            if (!ReferenceEquals(shownPinnedFolders, model.PinnedFolders))
            {
                string? previous = shownPinnedFolders is not null && pinnedFolders.SelectedIndex >= 0 && pinnedFolders.SelectedIndex < shownPinnedFolders.Count ? shownPinnedFolders[pinnedFolders.SelectedIndex] : null;
                shownPinnedFolders = model.PinnedFolders;
                pinnedFolders.SetItems(shownPinnedFolders.Select(name => new UiComboBoxItem(name, name)));
                pinnedFolders.SelectedIndex = previous is not null && shownPinnedFolders.Contains(previous, StringComparer.Ordinal) ? shownPinnedFolders.ToList().IndexOf(previous) : shownPinnedFolders.Count > 0 ? 0 : -1;
            }
            pinnedFolders.IsEnabled = model.NavigationReady && model.CanSwitchFolder && !model.IsSavingNavigation;
            openPinned.IsEnabled = pinnedFolders.SelectedIndex >= 0 && pinnedFolders.SelectedIndex < model.PinnedFolders.Count && model.CanOpenPinnedFolder(model.PinnedFolders[pinnedFolders.SelectedIndex]);
            offlineFrame.Visibility = searchExpanded ? UiVisibility.Collapsed : UiVisibility.Visible;
            offlineToggle.Text = model.IsSelectedFolderOffline ? "Unselect this folder for offline batches (keep mail)" : "Select this folder for offline batches";
            offlineToggle.IsEnabled = model.CanToggleOfflineFolder;
            offlineDownload.Text = model.HasOfflineFolderContinuation ? "Download next offline batch" : "Download selected offline folders";
            offlineDownload.IsEnabled = model.CanDownloadOfflineFolders;
            offlineHeaders.Text = model.HasOfflineHeaderWalk ? "Receive next selected-folder headers" : "Receive selected-folder headers";
            offlineHeaders.IsEnabled = model.CanReceiveOfflineHeaders;
            offlineShowHeaders.IsEnabled = model.CanShowSavedHeaders;
            offlineHeaderFeedback.Set(model.OfflineHeaderNotice, FeedbackKind.Information);
            captureSnapshot.IsEnabled = model.CanCaptureMailboxSnapshot; inspectSnapshot.IsEnabled = model.CanInspectMailboxSnapshot;
            snapshotFeedback.Set(model.MailboxSnapshotNotice, FeedbackKind.Information);
            offlineHeadersRestart.IsEnabled = model.CanRestartOfflineHeaders;
            offlineRestart.IsEnabled = model.CanRestartOfflineFolderBatches;
            offlineReload.IsEnabled = model.CanSwitchFolder && !model.IsBusy && !model.IsSavingOfflineFolders;
            foreach (var child in toolbar.Children) child.Visibility = searchExpanded && child != cancel ? UiVisibility.Collapsed : UiVisibility.Visible;
            undoMove.Visibility = model.CanUndoMove && !searchExpanded ? UiVisibility.Visible : UiVisibility.Collapsed;
            undoMove.IsEnabled = model.CanUndoMove;
            if (searchText.Text != model.SearchText) searchText.Text = model.SearchText;
            if (searchSender.Text != model.SearchSenderText) searchSender.Text = model.SearchSenderText;
            searchSender.IsEnabled = !model.IsBusy;
            int filterIndex = Array.FindIndex(flagChoices, choice => choice.Read == model.SearchReadState && choice.Star == model.SearchStarState && choice.Cached == model.SearchBodyCached);
            if (searchFilter.SelectedIndex != filterIndex) searchFilter.SelectedIndex = filterIndex;
            int dateIndex = model.SearchUsesCustomDates ? 4 : Array.IndexOf(searchDays, model.SearchReceivedWithinDays);
            if (searchDate.SelectedIndex != dateIndex) searchDate.SelectedIndex = dateIndex;
            if (searchField.SelectedIndex != (int)model.SearchField) searchField.SelectedIndex = (int)model.SearchField;
            searchField.IsEnabled = !model.IsBusy;
            searchScope.IsEnabled = !model.IsBusy; searchScope.SelectedIndex = model.SearchAllSavedFolders ? 1 : 0;
            list.ItemPresenter = model.IsAccountSearchActive ? accountSearchPresenter : folderSearchPresenter;
            customDates.Visibility = model.SearchUsesCustomDates ? UiVisibility.Visible : UiVisibility.Collapsed;
            if (customDates.Text != model.SearchCustomDateRange) customDates.Text = model.SearchCustomDateRange;
            customDates.IsEnabled = !model.IsBusy;
            searchDate.IsEnabled = !model.IsBusy;
            showSearch.IsEnabled = !model.IsBusy;
            search.IsEnabled = model.CanSearch;
            nextSearch.Visibility = model.IsSearchActive && (model.SearchHasMore || model.SearchPageNumber > 1) ? UiVisibility.Visible : UiVisibility.Collapsed;
            nextSearch.IsEnabled = model.CanLoadNextSearchPage;
            previousSearch.Visibility = model.IsSearchActive && (model.SearchPageNumber > 1 || previousSearch.Visibility == UiVisibility.Visible) ? UiVisibility.Visible : UiVisibility.Collapsed;
            previousSearch.IsEnabled = model.CanLoadPreviousSearchPage;
            searchText.IsEnabled = searchFilter.IsEnabled = !model.IsBusy;
            clearSearch.IsEnabled = !model.IsBusy;
            receive.IsEnabled = model.CanReceive;
            older.IsEnabled = model.CanLoadOlder;
            download.IsEnabled = model.CanDownloadLoaded;
            read.IsEnabled = !model.IsBusy && model.SelectedMessage is not null;
            cancel.IsEnabled = model.IsBusy;
            if (!layout.IsCompact && Math.Abs(split.SplitterFraction - model.SplitterFraction) > 0.001)
                split.SplitterFraction = model.SplitterFraction;
            if (!ReferenceEquals(shown, model.Messages))
            {
                shown = model.Messages;
                list.SetItems(shown.Select(message => new UiListItem(
                    Id(message),
                    $"{(message.IsStarred ? "Starred · " : "")}{(message.IsRead ? "Read" : "Unread")} · {message.Subject} — {message.Sender}",
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
            // A received inbox without messages has nothing to select; the reader says so, as the list does.
            bool empty = model.HasLoaded && model.Messages.Count == 0;
            subjectLine.Text = item is not null ? (item.Subject.Length > 0 ? item.Subject : "(No subject)")
                : model.IsSearchActive ? "Select a saved search result." : empty ? (model.SelectedMailboxId == "INBOX" ? "The inbox is empty." : "The selected folder is empty.") : "Select a message to read.";
            if (headersKey != item?.Key) { headersKey = item?.Key; extendedHeaders = false; }
            var sourceHeaders = model.Body is { } loaded && loaded.Key == item?.Key ? loaded.Composition : null;
            headerStack.MoveChild(backRow, layout.ShowsReaderOnly ? 0 : 3);
            back.Visibility = layout.ShowsReaderOnly ? UiVisibility.Visible : UiVisibility.Collapsed;
            backRow.Visibility = layout.ShowsReaderOnly || sourceHeaders is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            inspectHeaders.Visibility = sourceHeaders is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            details.AccessibleName = extendedHeaders ? "Parsed message headers" : "Sender and recipients";
            inspectHeaders.Text = extendedHeaders ? "Hide parsed headers" : "Show parsed headers";
            string detailText = item is null ? "" : string.Join("\n", ParsedMessageHeaders.Lines(item, sourceHeaders, extendedHeaders));
            if (detailText != details.GetPlainText()) details.SetPlainText(detailText);
            detailsFrame.Visibility = item is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            // The date with its time and the separator that ends it is one phrase, and the read state another
            // (non-breaking spaces), so a line, and a header that ends between lines, may end after "Received"
            // or after the separator, but never inside the date ("10:00" above "AM") or on "Unread on", and no
            // line starts with the separator, where it would read as a bullet.
            meta.Text = item is null ? "" : $"Received {dateFormat.Detail(item.ReceivedAt).Replace(' ', '\u00A0')}\u00A0· {(item.IsRead ? "Read" : "Unread")}\u00A0on\u00A0server" +
                (model.IsAccountSearchActive ? $" · Folder {item.Key.MailboxId} · Account {model.CurrentAccountProfile?.EmailAddress}" : "") +
                (item.IsStarred ? " · Starred" : "") +
                (item.IsBodyCached ? " · Available offline" : model.IsShowingCachedMail ? " · Body not downloaded" : "");
            // An empty label still takes a line, so without a message the heading is the header's only row.
            meta.Visibility = item is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            readStateActions.Visibility = (model.SupportsReadStateChanges || model.SupportsStarStateChanges) && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            if (!ReferenceEquals(shownMoveDestinations, model.MoveDestinations))
            {
                shownMoveDestinations = model.MoveDestinations;
                moveDestination.SetItems(shownMoveDestinations.Select(folder => new UiComboBoxItem(folder.MailboxId, folder.MailboxId)));
            }
            moveDestination.SelectedIndex = model.MoveDestinations.ToList().FindIndex(folder => folder.MailboxId == model.MoveDestinationMailboxId);
            moveDestination.IsEnabled = model.CanMoveSelected && model.MoveDestinations.Count > 0;
            moveActions.Visibility = model.SupportsMoves && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            moveFeedback.Set(model.MoveNotice ?? "", model.IsMoving ? FeedbackKind.Progress : FeedbackKind.Information);
            deleteActions.Visibility = model.SupportsPermanentDelete && model.IsPermanentDeleteFolder && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            requestDelete.IsEnabled = model.CanRequestPermanentDelete;
            deleteConfirmation.Visibility = model.PendingPermanentDelete is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            deleteConfirmationText.Text = model.PermanentDeleteConfirmationText; confirmDelete.IsEnabled = model.CanConfirmPermanentDelete;
            deleteFeedback.Set(model.DeleteNotice ?? "", model.IsDeleting ? FeedbackKind.Progress : FeedbackKind.Information);
            retryDeleteCache.Visibility = model.CanRetryDeleteCache ? UiVisibility.Visible : UiVisibility.Collapsed;
            retryDeleteCache.IsEnabled = model.CanRetryDeleteCache;
            moveMessage.IsEnabled = model.CanMoveToDestination; archiveMessage.IsEnabled = model.CanArchive; trashMessage.IsEnabled = model.CanTrash;
            retryMoveCache.Visibility = model.CanRetryMoveCache ? UiVisibility.Visible : UiVisibility.Collapsed;
            retryMoveCache.IsEnabled = model.CanRetryMoveCache;
            changeRead.Text = item?.IsRead == true ? "Mark unread" : "Mark read";
            changeRead.Visibility = model.SupportsReadStateChanges ? UiVisibility.Visible : UiVisibility.Collapsed;
            changeStar.Visibility = model.SupportsStarStateChanges ? UiVisibility.Visible : UiVisibility.Collapsed;
            changeStar.Text = item?.IsStarred == true ? "Unstar message" : "Star message";
            changeStar.IsEnabled = model.CanChangeStarState;
            changeRead.IsEnabled = model.CanChangeReadState;
            readStateFeedback.Set(model.FlagChangeNotice ?? "", model.IsChangingMessageFlags ? FeedbackKind.Progress : FeedbackKind.Information);
            replyActions.Visibility = item is null || commands is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            reply.IsEnabled = replyAll.IsEnabled = forward.IsEnabled = commands?.CanRespond == true;
            ShowStates();
            var body = model.Body;
            // While a page loads, the list notice shows its progress and Receive mail is unavailable, so the
            // reader of an empty list does not ask for it.
            string reader = body is null ? (model.SelectedMessage is null
                ? (model.Messages.Count > 0 ? "Choose a message from the inbox list." : model.IsLoadingList ? ""
                    : empty ? "Use Receive mail to check for new messages." : "Receive mail to load your inbox.")
                : (model.IsLoadingMessage ? "Loading message body…" : ""))
                : (body.IsHtmlFallback ? "Text extracted from HTML (formatting omitted).\n\n" : "") + body.PlainText +
                  (body.IsTruncated ? "\n\n[Preview limited to 32,000 characters.]" : "");
            // Rewriting unchanged text would reset the reader's selection during unrelated updates.
            if (reader != shownText) text.Text = shownText = reader;
            // With no text to make room for, such as a body that could not be loaded, the header may
            // take the pane, so a short reader shows the problem and Retry instead of empty space.
            headerColumn.MaximumFraction = reader.Length == 0 ? 1 : headerShare;
            if (!ReferenceEquals(shownBody, body))
            {
                bool restoreSourceFocus = panel.Session?.FocusedElement is { } sourceFocus && sourceFocus.IsDescendantOf(sourceArea);
                sourceExportActions = null;
                foreach (var child in sourceArea.Children.ToArray()) { sourceArea.RemoveChild(child); child.Dispose(); }
                sourceArea.Visibility = UiVisibility.Collapsed;
                if (findChangedMode) { preferHtml = settings?.Settings.PreferPlainText != true; findChangedMode = false; }
                messageFind = null; showTextForFind = null;
                foreach (var child in findArea.Children.ToArray()) { findArea.RemoveChild(child); child.Dispose(); }
                findArea.Visibility = UiVisibility.Collapsed;
                text.ScrollToStart();
                bool restoreReaderFocus = restoreSourceFocus || panel.Session?.FocusedElement is { } readerFocus &&
                    (readerFocus == text || readerFocus.IsDescendantOf(text));
                if (inlineContent is not null)
                {
                    restoreReaderFocus |= panel.Session?.FocusedElement is { } focused && (focused == inlineContent || focused.IsDescendantOf(inlineContent));
                    bodyArea.RemoveChild(inlineContent);
                    inlineContent.Dispose();
                    inlineContent = null;
                }
                text.Visibility = UiVisibility.Visible;
                foreach (var child in previewActions.Children.ToArray()) { previewActions.RemoveChild(child); child.Dispose(); }
                if (htmlPreview is not null && body?.HtmlText is not null)
                {
                    try
                    {
                        inlineContent = htmlPreview.CreateInlineContent(body);
                        var surface = inlineContent;
                        bodyArea.AddChild(inlineContent);
                        var toggle = new StandardButton();
                        var modeRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
                        modeRow.AddChild(toggle);
                        var zoomOut = new StandardButton { Text = "Zoom out" };
                        var zoomReset = new StandardButton();
                        var zoomIn = new StandardButton { Text = "Zoom in" };
                        var readerControls = surface as IInlineHtmlReader;
                        RemoteImageActions? imageActions = null;
                        if (surface is IInlineRemoteImages remote && remote.RemoteImages.Total > 0)
                            imageActions = new RemoteImageActions(remote, () => panel.Session?.SetFocus(toggle));
                        var renderingNotice = new StandardLabel
                        {
                            Text = "HTML: automatic remote resources blocked. Renderer process containment is pending.",
                            UseMnemonic = false, Wrapping = UiTextWrapping.Wrap,
                        };
                        void UpdateZoom()
                        {
                            if (readerControls is null || !ReferenceEquals(surface, inlineContent)) return;
                            zoomReset.Text = ((int)Math.Round(readerControls.Zoom * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";
                            zoomReset.AccessibleName = "Reset HTML zoom (" + zoomReset.Text + ")";
                            zoomOut.IsEnabled = preferHtml && readerControls.CanZoomOut;
                            zoomIn.IsEnabled = preferHtml && readerControls.CanZoomIn;
                            zoomReset.IsEnabled = preferHtml;
                            if (panel.Session?.FocusedElement is { } focused &&
                                ((focused == zoomIn && !zoomIn.IsEnabled) || (focused == zoomOut && !zoomOut.IsEnabled) ||
                                 (focused == zoomReset && !zoomReset.IsEnabled)))
                                panel.Session.SetFocus(preferHtml ? zoomReset : toggle);
                        }
                        if (readerControls is not null)
                        {
                            foreach (var button in new[] { zoomOut, zoomReset, zoomIn }) modeRow.AddChild(button);
                            zoomOut.Clicked += (_, _) => readerControls.ZoomBy(-1);
                            zoomIn.Clicked += (_, _) => readerControls.ZoomBy(1);
                            zoomReset.Clicked += (_, _) => readerControls.ResetZoom();
                            readerControls.ViewChanged += (_, _) => { if (!panel.IsDisposed) UpdateZoom(); };
                            readerControls.RenderingFailed += (_, _) =>
                            {
                                // Capture before a failed layout can collapse the HTML scroll range.
                                double failedPosition = surface is IReaderViewport failedViewport ? failedViewport.ReadingPosition : 0;
                                void Fallback()
                                {
                                    if (panel.IsDisposed || !ReferenceEquals(surface, inlineContent)) return;
                                    bool hadFocus = panel.Session?.FocusedElement is { } focused &&
                                        (focused == surface || focused.IsDescendantOf(surface) || focused.IsDescendantOf(modeRow) ||
                                         (imageActions is not null && focused.IsDescendantOf(imageActions)));
                                    inlineContent = null;
                                    imageActions?.SetHtmlVisible(false);
                                    bodyArea.RemoveChild(surface); surface.Dispose();
                                    text.Visibility = UiVisibility.Visible;
                                    text.RestoreReadingPosition(failedPosition);
                                    UpdateReaderNavigation();
                                    toggle.IsEnabled = zoomIn.IsEnabled = zoomOut.IsEnabled = zoomReset.IsEnabled = false;
                                    renderingNotice.Text = "HTML rendering failed. The text reader remains available.";
                                    if (hadFocus) panel.Session?.SetFocus(text.Editor);
                                }
                                // Do not change the tree during a measure/render traversal. An old
                                // surface's queued failure must never replace a newer message.
                                if (panel.Session is { } active) active.Dispatcher.Post(Fallback); else Fallback();
                            };
                        }
                        void ApplyMode()
                        {
                            text.Visibility = preferHtml ? UiVisibility.Collapsed : UiVisibility.Visible;
                            inlineContent.Visibility = preferHtml ? UiVisibility.Visible : UiVisibility.Collapsed;
                            toggle.Text = preferHtml ? "Plain text" : "HTML";
                            UpdateZoom();
                            imageActions?.SetHtmlVisible(preferHtml);
                            UpdateReaderNavigation();
                        }
                        showTextForFind = () =>
                        {
                            if (panel.IsDisposed || !ReferenceEquals(surface, inlineContent) || !ReferenceEquals(model.Body, body)) return;
                            if (preferHtml)
                            {
                                double position = surface is IReaderViewport viewport ? viewport.ReadingPosition : 0;
                                findChangedMode = true; preferHtml = false; ApplyMode(); text.RestoreReadingPosition(position);
                            }
                        };
                        toggle.Clicked += async (_, _) =>
                        {
                            if (panel.IsDisposed || !ReferenceEquals(surface, inlineContent) || !ReferenceEquals(model.Body, body)) return;
                            double position = preferHtml && surface is IReaderViewport viewport ? viewport.ReadingPosition : text.ReadingPosition;
                            findChangedMode = false; preferHtml = !preferHtml; ApplyMode();
                            if (preferHtml && surface is IReaderViewport next) next.RestoreReadingPosition(position);
                            else if (!preferHtml) text.RestoreReadingPosition(position);
                            if (settings is not null) await settings.RememberReaderModeAsync(!preferHtml);
                        };
                        previewActions.AddChild(modeRow);
                        if (imageActions is not null) previewActions.AddChild(imageActions);
                        previewActions.AddChild(renderingNotice);
                        ApplyMode();
                    }
                    catch (Exception)
                    {
                        if (inlineContent is not null) { bodyArea.RemoveChild(inlineContent); inlineContent.Dispose(); inlineContent = null; }
                        text.Visibility = UiVisibility.Visible;
                        previewActions.AddChild(new StandardLabel { Text = "HTML unavailable. The text reader remains available.", UseMnemonic = false });
                    }
                }
                else if (body?.HtmlUnavailableReason is { } unavailable)
                    previewActions.AddChild(new StandardLabel { Text = unavailable, UseMnemonic = false, Wrapping = UiTextWrapping.Wrap });
                if (body is not null)
                {
                    int offset = body.IsHtmlFallback ? "Text extracted from HTML (formatting omitted).\n\n".Length : 0;
                    var currentBody = body;
                    string findText = body.PlainText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                    messageFind = new MessageTextFindBar(text, findText, offset, () =>
                    {
                        showTextForFind?.Invoke(); findArea.Visibility = UiVisibility.Visible;
                    }, () => !panel.IsDisposed && ReferenceEquals(model.Body, currentBody), () =>
                    {
                        findArea.Visibility = UiVisibility.Collapsed;
                        if (panel.Session is { } session) FocusNavigation.FocusAndReveal(session, text.Editor);
                    });
                    findArea.AddChild(messageFind);
                    if (model.SupportsSourceExport && model.CurrentAccountProfile is { } sourceAccount)
                    {
                        var key = body.Key;
                        sourceExportActions = new MessageSourceExportActions(sourceAccount, key, model.SourceReceiver!, model.SourceExports!,
                            () => !panel.IsDisposed && model.CurrentAccountProfile == sourceAccount && model.SelectedMessage?.Key == key,
                            () => model.CanDownloadOriginalSource);
                        sourceArea.AddChild(sourceExportActions); sourceArea.Visibility = UiVisibility.Visible;
                    }
                }
                previewActions.Visibility = previewActions.Children.Count > 0 ? UiVisibility.Visible : UiVisibility.Collapsed;
                if (restoreReaderFocus) FocusReader();
            }
            shownBody = body;
            sourceExportActions?.RefreshAvailability();
            UpdateReaderNavigation();
            updating = false;
        }

        folders.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            int index = folders.SelectedIndex;
            if (index >= 0 && index < model.Folders.Count) model.SelectFolder(model.Folders[index].MailboxId);
            Update();
        };
        refreshFolders.Clicked += async (_, _) => await model.RefreshFoldersAsync();
        receive.Clicked += async (_, _) => await model.ReceiveAsync();
        download.Clicked += async (_, _) => await model.DownloadLoadedBodiesAsync();
        offlineShowHeaders.Clicked += async (_, _) => await model.ShowSavedHeadersAsync();
        offlineHeaders.Clicked += async (_, _) => await model.ReceiveOfflineHeadersAsync();
        offlineHeadersRestart.Clicked += (_, _) => model.RestartOfflineHeaders();
        offlineToggle.Clicked += async (_, _) => await model.ToggleOfflineFolderAsync();
        offlineDownload.Clicked += async (_, _) => await model.DownloadOfflineFoldersAsync();
        offlineRestart.Clicked += async (_, _) => await model.RestartOfflineFolderBatchesAsync();
        startFolder.Clicked += async (_, _) => await model.SetStartFolderAsync();
        pinFolder.Clicked += async (_, _) => await model.TogglePinnedFolderAsync();
        reloadNavigation.Clicked += async (_, _) => await model.ReloadAccountNavigationAsync();
        resetNavigation.Clicked += async (_, _) => await model.ResetAccountNavigationAsync();
        pinnedFolders.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            int index = pinnedFolders.SelectedIndex;
            openPinned.IsEnabled = index >= 0 && index < model.PinnedFolders.Count && model.CanOpenPinnedFolder(model.PinnedFolders[index]);
        };
        openPinned.Clicked += (_, _) =>
        {
            int index = pinnedFolders.SelectedIndex;
            if (index >= 0 && index < model.PinnedFolders.Count) model.OpenPinnedFolder(model.PinnedFolders[index]);
        };
        offlineReload.Clicked += async (_, _) => await model.ReloadOfflineFolderSelectionAsync();
        older.Clicked += async (_, _) => await model.LoadOlderAsync();
        void ShowStates()
        {
            // List notice: a problem with Retry, progress while receiving, why the list is empty, or why
            // Load older is unavailable at the session limit. A list problem stays while a message is read:
            // the rows are still from an earlier receive. So does the session limit, which the reader's
            // status would otherwise replace; after a list problem, it follows that explanation.
            bool listProblem = model.ListProblem is not null;
            string? limit = model.SessionLimitNotice;
            (string text, FeedbackKind kind) notice =
                model.ListProblem is { } problem ? (limit is null ? problem.Text : $"{problem.Text} {limit}", problem.IsCancellation ? FeedbackKind.Information : FeedbackKind.Error)
                : model.IsLoadingList ? (model.Status, FeedbackKind.Progress)
                : limit is not null ? (limit, FeedbackKind.Information)
                : model.CacheNotice is { } cacheNotice ? (cacheNotice, FeedbackKind.Information)
                : model.Messages.Count == 0 ? (model.HasLoaded ? (model.SelectedMailboxId == "INBOX" ? "The inbox is empty." : "The selected folder is empty.") : model.CanReceive ? (model.SelectedMailboxId == "INBOX" ? "Receive mail to load your inbox." : "Receive mail to load the selected folder.") : "", FeedbackKind.Information)
                : ("", FeedbackKind.Information);
            // The notice announces progress and problems itself, then disappears once rows arrive;
            // say that receiving finished, or a screen reader user hears nothing after the progress.
            // It comes before the notice, so the count is heard before why Load older is unavailable.
            if (listWasLoading && !model.IsLoadingList && !listProblem && model.Messages.Count > 0)
                panel.Session?.AnnounceStatus(list, model.Messages.Count == 1 ? "1 message loaded." : $"{model.Messages.Count} messages loaded.");
            listWasLoading = model.IsLoadingList;
            listFeedback.Set((model.RecoveryNotice is { } recoveryNotice ? recoveryNotice + "\n" : "") + (searchExpanded && model.SearchDateError is { } dateError ? dateError + "\n" : "") + (model.SearchNotice is { } searchNotice ? searchNotice + "\n" : "") + notice.text + (model.SupportsOfflineFolders ? "\n" + model.OfflineFolderNotice : "") + (model.MoveNotice is { } moveNotice ? "\n" + moveNotice : "") + (model.DeleteNotice is { } deleteNotice ? "\n" + deleteNotice : "") + (model.SupportsFolders && !model.IsLoadingList && model.ListProblem is null ? "\n" + model.FolderNotice + "\n" + model.FolderRoleNotice : ""), notice.kind);
            listRetry.Visibility = listProblem ? UiVisibility.Visible : UiVisibility.Collapsed;
            listRetryRow.Visibility = listProblem || model.CanRetryMoveCache || model.CanRetryDeleteCache ? UiVisibility.Visible : UiVisibility.Collapsed;
            listRetry.IsEnabled = model.CanRetryIn(InboxProblemScope.List);
            // Named for what it repeats (RetryAsync): the older page, or receiving the newest messages.
            listRetry.Text = model.IsSearchActive ? "Retry saved search" : model.ProblemIsOfflineHeaderReceive ? "Retry selected-folder headers" : model.ProblemIsFolderDiscovery ? "Retry folder discovery" : model.ProblemIsSelectedFolderDownload ? "Retry selected-folder download" : model.ProblemIsBodyDownload ? "Retry offline download" : model.ListRetryLoadsOlder ? "Retry loading older" : "Retry receiving";
            var messageProblem = model.SelectedMessage is null ? null : model.MessageProblem;
            messageFeedback.Set(messageProblem?.Text ?? "", messageProblem?.IsCancellation == true ? FeedbackKind.Information : FeedbackKind.Error);
            messageRetryRow.Visibility = messageProblem is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            messageRetry.IsEnabled = model.CanRetryIn(InboxProblemScope.Message);
            KeepFocusUsable();
        }
        void KeepFocusUsable()
        {
            // A Retry that disappears or a Cancel that is disabled must not keep keyboard focus.
            if (panel.Session is not { } session || session.FocusedElement is not { } focused) return;
            if ((focused == search || focused == nextSearch || focused == previousSearch) && model.IsBusy) session.SetFocus(cancel);
            else if (focused == listRetry && listRetryRow.Visibility != UiVisibility.Visible) session.SetFocus(list);
            else if (focused == messageRetry && messageRetryRow.Visibility != UiVisibility.Visible) FocusReader();
            else if (focused == cancel && !model.IsBusy) session.SetFocus(receive.IsEnabled ? receive : list);
            else if (focused == retryMoveCache && !model.CanRetryMoveCache) session.SetFocus(model.IsBusy ? cancel : list);
            else if (focused.IsDescendantOf(deleteConfirmation))
            {
                if (model.IsDeleting) session.SetFocus(cancel);
                else if (model.PendingPermanentDelete is null) FocusNavigation.FocusAndReveal(session, requestDelete.CanFocus ? requestDelete : list);
            }
            else if (focused == retryDeleteCache && !model.CanRetryDeleteCache) session.SetFocus(model.IsBusy ? cancel : list);
            else if (focused == undoMove && model.IsMoving) session.SetFocus(cancel);
            else if ((focused == moveMessage || focused == archiveMessage || focused == trashMessage) && model.IsMoving) session.SetFocus(cancel);
            else if ((focused == changeRead || focused == changeStar) && model.IsChangingMessageFlags) session.SetFocus(cancel);
            else if (focused == offlineHeaders && model.IsReceivingOfflineHeaders) session.SetFocus(cancel);
            else if ((focused == download || focused == offlineDownload) && model.IsDownloadingBodies) session.SetFocus(cancel);
            // Any other command that stays disabled once its work is done hands focus on; Load older
            // on the last page goes to the list it extended or, while the reader is shown alone, to
            // Back to inbox, which leads there.
            else if (!model.IsBusy) FocusNavigation.KeepFocusUsable(session, panel, focused != older ? null : layout.ShowsReaderOnly ? back : list);
        }
        inspectHeaders.Clicked += (_, _) => { extendedHeaders = !extendedHeaders; Update(); };
        showSearch.Clicked += (_, _) => { searchExpanded = true; searchRow.Visibility = UiVisibility.Visible; Update(); if (panel.Session is { } session) FocusNavigation.FocusAndReveal(session, searchText); };
        clearSearch.Clicked += async (_, _) =>
        {
            await model.ClearSearchAsync(); searchExpanded = false; searchRow.Visibility = UiVisibility.Collapsed; Update();
            if (panel.Session is { } session) FocusNavigation.FocusAndReveal(session, showSearch);
        };
        listRetry.Clicked += async (_, _) => await model.RetryAsync(InboxProblemScope.List);
        messageRetry.Clicked += async (_, _) => await model.RetryAsync(InboxProblemScope.Message);
        void FocusVisiblePane()
        {
            if (panel.Session is not { } session || !layout.IsCompact) return;
            UiElement hidden = layout.ShowsReaderOnly ? listPane : reading;
            if (session.FocusedElement is { } focused && (focused == hidden || focused.IsDescendantOf(hidden)))
                if (layout.ShowsReaderOnly) FocusReader(); else session.SetFocus(list);
        }
        // Called as the reader's header area is measured in its space, once its rows are measured: true when
        // the commands moved, so the area measures them again where they are now.
        bool PlaceCommands(BSize space) => MoveCommands(!layout.IsCompact && CommandsFitBelowHeader(space));
        bool CommandsFitBelowHeader(BSize space)
        {
            // Hidden commands, without a message, stay where they are, as they do in a space without bounds.
            var buttons = replyActions.Children.Where(button => button.Visibility != UiVisibility.Collapsed).ToArray();
            if (replyActions.Visibility == UiVisibility.Collapsed || buttons.Length == 0 || !double.IsFinite(space.Width) || !double.IsFinite(space.Height))
                return replyActions.Parent == pinnedCommands;
            // They stay in view only on one row at the reader's width, below the header's first line, and
            // above the lines the header keeps for the text. In a narrower or shorter reader, such as one
            // just wider than the compact reader at a large text size, they would wrap and take the
            // subject's and the text's room; there they end the header before the HTML preview's row, as in
            // the compact reader, which has no height to spare for them, and scroll with it.
            double padding = 2 * replyActions.Padding;
            double row = buttons.Sum(button => button.DesiredSize.Width) + ((buttons.Length - 1) * replyActions.Spacing) + padding;
            double width = Math.Min(space.Width - (2 * ReadingColumn.MarginFor(space.Width)), ReadingColumn.DefaultMaximumWidth);
            double commands = Math.Max(buttons.Max(button => button.DesiredSize.Height) + padding, replyActions.PreferredSize.Height) + (2 * commandsInset) + divider.Thickness;
            double firstLine = headerContent.VerticalMargin + BTextMeasurer.GetLineHeight(subjectLine.Font);
            return row <= width && space.Height - commands >= firstLine + text.HeightOfLines(textLinesKept);
        }
        bool MoveCommands(bool pin)
        {
            UiElement place = pin ? pinnedCommands : headerStack;
            if (replyActions.Parent == place) return false;
            var focused = panel.Session?.FocusedElement;
            bool keepFocus = focused is not null && (focused == replyActions || focused.IsDescendantOf(replyActions));
            replyActions.Parent?.RemoveChild(replyActions);
            if (pin) pinnedCommands.AddChild(replyActions);
            else headerStack.InsertChild(headerStack.Children.ToList().IndexOf(previewActions), replyActions);
            if (keepFocus && focused!.CanFocus) panel.Session!.SetFocus(focused);
            return true;
        }
        layout.ModeChanged += (_, _) =>
        {
            headerStack.MoveChild(backRow, layout.ShowsReaderOnly ? 0 : 3);
            // The compact reader keeps them in its header; beside the list, the header area places them as it is measured.
            if (layout.IsCompact) MoveCommands(pin: false);
            back.Visibility = layout.ShowsReaderOnly ? UiVisibility.Visible : UiVisibility.Collapsed;
            backRow.Visibility = layout.ShowsReaderOnly || model.Body?.Composition is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            FocusVisiblePane();
            PanesChanged?.Invoke(this, EventArgs.Empty);
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
            try { await loading; }
            catch (OperationCanceledException) { }
        };
        cancel.Clicked += (_, _) => model.Cancel();
        list.SelectionChanged += async (_, _) =>
        {
            if (!updating && !model.CanSelect) { Update(); return; }
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == list.SelectedItemId) is { } item)
            {
                try { await model.SelectAsync(item.Key); }
                catch (OperationCanceledException) { }
            }
        };
        list.ItemActivated += async (_, e) =>
        {
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == e.Item.Id) is { } item)
            {
                var loading = model.SelectAsync(item.Key);
                OpenReader();
                try { await loading; }
                catch (OperationCanceledException) { }
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=3F7EE3
    // Broiler-Falsified-If: two loaded messages with different keys get the same Id, so choosing one row opens the other
    // Broiler-Human:        PENDING
    private static string Id(MailMessageSummary message) => $"{message.Key.AccountId}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message.Key.MailboxId))}:{message.Key.UidValidity}:{message.Key.Uid}";
}
