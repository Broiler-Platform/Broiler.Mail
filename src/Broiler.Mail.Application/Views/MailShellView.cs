// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        5/5
// Exempt:           3
// Human-reviewed:   0/5
// IP risk:          Low
// Security risk:    Medium
// Criteria:         4/0
// Resource impact:  7/10 max
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.UI.Forms;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.TabView.Standard;
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
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=7; Fingerprint=D556FF
    // Broiler-Falsified-If: with a recovered draft or a draft load error the window opens on a tab other than Compose
    // Broiler-Human:        PENDING
    public MailShellView(MailShellViewModel model, IHtmlPreviewHost? htmlPreview = null, MessageDateFormatter? dates = null)
    {
        _model = model;
        Window = new StandardWindow { Title = model.Title };
        Window.ApplyTheme(StandardControlPaint.Theme);
        var layout = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var status = new StandardLabel { Text = model.Status.Replace("&", "&&", StringComparison.Ordinal), Foreground = StandardControlPaint.Text, Wrapping = UiTextWrapping.Wrap };
        layout.AddChild(status);
        layout.SetDock(status, UiDock.Bottom);

        Navigation = new StandardTabView();
        Inbox = new InboxView(model.Inbox, htmlPreview, dates, model.Compose);
        var inboxContent = new TabContent(Inbox.CreateContent());
        Navigation.AddTab("inbox", "Inbox", inboxContent);
        var account = new AccountProfileView(model.Account);
        Navigation.AddTab("account", "Account", new TabContent(account.CreateContent()));
        // The setup checklist ends in the inbox, receiving for the first time.
        account.InboxRequested += (_, _) => { Navigation.SelectTab("inbox"); _ = model.Inbox.ReceiveAsync(); };
        Navigation.AddTab("settings", "Settings", new TabContent(new SettingsView(model.Settings).CreateContent()));
        var composer = new ComposerView(model.Composer, model.Inbox, model.Compose);
        Navigation.AddTab("compose", "Compose", new TabContent(composer.CreateContent()));
        if (model.Account.Profile is null) Navigation.SelectTab("account");
        if (model.Composer.HasDraft || model.Composer.HasLoadError) Navigation.SelectTab("compose");
        void RefreshStatus()
        {
            if (status.IsDisposed) return;
            string text = Navigation.SelectedTab?.Id switch
            {
                // A problem is explained below the form's buttons; the footer names it and points there.
                "account" => IsProblem(model.Account.StatusKind) ? $"{model.Account.StatusSummary} {DetailsBelow}"
                    : !string.IsNullOrEmpty(model.Account.Status) ? model.Account.Status
                    : model.Account.NextStep == AccountSetupStep.Ready ? "This account is ready to receive mail."
                    : "Save your account details, then save a password and test the connection.",
                "settings" => IsProblem(model.Settings.StatusKind) ? $"{model.Settings.StatusSummary} {DetailsBelow}"
                    : string.IsNullOrEmpty(model.Settings.Status) ? "A saved theme applies immediately. The window reopens at its last size and position." : model.Settings.Status,
                // Informational composer messages appear only here; the composer shows the others inline.
                "compose" => !string.IsNullOrEmpty(model.Composer.Status) && model.Composer.StatusKind == FeedbackKind.Information && !model.Composer.IsBusy
                    ? model.Composer.Status
                    : IsProblem(model.Composer.StorageKind) ? $"The draft is not saved. {DetailsBelow}" : model.Composer.StorageStatus,
                // The explanation and Retry sit beside the affected pane; the footer only points there.
                _ => model.Inbox.ProblemScope switch
                {
                    InboxProblemScope.List when !model.Inbox.ProblemIsCancellation => "Mail could not be received. Details and Retry are beside the list.",
                    InboxProblemScope.Message when !model.Inbox.ProblemIsCancellation => "The message could not be loaded. Details and Retry are beside it.",
                    InboxProblemScope.None => model.Inbox.Status,
                    _ => "Canceled. Retry is available.",
                },
            };
            status.Text = text.Replace("&", "&&", StringComparison.Ordinal);
        }
        Navigation.SelectionChanged += (_, _) =>
        {
            // Returning to the inbox after composing resumes where the reader was, not at the tabs.
            var resume = Navigation.SelectedTab?.Id == "inbox" ? _readerFocus : null;
            _readerFocus = null;
            Window.Session?.SetFocus(resume is { IsAttached: true, CanFocus: true } ? resume : Navigation);
            RefreshStatus();
        };
        model.Compose.Requested += (_, e) =>
        {
            var session = Window.Session;
            var focused = session?.FocusedElement;
            Navigation.SelectTab("compose");
            if (focused is not null && focused.IsDescendantOf(inboxContent)) _readerFocus = focused;
            var target = e.Focus == CompositionFocus.Recipients ? composer.Recipients : composer.Body;
            if (session is not null && target is not null) FocusNavigation.FocusAndReveal(session, target);
        };
        model.Inbox.Changed += (_, _) => RefreshStatus();
        model.Account.Changed += (_, _) => RefreshStatus();
        model.Settings.Changed += (_, _) => RefreshStatus();
        model.Composer.Changed += (_, _) => RefreshStatus();
        RefreshStatus();
        layout.AddChild(Navigation);
        Window.AddChild(layout);
    }

    private UiElement? _readerFocus;

    private const string DetailsBelow = "Details are below the buttons.";

    private static bool IsProblem(FeedbackKind kind) => kind is FeedbackKind.Error or FeedbackKind.Warning;

    public StandardWindow Window { get; }
    public StandardTabView Navigation { get; }
    public InboxView Inbox { get; }
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=3; Fingerprint=A9E9EB
    // Broiler-Falsified-If: PrepareCloseAsync completes true without waiting for Composer.PrepareCloseAsync, so the window may close before the draft reaches storage
    // Broiler-Human:        PENDING
    public Task<bool> PrepareCloseAsync() => _model.Composer.PrepareCloseAsync();

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=886976
    // Broiler-Human:        PENDING
    public MailKeyboardNavigation CreateKeyboardNavigation(UiSession session) => new(session, this, _model);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=53D728
    // Broiler-Falsified-If: a connection test still running when the view is disposed is not canceled
    // Broiler-Human:        PENDING
    public void Dispose()
    {
        _model.Account.CancelConnectionTest();
        _model.Inbox.Dispose();
        _model.Composer.Dispose();
        Window.Dispose();
    }
}
