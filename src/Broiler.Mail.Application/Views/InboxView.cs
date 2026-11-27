// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   18
// Annotated:        3/18
// Exempt:           14
// Human-reviewed:   0/18
// IP risk:          Low
// Security risk:    Medium
// Criteria:         3/0
// Resource impact:  7/10 max
// Unverified:       18
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
using Broiler.UI.Button;
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
using Broiler.UI.Menu;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Edit;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Splitter.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

// Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=576E70
// Hairs-Falsified-If: selecting another message leaves the HTML preview window of the previous body open
// Hairs-Human:        PENDING
public sealed class InboxView(InboxViewModel model, IInlineHtmlPreviewHost? htmlPreview = null, MessageDateFormatter? dates = null, CompositionCommands? commands = null, SettingsViewModel? settings = null)
{
    internal MailShellView? Shell { get; init; }

    /// <summary>The picker the reader's attachment saves use; the shell's when not set, and otherwise none.</summary>
    internal IMailFilePicker? Files { get; init; }

    private Func<bool>? _back;
    private Func<bool>? _open;
    private Func<bool>? _find;
    private Func<bool>? _closeFind;
    private Func<bool>? _showsMessage;
    private Func<bool>? _search;
    private Func<bool>? _clearSearch;
    private Func<UiInputEvent, bool>? _readerInput;
    public bool HandleReaderInput(UiInputEvent input) => _readerInput?.Invoke(input) == true;
    private AdaptiveInboxLayout? _layout;

    /// <summary>The label of the compact reader's way back to the list, which the footer names.</summary>
    public const string BackText = "Back to inbox";

    /// <summary>In compact mode, returns from the reader to the list. False when there is nothing to go back from.</summary>
    public bool GoBackToList() => _back?.Invoke() == true;
    /// <summary>Reloads the selected message and, in compact mode, shows it in place of the list.</summary>
    public bool OpenSelected() => _open?.Invoke() == true;
    /// <summary>Shows the find bar for the open message's text. False when no message body is open.</summary>
    public bool ShowFindInMessage() => _find?.Invoke() == true;
    /// <summary>
    /// Closes the open message's find bar and focuses its text. False when no find bar is open, or when the reader
    /// that holds it is not shown, so Escape and Back go on to what the user can see.
    /// </summary>
    public bool CloseFindInMessage() => _closeFind?.Invoke() == true;
    /// <summary>
    /// Whether the reader shows a selected message, beside the list or alone in compact mode, even one whose body
    /// failed to load. Ctrl+F falls back to mailbox search only when it does not.
    /// </summary>
    public bool ShowsMessage => _showsMessage?.Invoke() == true;
    /// <summary>Shows and focuses mailbox search. False when this inbox cannot search saved mail now.</summary>
    public bool ShowSearch() => _search?.Invoke() == true;
    /// <summary>
    /// Clears the search header's query or, when it is empty, closes the header and exits search, as Escape
    /// does there. False when no search header is in view or work runs.
    /// </summary>
    public bool ClearOrExitSearch() => _clearSearch?.Invoke() == true;

    private Func<bool>? _canSaveEml;
    private Func<Task<bool>>? _saveEml;
    /// <summary>Whether Save as .eml… can save the open message now: it has a source, the picker saves files, and nothing runs.</summary>
    public bool CanSaveMessageAsEml => _canSaveEml?.Invoke() == true;
    /// <summary>
    /// Saves the open message as an .eml file the person chooses, as the reader's Save as .eml… does; the compact
    /// list opens the reader first. False when it cannot run now or nothing was saved.
    /// </summary>
    public Task<bool> SaveMessageAsEml() => _saveEml?.Invoke() ?? Task.FromResult(false);

    private Func<bool>? _cancelAttachmentSave;
    /// <summary>
    /// Cancels the reader's running attachment save, as Escape does wherever focus is. False when no save runs, so
    /// Escape goes on to close the find bar or leave the reader.
    /// </summary>
    public bool CancelAttachmentSave() => _cancelAttachmentSave?.Invoke() == true;

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

    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=2611B8
    // Hairs-Falsified-If: selecting another message leaves the HTML preview window of the previous body open
    // Hairs-Human:        PENDING
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
        var compose = MailToolbarButton.Labeled("New message", MailCommand.NewMessage);
        if (commands is not null)
        {
            toolbar.AddChild(compose);
            compose.Clicked += (_, _) => commands.StartNew();
        }
        var receive = new StandardButton { Text = "Receive mail", ToolTipText = $"Receive mail ({MailShortcuts.For(MailCommand.Receive).Gesture})" };
        var older = new StandardButton { Text = "Load older" };
        var read = new StandardButton { Text = "Read message" };
        var cancel = MailToolbarButton.WithSymbol("Cancel", "×");
        foreach (var button in new[] { receive, read, older, cancel }) toolbar.AddChild(button);
        var download = new StandardButton { Text = "Download loaded messages" };
        var folders = new StandardComboBox { AccessibleName = "Mail folder", PreferredSize = new BSize(240, 32) };
        var refreshFolders = new StandardButton { Text = "Refresh folders" };
        IReadOnlyList<MailFolder>? shownFolders = null;
        if (model.SupportsFolders) toolbar.AddChild(folders);
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
        captureSnapshot.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.CaptureMailboxSnapshotAsync());
        inspectSnapshot.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.InspectMailboxSnapshotAsync());
        offlineActions.AddChild(offlineToggle); offlineActions.AddChild(offlineDownload); offlineActions.AddChild(offlineRestart); offlineActions.AddChild(offlineReload); offlineActions.AddChild(offlineHeaders); offlineActions.AddChild(offlineHeadersRestart); offlineActions.AddChild(offlineShowHeaders);
        offlineSection.Content.AddChild(offlineActions); offlineSection.Content.AddChild(offlineHeaderFeedback);
        if (model.SupportsMailboxSnapshots) offlineSection.Content.AddChild(snapshotFeedback);
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
        var undoMove = new StandardButton { Text = "Undo last move", Visibility = UiVisibility.Collapsed };
        if (model.SupportsMoves) toolbar.AddChild(undoMove);
        undoMove.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.UndoLastMoveAsync());
        // Search is a header at the top of the list pane, never a dialog: open from the toolbar, Ctrl+K or the
        // menu, and shown while a search is active, so its results and pages stay in view beside the list.
        bool searchOpen = false;
        UiElement? searchReturnFocus = null;
        var showSearch = MailToolbarButton.Labeled("Search saved mail", MailCommand.SearchMail);
        if (model.SupportsLocalSearch) toolbar.AddChild(showSearch);
        var options = new ToolbarDisclosure(new StandardButton { Text = "Mailbox options" });
        bool hasMailboxOptions = model.HasPersistentCache || model.SupportsFolders || model.SupportsOfflineFolders ||
            model.SupportsMailboxSnapshots || model.SupportsAccountNavigation;
        if (hasMailboxOptions) toolbar.AddChild(options.Button);
        var mailboxActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        if (model.HasPersistentCache) mailboxActions.AddChild(download);
        if (model.SupportsFolders) mailboxActions.AddChild(refreshFolders);
        if (mailboxActions.Children.Count > 0) options.Content.AddChild(mailboxActions);
        var mailboxInfo = new InlineFeedback();
        options.Content.AddChild(mailboxInfo);
        if (model.SupportsOfflineFolders || model.SupportsMailboxSnapshots) options.Content.AddChild(offlineSection);
        if (model.SupportsAccountNavigation) options.Content.AddChild(navigationSection);
        var optionsFrame = new BoundedScrollArea(options, 0.35, "Mailbox options");
        var mailboxDialogCancel = new StandardButton { Text = "Cancel operation" };
        mailboxDialogCancel.Clicked += (_, _) => model.Cancel();
        if (Shell is not null && hasMailboxOptions)
        {
            options.RemoveChild(options.Content);
            options.Content.InsertChild(0, mailboxDialogCancel);
            Shell.RegisterInboxDialog("mailbox-options", "Mailbox options", options.Content, () => true);
            // The toolbar opens the dialog directly in the shell.
            toolbar.RemoveChild(options.Button);
            var openMailboxOptions = new StandardButton { Text = "Mailbox options" };
            openMailboxOptions.Clicked += (_, _) => Shell.ShowView("mailbox-options");
            toolbar.AddChild(openMailboxOptions);
        }
        var searchText = new StandardEdit { PlaceholderText = "Search mail…", AccessibleName = "Saved mail search text", MaxLength = 256, PreferredSize = new BSize(220, 32) };
        var searchSender = new StandardEdit { AccessibleName = "Saved sender header contains (optional)", PlaceholderText = "Sender contains (optional)", MaxLength = 256, PreferredSize = new BSize(220, 32) };
        var searchScope = new StandardComboBox { AccessibleName = "Saved search folder scope", PreferredSize = new BSize(220,32) };
        searchScope.SetItems(new[] { new UiComboBoxItem("folder","Current folder"),new UiComboBoxItem("account","All saved folders for this account") }); searchScope.SelectedIndex = 0;
        var accountSearchPresenter = new MailMessageItemPresenter(dateFormat,showMailbox:true);
        var folderSearchPresenter = dates is null ? MailMessageItemPresenter.Instance : new MailMessageItemPresenter(dateFormat);
        // Where the text is looked for, as three human scopes; the cache picks the index or a direct scan itself.
        // Toolbar combos carry no caption, so each item says what it filters.
        var searchIn = new StandardComboBox { AccessibleName = "Search in", PreferredSize = new BSize(210, 32) };
        searchIn.SetItems([new UiComboBoxItem("everywhere", "Everywhere (headers & body)"), new UiComboBoxItem("headers", "Sender or subject"), new UiComboBoxItem("body", "Message body")]);
        searchIn.SelectedIndex = 0;
        bool?[] readChoices = [null, false, true];
        var searchRead = new StandardComboBox { AccessibleName = "Read state", PreferredSize = new BSize(150, 32) };
        searchRead.SetItems([new UiComboBoxItem("any", "Read or unread"), new UiComboBoxItem("unread", "Unread"), new UiComboBoxItem("read", "Read")]);
        searchRead.SelectedIndex = 0;
        bool?[] starChoices = [null, true, false];
        var searchStarred = new StandardComboBox { AccessibleName = "Starred state", PreferredSize = new BSize(150, 32) };
        searchStarred.SetItems([new UiComboBoxItem("any", "Starred or not"), new UiComboBoxItem("starred", "Starred"), new UiComboBoxItem("unstarred", "Not starred")]);
        searchStarred.SelectedIndex = 0;
        bool?[] offlineChoices = [null, true, false];
        var searchOffline = new StandardComboBox { AccessibleName = "Offline availability", PreferredSize = new BSize(170, 32) };
        searchOffline.SetItems([new UiComboBoxItem("any", "Downloaded or not"), new UiComboBoxItem("downloaded", "Downloaded"), new UiComboBoxItem("headers", "Headers only")]);
        searchOffline.SelectedIndex = 0;
        // Shown only while the edited scope and filters cannot match anything, which is also why Search is unavailable.
        var searchConflict = new InlineFeedback();
        var searchDate = new StandardComboBox { AccessibleName = "Saved search received date", PreferredSize = new BSize(160, 32) };
        int?[] searchDays = [null, 7, 30, 365];
        searchDate.SetItems(new[] { "Any date", "Last 7 days", "Last 30 days", "Last year", "Custom UTC dates" }.Select((text, i) => new UiComboBoxItem(i.ToString(), text)));
        searchDate.SelectedIndex = 0;
        var customDates = new StandardEdit { AccessibleName = "UTC date range (YYYY-MM-DD..YYYY-MM-DD, end exclusive)", PlaceholderText = "YYYY-MM-DD..YYYY-MM-DD", MaxLength = 24, PreferredSize = new BSize(240, 32), Visibility = UiVisibility.Collapsed };
        var search = new StandardButton { Text = "Search", ToolTipText = "Search saved mail (Enter)" };
        // The pages are symbol buttons, so the results strip keeps one row of buttons in a narrow list at a large text size.
        var previousSearch = MailToolbarButton.WithSymbol("Previous saved results", "←");
        previousSearch.AccessibleName = "Previous saved results"; previousSearch.Visibility = UiVisibility.Collapsed;
        var nextSearch = MailToolbarButton.WithSymbol("Next saved results", "→");
        nextSearch.AccessibleName = "Next saved results"; nextSearch.Visibility = UiVisibility.Collapsed;
        var exitSearch = new StandardButton { Text = "Exit search" };
        // Shown only when saved folders changed under the search's pages: it restarts the submitted search from its newest page.
        var refreshSearch = new StandardButton { Text = "Refresh results", ToolTipText = "Run this search again from the newest page", Visibility = UiVisibility.Collapsed };
        var clearQuery = MailToolbarButton.WithSymbol("Clear search", "×");
        // What the active search covers, which is long, opens below the filters, where it scrolls with the controls
        // instead of taking the list's rows. Its button is shown while a search is active.
        var searchDetails = new ToolbarDisclosure(new StandardButton { Text = "Details", AccessibleName = "Search details", Visibility = UiVisibility.Collapsed });
        var searchDetailsText = new InlineFeedback();
        searchDetails.Content.AddChild(searchDetailsText);
        // Row 1 is the query, its scope, Search, Filters, Details and Clear, in tab order; the filters open below it.
        var searchRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var filters = new ToolbarDisclosure(new StandardButton { Text = "Filters", AccessibleName = "Search filters" });
        foreach (var control in new UiElement[] { searchText, searchScope, search, filters.Button, searchDetails.Button, clearQuery })
            searchRow.AddChild(control);
        var filterRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        foreach (var control in new UiElement[] { searchSender, searchIn, searchRead, searchStarred, searchOffline, searchDate, customDates })
            filterRow.AddChild(control);
        filters.Content.AddChild(filterRow);
        filters.Content.AddChild(searchConflict);
        var searchControls = new StandardPanel();
        searchControls.AddChild(searchRow);
        searchControls.AddChild(filters);
        searchControls.AddChild(searchDetails);
        var searchFrame = new BoundedScrollArea(searchControls, 0.35, "Saved search controls");
        // While a search is active its results strip, one wrapping row, says what the list shows and offers the
        // pages and the way out. It keeps its height below the controls, which scroll at a large text size instead.
        var searchSummary = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, Role = StandardLabelRole.Muted, ToolTipText = InboxViewModel.SearchPausesReceiving };
        // What the search could not cover (the index was off, or some mail has headers only) is a short note in the
        // strip; the full sentence is its tooltip and is repeated in Details.
        var searchCoverage = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, Role = StandardLabelRole.Muted, AccessibleName = "Search coverage", Visibility = UiVisibility.Collapsed };
        var resultsStrip = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8, AccessibleName = "Saved search results", Visibility = UiVisibility.Collapsed };
        foreach (var control in new UiElement[] { searchSummary, searchCoverage, previousSearch, nextSearch, refreshSearch, exitSearch }) resultsStrip.AddChild(control);
        // The controls' share is of the height the strip and the list's kept rows leave (set with the list below).
        Func<double> keptListHeight = () => 0;
        var searchHeader = new FirstTakesRestStack { Visibility = UiVisibility.Collapsed, Reserve = () => keptListHeight() };
        searchHeader.Add(searchFrame);
        // The strip is whole while that leaves the list its kept rows, or else takes up to half the header's height and
        // scrolls, so in a pane too short for it, such as at 640x480 with 200 % text, Exit search can still be reached.
        var resultsArea = new BoundedScrollArea(resultsStrip, 0.5, "Search results strip") { MinimumRemaining = () => keptListHeight(), Visibility = UiVisibility.Collapsed };
        searchHeader.AddBounded(resultsArea);
        panel.AddChild(toolbar); panel.SetDock(toolbar, UiDock.Top);
        if (Shell is null && hasMailboxOptions) { panel.AddChild(optionsFrame); panel.SetDock(optionsFrame, UiDock.Top); }
        searchText.TextChanged += (_, _) => { if (!updating) model.SetSearchText(searchText.Text); };
        searchSender.TextChanged += (_, _) => { if (!updating) model.SetSearchSenderText(searchSender.Text); };
        searchRead.SelectionChanged += (_, _) => { if (!updating && searchRead.SelectedIndex is >= 0 and < 3) model.SetSearchReadState(readChoices[searchRead.SelectedIndex]); };
        searchStarred.SelectionChanged += (_, _) => { if (!updating && searchStarred.SelectedIndex is >= 0 and < 3) model.SetSearchStarState(starChoices[searchStarred.SelectedIndex]); };
        searchOffline.SelectionChanged += (_, _) => { if (!updating && searchOffline.SelectedIndex is >= 0 and < 3) model.SetSearchBodyAvailability(offlineChoices[searchOffline.SelectedIndex]); };
        searchScope.SelectionChanged += (_,_) => { if (!updating && searchScope.SelectedIndex is 0 or 1) model.SetSearchAllSavedFolders(searchScope.SelectedIndex == 1); };
        searchIn.SelectionChanged += (_, _) => { if (!updating && searchIn.SelectedIndex is >= 0 and < 3) model.SetSearchScope((MailSearchScope)searchIn.SelectedIndex); };
        searchDate.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            if (searchDate.SelectedIndex is >= 0 and < 4) model.SetSearchReceivedWithinDays(searchDays[searchDate.SelectedIndex]);
            else if (searchDate.SelectedIndex == 4) model.SetSearchCustomDateRange(customDates.Text);
        };
        customDates.TextChanged += (_, _) => { if (!updating && model.SearchUsesCustomDates) model.SetSearchCustomDateRange(customDates.Text); };
        previousSearch.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.LoadPreviousSearchPageAsync());
        nextSearch.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.LoadNextSearchPageAsync());
        refreshSearch.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            // Pages of the stale search are not offered again.
            previousSearch.Visibility = UiVisibility.Collapsed;
            await model.RefreshSearchResultsAsync();
        });
        search.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.SearchSavedHeadersAsync());
        // Enter in the query searches; the header stays open with its results below.
        searchText.Submitted += UiAsync.Handler<UiEditSubmittedEventArgs>(async (_, _) => { if (model.CanSearch) await model.SearchSavedHeadersAsync(); });
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
        retryMoveCache.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RetryMoveCacheAsync());
        var retryDeleteCache = new StandardButton { Text = "Retry local deletion cleanup", Visibility = UiVisibility.Collapsed };
        if (model.SupportsPermanentDelete) listRetryRow.AddChild(retryDeleteCache);
        retryDeleteCache.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RetryDeleteCacheAsync());
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
        keptListHeight = () => listRowsKept * list.EffectiveItemHeight;
        var listNotice = new FirstTakesRestStack();
        listNotice.Add(noticeArea);
        listNotice.Add(listRetryRow);
        if (model.SupportsLocalSearch) { listPane.AddChild(searchHeader); listPane.SetDock(searchHeader, UiDock.Top); }
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
        var meta = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false, Role = StandardLabelRole.Muted };
        // The reader's rows of commands are framed strips like the toolbar, and inset their buttons as it does.
        var replyActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36) };
        var reply = MailToolbarButton.Labeled("Reply", MailCommand.Reply);
        var replyAll = MailToolbarButton.Labeled("Reply all", MailCommand.ReplyAll);
        var forward = MailToolbarButton.Labeled("Forward", MailCommand.Forward);
        foreach (var button in new[] { reply, replyAll, forward }) replyActions.AddChild(button);
        // The HTML preview's row is collapsed while empty, so it adds no gap below the row above it.
        var previewActions = new StandardPanel { Visibility = UiVisibility.Collapsed };
        MessageTextFindBar? messageFind = null;
        RemoteImageActions? currentImageActions = null;
        var findArea = new StandardPanel { Visibility = UiVisibility.Collapsed, AccessibleName = "Message text find" };
        // The remote-images banner is shown while the HTML reader is and the message has remote images.
        var imageArea = new StandardPanel { Visibility = UiVisibility.Collapsed, AccessibleName = "Remote images notice" };
        // The attachment strip comes first, above the remote-images notice, in the HTML reader as in plain text.
        var attachmentArea = new StandardPanel { Visibility = UiVisibility.Collapsed, AccessibleName = "Attachments" };
        MessageAttachmentStrip? attachmentStrip = null;
        // The reader's banners, the remote-images notice and then Find, share one slot below the line under the
        // header and above the message text, in the reader as beside the list. It is collapsed while none of
        // them is shown, so a reader without one keeps its layout, and inset like the commands above the line.
        // It follows the header's rule: it shows its banners whole while that leaves the text its six lines below
        // a header at its share, or else up to its own share of the height the commands and the line leave, and
        // scrolls the rest, so Learn more, a long find result or both banners at once never take the text's lines.
        var readerBanners = new StandardPanel { Spacing = 4, AccessibleName = "Message notices" };
        readerBanners.AddChild(attachmentArea);
        readerBanners.AddChild(imageArea);
        readerBanners.AddChild(findArea);
        // A message that could not be loaded explains why below its subject, sender and date, and offers
        // Retry there, in the compact reader as beside the list.
        var messageFeedback = new InlineFeedback();
        var messageRetry = new StandardButton { Text = "Retry loading" };
        var messageRetryRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36) };
        messageRetryRow.AddChild(messageRetry);
        var back = new StandardButton { Text = BackText };
        var backRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, PreferredSize = new BSize(0, 36), Visibility = UiVisibility.Collapsed };
        backRow.AddChild(back);
        // Star sits beside the subject where the account can star messages.
        var star = SubjectRow.CreateStar(() => model.SelectedMessage?.IsStarred == true);
        star.Visibility = UiVisibility.Collapsed;
        var readStateFeedback = new InlineFeedback();
        star.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.SetSelectedStarStateAsync(model.SelectedMessage?.IsStarred != true));
        UiElement subjectRow = model.SupportsStarStateChanges ? new SubjectRow(subjectLine, star) : subjectLine;
        var moveDestination = new StandardComboBox { AccessibleName = "Move destination", PreferredSize = new BSize(240, 32) };
        var moveMessage = new StandardButton { Text = "Move message" };
        var closeMove = MailToolbarButton.WithSymbol("Close move picker", "×");
        var archiveMessage = new StandardButton { Text = "Archive" };
        var trashMessage = new StandardButton { Text = "Trash", ToolTipText = "Move to trash" };
        var moveFeedback = new InlineFeedback();
        // Move's popup offers a few folders; Choose folder… opens this row, which lists them all.
        bool moveBarOpen = false;
        var moveBar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8, Visibility = UiVisibility.Collapsed, AccessibleName = "Move to folder" };
        foreach (var control in new UiElement[] { moveDestination, moveMessage, closeMove }) moveBar.AddChild(control);
        IReadOnlyList<MailFolder>? shownMoveDestinations = null;
        moveDestination.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            int index = moveDestination.SelectedIndex;
            if (index >= 0 && index < model.MoveDestinations.Count) model.ChooseMoveDestination(model.MoveDestinations[index].MailboxId);
        };
        moveMessage.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.MoveSelectedAsync(model.MoveDestinationMailboxId));
        archiveMessage.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ArchiveSelectedAsync());
        trashMessage.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.TrashSelectedAsync());
        var requestDelete = new StandardButton { Text = "Delete permanently…" };
        var deleteConfirmation = new StandardPanel { Spacing = 4, Visibility = UiVisibility.Collapsed, AccessibleName = "Permanent deletion confirmation" };
        var deleteConfirmationText = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap };
        var deleteConfirmationActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = toolbar.Padding, Spacing = 8 };
        var cancelDelete = new StandardButton { Text = "Keep message" };
        var confirmDelete = new StandardButton { Text = "Confirm permanent deletion" };
        deleteConfirmationActions.AddChild(cancelDelete); deleteConfirmationActions.AddChild(confirmDelete);
        deleteConfirmation.AddChild(deleteConfirmationText); deleteConfirmation.AddChild(deleteConfirmationActions);
        var deleteFeedback = new InlineFeedback();
        requestDelete.Clicked += (_, _) =>
        { if (model.RequestPermanentDelete() && deleteConfirmation.Session is { } session) FocusNavigation.FocusAndReveal(session, cancelDelete); };
        cancelDelete.Clicked += (_, _) => model.CancelPermanentDeleteConfirmation();
        confirmDelete.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ConfirmPermanentDeleteAsync());
        // The reader's commands are one strip of groups: Reply, Reply all and Forward; then Archive, Trash (Delete
        // permanently… in Trash) and Move; then More message actions, whose popup holds the rest. Nothing opens a dialog.
        Func<IEnumerable<UiMenuItem>> moveItems = () => [], moreItems = () => [];
        var moveMenu = new MailMenuButton(MailToolbarButton.Labeled("Move ▾"), () => moveItems(), "Move to folder");
        moveMenu.Trigger.AccessibleName = "Move to folder";
        moveMenu.Trigger.ToolTipText = "Move to folder";
        var moreActions = new MailMenuButton(MailToolbarButton.WithSymbol("More message actions", "⋯"), () => moreItems(), "More message actions");
        UiElement[] respondGroup = [reply, replyAll, forward];
        UiElement[] triageGroup = [archiveMessage, trashMessage, requestDelete, moveMenu];
        foreach (var element in triageGroup) { element.Visibility = UiVisibility.Collapsed; replyActions.AddChild(element); }
        replyActions.AddChild(moreActions);
        // A separator starts each group after the first that is shown, so a strip without triage is laid out as before.
        void UpdateGroups()
        {
            bool respond = respondGroup.Any(element => element.Visibility != UiVisibility.Collapsed);
            var firstTriage = triageGroup.FirstOrDefault(element => element.Visibility != UiVisibility.Collapsed);
            foreach (var element in triageGroup)
                replyActions.SetBreakBefore(element, element == firstTriage && respond ? UiToolbarBreak.Separator : UiToolbarBreak.None);
            replyActions.SetBreakBefore(moreActions, firstTriage is not null ? UiToolbarBreak.Separator : UiToolbarBreak.None);
        }
        var sourceArea = new StandardPanel { Visibility = UiVisibility.Collapsed };
        MessageSourceExportActions? sourceExportActions = null;
        var headerStack = new StandardPanel { Spacing = 4 };
        foreach (var element in new UiElement[] { backRow, subjectRow, detailsFrame, meta, messageFeedback, messageRetryRow,
            replyActions, previewActions }) headerStack.AddChild(element);
        if (model.SupportsMoves) headerStack.AddChild(moveBar);
        if (model.SupportsPermanentDelete) headerStack.AddChild(deleteConfirmation);
        headerStack.AddChild(readStateFeedback);
        if (model.SupportsMoves) headerStack.AddChild(moveFeedback);
        if (model.SupportsPermanentDelete) headerStack.AddChild(deleteFeedback);
        // The original .eml export is a collapsed section at the end of the header, which More message actions opens.
        headerStack.AddChild(sourceArea);
        // A long subject or many recipients at a large text size scroll within the header, so the
        // message text keeps most of the pane. The header ends between its rows or lines of text, not
        // inside one: while the text keeps a few lines it grows to show the whole header, or the next
        // row of buttons whole with the gap below it, which may narrow the text's margin below its lines,
        // and otherwise ends above the row or line its share would cut.
        const double headerShare = 0.45;
        const int textLinesKept = 6;
        var text = new ScrollableMessageText();
        // A preview cut at 32,000 characters ends with a command that shows the complete retained text the
        // message already carries. It scrolls with the text, below its end, so it never takes rows from the header.
        bool fullText = false;
        var expandText = new StandardButton { ToolTipText = "Shows the complete decoded text saved with this message, up to 1,048,576 characters. Find then searches all of it." };
        var expandRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8, AccessibleName = "Full message text", Visibility = UiVisibility.Collapsed };
        expandRow.AddChild(expandText);
        text.Footer = expandRow;
        var headerContent = new ReadingColumn(headerStack, verticalMargin: 8);
        UiElement[] buttonRows = [backRow, messageRetryRow, replyActions, moveBar, deleteConfirmation];
        var headerColumn = new BoundedScrollArea(headerContent, headerShare, "Message header")
        {
            Rows = HeaderRows,
            MinimumRemaining = () => text.HeightOfLines(textLinesKept),
        };
        // The banners leave the header and the text enough that the header at its share still leaves the text its six lines.
        const double bannerShare = 0.35;
        var bannerColumn = new ReadingColumn(new Inset(readerBanners, 0, 4));
        var bannerSlot = new BoundedScrollArea(bannerColumn, bannerShare, "Reader notices")
        {
            Visibility = UiVisibility.Collapsed,
            MinimumRemaining = () => text.HeightOfLines(textLinesKept) / (1 - headerShare),
        };
        // While the banners scroll, the bar sits in the right margin and they keep their layout, as the header does.
        bannerColumn.PaneWidth = () => bannerSlot.PaneWidth;
        void UpdateBanners() => bannerSlot.Visibility = readerBanners.Children.Any(child => child.Visibility != UiVisibility.Collapsed)
            ? UiVisibility.Visible : UiVisibility.Collapsed;
        // While the header scrolls, its bar sits in the right margin and its rows keep their layout.
        headerContent.PaneWidth = () => headerColumn.PaneWidth;
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
        // Measured before the header, in the height the commands and the line leave, so the header's share and
        // the lines it keeps for the text are of the height an open banner leaves.
        headerArea.AddBounded(bannerSlot);
        reading.AddChild(headerArea);
        reading.SetDock(headerArea, UiDock.Top);
        var bodyArea = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        bodyArea.AddChild(text);
        reading.AddChild(bodyArea);
        UiElement? inlineContent = null;
        Action? showTextForFind = null;
        bool preferHtml = settings?.Settings.PreferPlainText != true;
        bool findChangedMode = false;
        _readerInput = input =>
        {
            if (panel.IsDisposed) return false;
            // Escape in the search header clears its query, then closes it and exits search; while work runs it
            // falls through to Escape's cancel instead.
            if (MailShortcuts.Match(input)?.Command == MailCommand.BackOrCancel && !model.IsBusy && attachmentStrip?.IsBusy != true &&
                panel.Session?.FocusedElement is { } inSearch && inSearch.IsDescendantOf(searchHeader) && _clearSearch?.Invoke() == true)
                return true;
            if (layout.IsCompact && !layout.ShowsReaderOnly) return false;
            var focused = panel.Session?.FocusedElement;
            // Typing in the find query never reaches the HTML reader's zoom keys.
            bool inReader = focused is not null && (focused == text || focused.IsDescendantOf(text) ||
                (inlineContent is not null && (focused == inlineContent || focused.IsDescendantOf(inlineContent))) ||
                focused.IsDescendantOf(previewActions) || focused.IsDescendantOf(imageArea));
            bool findScope = focused is not null && (focused == reading || focused.IsDescendantOf(reading));
            // Escape in the reader cancels a running attachment save before it closes find or leaves the reader.
            if (findScope && attachmentStrip is { IsBusy: true } saving && MailShortcuts.Match(input)?.Command == MailCommand.BackOrCancel)
            { saving.Cancel(); return true; }
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
                    SubjectRow row => BTextMeasurer.GetLineHeight(row.Label.Font),
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
        void MoveReader(double fraction)
        {
            if (panel.IsDisposed || model.Body is null || !ReferenceEquals(shownBody, model.Body)) return;
            ActiveViewport()?.RestoreReadingPosition(fraction);
        }
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
            cancel.Visibility = model.IsBusy ? UiVisibility.Visible : UiVisibility.Collapsed;
            compose.IsEnabled = commands?.CanCompose == true;
            undoMove.Visibility = model.CanUndoMove ? UiVisibility.Visible : UiVisibility.Collapsed;
            undoMove.IsEnabled = model.CanUndoMove;
            if (searchText.Text != model.SearchText) searchText.Text = model.SearchText;
            if (searchSender.Text != model.SearchSenderText) searchSender.Text = model.SearchSenderText;
            searchSender.IsEnabled = !model.IsBusy;
            int readIndex = Array.IndexOf(readChoices, model.SearchReadState);
            if (searchRead.SelectedIndex != readIndex) searchRead.SelectedIndex = readIndex;
            int starIndex = Array.IndexOf(starChoices, model.SearchStarState);
            if (searchStarred.SelectedIndex != starIndex) searchStarred.SelectedIndex = starIndex;
            int offlineIndex = Array.IndexOf(offlineChoices, model.SearchBodyCached);
            if (searchOffline.SelectedIndex != offlineIndex) searchOffline.SelectedIndex = offlineIndex;
            int dateIndex = model.SearchUsesCustomDates ? 4 : Array.IndexOf(searchDays, model.SearchReceivedWithinDays);
            if (searchDate.SelectedIndex != dateIndex) searchDate.SelectedIndex = dateIndex;
            if (searchIn.SelectedIndex != (int)model.SearchScope) searchIn.SelectedIndex = (int)model.SearchScope;
            searchIn.IsEnabled = searchRead.IsEnabled = searchStarred.IsEnabled = searchOffline.IsEnabled = !model.IsBusy;
            searchConflict.Set(model.SearchFilterConflict ?? "", FeedbackKind.Error);
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
            refreshSearch.Visibility = model.CanRefreshSearchResults ? UiVisibility.Visible : UiVisibility.Collapsed;
            // The query stays enabled while a search runs, so focus stays in it and Escape still reaches Cancel.
            exitSearch.IsEnabled = clearQuery.IsEnabled = !model.IsBusy;
            searchHeader.Visibility = model.SupportsLocalSearch && (searchOpen || model.IsSearchActive) ? UiVisibility.Visible : UiVisibility.Collapsed;
            resultsStrip.Visibility = resultsArea.Visibility = model.IsSearchActive ? UiVisibility.Visible : UiVisibility.Collapsed;
            searchSummary.Text = model.SearchSummary ?? "";
            searchCoverage.Text = model.SearchCoverageSummary ?? "";
            searchCoverage.ToolTipText = model.SearchCoverageNote ?? "";
            searchCoverage.Visibility = model.SearchCoverageSummary is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            searchDetailsText.Set(model.SearchNotice is { } notice ? model.SearchCoverageNote is { } coverage ? notice + " " + coverage : notice : "", FeedbackKind.Information);
            if (!model.IsSearchActive) searchDetails.Collapse();
            searchDetails.Button.Visibility = model.IsSearchActive ? UiVisibility.Visible : UiVisibility.Collapsed;
            receive.IsEnabled = model.CanReceive;
            receive.ToolTipText = model.IsSearchActive ? "Exit search to receive mail" : $"Receive mail ({MailShortcuts.For(MailCommand.Receive).Gesture})";
            older.IsEnabled = model.CanLoadOlder;
            download.IsEnabled = model.CanDownloadLoaded;
            read.IsEnabled = !model.IsBusy && model.SelectedMessage is not null;
            cancel.IsEnabled = mailboxDialogCancel.IsEnabled = model.IsBusy;
            mailboxDialogCancel.Visibility = model.IsBusy ? UiVisibility.Visible : UiVisibility.Collapsed;
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
            if (headersKey != item?.Key)
            {
                // A popup or the folder picker of the previous message closes; the new one shows its own commands.
                headersKey = item?.Key; extendedHeaders = false; moveBarOpen = false;
                moreActions.Collapse(); moveMenu.Collapse();
            }
            var sourceHeaders = model.Body is { } loaded && loaded.Key == item?.Key ? loaded.Composition : null;
            headerStack.MoveChild(backRow, layout.ShowsReaderOnly ? 0 : 3);
            back.Visibility = layout.ShowsReaderOnly ? UiVisibility.Visible : UiVisibility.Collapsed;
            backRow.Visibility = back.Visibility;
            // The parsed headers replace the sender and recipients inline, in the shell as without it.
            bool showParsed = extendedHeaders && sourceHeaders is not null;
            details.AccessibleName = showParsed ? "Parsed message headers" : "Sender and recipients";
            string detailText = item is null ? "" : string.Join("\n", ParsedMessageHeaders.Lines(item, sourceHeaders, showParsed));
            if (detailText != details.GetPlainText()) details.SetPlainText(detailText);
            detailsFrame.Visibility = item is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            // The date with its time and the separator that ends it is one phrase, and the read state another
            // (non-breaking spaces), so a line, and a header that ends between lines, may end after "Received"
            // or after the separator, but never inside the date ("10:00" above "AM") or on "Unread on", and no
            // line starts with the separator, where it would read as a bullet.
            meta.Text = item is null ? "" : $"Received {dateFormat.Detail(item.ReceivedAt).Replace(' ', '\u00A0')}\u00A0· {(item.IsRead ? "Read" : "Unread")}\u00A0on\u00A0server" +
                (model.IsAccountSearchActive ? $" · Folder {item.Key.MailboxId} · Account {model.CurrentAccountProfile?.EmailAddress}" : "") +
                (item.IsStarred ? " · Starred" : "") + AttachmentMeta(item, model.Body) +
                (item.IsBodyCached ? " · Available offline" : model.IsShowingCachedMail ? " · Body not downloaded" : "");
            // An empty label still takes a line, so without a message the heading is the header's only row.
            meta.Visibility = item is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            if (!ReferenceEquals(shownMoveDestinations, model.MoveDestinations))
            {
                shownMoveDestinations = model.MoveDestinations;
                moveDestination.SetItems(shownMoveDestinations.Select(folder => new UiComboBoxItem(folder.MailboxId, folder.MailboxId)));
            }
            moveDestination.SelectedIndex = model.MoveDestinations.ToList().FindIndex(folder => folder.MailboxId == model.MoveDestinationMailboxId);
            moveDestination.IsEnabled = model.CanMoveSelected && model.MoveDestinations.Count > 0;
            moveBar.Visibility = moveBarOpen && model.SupportsMoves && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            moveFeedback.Set(model.MoveNotice ?? "", model.IsMoving ? FeedbackKind.Progress : FeedbackKind.Information);
            // In the verified Trash, Delete permanently… takes Trash's place.
            bool inTrash = model.SupportsPermanentDelete && model.IsPermanentDeleteFolder;
            requestDelete.Visibility = inTrash && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            archiveMessage.Visibility = model.SupportsMoves && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            trashMessage.Visibility = model.SupportsMoves && !inTrash && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            moveMenu.Visibility = archiveMessage.Visibility;
            moveMenu.Trigger.IsEnabled = model.CanMoveSelected && model.MoveDestinations.Count > 0;
            requestDelete.IsEnabled = model.CanRequestPermanentDelete;
            deleteConfirmation.Visibility = model.PendingPermanentDelete is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            deleteConfirmationText.Text = model.PermanentDeleteConfirmationText; confirmDelete.IsEnabled = model.CanConfirmPermanentDelete;
            deleteFeedback.Set(model.DeleteNotice ?? "", model.IsDeleting ? FeedbackKind.Progress : FeedbackKind.Information);
            retryDeleteCache.Visibility = model.CanRetryDeleteCache ? UiVisibility.Visible : UiVisibility.Collapsed;
            retryDeleteCache.IsEnabled = model.CanRetryDeleteCache;
            moveMessage.IsEnabled = model.CanMoveToDestination; archiveMessage.IsEnabled = model.CanArchive; trashMessage.IsEnabled = model.CanTrash;
            retryMoveCache.Visibility = model.CanRetryMoveCache ? UiVisibility.Visible : UiVisibility.Collapsed;
            retryMoveCache.IsEnabled = model.CanRetryMoveCache;
            star.Visibility = model.SupportsStarStateChanges && item is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            star.Text = star.AccessibleName = star.ToolTipText = item?.IsStarred == true ? "Unstar message" : "Star message";
            star.IsEnabled = model.CanChangeStarState;
            star.Invalidate(UiInvalidationKind.Render);
            readStateFeedback.Set(model.FlagChangeNotice ?? "", model.IsChangingMessageFlags ? FeedbackKind.Progress : FeedbackKind.Information);
            replyActions.Visibility = item is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            reply.Visibility = replyAll.Visibility = forward.Visibility = commands is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            moreActions.Trigger.IsEnabled = item is not null && (model.Body is not null || sourceHeaders is not null || model.SupportsReadStateChanges);
            if (!moreActions.Trigger.IsEnabled) moreActions.Collapse();
            if (!moveMenu.Trigger.IsEnabled || moveMenu.Visibility != UiVisibility.Visible) moveMenu.Collapse();
            UpdateGroups();
            reply.IsEnabled = replyAll.IsEnabled = forward.IsEnabled = commands?.CanRespond == true;
            ShowStates();
            var body = model.Body;
            // A new body starts as its preview; reset before the reader string, so an expansion never carries over.
            if (!ReferenceEquals(shownBody, body)) fullText = false;
            // While a page loads, the list notice shows its progress and Receive mail is unavailable, so the
            // reader of an empty list does not ask for it.
            string reader = body is null ? (model.SelectedMessage is null
                ? (model.Messages.Count > 0 ? "Choose a message from the inbox list." : model.IsLoadingList ? ""
                    : empty ? "Use Receive mail to check for new messages." : "Receive mail to load your inbox.")
                : (model.IsLoadingMessage ? "Loading message body…" : ""))
                : ReaderBodyText.Display(body, fullText);
            // Rewriting unchanged text would reset the reader's selection during unrelated updates.
            if (reader != shownText) text.Text = shownText = reader;
            expandRow.Visibility = body is not null && !fullText && ReaderBodyText.CanExpand(body) ? UiVisibility.Visible : UiVisibility.Collapsed;
            if (body is not null && expandRow.Visibility == UiVisibility.Visible) expandText.Text = ReaderBodyText.ExpandLabel(body);
            // With no text to make room for, such as a body that could not be loaded, the header may
            // take the pane, so a short reader shows the problem and Retry instead of empty space.
            headerColumn.MaximumFraction = reader.Length == 0 ? 1 : headerShare;
            if (!ReferenceEquals(shownBody, body))
            {
                moreActions.Collapse();
                bool restoreSourceFocus = panel.Session?.FocusedElement is { } sourceFocus && (sourceFocus.IsDescendantOf(sourceArea) || sourceFocus.IsDescendantOf(previewActions));
                sourceExportActions = null;
                foreach (var child in sourceArea.Children.ToArray()) { sourceArea.RemoveChild(child); child.Dispose(); }
                sourceArea.Visibility = UiVisibility.Collapsed;
                if (findChangedMode) { preferHtml = settings?.Settings.PreferPlainText != true; findChangedMode = false; }
                // Asked before the old find bar is disposed: focus in its query moves to the new message's
                // reader, never to a disposed control.
                bool restoreFindFocus = panel.Session?.FocusedElement is { } findFocus && (findFocus.IsDescendantOf(findArea) || findFocus.IsDescendantOf(imageArea) || findFocus.IsDescendantOf(attachmentArea));
                // A new body never inherits an attachment save: disposing the strip cancels it and drops the original.
                attachmentStrip = null;
                foreach (var child in attachmentArea.Children.ToArray()) { attachmentArea.RemoveChild(child); child.Dispose(); }
                attachmentArea.Visibility = UiVisibility.Collapsed;
                messageFind = null; showTextForFind = null;
                foreach (var child in findArea.Children.ToArray()) { findArea.RemoveChild(child); child.Dispose(); }
                findArea.Visibility = UiVisibility.Collapsed;
                // A new body never inherits remote-image consent: disposing the banner cancels its fetch.
                currentImageActions = null;
                foreach (var child in imageArea.Children.ToArray()) { imageArea.RemoveChild(child); child.Dispose(); }
                imageArea.Visibility = UiVisibility.Collapsed;
                UpdateBanners();
                text.ScrollToStart();
                bool restoreReaderFocus = restoreSourceFocus || restoreFindFocus || panel.Session?.FocusedElement is { } readerFocus &&
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
                        {
                            // The banner is inline, so focus leaving it goes to the reader, never to a control elsewhere.
                            currentImageActions = imageActions = new RemoteImageActions(remote, FocusReader);
                            var banner = imageActions;
                            void ShowBanner()
                            {
                                if (panel.IsDisposed || !ReferenceEquals(currentImageActions, banner)) return;
                                imageArea.Visibility = banner.Visibility;
                                UpdateBanners();
                            }
                            imageActions.AvailabilityChanged += (_, _) => ShowBanner();
                            imageArea.AddChild(imageActions);
                            ShowBanner();
                        }
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
                            if (modeRow.Session is { FocusedElement: { } focused } modeSession &&
                                ((focused == zoomIn && !zoomIn.IsEnabled) || (focused == zoomOut && !zoomOut.IsEnabled) ||
                                 (focused == zoomReset && !zoomReset.IsEnabled)))
                                modeSession.SetFocus(preferHtml ? zoomReset : toggle);
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
                        toggle.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
                        {
                            if (panel.IsDisposed || !ReferenceEquals(surface, inlineContent) || !ReferenceEquals(model.Body, body)) return;
                            double position = preferHtml && surface is IReaderViewport viewport ? viewport.ReadingPosition : text.ReadingPosition;
                            findChangedMode = false; preferHtml = !preferHtml; ApplyMode();
                            if (preferHtml && surface is IReaderViewport next) next.RestoreReadingPosition(position);
                            else if (!preferHtml) text.RestoreReadingPosition(position);
                            if (settings is not null) await settings.RememberReaderModeAsync(!preferHtml);
                        });
                        previewActions.AddChild(modeRow);
                        previewActions.AddChild(renderingNotice);
                        ApplyMode();
                    }
                    catch (Exception)
                    {
                        if (inlineContent is not null) { bodyArea.RemoveChild(inlineContent); inlineContent.Dispose(); inlineContent = null; }
                        currentImageActions = null;
                        foreach (var child in imageArea.Children.ToArray()) { imageArea.RemoveChild(child); child.Dispose(); }
                        imageArea.Visibility = UiVisibility.Collapsed;
                        UpdateBanners();
                        text.Visibility = UiVisibility.Visible;
                        previewActions.AddChild(new StandardLabel { Text = "HTML unavailable. The text reader remains available.", UseMnemonic = false });
                    }
                }
                else if (body?.HtmlUnavailableReason is { } unavailable)
                    previewActions.AddChild(new StandardLabel { Text = unavailable, UseMnemonic = false, Wrapping = UiTextWrapping.Wrap });
                if (body is not null)
                {
                    int offset = ReaderBodyText.FindOffset(body);
                    var currentBody = body;
                    string findText = ReaderBodyText.FindText(body, false);
                    // Find docks in the reader's banner slot above the text, never in a dialog, and the compact list
                    // opens the reader.
                    messageFind = new MessageTextFindBar(text, findText, offset, () =>
                    {
                        if (layout.IsCompact && !layout.ShowsReaderOnly) OpenReader();
                        showTextForFind?.Invoke(); findArea.Visibility = UiVisibility.Visible;
                        UpdateBanners();
                    }, () => !panel.IsDisposed && ReferenceEquals(model.Body, currentBody), () =>
                    {
                        findArea.Visibility = UiVisibility.Collapsed;
                        UpdateBanners();
                        FocusReader();
                    })
                    // Escape cancels running work, an attachment save included, before it closes the bar, wherever focus is.
                    { IsWorkRunning = () => model.IsBusy || attachmentStrip?.IsBusy == true };
                    if (ReaderBodyText.CanExpand(body))
                        messageFind.MoreTextHint = "Use " + ReaderBodyText.ExpandLabel(body) + " below the message to search the rest.";
                    findArea.AddChild(messageFind);
                    if (model.SupportsSourceExport && model.CurrentAccountProfile is { } sourceAccount)
                    {
                        var key = body.Key;
                        sourceExportActions = new MessageSourceExportActions(sourceAccount, key, model.SourceReceiver!, model.SourceExports!,
                            () => !panel.IsDisposed && model.CurrentAccountProfile == sourceAccount && model.SelectedMessage?.Key == key,
                            () => model.CanDownloadOriginalSource,
                            Files ?? Shell?.Files ?? UnavailableMailFilePicker.NotOnThisDeviceYet,
                            model.SelectedMessage is { } named && named.Key == key ? EmlFileName.Suggest(named.Subject, named.ReceivedAt) : EmlFileName.Suggest(body.Composition?.Subject, null));
                        sourceArea.AddChild(sourceExportActions); sourceArea.Visibility = UiVisibility.Visible;
                    }
                    if (body.Attachments is { Count: > 0 })
                    {
                        var attachmentKey = body.Key; var attachmentAccount = model.CurrentAccountProfile;
                        var strip = new MessageAttachmentStrip(attachmentAccount?.Id == attachmentKey.AccountId ? attachmentAccount : null, body, model.AttachmentReader,
                            Files ?? Shell?.Files ?? UnavailableMailFilePicker.NotOnThisDeviceYet,
                            () => !panel.IsDisposed && model.CurrentAccountProfile == attachmentAccount && model.SelectedMessage?.Key == attachmentKey && ReferenceEquals(model.Body, currentBody),
                            FocusReader, sourceExportActions is null ? null : () => sourceExportActions?.Reveal());
                        attachmentStrip = strip;
                        strip.AvailabilityChanged += (_, _) => { if (!panel.IsDisposed && ReferenceEquals(attachmentStrip, strip)) UpdateBanners(); };
                        attachmentArea.AddChild(strip); attachmentArea.Visibility = UiVisibility.Visible;
                        UpdateBanners();
                    }
                }
                previewActions.Visibility = previewActions.Children.Count > 0 ? UiVisibility.Visible : UiVisibility.Collapsed;
                if (restoreReaderFocus) FocusReader();
            }
            shownBody = body;
            sourceExportActions?.RefreshAvailability();
            attachmentStrip?.RefreshContext();
            updating = false;
        }

        expandText.Clicked += (_, _) =>
        {
            // Only for the body the button was shown for, and once: Receive keeps the same body and its expansion.
            if (panel.IsDisposed || fullText || model.Body is not { } expanded || !ReferenceEquals(shownBody, expanded) || !ReaderBodyText.CanExpand(expanded)) return;
            int resumeAt = ReaderBodyText.FindOffset(expanded) + ReaderBodyText.FindText(expanded, false).Length;
            fullText = true;
            text.Text = shownText = ReaderBodyText.Display(expanded, true);
            expandRow.Visibility = UiVisibility.Collapsed;
            // The button goes away, so focus moves into the text, which continues where the preview ended.
            panel.Session?.SetFocus(text.Editor);
            text.RevealOffset(resumeAt);
            if (messageFind is { } bar) { bar.MoreTextHint = null; bar.ReplaceText(ReaderBodyText.FindText(expanded, true)); }
            Update();
        };
        folders.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            int index = folders.SelectedIndex;
            if (index >= 0 && index < model.Folders.Count) model.SelectFolder(model.Folders[index].MailboxId);
            Update();
        };
        refreshFolders.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RefreshFoldersAsync());
        receive.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ReceiveAsync());
        download.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.DownloadLoadedBodiesAsync());
        offlineShowHeaders.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ShowSavedHeadersAsync());
        offlineHeaders.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ReceiveOfflineHeadersAsync());
        offlineHeadersRestart.Clicked += (_, _) => model.RestartOfflineHeaders();
        offlineToggle.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ToggleOfflineFolderAsync());
        offlineDownload.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.DownloadOfflineFoldersAsync());
        offlineRestart.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RestartOfflineFolderBatchesAsync());
        startFolder.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.SetStartFolderAsync());
        pinFolder.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.TogglePinnedFolderAsync());
        reloadNavigation.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ReloadAccountNavigationAsync());
        resetNavigation.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ResetAccountNavigationAsync());
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
        offlineReload.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ReloadOfflineFolderSelectionAsync());
        older.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.LoadOlderAsync());
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
                : model.Messages.Count == 0 ? (model.HasLoaded ? (model.SelectedMailboxId == "INBOX" ? "The inbox is empty." : "The selected folder is empty.") : model.CanReceive ? (model.SelectedMailboxId == "INBOX" ? "Receive mail to load your inbox." : "Receive mail to load the selected folder.") : "", FeedbackKind.Information)
                : ("", FeedbackKind.Information);
            // The notice announces progress and problems itself, then disappears once rows arrive;
            // say that receiving finished, or a screen reader user hears nothing after the progress.
            // It comes before the notice, so the count is heard before why Load older is unavailable.
            if (listWasLoading && !model.IsLoadingList && !listProblem && model.Messages.Count > 0)
                panel.Session?.AnnounceStatus(list, model.Messages.Count == 1 ? "1 message loaded." : $"{model.Messages.Count} messages loaded.");
            listWasLoading = model.IsLoadingList;
            listFeedback.Set(string.Join("\n", new[]
            {
                model.RecoveryNotice, searchOpen ? model.SearchDateError : null,
                model.IsShowingCachedMail && !model.IsLoadingList && !listProblem ? "Showing saved mail." : null,
                notice.text, model.MoveNotice, model.DeleteNotice,
            }.Where(value => !string.IsNullOrEmpty(value))), notice.kind);
            mailboxInfo.Set(string.Join("\n", new[]
            {
                Shell is not null ? model.ListProblem?.Text ?? (model.IsBusy ? model.Status : null) : null,
                model.CacheNotice, model.SupportsOfflineFolders ? model.OfflineFolderNotice : null,
                model.SupportsFolders ? model.FolderNotice : null,
                model.SupportsFolders ? model.FolderRoleNotice : null,
            }.Where(value => !string.IsNullOrEmpty(value))), Shell is not null && listProblem ? notice.kind : FeedbackKind.Information);
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
            if (Shell is not null)
            {
                var active = Shell.ActiveDialog?.Session;
                var dialogCancel = Shell.ActiveViewId switch
                {
                    "mailbox-options" => mailboxDialogCancel,
                    _ => null,
                };
                if (active is not null && dialogCancel is not null)
                    FocusNavigation.KeepFocusUsable(active, Shell.ActiveContent, model.IsBusy ? dialogCancel : Shell.NavigationFocus);
            }
        }
        void KeepFocusUsable()
        {
            // A Retry that disappears or a Cancel that is disabled must not keep keyboard focus.
            if (panel.Session is not { } session || session.FocusedElement is not { } focused) return;
            if ((focused == search || focused == nextSearch || focused == previousSearch || focused == refreshSearch || focused == exitSearch || focused == clearQuery) && model.IsBusy) session.SetFocus(cancel);
            // Refresh results goes once the newest page is shown; the list holds that page.
            else if (focused == refreshSearch && refreshSearch.Visibility != UiVisibility.Visible) session.SetFocus(list);
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
            else if ((focused == moveMessage || focused == archiveMessage || focused == trashMessage || focused == moveMenu.Trigger || focused == moveDestination) && model.IsMoving) session.SetFocus(cancel);
            else if (focused == star && model.IsChangingMessageFlags) session.SetFocus(cancel);
            else if (focused == offlineHeaders && model.IsReceivingOfflineHeaders) session.SetFocus(cancel);
            else if ((focused == download || focused == offlineDownload) && model.IsDownloadingBodies) session.SetFocus(cancel);
            // Any other command that stays disabled once its work is done hands focus on; Load older
            // on the last page goes to the list it extended or, while the reader is shown alone, to
            // Back to inbox, which leads there.
            else if (!model.IsBusy) FocusNavigation.KeepFocusUsable(session, panel, focused != older ? null : layout.ShowsReaderOnly ? back : list);
        }
        void OpenSearch()
        {
            // Closing the header returns focus where it was before, or to the list when that was the compact reader,
            // which goes back to the list first, as Back to inbox does.
            var focused = panel.Session?.FocusedElement;
            if (focused is not null && focused != searchHeader && !focused.IsDescendantOf(searchHeader)) searchReturnFocus = focused;
            if (layout.ShowsReaderOnly) { DismissFind(); readerOpen = false; layout.Refresh(); searchReturnFocus = list; }
            searchOpen = true; Update();
            if (searchText.Session is { } session) FocusNavigation.FocusAndReveal(session, searchText);
        }
        async Task CloseSearch()
        {
            var target = searchReturnFocus is { CanFocus: true, IsDisposed: false } returnTo && returnTo != searchHeader && !returnTo.IsDescendantOf(searchHeader)
                ? returnTo : showSearch.CanFocus ? showSearch : list;
            searchReturnFocus = null;
            searchOpen = false; filters.Collapse(); searchDetails.Collapse();
            // Pages from this search are not offered again by the next one.
            previousSearch.Visibility = UiVisibility.Collapsed;
            // Focus leaves the header before it collapses. Collapsing it changes no row or selection; only ending
            // the search reloads the folder, during which the toolbar's Search is disabled, so focus returns there
            // once it is done.
            if (panel.Session is { } before && target.CanFocus) before.SetFocus(target);
            var clearing = model.IsSearchActive ? model.ClearSearchAsync() : Task.CompletedTask;
            Update();
            await clearing;
            if (panel.IsDisposed || panel.Session is not { } session) return;
            if (target.CanFocus && !target.IsDisposed) session.SetFocus(target);
            else if (session.FocusedElement is not { CanFocus: true }) session.SetFocus(list);
        }
        _clearSearch = () =>
        {
            if (panel.IsDisposed || searchHeader.Visibility != UiVisibility.Visible || layout.ShowsReaderOnly || model.IsBusy) return false;
            if (searchText.Text.Length > 0) { model.SetSearchText(""); return true; }
            RunAsync(exitSearch, CloseSearch);
            return true;
        };
        exitSearch.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await CloseSearch());
        clearQuery.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            if (searchText.Text.Length > 0)
            {
                model.SetSearchText("");
                if (panel.Session is { } session && searchText.CanFocus) session.SetFocus(searchText);
                return;
            }
            await CloseSearch();
        });
        showSearch.Clicked += (_, _) => OpenSearch();
        _search = () =>
        {
            if (panel.IsDisposed || !model.SupportsLocalSearch || !showSearch.IsEnabled) return false;
            OpenSearch(); return true;
        };
        _find = () =>
        {
            if (panel.IsDisposed || model.Body is null || messageFind is null) return false;
            messageFind.ShowFind(); return messageFind.Visibility == UiVisibility.Visible;
        };
        _closeFind = () =>
        {
            // A bar in a reader that compact mode hides is not what Escape or Back is for, and closing it must not
            // send focus into that hidden reader.
            if (panel.IsDisposed || messageFind is not { Visibility: UiVisibility.Visible } bar || (layout.IsCompact && !layout.ShowsReaderOnly)) return false;
            bar.CloseFind(); return bar.Visibility == UiVisibility.Collapsed;
        };
        _cancelAttachmentSave = () =>
        {
            if (panel.IsDisposed || attachmentStrip is not { IsBusy: true } saving) return false;
            saving.Cancel(); return true;
        };
        // Leaving the compact reader closes its find bar where it is, without moving focus into the reader.
        void DismissFind()
        {
            if (messageFind is not { Visibility: UiVisibility.Visible } bar) return;
            bar.Visibility = UiVisibility.Collapsed;
            findArea.Visibility = UiVisibility.Collapsed;
            UpdateBanners();
        }
        _showsMessage = () => !panel.IsDisposed && model.SelectedMessage is not null && (!layout.IsCompact || layout.ShowsReaderOnly);
        // The popups' items are built as they open, for the message open then; a command chosen after another
        // message was selected, or the body replaced, does nothing.
        void RunAsync(UiElement sender, Func<Task> work) => UiAsync.Handler<EventArgs>(async (_, _) => await work())(sender, EventArgs.Empty);
        MailMessageKey? moveKey = null;
        const int quickMoveTargets = 10;
        moveItems = () =>
        {
            moveKey = model.SelectedMessage?.Key;
            var destinations = model.MoveDestinations;
            // A server may list hundreds of folders, more than a popup can show: it offers the folders with a role,
            // then the pinned ones, then the rest while there are few, and Choose folder… for any of them.
            var quick = new List<(string Id, string Text)>();
            void Offer(string id, string label)
            {
                if (quick.Count < quickMoveTargets && destinations.Any(folder => folder.MailboxId == id) && !quick.Any(target => target.Id == id))
                    quick.Add((id, label));
            }
            foreach (var role in model.FolderRoles.Where(role => role.IsConfirmed && role.MailboxId is not null)) Offer(role.MailboxId!, $"{role.MailboxId} — {role.Role}");
            foreach (var pinned in model.PinnedFolders) Offer(pinned, pinned);
            if (destinations.Count <= quickMoveTargets) foreach (var folder in destinations) Offer(folder.MailboxId, folder.MailboxId);
            var items = quick.Select(target => new UiMenuItem("to:" + target.Id, target.Text) { IsEnabled = model.CanMoveSelected }).ToList();
            items.Add(MailMenuButton.Separator("choose-separator"));
            items.Add(new UiMenuItem("choose", "Choose folder…"));
            return MailMenuButton.Tidy(items).ToArray();
        };
        moveMenu.Invoked += id =>
        {
            if (panel.IsDisposed || model.SelectedMessage is null || model.SelectedMessage.Key != moveKey) return;
            if (id == "choose")
            {
                moveBarOpen = true;
                Update();
                if (panel.Session is { } session && moveDestination.CanFocus) FocusNavigation.FocusAndReveal(session, moveDestination);
            }
            else if (id.StartsWith("to:", StringComparison.Ordinal))
            {
                string destination = id["to:".Length..];
                RunAsync(moveMenu, () => model.MoveSelectedAsync(destination));
            }
        };
        closeMove.Clicked += (_, _) =>
        {
            moveBarOpen = false;
            Update();
            if (panel.Session is { } session && moveMenu.Trigger.CanFocus) session.SetFocus(moveMenu.Trigger);
        };
        MailMessageKey? moreKey = null; MailMessageBody? moreBody = null;
        // The read state Mark read or Mark unread promised when the popup was built: choosing it sets that state, and
        // does nothing when a Receive already brought the message back in it while the popup was open.
        bool? moreReadTarget = null;
        moreItems = () =>
        {
            var item = model.SelectedMessage;
            moreKey = item?.Key; moreBody = model.Body; moreReadTarget = item is null ? null : !item.IsRead;
            if (item is null) return [];
            var headers = model.Body is { } loaded && loaded.Key == item.Key ? loaded.Composition : null;
            bool hasBody = model.Body is not null && ReferenceEquals(shownBody, model.Body);
            bool canMoveReader = hasBody && ActiveViewport() is not null;
            var items = new List<UiMenuItem>();
            if (model.SupportsReadStateChanges)
                items.Add(new UiMenuItem("read", item.IsRead ? "Mark unread" : "Mark read") { IsEnabled = model.CanChangeReadState });
            items.Add(MailMenuButton.Separator("view-separator"));
            items.Add(new UiMenuItem("find", "Find in message") { Accelerator = MailShortcuts.For(MailCommand.FindInMessage).Gesture, IsEnabled = hasBody && messageFind is not null });
            items.Add(new UiMenuItem("headers", "Show parsed headers") { IsCheckable = true, IsChecked = extendedHeaders && headers is not null, IsEnabled = headers is not null });
            items.Add(MailMenuButton.Separator("position-separator"));
            items.Add(new UiMenuItem("top", "Top of message") { IsEnabled = canMoveReader });
            items.Add(new UiMenuItem("end", "End of message") { IsEnabled = canMoveReader });
            if (currentImageActions?.CanBlockAgain == true) items.Add(new UiMenuItem("block-images", "Block remote images again"));
            // Only where the attachments can be saved here; otherwise the strip lists them with its buttons disabled.
            if (hasBody && attachmentStrip is { CanSave: true } strip)
                items.Add(new UiMenuItem("attachments", strip.SavableCount == 1 ? "Save attachment…" : $"Attachments ({strip.Attachments.Count})…"));
            items.Add(MailMenuButton.Separator("source-separator"));
            if (sourceExportActions is not null)
                items.Add(new UiMenuItem("save-eml", MessageSourceExportActions.SaveAsText)
                { Accelerator = MailShortcuts.For(MailCommand.SaveMessageAsEml).Gesture, IsEnabled = hasBody && sourceExportActions.CanSaveAs });
            if (sourceExportActions is not null) items.Add(new UiMenuItem("source", "Original message (.eml)…"));
            return MailMenuButton.Tidy(items).ToArray();
        };
        moreActions.Invoked += id =>
        {
            if (panel.IsDisposed || model.SelectedMessage is not { } item || item.Key != moreKey) return;
            bool sameBody = ReferenceEquals(model.Body, moreBody) && ReferenceEquals(shownBody, moreBody);
            switch (id)
            {
                case "read":
                    if (moreReadTarget is { } read && item.IsRead != read) RunAsync(moreActions, () => model.SetSelectedReadStateAsync(read));
                    break;
                case "find": if (sameBody) _find?.Invoke(); break;
                case "headers": extendedHeaders = !extendedHeaders; Update(); break;
                case "top" or "end":
                    if (sameBody) { MoveReader(id == "top" ? 0 : 1); FocusReader(); }
                    break;
                case "block-images": if (sameBody) currentImageActions?.BlockAgain(); break;
                case "attachments":
                    if (sameBody && attachmentStrip is { } strip)
                    {
                        if (layout.IsCompact && !layout.ShowsReaderOnly) OpenReader();
                        strip.FocusFirst();
                    }
                    break;
                case "save-eml": if (sameBody) _ = SaveEml(); break;
                case "source": if (sameBody) sourceExportActions?.Reveal(); break;
            }
        };
        // Save as .eml…, from the ⋯ menu, the File menu or Ctrl+Shift+S: the reader's section saves, in view.
        bool CanSaveEml() => !panel.IsDisposed && model.Body is { } open && ReferenceEquals(shownBody, open) && model.SelectedMessage?.Key == open.Key
            && sourceExportActions is { CanSaveAs: true };
        async Task<bool> SaveEml()
        {
            if (!CanSaveEml() || sourceExportActions is not { } actions) return false;
            if (layout.IsCompact && !layout.ShowsReaderOnly) OpenReader();
            return await actions.SaveAsAsync();
        }
        _canSaveEml = CanSaveEml;
        _saveEml = SaveEml;
        listRetry.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RetryAsync(InboxProblemScope.List));
        messageRetry.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RetryAsync(InboxProblemScope.Message));
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
            // A separator between groups takes its room too, except before the first button.
            double breaks = buttons.Skip(1).Sum(button => replyActions.GetBreakBefore(button) switch
            {
                UiToolbarBreak.Separator => replyActions.SeparatorExtent,
                UiToolbarBreak.Gap => replyActions.GroupExtent,
                _ => 0,
            });
            double row = buttons.Sum(button => button.DesiredSize.Width) + ((buttons.Length - 1) * replyActions.Spacing) + breaks + padding;
            double width = Math.Min(space.Width - (2 * ReadingColumn.MarginFor(space.Width)), ReadingColumn.DefaultMaximumWidth);
            double commands = Math.Max(buttons.Max(button => button.DesiredSize.Height) + padding, replyActions.PreferredSize.Height) + (2 * commandsInset) + divider.Thickness;
            double firstLine = headerContent.VerticalMargin + BTextMeasurer.GetLineHeight(subjectLine.Font);
            // An open banner, such as Find, takes its height below the line too: what it would take below pinned
            // commands, so the answer does not depend on where the commands are now.
            double banners = bannerSlot.Visibility == UiVisibility.Collapsed ? 0 : bannerSlot.HeightIn(Math.Max(0, space.Height - commands));
            double rest = space.Height - commands - banners, kept = text.HeightOfLines(textLinesKept);
            if (row > width || rest < firstLine + kept) return false;
            if (banners == 0) return true;
            // An open banner takes the header's budget, so below it the header, without the commands, must still leave
            // the text its lines: whole, or at its share.
            double header = headerColumn.NaturalHeight -
                (replyActions.Parent == pinnedCommands ? 0 : replyActions.DesiredSize.Height + headerStack.Spacing);
            return rest - header >= kept || (1 - headerShare) * rest >= kept;
        }
        bool MoveCommands(bool pin)
        {
            UiElement place = pin ? pinnedCommands : headerStack;
            if (replyActions.Parent == place) return false;
            var focused = panel.Session?.FocusedElement;
            // An open popup belongs to where the strip was; it closes before the strip moves.
            moreActions.Collapse(); moveMenu.Collapse();
            focused = panel.Session?.FocusedElement;
            bool keepFocus = focused is not null && (focused == replyActions || focused.IsDescendantOf(replyActions));
            replyActions.Parent?.RemoveChild(replyActions);
            if (pin) pinnedCommands.AddChild(replyActions);
            else headerStack.InsertChild(headerStack.Children.ToList().IndexOf(previewActions), replyActions);
            if (keepFocus && focused!.CanFocus) panel.Session!.SetFocus(focused);
            return true;
        }
        layout.ModeChanged += (_, _) =>
        {
            // The compact reader keeps them in its header; beside the list, the header area places them as it is measured.
            if (layout.IsCompact) MoveCommands(pin: false);
            back.Visibility = layout.ShowsReaderOnly ? UiVisibility.Visible : UiVisibility.Collapsed;
            backRow.Visibility = back.Visibility;
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
            DismissFind();
            readerOpen = false;
            layout.Refresh();
            // The collapsed list kept its scroll offset, so the selected row is where the user left it.
            panel.Session?.SetFocus(list);
            return true;
        };
        back.Clicked += (_, _) => _back();
        layout.ListClicked += (_, _) => OpenReader();
        read.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            if (model.SelectedMessage is not { } item) return;
            var loading = model.SelectAsync(item.Key);
            OpenReader();
            try { await loading; }
            catch (OperationCanceledException) { }
        });
        cancel.Clicked += (_, _) => model.Cancel();
        list.SelectionChanged += UiAsync.Handler<UiListSelectionChangedEventArgs>(async (_, _) =>
        {
            if (!updating && !model.CanSelect) { Update(); return; }
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == list.SelectedItemId) is { } item)
            {
                try { await model.SelectAsync(item.Key); }
                catch (OperationCanceledException) { }
            }
        });
        list.ItemActivated += UiAsync.Handler<UiListItemEventArgs>(async (_, e) =>
        {
            if (!updating && model.Messages.FirstOrDefault(item => Id(item) == e.Item.Id) is { } item)
            {
                var loading = model.SelectAsync(item.Key);
                OpenReader();
                try { await loading; }
                catch (OperationCanceledException) { }
            }
        });
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

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=BF0110
    // Hairs-Falsified-If: two loaded messages with different keys get the same Id, so choosing one row opens the other
    // Hairs-Human:        PENDING
    private static string Id(MailMessageSummary message) => $"{message.Key.AccountId}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message.Key.MailboxId))}:{message.Key.UidValidity}:{message.Key.Uid}";

    /// <summary>
    /// The reader meta line's attachment part: the count from the loaded body's list ("+" when the list
    /// stopped early), or only that the message has attachments when its row is marked but no list is loaded.
    /// </summary>
    internal static string AttachmentMeta(MailMessageSummary item, MailMessageBody? body)
    {
        if (body is { Attachments: { Count: > 0 } attachments } && body.Key == item.Key)
            return $" · {attachments.Count}{(body.IsAttachmentListIncomplete ? "+" : "")} attachment{(attachments.Count == 1 && !body.IsAttachmentListIncomplete ? "" : "s")}";
        return item.HasAttachments ? " · Has attachments" : "";
    }
}
