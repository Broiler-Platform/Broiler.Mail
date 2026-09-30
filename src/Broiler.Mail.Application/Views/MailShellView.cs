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
    public MailShellView(MailShellViewModel model, IHtmlPreviewHost? htmlPreview = null)
    {
        _model = model;
        Window = new StandardWindow { Title = model.Title };
        Window.ApplyTheme(StandardControlPaint.Theme);
        var layout = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var status = new StandardLabel { Text = model.Status.Replace("&", "&&", StringComparison.Ordinal), Foreground = StandardControlPaint.Text, Wrapping = UiTextWrapping.Wrap };
        layout.AddChild(status);
        layout.SetDock(status, UiDock.Bottom);

        Navigation = new StandardTabView();
        Navigation.AddTab("inbox", "Inbox", new TabContent(new InboxView(model.Inbox, htmlPreview).CreateContent()));
        Navigation.AddTab("account", "Account", new TabContent(new AccountProfileView(model.Account).CreateContent()));
        Navigation.AddTab("settings", "Settings", new TabContent(new SettingsView(model.Settings).CreateContent()));
        Navigation.AddTab("compose", "Compose", new TabContent(new ComposerView(model.Composer, model.Inbox).CreateContent()));
        if (model.Account.Profile is null) Navigation.SelectTab("account");
        if (model.Composer.HasDraft || model.Composer.HasLoadError) Navigation.SelectTab("compose");
        void RefreshStatus()
        {
            if (status.IsDisposed) return;
            string text = Navigation.SelectedTab?.Id switch
            {
                "account" => string.IsNullOrEmpty(model.Account.Status) ? "Save your account details, then save a password and test the connection." : model.Account.Status,
                "settings" => string.IsNullOrEmpty(model.Settings.Status) ? "Theme and initial window size apply on restart." : model.Settings.Status,
                "compose" => model.Composer.Status,
                _ => model.Inbox.Status,
            };
            status.Text = text.Replace("&", "&&", StringComparison.Ordinal);
        }
        Navigation.SelectionChanged += (_, _) => { Window.Session?.SetFocus(Navigation); RefreshStatus(); };
        model.Inbox.Changed += (_, _) => RefreshStatus();
        model.Account.Changed += (_, _) => RefreshStatus();
        model.Settings.Changed += (_, _) => RefreshStatus();
        model.Composer.Changed += (_, _) => RefreshStatus();
        RefreshStatus();
        layout.AddChild(Navigation);
        Window.AddChild(layout);
    }

    public StandardWindow Window { get; }
    public StandardTabView Navigation { get; }
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
