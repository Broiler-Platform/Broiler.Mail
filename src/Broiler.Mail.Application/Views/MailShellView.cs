// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   28
// Annotated:        5/28
// Exempt:           30
// Human-reviewed:   0/28
// IP risk:          Low
// Security risk:    Medium
// Criteria:         4/0
// Resource impact:  7/10 max
// Unverified:       28
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Services;
using Broiler.UI.Forms;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Button.Standard;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Window;
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI.Window.Standard;
using Broiler.UI.Standard;
using Broiler.UI;
using Broiler.Mail.Application.Preview;

namespace Broiler.Mail.Application.Views;

/// <summary>Single-account inbox, reading pane, and configuration workflow.</summary>
// Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=126B98
// Hairs-Falsified-If: PrepareCloseAsync completes true without waiting for Composer.PrepareCloseAsync, so the window may close before the draft reaches storage
// Hairs-Human:        PENDING
public sealed class MailShellView : IDisposable
{
    private readonly MailShellViewModel _model;
    private readonly Action<Uri>? _openUrl;
    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=70D8B8
    // Hairs-Falsified-If: with an unconfigured account the Account setup dialog is not opened
    // Hairs-Human:        PENDING
    public MailShellView(MailShellViewModel model, IInlineHtmlPreviewHost? htmlPreview = null, MessageDateFormatter? dates = null, Action<Uri>? openUrl = null, IMailFilePicker? files = null)
    {
        _model = model;
        _openUrl = openUrl;
        Window = new StandardWindow { Title = model.Title };
        // One picker for every view. The default is the desktop file dialog, presented over the open form or the window.
        Files = files ?? (_ownedFileDialogs = new MailFileDialogs(() => (UiWindow?)ActiveDialog ?? Window));
        Window.ApplyTheme(StandardControlPaint.Theme);
        var layout = _layout;
        // Literal: statuses carry addresses and server text, where '&' is a character, not an access key.
        var status = Footer = new StandardLabel { Text = model.Status, Wrapping = UiTextWrapping.Wrap, UseMnemonic = false };
        Menu = CreateMenu();
        layout.AddChild(Menu);
        layout.SetDock(Menu, UiDock.Top);
        if (model.Accounts is { } accounts)
        {
            accounts.CanChangeView = () => ActiveAboutDialog is null && ActiveFileDialog is null &&
                (ActiveDialog is null || ActiveViewId is "manage-accounts" or "account-activity" or "removed-account-mail");
            var selection = new AccountSelectionView(accounts, () => ShowView("account"), model.AccountReceives) { Shell = this }.CreateContent();
            AccountSelectionContent = selection;
            layout.AddChild(selection); layout.SetDock(selection, UiDock.Top);
        }
        // Keep the status clear of the window edges.
        var footer = _footer = new Inset(status, 12, FooterPadding);
        layout.AddChild(footer);
        layout.SetDock(footer, UiDock.Bottom);

        Inbox = new InboxView(model.Inbox, htmlPreview, dates, model.Compose, model.Settings) { Shell = this };
        var inboxContent = Inbox.CreateContent();
        _contents.Add("inbox", inboxContent);
        // Again after the inbox view, which updates its reader on the same event, so Save message as .eml… follows it.
        model.Inbox.Changed += (_, _) => _refreshCommands?.Invoke();
        _refreshCommands?.Invoke();
        var account = new AccountProfileView(model.Account);
        _contents.Add("account", account.CreateContent());
        // The setup checklist ends in the inbox, receiving for the first time.
        account.InboxRequested += (_, _) => { ShowView("inbox"); _ = model.Inbox.ReceiveAsync(); };
        _contents.Add("settings", new SettingsView(model.Settings, Files).CreateContent());
        var composer = Composer = new ComposerView(model.Composer, model.Inbox, model.Compose, Files);
        _contents.Add("compose", composer.CreateContent());
        Outbox = new OutboxView(model.Composer, () => { if (!_disposing && ActiveViewId == "outbox") ShowView("compose"); });
        _contents.Add("outbox", Outbox.CreateContent());
        SavedOriginals = new SavedOriginalsView(model.Inbox.SourceExports, htmlPreview as ILocalOriginalHtmlHost, Files); SavedOriginals.SetAccount(model.CurrentAccount);
        _contents.Add("originals", SavedOriginals);
        UnifiedInbox = new UnifiedSavedInboxView(model.Inbox.LocalCache);
        void UpdateUnifiedAccounts() => UnifiedInbox.SetAccounts(model.Accounts?.Profiles ?? (model.CurrentAccount is {} current ? [current] : []));
        UpdateUnifiedAccounts(); _contents.Add("unified", UnifiedInbox);
        model.Account.Changed += (_,_) => UpdateUnifiedAccounts();
        if (model.Accounts is {} collection) collection.Changed += (_,_) => UpdateUnifiedAccounts();
        model.Account.Changed += (_,_) => SavedOriginals.SetAccount(model.CurrentAccount);
        if (model.Account.Profile is null) ActiveViewId = "account";
        _refreshStatus = RefreshStatus;
        void RefreshStatus()
        {
            if (status.IsDisposed) return;
            string text = ActiveViewId switch
            {
                // A problem is explained below the form's buttons; the footer names it and points there.
                "account" => IsProblem(model.Account.StatusKind) ? $"{model.Account.StatusSummary} {DetailsBelow}"
                    : !string.IsNullOrEmpty(model.Account.Status) ? model.Account.Status
                    : model.Account.NextStep == AccountSetupStep.Ready ? "This account is ready to receive mail."
                    : "Save your account details, then save a password and test the connection.",
                "outbox" => model.Composer.OutboxNotice,
                "unified" => "Unified saved INBOX mail across configured accounts. Refresh explicitly; all reading stays offline.",
                "originals" => "Saved originals for the selected account. List and verify explicitly; no receiving or automatic opening.",
                "settings" => IsProblem(model.Settings.StatusKind) ? $"{model.Settings.StatusSummary} {DetailsBelow}"
                    : string.IsNullOrEmpty(model.Settings.Status) ? "Saved appearance and inbox spacing apply immediately. The window reopens at its last size and position." : model.Settings.Status,
                // Informational composer messages appear only here; the composer shows the others inline,
                // and the footer points to a problem there as the other forms do.
                "compose" => !string.IsNullOrEmpty(model.Composer.Status) && model.Composer.StatusKind == FeedbackKind.Information && !model.Composer.IsBusy
                    ? model.Composer.Status
                    : IsProblem(model.Composer.StorageKind) ? $"The draft is not saved. {DetailsBelow}"
                    : IsProblem(model.Composer.StatusKind) && !string.IsNullOrEmpty(model.Composer.Status) ? $"The draft has a problem. {DetailsBelow}"
                    : model.Composer.StorageStatus,
                _ => InboxStatus(model.Inbox, Inbox),
            };
            status.Text = text;
        }
        model.Compose.Requested += (_, e) =>
        {
            var session = Window.Session;
            var focused = session?.FocusedElement;
            ShowView("compose");
            if (focused is not null && focused.IsDescendantOf(inboxContent)) _readerFocus = focused;
            var target = e.Focus == CompositionFocus.Recipients ? composer.Recipients : composer.Body;
            var activeSession = ActiveDialog?.Session ?? session;
            if (activeSession is not null && target is not null) FocusNavigation.FocusAndReveal(activeSession, target);
        };
        model.Inbox.Changed += (_, _) => RefreshStatus();
        // Opening a message, Back, or a resize hides or shows a pane. A resize does so during layout,
        // after the footer was laid out, so the new text waits until after that frame and is laid out
        // in the next one.
        Inbox.PanesChanged += (_, _) =>
        {
            if (Window.Session is { } session) session.Dispatcher.Post(RefreshStatus);
            else RefreshStatus();
        };
        model.Account.Changed += (_, _) => RefreshStatus();
        model.Settings.Changed += (_, _) => RefreshStatus();
        model.Composer.Changed += (_, _) => RefreshStatus();
        RefreshStatus();
        layout.AddChild(_parked);
        foreach (var id in new[] { "account", "settings", "compose", "outbox", "originals" }) _parked.AddChild(_contents[id]);
        layout.AddChild(inboxContent);
        Window.AddChild(new MeasuredContent(layout, size =>
        {
            double line = BTextMeasurer.GetLineHeight(Menu.Font);
            double height = Math.Max(28, line + 8);
            if (Menu.MenuBarHeight != height)
            {
                Menu.MenuBarHeight = height;
                Menu.ItemHeight = Math.Max(26, line + 6);
                Menu.InvalidateMeasure();
            }
            if (ActiveDialog is { } dialog)
            {
                dialog.TitleFont = StandardControlPaint.GetTheme(Window).FontBody;
                dialog.TitleBarHeight = Math.Max(28, BTextMeasurer.GetLineHeight(dialog.TitleFont) + 8);
            }
            if (ActiveDialog is { IsBrokenOut: false } && size != _ownerSize) ActiveDialog.SetPlacement(DialogPlacement(size));
            if (ActiveAboutDialog is { IsBrokenOut: false } about && size != _ownerSize)
            {
                double aboutWidth = Math.Min(about.PreferredSize.Width, Math.Max(0, size.Width - 32));
                double aboutHeight = Math.Min(about.PreferredSize.Height, Math.Max(0, size.Height - 32));
                about.SetPlacement(new BRect(Math.Max(0, (size.Width - aboutWidth) / 2), Math.Max(0, (size.Height - aboutHeight) / 2), aboutWidth, aboutHeight));
            }
            if (size != _ownerSize) _ownedFileDialogs?.Reposition(size);
            _ownerSize = size;
        }));
    }

    private UiElement? _readerFocus;
    private readonly Dictionary<string, UiElement> _contents = [];
    private readonly Dictionary<string, (string Title, Func<bool> Available, Action? Closed, Action? Opened, UiMenuItem Item)> _inboxDialogs = [];

    internal void RegisterInboxDialog(string id, string title, UiElement content, Func<bool> available, Action? closed = null, Action? opened = null, bool accountTool = false)
    {
        var scroll = new StandardScrollView { Constraint = UiScrollConstraint.ConstrainWidth, FocusWhenScrollable = true, AccessibleName = title };
        scroll.AddChild(new Inset(content, 12, 8));
        _contents.Add(id, scroll);
        var item = new UiMenuItem(id, title + "…") { IsEnabled = available() };
        _inboxDialogs.Add(id, (title, available, closed, opened, item));
        _parked.AddChild(scroll);
        var group = accountTool ? _accountMenu : _mailboxMenu;
        group.Children.Add(item);
        group.IsEnabled = true;
    }
    private readonly Dictionary<string, (UiScrollView Scroll, BPoint Offset)[]> _scrollOffsets = [];
    private readonly StandardPanel _layout = new() { LayoutMode = UiPanelLayoutMode.Dock };
    // Keep forms alive and themed between presentations, including unsaved fields and scroll positions.
    private readonly UiMenuItem _accountMenu = new("account-tools", "Accounts") { IsEnabled = false };
    private readonly UiMenuItem _mailboxMenu = new("mailbox-tools", "Mailbox and message") { IsEnabled = false };
    private readonly StandardPanel _parked = new() { Visibility = UiVisibility.Collapsed };
    private readonly Inset _footer;
    private readonly Action _refreshStatus;
    private bool _disposing;
    private BSize _ownerSize;
    private StandardButton? _dialogClose;
    private readonly MailFileDialogs? _ownedFileDialogs;

    /// <summary>Attaches the shell and opens setup or a recovered draft when necessary.</summary>
    public void Attach(UiSession session)
    {
        session.AddRoot(Window);
        var initial = ActiveViewId;
        ActiveViewId = "inbox";
        ShowView(initial);
    }

    /// <summary>Returns to the inbox or presents one of its forms as a modal dialog.</summary>
    public bool ShowView(string id)
    {
        if (!_contents.ContainsKey(id)) return false;
        if (_inboxDialogs.TryGetValue(id, out var inboxDialog) && !inboxDialog.Available()) return false;
        // While a file dialog is open over a form, nothing replaces that form underneath it.
        if (ActiveFileDialog is not null) return ActiveViewId == id;
        if (Window.Session is not { } session) { ActiveViewId = id; SavedOriginals.SetActive(id == "originals"); UnifiedInbox.SetActive(id == "unified"); return true; }
        if (ActiveDialog is not null && ActiveViewId == id) return true;
        CloseDialog();
        if (id == "inbox") { ActiveViewId = id; _refreshStatus(); return true; }

        RememberReaderFocus();
        var content = GetContent(id);
        var offsets = _scrollOffsets.GetValueOrDefault(id) ?? [];
        _scrollOffsets.Remove(id);
        _parked.RemoveChild(content);
        var dialog = new StandardDialog
        {
            Title = inboxDialog.Title ?? id switch { "account" => "Account", "settings" => "Settings", "outbox" => "Outbox", "originals" => "Saved originals", "unified" => "Unified saved inbox", _ => "Compose message" },
            PreferredSize = new BSize(940, 680), Padding = 0,
            BreakOutMode = UiWindowBreakOutMode.Automatic,
        };
        var body = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var close = _dialogClose = new StandardButton { Text = "Close", AccessibleName = "Close dialog and return to inbox" };
        close.Clicked += (_, _) => CloseDialog();
        var actions = new StandardPanel { LayoutMode = UiPanelLayoutMode.Stack, StackOrientation = UiStackOrientation.Horizontal };
        actions.AddChild(close);
        var actionsInset = new Inset(actions, 12, 4);
        body.AddChild(actionsInset);
        body.SetDock(actionsInset, UiDock.Bottom);
        bool moveFooter = inboxDialog.Title is null;
        if (moveFooter)
        {
            _layout.RemoveChild(_footer);
            body.AddChild(_footer);
            body.SetDock(_footer, UiDock.Bottom);
        }
        body.AddChild(content);
        dialog.AddChild(new MeasuredContent(body, measureLimit: () =>
            dialog.IsBrokenOut || dialog.Placement.IsEmpty
                ? null
                : new BSize(dialog.Placement.Width, Math.Max(0, dialog.Placement.Height - dialog.TitleBarHeight))));
        ActiveViewId = id; SavedOriginals.SetActive(id == "originals"); UnifiedInbox.SetActive(id == "unified");
        ActiveDialog = dialog;
        _model.Accounts?.RefreshAvailability();
        // Detach persistent controls before UiWindow.Close disposes the dialog's children.
        dialog.Closing += (_, _) =>
        {
            if (id == "originals") SavedOriginals.SetActive(false);
            if (id == "unified") UnifiedInbox.SetActive(false);
            _scrollOffsets[id] = Descendants(content).OfType<UiScrollView>()
                .Where(scroll => !scroll.IsDisposed)
                .Select(scroll => (scroll, scroll.Offset))
                .ToArray();
            body.RemoveChild(content);
            _parked.AddChild(content);
            if (moveFooter)
            {
                body.RemoveChild(_footer);
                _layout.InsertChild(1, _footer);
                _layout.SetDock(_footer, UiDock.Bottom);
            }
        };
        dialog.Closed += (_, _) =>
        {
            ActiveDialog = null;
            _dialogClose = null;
            ActiveViewId = "inbox";
            inboxDialog.Closed?.Invoke();
            _model.Accounts?.RefreshAvailability();
            if (_disposing || session.IsDisposed) return;
            session.SetFocus(_readerFocus is { CanFocus: true } ? _readerFocus : Menu);
            _readerFocus = null;
            _refreshStatus();
        };
        _refreshStatus();
        _ = dialog.ShowModal(Window, DialogPlacement(_ownerSize == BSize.Empty ? session.Host.ViewportSize : _ownerSize));
        var activeSession = dialog.Session ?? session;
        StandardThemeController.ApplyToSubtree(dialog, StandardControlPaint.GetTheme(Window));
        session.RenderFrame();
        if (activeSession != session) activeSession.RenderFrame();
        foreach (var (scroll, offset) in offsets)
        {
            if (!scroll.IsDisposed && (scroll == content || scroll.IsDescendantOf(content)))
                scroll.SetOffset(offset);
        }
        activeSession.RenderFrame();
        // Start in the form, not on the Close button below it: the first enabled control of the
        // content (Account's first field, Settings' Theme). A view that knows better, such as Compose
        // choosing To or the body, moves focus again when it opens.
        var first = MailKeyboardNavigation.TabStops(content).FirstOrDefault(stop => stop is not UiScrollView);
        activeSession.SetFocus(first ?? close);
        // A reopened view keeps the scroll position it had; only a first opening scrolls to the field.
        if (first is not null && offsets.Length == 0) FocusNavigation.Reveal(activeSession, first);
        inboxDialog.Opened?.Invoke();
        return true;
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    public bool CloseDialog()
    {
        // A file dialog sits over the form that opened it: Escape or Back closes only the file dialog.
        if (ActiveFileDialog is { } file)
            return file.Cancel();
        if (ActiveAboutDialog is { } about)
            return about.Cancel();
        return ActiveDialog?.Cancel() ?? false;
    }

    public bool ShowAbout()
    {
        if (Window.Session is not { IsDisposed: false } session) return false;
        if (ActiveAboutDialog is not null) return true;
        if (ActiveFileDialog is not null) return false;
        CloseDialog();
        RememberReaderFocus();
        var viewport = _ownerSize == BSize.Empty ? session.Host.ViewportSize : _ownerSize;
        var dialog = MailAbout.Show(Window, viewport, typeof(MailShellView).Assembly, _openUrl);
        if (dialog is null) return false;
        ActiveAboutDialog = dialog;
        _model.Accounts?.RefreshAvailability();
        var activeSession = dialog.Session ?? session;
        StandardThemeController.ApplyToSubtree(dialog, StandardControlPaint.GetTheme(Window));
        dialog.Closed += (_, _) =>
        {
            ActiveAboutDialog = null;
            _model.Accounts?.RefreshAvailability();
            if (_disposing || session.IsDisposed) return;
            session.SetFocus(_readerFocus is { CanFocus: true } ? _readerFocus : Menu);
            _readerFocus = null;
            _refreshStatus();
        };
        session.RenderFrame();
        if (activeSession != session) activeSession.RenderFrame();
        return true;
    }

    internal void RememberReaderFocus()
    {
        if (Window.Session?.FocusedElement is { } focused && (focused.IsDescendantOf(GetContent("inbox")) ||
            AccountSelectionContent is { } accounts && focused.IsDescendantOf(accounts)))
            _readerFocus = focused;
    }

    internal void RestoreReaderFocus()
    {
        if (_readerFocus is { CanFocus: true }) Window.Session?.SetFocus(_readerFocus);
        _readerFocus = null;
    }

    private static BRect DialogPlacement(BSize size)
    {
        double width = Math.Min(940, Math.Max(0, size.Width - 24));
        double height = Math.Min(680, Math.Max(0, size.Height - 24));
        return new BRect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }

    private StandardMenu CreateMenu()
    {
        var menu = new StandardMenu { PresentationMode = UiMenuPresentationMode.MenuBar, Focusable = true, AccessibleName = "Mail menu", PopupWidth = 300 };
        double line = BTextMeasurer.GetLineHeight(menu.Font);
        menu.MenuBarHeight = Math.Max(28, line + 8);
        menu.ItemHeight = Math.Max(26, line + 6);
        UiMenuItem Item(string id, string text, MailCommand command) => new(id, text) { Accelerator = MailShortcuts.For(command).Gesture };
        var mail = new UiMenuItem("file", "File");
        mail.Children.Add(Item("inbox", "Inbox", MailCommand.Inbox));
        var newMessage = Item("new", "New message", MailCommand.NewMessage);
        mail.Children.Add(newMessage);
        // Both ask for a file, the documented exemption to the no-modal-while-reading rule; neither is in Mailbox and message.
        var openEml = Item("open-eml", "Open .eml file…", MailCommand.OpenLocalOriginal);
        var saveEml = Item("save-eml", "Save message as .eml…", MailCommand.SaveMessageAsEml);
        mail.Children.Add(openEml);
        mail.Children.Add(saveEml);
        mail.Children.Add(Item("compose", "Open draft", MailCommand.Compose));
        var receive = Item("receive", "Receive mail", MailCommand.Receive);
        mail.Children.Add(receive);
        var otherMail = new UiMenuItem("other-mail", "Other mail views");
        var unified = Item("unified", "Unified saved inbox", MailCommand.UnifiedSavedInbox);
        var originals = Item("originals", "Saved originals", MailCommand.SavedOriginals);
        otherMail.Children.Add(Item("outbox", "Outbox", MailCommand.Outbox));
        otherMail.Children.Add(unified);
        otherMail.Children.Add(originals);
        mail.Children.Add(otherMail);
        mail.Children.Add(_accountMenu);
        // Search opens the inbox's search header, as Ctrl+K does; it is not a dialog.
        var searchMail = Item("search", "Search saved mail", MailCommand.SearchMail);
        if (_model.Inbox.SupportsLocalSearch) _mailboxMenu.Children.Insert(0, searchMail);
        mail.Children.Add(_mailboxMenu);
        var message = new UiMenuItem("message", "Message");
        message.Children.Add(Item("reply", "Reply", MailCommand.Reply));
        message.Children.Add(Item("reply-all", "Reply all", MailCommand.ReplyAll));
        message.Children.Add(Item("forward", "Forward", MailCommand.Forward));
        // Find opens the reader's inline find bar, as Ctrl+F does; it is not a dialog.
        var findInMessage = Item("find-message", "Find in message", MailCommand.FindInMessage);
        message.Children.Add(findInMessage);
        var tools = new UiMenuItem("tools", "Tools");
        tools.Children.Add(Item("account", "Account...", MailCommand.Account));
        tools.Children.Add(Item("settings", "Settings...", MailCommand.Settings));
        var help = new UiMenuItem("help", "Help");
        help.Children.Add(new UiMenuItem("about", "About Broiler Mail"));
        menu.SetItems([mail, message, tools, help]);
        menu.ItemInvoked += (_, e) =>
        {
            // StandardMenu restores focus after ItemInvoked. Dispatch afterward so it cannot
            // move focus behind the dialog or undo the composer's recipient/body focus.
            Window.Session?.Dispatcher.Post(() =>
            {
                if (_disposing || Window.Session is not { IsDisposed: false } session) return;
                if (e.Item.Id == "about")
                {
                    ShowAbout();
                    return;
                }
                if (_inboxDialogs.ContainsKey(e.Item.Id))
                {
                    ShowView(e.Item.Id);
                    return;
                }
                var command = e.Item.Id switch
                {
                    "inbox" => MailCommand.Inbox, "new" => MailCommand.NewMessage,
                    "compose" => MailCommand.Compose, "receive" => MailCommand.Receive, "outbox" => MailCommand.Outbox, "originals" => MailCommand.SavedOriginals, "unified" => MailCommand.UnifiedSavedInbox,
                    "reply" => MailCommand.Reply, "reply-all" => MailCommand.ReplyAll,
                    "forward" => MailCommand.Forward, "account" => MailCommand.Account,
                    "find-message" => MailCommand.FindInMessage, "search" => MailCommand.SearchMail,
                    "open-eml" => MailCommand.OpenLocalOriginal, "save-eml" => MailCommand.SaveMessageAsEml,
                    _ => MailCommand.Settings,
                };
                CreateKeyboardNavigation(session).Execute(command);
            });
        };
        void RefreshCommands()
        {
            if (menu.IsDisposed) return;
            newMessage.IsEnabled = _model.Compose.CanCompose;
            receive.IsEnabled = _model.Inbox.CanReceive;
            openEml.IsEnabled = CanOpenLocalOriginal;
            // The inbox view does not exist yet while the menu is first built.
            saveEml.IsEnabled = Files.Capabilities.HasFlag(MailFilePickerCapabilities.SaveFile) && Inbox?.CanSaveMessageAsEml == true;
            unified.IsEnabled = _model.Inbox.LocalCache is not null;
            originals.IsEnabled = _model.Inbox.SourceExports is IAccountSourceExportCatalog;
            foreach (var item in message.Children) item.IsEnabled = _model.Compose.CanRespond;
            findInMessage.IsEnabled = _model.Inbox.Body is not null;
            searchMail.IsEnabled = _model.Inbox.SupportsLocalSearch && !_model.Inbox.IsBusy;
            foreach (var page in _inboxDialogs.Values) page.Item.IsEnabled = page.Available();
            _accountMenu.IsEnabled = _accountMenu.Children.Any(item => item.IsEnabled);
            _mailboxMenu.IsEnabled = _mailboxMenu.Children.Any(item => item.IsEnabled);
            menu.Invalidate(UiInvalidationKind.Render | UiInvalidationKind.Semantic);
        }
        _model.Inbox.Changed += (_, _) => RefreshCommands();
        _model.Composer.Changed += (_, _) => RefreshCommands();
        if (_model.Accounts is { } accounts) accounts.Changed += (_, _) => RefreshCommands();
        _refreshCommands = RefreshCommands;
        RefreshCommands();
        return menu;
    }

    private Action? _refreshCommands;

    /// <summary>Brings the menu's enabled items up to date, as before the menu opens.</summary>
    internal void RefreshMenuCommands() => _refreshCommands?.Invoke();

    /// <summary>Open .eml file… can run: the picker opens files and the host keeps local originals.</summary>
    public bool CanOpenLocalOriginal => Files.Capabilities.HasFlag(MailFilePickerCapabilities.OpenFile) && _model.Inbox.SourceExports is ILocalMessageOriginals;

    /// <summary>
    /// Asks for an .eml file first and, only once one is chosen, shows Saved originals and opens it there read-only.
    /// Canceling the picker changes nothing. The file dialog is the documented exemption to the no-modal rule.
    /// </summary>
    public async Task<bool> OpenLocalOriginalAsync()
    {
        if (_disposing || !CanOpenLocalOriginal || ActiveFileDialog is not null || ActiveAboutDialog is not null) return false;
        if (ActiveDialog is not null && ActiveViewId != "originals") return false;
        // A pending removal or another running operation would drop the file: say why instead of asking for one.
        if (ExplainLocalOriginalOpenBlocked()) return false;
        var pick = await Files.PickOpenFileAsync(LocalOriginalsView.OpenRequest);
        if (_disposing || pick.Status != MailFilePickStatus.Picked || pick.Item is not { } file) return false;
        if (ActiveDialog is not null && ActiveViewId != "originals") return false;
        if (!ShowView("originals")) return false;
        if (ExplainLocalOriginalOpenBlocked()) return false;
        var opening = SavedOriginals.LocalImports.OpenPickedAsync(file);
        if ((ActiveDialog?.Session ?? Window.Session) is { } session) FocusNavigation.Reveal(session, SavedOriginals.LocalImports);
        await opening;
        return true;
    }

    private bool ExplainLocalOriginalOpenBlocked()
    {
        if (!SavedOriginals.LocalImports.ExplainOpenBlocked()) return false;
        if ((ActiveDialog?.Session ?? Window.Session) is { } session) FocusNavigation.Reveal(session, SavedOriginals.LocalImports);
        return true;
    }

    private const string DetailsBelow = "Details are below the buttons.";

    /// <summary>The space above and below the footer's text.</summary>
    public const double FooterPadding = 4;

    private static bool IsProblem(FeedbackKind kind) => kind is FeedbackKind.Error or FeedbackKind.Warning;

    /// <summary>
    /// The inbox's status or, for a problem, a pointer to its explanation and Retry, which sit in the
    /// affected pane: above the list, or under the message's header. While compact mode hides that
    /// pane, the footer says how to show it instead, naming the reader's way back by its label. So
    /// does the status of a message the compact list selected without showing it. At the session limit,
    /// which is explained above the list, the compact reader's footer likewise points there instead of
    /// giving the reading status, so it is no longer than that status. A message problem, in the reader,
    /// comes first.
    /// </summary>
    private static string InboxStatus(InboxViewModel inbox, InboxView view)
    {
        bool paneShown = view.ShowsPaneOf(inbox.ProblemScope);
        return (inbox.ProblemScope, inbox.ProblemIsCancellation, paneShown) switch
        {
            (InboxProblemScope.None, _, _) => inbox.SessionLimitNotice is not null && !view.ShowsPaneOf(InboxProblemScope.List)
                ? $"Session limit reached. Use {InboxView.BackText} to see the details."
                : inbox.StatusIsAboutMessage && !view.ShowsPaneOf(InboxProblemScope.Message) ? "Message selected. Open it to read." : inbox.Status,
            (_, true, true) => "Canceled. Retry is available.",
            (InboxProblemScope.Message, true, false) => "Canceled. Open the message to retry.",
            (_, true, false) => $"Canceled. Use {InboxView.BackText} to retry.",
            (InboxProblemScope.Message, false, true) => "The message could not be loaded. Details and Retry are below its date.",
            (InboxProblemScope.Message, false, false) => "The message could not be loaded. Open it to see the details and Retry.",
            _ => (inbox.ProblemIsOlderPage ? "Older messages could not be loaded. " : "Mail could not be received. ")
                + (paneShown ? "Details and Retry are above the list." : $"Use {InboxView.BackText} to see the details and Retry."),
        };
    }

    public StandardWindow Window { get; }
    public StandardMenu Menu { get; }
    public StandardDialog? ActiveDialog { get; private set; }
    public StandardAboutDialog? ActiveAboutDialog { get; private set; }
    /// <summary>The picker every view chooses files and folders with; never null.</summary>
    public IMailFilePicker Files { get; }
    /// <summary>The open file or folder dialog of the shell's own picker, or null.</summary>
    public UiDialog? ActiveFileDialog => _ownedFileDialogs?.ActiveDialog;
    /// <summary>Gives the open file dialog the keys it takes before its focused control, such as Ctrl+Enter.</summary>
    internal bool PreviewFileDialogInput(UiInputEvent input) => _ownedFileDialogs?.PreviewInput(input) == true;
    internal ComposerView Composer { get; }
    public string ActiveViewId { get; private set; } = "inbox";
    public UiElement ActiveContent => GetContent(ActiveViewId);
    public UiElement NavigationFocus => (UiElement?)_dialogClose ?? Menu;
    public UiElement GetContent(string id) => _contents[id];
    public InboxView Inbox { get; }
    public OutboxView Outbox { get; }
    public SavedOriginalsView SavedOriginals { get; }
    public UnifiedSavedInboxView UnifiedInbox { get; }
    public UiElement? AccountSelectionContent { get; }
    /// <summary>The status line for the active workspace or dialog.</summary>
    public StandardLabel Footer { get; }
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=3; Fingerprint=A9E9EB
    // Hairs-Falsified-If: PrepareCloseAsync completes true without waiting for Composer.PrepareCloseAsync, so the window may close before the draft reaches storage
    // Hairs-Human:        PENDING
    public Task<bool> PrepareCloseAsync() => _model.Composer.PrepareCloseAsync();

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=886976
    // Hairs-Human:        PENDING
    public MailKeyboardNavigation CreateKeyboardNavigation(UiSession session) => new(session, this, _model);

    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=E27513
    // Hairs-Falsified-If: a connection test still running when the view is disposed is not canceled
    // Hairs-Human:        PENDING
    public void Dispose()
    {
        _disposing = true;
        _ownedFileDialogs?.Dispose();
        CloseDialog();
        _model.Account.CancelConnectionTest();
        _model.AccountReceives?.Dispose();
        _model.Inbox.Dispose();
        _model.Settings.Storage?.Dispose();
        _model.Composer.Dispose();
        Window.Dispose();
    }
}
