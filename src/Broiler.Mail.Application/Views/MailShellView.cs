// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   20
// Annotated:        5/20
// Exempt:           17
// Human-reviewed:   0/20
// IP risk:          Low
// Security risk:    Medium
// Criteria:         4/0
// Resource impact:  7/10 max
// Unverified:       20
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.UI.Forms;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Window;
using Broiler.UI.ScrollView;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI.Window.Standard;
using Broiler.UI.Standard;
using Broiler.UI;
using Broiler.Mail.Application.Preview;

namespace Broiler.Mail.Application.Views;

/// <summary>Single-account inbox, reading pane, and configuration workflow.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=126B98
// Broiler-Falsified-If: PrepareCloseAsync completes true without waiting for Composer.PrepareCloseAsync, so the window may close before the draft reaches storage
// Broiler-Human:        PENDING
public sealed class MailShellView : IDisposable
{
    private readonly MailShellViewModel _model;
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=ADE6BB
    // Broiler-Falsified-If: with an unconfigured account the Account setup dialog is not opened
    // Broiler-Human:        PENDING
    public MailShellView(MailShellViewModel model, IHtmlPreviewHost? htmlPreview = null, MessageDateFormatter? dates = null)
    {
        _model = model;
        Window = new StandardWindow { Title = model.Title };
        Window.ApplyTheme(StandardControlPaint.Theme);
        var layout = _layout;
        // Literal: statuses carry addresses and server text, where '&' is a character, not an access key.
        var status = Footer = new StandardLabel { Text = model.Status, Wrapping = UiTextWrapping.Wrap, UseMnemonic = false };
        Menu = CreateMenu();
        layout.AddChild(Menu);
        layout.SetDock(Menu, UiDock.Top);
        // Keep the status clear of the window edges.
        var footer = _footer = new Inset(status, 12, FooterPadding);
        layout.AddChild(footer);
        layout.SetDock(footer, UiDock.Bottom);

        Inbox = new InboxView(model.Inbox, htmlPreview, dates, model.Compose, model.Settings);
        var inboxContent = Inbox.CreateContent();
        _contents.Add("inbox", inboxContent);
        var account = new AccountProfileView(model.Account);
        _contents.Add("account", account.CreateContent());
        // The setup checklist ends in the inbox, receiving for the first time.
        account.InboxRequested += (_, _) => { ShowView("inbox"); _ = model.Inbox.ReceiveAsync(); };
        _contents.Add("settings", new SettingsView(model.Settings).CreateContent());
        var composer = new ComposerView(model.Composer, model.Inbox, model.Compose);
        _contents.Add("compose", composer.CreateContent());
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
        foreach (var id in new[] { "account", "settings", "compose" }) _parked.AddChild(_contents[id]);
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
            _ownerSize = size;
        }));
    }

    private UiElement? _readerFocus;
    private readonly Dictionary<string, UiElement> _contents = [];
    private readonly Dictionary<string, (UiScrollView Scroll, BPoint Offset)[]> _scrollOffsets = [];
    private readonly StandardPanel _layout = new() { LayoutMode = UiPanelLayoutMode.Dock };
    // Keep forms alive and themed between presentations, including unsaved fields and scroll positions.
    private readonly StandardPanel _parked = new() { Visibility = UiVisibility.Collapsed };
    private readonly Inset _footer;
    private readonly Action _refreshStatus;
    private bool _disposing;
    private BSize _ownerSize;
    private StandardButton? _dialogClose;

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
        if (Window.Session is not { } session) { ActiveViewId = id; return true; }
        if (ActiveDialog is not null && ActiveViewId == id) return true;
        CloseDialog();
        if (id == "inbox") { ActiveViewId = id; _refreshStatus(); return true; }

        if (session.FocusedElement is { } focused && focused.IsDescendantOf(GetContent("inbox"))) _readerFocus = focused;
        var content = GetContent(id);
        var offsets = _scrollOffsets.GetValueOrDefault(id) ?? [];
        _parked.RemoveChild(content);
        var dialog = new StandardDialog
        {
            Title = id switch { "account" => "Account", "settings" => "Settings", _ => "Compose message" },
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
        _layout.RemoveChild(_footer);
        body.AddChild(_footer);
        body.SetDock(_footer, UiDock.Bottom);
        body.AddChild(content);
        dialog.AddChild(new MeasuredContent(body, measureLimit: () =>
            dialog.IsBrokenOut || dialog.Placement.IsEmpty
                ? null
                : new BSize(dialog.Placement.Width, Math.Max(0, dialog.Placement.Height - dialog.TitleBarHeight))));
        ActiveViewId = id;
        ActiveDialog = dialog;
        // Detach persistent controls before UiWindow.Close disposes the dialog's children.
        dialog.Closing += (_, _) =>
        {
            _scrollOffsets[id] = Descendants(content).OfType<UiScrollView>().Select(scroll => (scroll, scroll.Offset)).ToArray();
            body.RemoveChild(content);
            _parked.AddChild(content);
            body.RemoveChild(_footer);
            _layout.InsertChild(1, _footer);
            _layout.SetDock(_footer, UiDock.Bottom);
        };
        dialog.Closed += (_, _) =>
        {
            ActiveDialog = null;
            _dialogClose = null;
            ActiveViewId = "inbox";
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
        foreach (var (scroll, offset) in offsets) scroll.SetOffset(offset);
        activeSession.RenderFrame();
        activeSession.SetFocus(close);
        return true;
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    public bool CloseDialog() => ActiveDialog?.Cancel() ?? false;

    internal void RememberReaderFocus()
    {
        if (Window.Session?.FocusedElement is { } focused && focused.IsDescendantOf(GetContent("inbox")))
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
        var mail = new UiMenuItem("mail", "Mail");
        mail.Children.Add(Item("inbox", "Inbox", MailCommand.Inbox));
        mail.Children.Add(Item("new", "New message", MailCommand.NewMessage));
        mail.Children.Add(Item("compose", "Open draft", MailCommand.Compose));
        mail.Children.Add(Item("receive", "Receive mail", MailCommand.Receive));
        var message = new UiMenuItem("message", "Message");
        message.Children.Add(Item("reply", "Reply", MailCommand.Reply));
        message.Children.Add(Item("reply-all", "Reply all", MailCommand.ReplyAll));
        message.Children.Add(Item("forward", "Forward", MailCommand.Forward));
        var tools = new UiMenuItem("tools", "Tools");
        tools.Children.Add(Item("account", "Account...", MailCommand.Account));
        tools.Children.Add(Item("settings", "Settings...", MailCommand.Settings));
        menu.SetItems([mail, message, tools]);
        menu.ItemInvoked += (_, e) =>
        {
            // StandardMenu restores focus after ItemInvoked. Dispatch afterward so it cannot
            // move focus behind the dialog or undo the composer's recipient/body focus.
            Window.Session?.Dispatcher.Post(() =>
            {
                if (_disposing || Window.Session is not { IsDisposed: false } session) return;
                var command = e.Item.Id switch
                {
                    "inbox" => MailCommand.Inbox, "new" => MailCommand.NewMessage,
                    "compose" => MailCommand.Compose, "receive" => MailCommand.Receive,
                    "reply" => MailCommand.Reply, "reply-all" => MailCommand.ReplyAll,
                    "forward" => MailCommand.Forward, "account" => MailCommand.Account,
                    _ => MailCommand.Settings,
                };
                CreateKeyboardNavigation(session).Execute(command);
            });
        };
        void RefreshCommands()
        {
            if (menu.IsDisposed) return;
            mail.Children[1].IsEnabled = _model.Compose.CanCompose;
            mail.Children[3].IsEnabled = !_model.Inbox.IsBusy;
            foreach (var item in message.Children) item.IsEnabled = _model.Compose.CanRespond;
            menu.Invalidate(UiInvalidationKind.Render | UiInvalidationKind.Semantic);
        }
        _model.Inbox.Changed += (_, _) => RefreshCommands();
        _model.Composer.Changed += (_, _) => RefreshCommands();
        RefreshCommands();
        return menu;
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
    public string ActiveViewId { get; private set; } = "inbox";
    public UiElement ActiveContent => GetContent(ActiveViewId);
    public UiElement NavigationFocus => (UiElement?)_dialogClose ?? Menu;
    public UiElement GetContent(string id) => _contents[id];
    public InboxView Inbox { get; }
    /// <summary>The status line for the active workspace or dialog.</summary>
    public StandardLabel Footer { get; }
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=3; Fingerprint=A9E9EB
    // Broiler-Falsified-If: PrepareCloseAsync completes true without waiting for Composer.PrepareCloseAsync, so the window may close before the draft reaches storage
    // Broiler-Human:        PENDING
    public Task<bool> PrepareCloseAsync() => _model.Composer.PrepareCloseAsync();

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=886976
    // Broiler-Human:        PENDING
    public MailKeyboardNavigation CreateKeyboardNavigation(UiSession session) => new(session, this, _model);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=61DA59
    // Broiler-Falsified-If: a connection test still running when the view is disposed is not canceled
    // Broiler-Human:        PENDING
    public void Dispose()
    {
        _disposing = true;
        CloseDialog();
        _model.Account.CancelConnectionTest();
        _model.Inbox.Dispose();
        _model.Composer.Dispose();
        Window.Dispose();
    }
}
