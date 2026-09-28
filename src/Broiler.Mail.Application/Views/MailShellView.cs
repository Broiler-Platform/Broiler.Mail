using Broiler.Mail.Application.ViewModels;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.TabView.Standard;
using Broiler.UI.Window.Standard;
using Broiler.UI.Standard;
using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>Single-account inbox, reading pane, and configuration workflow.</summary>
public sealed class MailShellView : IDisposable
{
    private readonly MailShellViewModel _model;
    public MailShellView(MailShellViewModel model)
    {
        _model = model;
        Window = new StandardWindow { Title = model.Title };
        Window.ApplyTheme(StandardControlPaint.Theme);
        var layout = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock };
        var status = new StandardLabel { Text = model.Status.Replace("&", "&&", StringComparison.Ordinal), Foreground = StandardControlPaint.Text, Wrapping = UiTextWrapping.Wrap };
        layout.AddChild(status);
        layout.SetDock(status, UiDock.Bottom);

        Navigation = new StandardTabView();
        Navigation.AddTab("inbox", "Inbox", new TabContent(new InboxView(model.Inbox).CreateContent()));
        Navigation.AddTab("account", "Account", new TabContent(new AccountProfileView(model.Account).CreateContent()));
        Navigation.AddTab("settings", "Settings", new TabContent(new SettingsView(model.Settings).CreateContent()));
        if (model.Account.Profile is null) Navigation.SelectTab("account");
        void RefreshStatus()
        {
            if (status.IsDisposed) return;
            string text = Navigation.SelectedTab?.Id switch
            {
                "account" => string.IsNullOrEmpty(model.Account.Status) ? "Save your account details, then save a password and test the connection." : model.Account.Status,
                "settings" => string.IsNullOrEmpty(model.Settings.Status) ? "Theme and initial window size apply on restart." : model.Settings.Status,
                _ => model.Inbox.Status,
            };
            status.Text = text.Replace("&", "&&", StringComparison.Ordinal);
        }
        Navigation.SelectionChanged += (_, _) => { Window.Session?.SetFocus(Navigation); RefreshStatus(); };
        model.Inbox.Changed += (_, _) => RefreshStatus();
        model.Account.Changed += (_, _) => RefreshStatus();
        model.Settings.Changed += (_, _) => RefreshStatus();
        RefreshStatus();
        layout.AddChild(Navigation);
        Window.AddChild(layout);
    }

    public StandardWindow Window { get; }
    public StandardTabView Navigation { get; }

    public MailKeyboardNavigation CreateKeyboardNavigation(UiSession session) => new(session, this, _model);

    public void Dispose()
    {
        _model.Account.CancelConnectionTest();
        _model.Inbox.Dispose();
        Window.Dispose();
    }
}
