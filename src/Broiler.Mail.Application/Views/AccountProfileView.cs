using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Panel.Standard;

namespace Broiler.Mail.Application.Views;

public sealed class AccountProfileView(AccountProfileViewModel model)
{
    public UiElement CreateContent()
    {
        var panel = new StandardPanel { Spacing = 8 };
        ConfigurationForm.AddText(panel, "Account profile — one account");
        ConfigurationForm.AddText(panel, "Save your account details, then save an IMAP password and test the receiving connection. No messages are fetched during the test.");
        var name = ConfigurationForm.AddField(panel, "Display name", model.DisplayName);
        var email = ConfigurationForm.AddField(panel, "Email address", model.EmailAddress);
        ConfigurationForm.AddText(panel, "Incoming mail — IMAP");
        var host = ConfigurationForm.AddField(panel, "IMAP server (hostname only)", model.Host);
        var port = ConfigurationForm.AddField(panel, "IMAP port", model.Port);
        var user = ConfigurationForm.AddField(panel, "Username", model.UserName);
        var security = new StandardComboBox();
        security.SetItems([new UiComboBoxItem("Tls", "TLS (usually port 993)"), new UiComboBoxItem("StartTls", "Required STARTTLS (usually port 143)")]);
        security.SelectedIndex = model.Security == TransportSecurity.StartTls ? 1 : 0;
        ConfigurationForm.AddLabeledControl(panel, "Connection security", security);
        var authentication = new StandardComboBox();
        authentication.SetItems([new UiComboBoxItem("Password", "Password or app password")]);
        authentication.SelectedIndex = 0;
        ConfigurationForm.AddLabeledControl(panel, "Authentication (OAuth sign-in is not available yet)", authentication);

        ConfigurationForm.AddText(panel, "Outgoing mail — SMTP");
        ConfigurationForm.AddText(panel, "Configure outgoing mail and save its separate SMTP password. The connection test below checks IMAP only.");
        var smtpSetup = new StandardComboBox();
        smtpSetup.SetItems([new UiComboBoxItem("None", "Not configured"), new UiComboBoxItem("Smtp", "Configure SMTP")]);
        smtpSetup.SelectedIndex = model.ConfigureSmtp ? 1 : 0;
        ConfigurationForm.AddLabeledControl(panel, "Outgoing mail setup", smtpSetup);
        var smtpFields = new StandardPanel { Spacing = 8 };
        panel.AddChild(smtpFields);
        var smtpHost = ConfigurationForm.AddField(smtpFields, "SMTP server (hostname only)", model.SmtpHost);
        var smtpPort = ConfigurationForm.AddField(smtpFields, "SMTP port", model.SmtpPort);
        var smtpUser = ConfigurationForm.AddField(smtpFields, "SMTP username", model.SmtpUserName);
        var smtpSecurity = new StandardComboBox();
        smtpSecurity.SetItems([new UiComboBoxItem("Tls", "TLS (usually port 465)"), new UiComboBoxItem("StartTls", "Required STARTTLS (usually port 587)")]);
        smtpSecurity.SelectedIndex = model.SmtpSecurity == TransportSecurity.StartTls ? 1 : 0;
        ConfigurationForm.AddLabeledControl(smtpFields, "SMTP connection security", smtpSecurity);
        var smtpAuthentication = new StandardComboBox();
        var loadedSmtpAuthentication = model.SmtpAuthentication;
        // Preserve a previously stored unsupported mode; editing another field must not change its identity.
        smtpAuthentication.SetItems(model.SmtpAuthentication == AuthenticationMethod.Password
            ? [new UiComboBoxItem("Password", "Password or app password")]
            : [new UiComboBoxItem("Password", "Password or app password"), new UiComboBoxItem("Unsupported", $"{model.SmtpAuthentication} (not available yet)")]);
        smtpAuthentication.SelectedIndex = model.SmtpAuthentication == AuthenticationMethod.Password ? 0 : 1;
        ConfigurationForm.AddLabeledControl(smtpFields, "SMTP authentication (OAuth sign-in is not available yet)", smtpAuthentication);
        var smtpPassword = ConfigurationForm.AddField(smtpFields, "SMTP password / app password", string.Empty);
        smtpPassword.IsPassword = true;
        smtpPassword.MaxLength = 1280;
        var saveSmtpPassword = new StandardButton { Text = "Save SMTP password" };
        var forgetSmtpPassword = new StandardButton { Text = "Forget SMTP password" };
        smtpFields.AddChild(saveSmtpPassword);
        smtpFields.AddChild(forgetSmtpPassword);
        ConfigurationForm.AddText(smtpFields, "Save account changes before saving a password. SMTP credentials are stored separately in Windows Credential Manager. Forget the SMTP password before removing outgoing setup.");
        var sentCopy = new StandardComboBox();
        sentCopy.SetItems([new UiComboBoxItem("None", "Not configured — no app copy"),
            new UiComboBoxItem("Provider", "Provider saves a copy automatically"),
            new UiComboBoxItem("Append", "Broiler.Mail appends one copy via IMAP")]);
        sentCopy.SelectedIndex = (int)model.SentCopyMode;
        ConfigurationForm.AddLabeledControl(smtpFields, "Sent-copy handling", sentCopy);
        var sentFolder = ConfigurationForm.AddField(smtpFields, "Sent folder path (exact IMAP path)", model.SentFolder);
        ConfigurationForm.AddText(smtpFields, "Choose append only after confirming that your provider does not save sent mail automatically. The folder must already exist; no folders are created. A failed or uncertain copy never resends mail or automatically retries the copy.");
        var save = new StandardButton { Text = "Save account" };
        panel.AddChild(save);
        ConfigurationForm.AddText(panel, "IMAP password and connection test");
        var password = ConfigurationForm.AddField(panel, "Password / app password", string.Empty);
        password.IsPassword = true;
        password.MaxLength = 1280;
        ConfigurationForm.AddText(panel, "Saved passwords are kept in Windows Credential Manager and are never filled back into this field. Connection changes require saving a password again.");
        var savePassword = new StandardButton { Text = "Save password" };
        var forgetPassword = new StandardButton { Text = "Forget saved password" };
        var test = new StandardButton { Text = "Test connection" };
        var cancel = new StandardButton { Text = "Cancel test" };
        panel.AddChild(savePassword);
        panel.AddChild(forgetPassword);
        panel.AddChild(test);
        panel.AddChild(cancel);
        var status = ConfigurationForm.AddText(panel, model.Status);

        void RefreshState()
        {
            if (panel.IsDisposed)
                return;
            foreach (var field in new[] { name, email, host, port, user })
                field.IsEnabled = model.CanSave;
            security.IsEnabled = model.CanSave;
            authentication.IsEnabled = model.CanSave;
            smtpSetup.IsEnabled = model.CanSave;
            smtpFields.Visibility = smtpSetup.SelectedIndex == 1 ? UiVisibility.Visible : UiVisibility.Collapsed;
            foreach (var field in new[] { smtpHost, smtpPort, smtpUser })
                field.IsEnabled = model.CanSave && smtpSetup.SelectedIndex == 1;
            smtpSecurity.IsEnabled = smtpAuthentication.IsEnabled = model.CanSave && smtpSetup.SelectedIndex == 1;
            smtpPassword.IsEnabled = saveSmtpPassword.IsEnabled = forgetSmtpPassword.IsEnabled = model.CanManageSmtpPassword && smtpSetup.SelectedIndex == 1;
            sentCopy.IsEnabled = model.CanSave && smtpSetup.SelectedIndex == 1;
            sentFolder.IsEnabled = sentCopy.IsEnabled && sentCopy.SelectedIndex == 2;
            save.IsEnabled = model.CanSave;
            password.IsEnabled = savePassword.IsEnabled = forgetPassword.IsEnabled = test.IsEnabled = model.CanManagePassword;
            test.IsEnabled = model.CanManagePassword && password.Text.Length == 0;
            cancel.IsEnabled = model.CanCancelTest;
            status.Text = model.Status;
        }
        model.Changed += (_, _) => RefreshState();
        password.TextChanged += (_, _) => RefreshState();
        smtpSetup.SelectionChanged += (_, _) => { smtpPassword.Text = string.Empty; RefreshState(); };
        sentCopy.SelectionChanged += (_, _) => RefreshState();
        void CaptureFields()
        {
            model.DisplayName = name.Text;
            model.EmailAddress = email.Text;
            model.Host = host.Text;
            model.Port = port.Text;
            model.UserName = user.Text;
            model.Security = security.SelectedIndex == 1 ? TransportSecurity.StartTls : TransportSecurity.Tls;
            model.Authentication = AuthenticationMethod.Password;
            model.ConfigureSmtp = smtpSetup.SelectedIndex == 1;
            model.SmtpHost = smtpHost.Text;
            model.SmtpPort = smtpPort.Text;
            model.SmtpUserName = smtpUser.Text;
            model.SmtpSecurity = smtpSecurity.SelectedIndex == 1 ? TransportSecurity.StartTls : TransportSecurity.Tls;
            model.SmtpAuthentication = smtpAuthentication.SelectedIndex == 0 ? AuthenticationMethod.Password : loadedSmtpAuthentication;
            model.SentCopyMode = (SentCopyMode)sentCopy.SelectedIndex;
            model.SentFolder = sentFolder.Text;
        }
        save.Clicked += async (_, _) =>
        {
            CaptureFields();
            password.Text = string.Empty;
            smtpPassword.Text = string.Empty;
            await model.SaveAsync();
        };
        saveSmtpPassword.Clicked += async (_, _) =>
        {
            CaptureFields();
            string secret = smtpPassword.Text;
            smtpPassword.Text = string.Empty;
            await model.SavePasswordAsync(secret, MailProtocol.Smtp);
        };
        forgetSmtpPassword.Clicked += async (_, _) =>
        {
            CaptureFields();
            smtpPassword.Text = string.Empty;
            await model.ForgetPasswordAsync(MailProtocol.Smtp);
        };
        savePassword.Clicked += async (_, _) =>
        {
            CaptureFields();
            string secret = password.Text;
            password.Text = string.Empty;
            await model.SavePasswordAsync(secret);
        };
        forgetPassword.Clicked += async (_, _) =>
        {
            CaptureFields();
            password.Text = string.Empty;
            await model.ForgetPasswordAsync();
        };
        test.Clicked += async (_, _) =>
        {
            CaptureFields();
            password.Text = string.Empty;
            await model.TestConnectionAsync();
        };
        cancel.Clicked += (_, _) => model.CancelConnectionTest();
        RefreshState();
        return ConfigurationForm.Wrap(panel);
    }
}
