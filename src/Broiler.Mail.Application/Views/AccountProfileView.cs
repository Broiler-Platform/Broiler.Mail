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
        ConfigurationForm.AddText(panel, "Account profile — one account for version 1");
        ConfigurationForm.AddText(panel, "Save your account details, then save a password and test the connection. No messages are fetched during the test.");
        var name = ConfigurationForm.AddField(panel, "Display name", model.DisplayName);
        var email = ConfigurationForm.AddField(panel, "Email address", model.EmailAddress);
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
        var save = new StandardButton { Text = "Save account" };
        panel.AddChild(save);
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
            save.IsEnabled = model.CanSave;
            password.IsEnabled = savePassword.IsEnabled = forgetPassword.IsEnabled = test.IsEnabled = model.CanManagePassword;
            test.IsEnabled = model.CanManagePassword && password.Text.Length == 0;
            cancel.IsEnabled = model.CanCancelTest;
            status.Text = model.Status;
        }
        model.Changed += (_, _) => RefreshState();
        password.TextChanged += (_, _) => RefreshState();
        void CaptureFields()
        {
            model.DisplayName = name.Text;
            model.EmailAddress = email.Text;
            model.Host = host.Text;
            model.Port = port.Text;
            model.UserName = user.Text;
            model.Security = security.SelectedIndex == 1 ? TransportSecurity.StartTls : TransportSecurity.Tls;
            model.Authentication = AuthenticationMethod.Password;
        }
        save.Clicked += async (_, _) =>
        {
            CaptureFields();
            password.Text = string.Empty;
            await model.SaveAsync();
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
