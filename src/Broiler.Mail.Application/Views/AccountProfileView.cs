// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        2/3
// Exempt:           0
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  7/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.UI.Forms.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.TabView.Standard;

namespace Broiler.Mail.Application.Views;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=C09C5B
// Broiler-Falsified-If: the text of the SMTP password field reaches SavePasswordAsync without MailProtocol.Smtp, so it is stored and later sent as the IMAP password
// Broiler-Human:        PENDING
public sealed class AccountProfileView(AccountProfileViewModel model)
{
    /// <summary>Raised by the setup checklist's final action, once the account is ready to receive mail.</summary>
    public event EventHandler? InboxRequested;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=3FA8B6
    // Broiler-Falsified-If: the text of the SMTP password field reaches SavePasswordAsync without MailProtocol.Smtp, so it is stored and later sent as the IMAP password
    // Broiler-Human:        PENDING
    public UiElement CreateContent()
    {
        var panel = new StandardPanel { Spacing = 20 };
        // Setup checklist: each step states its result, and one button performs or reveals the next step.
        // Not collapsible: the next-step button must stay the first stop for keyboard users. Once the
        // account is ready, the checklist shrinks to a single line instead.
        var setup = new FormSection("Account setup", "");
        panel.AddChild(setup);
        StandardLabel Step() { var label = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false }; setup.Content.AddChild(label); return label; }
        var detailsStep = Step();
        var passwordStep = Step();
        var connectionStep = Step();
        var outgoingStep = Step();
        var next = new StandardButton();
        var dismissHints = new StandardButton { Text = "Dismiss hints", AccessibleName = "Dismiss setup hints", IsTabStop = false };
        setup.Content.AddChild(FormSurface.ActionBar(next, dismissHints));
        bool hintsDismissed = false;
        dismissHints.Clicked += (_, _) =>
        {
            hintsDismissed = !hintsDismissed;
            dismissHints.Text = hintsDismissed ? "Show hints" : "Dismiss hints";
            dismissHints.AccessibleName = hintsDismissed ? "Show setup hints" : "Dismiss setup hints";
            RefreshSetup();
        };

        var identity = ConfigurationForm.AddSection(panel, "Account identity");
        var name = ConfigurationForm.AddField(identity, "Display name", model.DisplayName);
        var email = ConfigurationForm.AddField(identity, "Email address", model.EmailAddress);

        var tabs = new StandardTabView();
        panel.AddChild(tabs);

        // Tab 1: Incoming mail — IMAP
        var incomingPanel = new StandardPanel { Spacing = 16 };
        var host = new StandardEdit { Text = model.Host, MaxLength = 320 };
        var port = new StandardEdit { Text = model.Port, MaxLength = 320 };
        incomingPanel.AddChild(new FormRow(
            new FormField("IMAP server (hostname only)", host),
            new FormField("IMAP port", port),
            0.72));

        var user = ConfigurationForm.AddField(incomingPanel, "Username", model.UserName);

        var security = new StandardComboBox();
        security.SetItems([new UiComboBoxItem("Tls", "TLS (usually port 993)"), new UiComboBoxItem("StartTls", "Required STARTTLS (usually port 143)")]);
        security.SelectedIndex = model.Security == TransportSecurity.StartTls ? 1 : 0;

        var authentication = new StandardComboBox();
        authentication.SetItems([new UiComboBoxItem("Password", "Password or app password")]);
        authentication.SelectedIndex = 0;

        incomingPanel.AddChild(new FormRow(
            new FormField("Connection security", security),
            new FormField("Authentication (OAuth sign-in is not available yet)", authentication),
            0.5));

        var credentials = ConfigurationForm.AddSection(incomingPanel, "IMAP password", "Stored in this device's credential store and never shown again. Saving new server details requires saving the password again.");
        var password = ConfigurationForm.AddField(credentials, "Password / app password", string.Empty);
        password.IsPassword = true;
        password.MaxLength = 1280;
        var savePassword = new StandardButton { Text = "Save password" };
        var forgetPassword = new StandardButton { Text = "Forget saved password" };
        credentials.AddChild(FormSurface.ActionBar(savePassword, forgetPassword));

        var incomingCard = new Inset(incomingPanel, 12, 12);
        tabs.AddTab("incoming", "Incoming (IMAP)", incomingCard);

        // Tab 2: Outgoing mail — SMTP
        var outgoingPanel = new StandardPanel { Spacing = 12 };
        var smtpSetup = new StandardComboBox();
        smtpSetup.SetItems([new UiComboBoxItem("None", "Not configured"), new UiComboBoxItem("Smtp", "Configure SMTP")]);
        smtpSetup.SelectedIndex = model.ConfigureSmtp ? 1 : 0;
        ConfigurationForm.AddLabeledControl(outgoingPanel, "Outgoing mail setup", smtpSetup);

        var smtpFields = new StandardPanel { Spacing = 12 };
        outgoingPanel.AddChild(smtpFields);

        var smtpHost = new StandardEdit { Text = model.SmtpHost, MaxLength = 320 };
        var smtpPort = new StandardEdit { Text = model.SmtpPort, MaxLength = 320 };
        smtpFields.AddChild(new FormRow(
            new FormField("SMTP server (hostname only)", smtpHost),
            new FormField("SMTP port", smtpPort),
            0.72));

        var smtpUser = ConfigurationForm.AddField(smtpFields, "SMTP username", model.SmtpUserName);

        var smtpSecurity = new StandardComboBox();
        smtpSecurity.SetItems([new UiComboBoxItem("Tls", "TLS (usually port 465)"), new UiComboBoxItem("StartTls", "Required STARTTLS (usually port 587)")]);
        smtpSecurity.SelectedIndex = model.SmtpSecurity == TransportSecurity.StartTls ? 1 : 0;

        var smtpAuthentication = new StandardComboBox();
        var loadedSmtpAuthentication = model.SmtpAuthentication;
        // Preserve a previously stored unsupported mode; editing another field must not change its identity.
        smtpAuthentication.SetItems(model.SmtpAuthentication == AuthenticationMethod.Password
            ? [new UiComboBoxItem("Password", "Password or app password")]
            : [new UiComboBoxItem("Password", "Password or app password"), new UiComboBoxItem("Unsupported", $"{model.SmtpAuthentication} (not available yet)")]);
        smtpAuthentication.SelectedIndex = model.SmtpAuthentication == AuthenticationMethod.Password ? 0 : 1;

        smtpFields.AddChild(new FormRow(
            new FormField("SMTP connection security", smtpSecurity),
            new FormField("SMTP authentication (OAuth sign-in is not available yet)", smtpAuthentication),
            0.5));

        var smtpPassword = ConfigurationForm.AddField(smtpFields, "SMTP password / app password", string.Empty);
        smtpPassword.IsPassword = true;
        smtpPassword.MaxLength = 1280;
        var saveSmtpPassword = new StandardButton { Text = "Save SMTP password" };
        var forgetSmtpPassword = new StandardButton { Text = "Forget SMTP password" };
        var testSmtp = new StandardButton { Text = "Test SMTP sign-in" };
        smtpFields.AddChild(FormSurface.ActionBar(saveSmtpPassword, forgetSmtpPassword, testSmtp));
        ConfigurationForm.AddText(smtpFields, "Forget the SMTP password before removing outgoing setup.");

        var advanced = new FormSection("Sent-copy settings", collapsible: true, expanded: model.SentCopyMode != SentCopyMode.NotConfigured)
        {
            // "Sent" keeps its capital: it names the Sent folder, as everywhere else in the app.
            ShowText = "Show Sent-copy settings", HideText = "Hide Sent-copy settings",
        };
        // Named for the toggle that controls it, and apart from the section's own name, as the composer's Cc and Bcc fields are.
        advanced.Content.AccessibleName = "Sent-copy handling and folder";
        smtpFields.AddChild(advanced);

        var sentCopy = new StandardComboBox();
        sentCopy.SetItems([new UiComboBoxItem("None", "Not configured — no app copy"),
            new UiComboBoxItem("Provider", "Provider saves a copy automatically"),
            new UiComboBoxItem("Append", "Broiler.Mail appends one copy via IMAP")]);
        sentCopy.SelectedIndex = (int)model.SentCopyMode;
        ConfigurationForm.AddLabeledControl(advanced.Content, "Sent-copy handling", sentCopy);
        var sentFolder = ConfigurationForm.AddField(advanced.Content, "Sent folder path (exact IMAP path)", model.SentFolder);
        ConfigurationForm.AddText(advanced.Content, "Choose append only after confirming that your provider does not save sent mail automatically. The folder must already exist; no folders are created. A failed or uncertain copy never resends mail or automatically retries the copy.");

        var outgoingCard = new Inset(outgoingPanel, 12, 12);
        tabs.AddTab("smtp", "Outgoing (SMTP)", outgoingCard);

        void SelectIncomingTab() => tabs.SelectedIndex = 0;
        void SelectOutgoingTab() => tabs.SelectedIndex = 1;

        if (model.ConfigureSmtp && (model.OutgoingStep == OutgoingSetupStep.Test || model.OutgoingStep == OutgoingSetupStep.SavePassword || model.NextStep == AccountSetupStep.Ready))
            SelectOutgoingTab();
        else
            SelectIncomingTab();

        var save = new StandardButton { Text = "Save account", IsDefault = true };
        var test = new StandardButton { Text = "Test connection" };
        var cancel = new StandardButton { Text = "Cancel test" };
        var status = new InlineFeedback();
        var surface = ConfigurationForm.NameFeedback(new FormSurface(panel, FormSurface.ActionBar(save, test, cancel), status));

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
            // As for IMAP, typed text is never silently replaced by the saved secret: save it first.
            testSmtp.Visibility = model.SupportsOutgoingTest ? UiVisibility.Visible : UiVisibility.Collapsed;
            testSmtp.IsEnabled = model.CanTestOutgoing && model.HasSmtpPassword != false && smtpSetup.SelectedIndex == 1 && smtpPassword.Text.Length == 0;
            cancel.IsEnabled = model.CanCancelTest;
            bool returnFocus = !model.CanCancelTest && surface.Session?.FocusedElement == cancel;
            cancel.Visibility = model.CanCancelTest ? UiVisibility.Visible : UiVisibility.Collapsed;
            RefreshSetup();
            if (model.ValidationField is { } valField)
            {
                if (valField.StartsWith("OutgoingServer", StringComparison.Ordinal) || valField.StartsWith("SentFolder", StringComparison.Ordinal))
                    SelectOutgoingTab();
                else if (valField.StartsWith("IncomingServer", StringComparison.Ordinal))
                    SelectIncomingTab();
            }
            // Focus goes back to the test that ran, or to Test connection when that one cannot take it. The SMTP
            // test sits in the scrolling form, which may have scrolled away from it meanwhile, so it is revealed
            // once the checklist above it has its new text.
            if (returnFocus && surface.Session is { } session)
            {
                if (model.LastTest == MailProtocol.Smtp)
                    SelectOutgoingTab();
                else
                    SelectIncomingTab();
                FocusNavigation.FocusAndReveal(session, model.LastTest == MailProtocol.Smtp && testSmtp.CanFocus ? testSmtp : test);
            }
            // A command that stays disabled once its work is done hands focus on: an SMTP test that found
            // no saved password to the SMTP password, which comes next. Focus stays while it runs.
            else if (!model.IsBusy && surface.Session is { } focusSession)
            {
                if (focusSession.FocusedElement == testSmtp)
                    SelectOutgoingTab();
                FocusNavigation.KeepFocusUsable(focusSession, surface, focusSession.FocusedElement == testSmtp ? smtpPassword : null);
            }
        }
        void RefreshSetup()
        {
            var step = model.NextStep;
            detailsStep.Text = model.Profile is null ? "Next — Enter your email address and IMAP server details, then save them."
                : model.HasUnsavedChanges ? "Next — You have unsaved changes. Save them before continuing."
                : "Done — Account details saved.";
            passwordStep.Text = model.Profile is null ? "Then — Save your password or app password."
                : model.HasPassword switch
                {
                    true => "Done — Password saved on this device.",
                    false => step == AccountSetupStep.SavePassword ? "Next — Save your password or app password." : "Then — Save your password or app password.",
                    _ => "Checking for a saved password…",
                };
            connectionStep.Text = model.ConnectionCheck switch
            {
                ConnectionCheck.Passed => "Done — Connection tested. Receiving works.",
                ConnectionCheck.Running => "Testing the connection…",
                ConnectionCheck.Failed => $"Next — {model.ConnectionFailure} Check the server details and password, then test again.",
                _ => step == AccountSetupStep.TestConnection ? "Next — Test the connection. No messages are fetched." : "Then — Test the connection.",
            };
            // Sending stays optional and apart from receiving: its result stands on this line only, and the
            // next-step button never offers it. A failure is explained here, beside the step.
            outgoingStep.Text = model.Profile?.OutgoingServer is null ? "Optional — Outgoing mail is not set up; add it below to send messages."
                : model.OutgoingCheck switch
                {
                    ConnectionCheck.Running => "Testing the SMTP sign-in… No message is sent.",
                    ConnectionCheck.Failed => $"Optional — {model.OutgoingFailure}",
                    ConnectionCheck.Passed => "Done — Outgoing sign-in tested; no message was sent.",
                    _ => model.HasSmtpPassword switch
                    {
                        true when model.SupportsOutgoingTest => "Optional — SMTP password saved. Test the SMTP sign-in below; no message is sent.",
                        true => "Done — Outgoing mail set up with a saved password.",
                        false => "Optional — Outgoing mail is set up; save its SMTP password to send messages.",
                        _ => "Checking for a saved SMTP password…",
                    },
                };
            next.Text = step switch
            {
                AccountSetupStep.SaveDetails => "Next: save account details",
                AccountSetupStep.SavePassword => "Next: enter password",
                AccountSetupStep.TestConnection => "Next: test connection",
                _ => "Open Inbox",
            };
            next.IsEnabled = step switch
            {
                AccountSetupStep.SaveDetails => model.CanSave,
                AccountSetupStep.SavePassword => model.CanManagePassword,
                AccountSetupStep.TestConnection => model.CanManagePassword && model.ConnectionCheck != ConnectionCheck.Running,
                _ => true,
            };
            if (model.ConnectionCheck == ConnectionCheck.Failed || model.OutgoingCheck == ConnectionCheck.Failed)
            {
                hintsDismissed = false;
                dismissHints.Text = "Dismiss hints";
                dismissHints.AccessibleName = "Dismiss setup hints";
            }
            bool ready = step == AccountSetupStep.Ready;
            detailsStep.Text = ready ? "Ready — this account can receive mail." : detailsStep.Text;
            if (hintsDismissed)
            {
                foreach (var line in new[] { detailsStep, passwordStep, connectionStep, outgoingStep })
                    line.Visibility = UiVisibility.Collapsed;
            }
            else
            {
                detailsStep.Visibility = UiVisibility.Visible;
                outgoingStep.Visibility = UiVisibility.Visible;
                foreach (var line in new[] { passwordStep, connectionStep })
                    line.Visibility = ready ? UiVisibility.Collapsed : UiVisibility.Visible;
            }
        }
        model.Changed += (_, _) => RefreshState();
        ConfigurationForm.BindFeedback(model, surface, status, new Dictionary<string, StandardEdit>
        {
            ["DisplayName"] = name, ["EmailAddress"] = email,
            ["IncomingServer.Host"] = host, ["IncomingServer.Port"] = port, ["IncomingServer.UserName"] = user,
            ["OutgoingServer.Host"] = smtpHost, ["OutgoingServer.Port"] = smtpPort, ["OutgoingServer.UserName"] = smtpUser,
            ["SentFolder"] = sentFolder,
        });
        password.TextChanged += (_, _) => RefreshState();
        smtpPassword.TextChanged += (_, _) => RefreshState();
        // Copying edits as they happen keeps the unsaved-changes step accurate.
        foreach (var field in new[] { name, email, host, port, user, smtpHost, smtpPort, smtpUser, sentFolder })
            field.TextChanged += (_, _) => { CaptureFields(); model.NotifyEdited(); };
        foreach (var combo in new[] { security, smtpSecurity, sentCopy })
            combo.SelectionChanged += (_, _) => { CaptureFields(); model.NotifyEdited(); };
        next.Clicked += async (_, _) =>
        {
            switch (model.NextStep)
            {
                case AccountSetupStep.SaveDetails:
                    save.Click();
                    break;
                case AccountSetupStep.SavePassword:
                    // The password is typed by the user; the step only takes them there.
                    SelectIncomingTab();
                    if (surface.Session is { } session) FocusNavigation.FocusAndReveal(session, password);
                    break;
                case AccountSetupStep.TestConnection:
                    test.Click();
                    break;
                default:
                    InboxRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
            await Task.CompletedTask;
        };
        smtpSetup.SelectionChanged += (_, _) =>
        {
            if (smtpSetup.SelectedIndex == 1)
                SelectOutgoingTab();
            smtpPassword.Text = string.Empty;
            CaptureFields();
            model.NotifyEdited();
            RefreshState();
        };
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
            SelectOutgoingTab();
            CaptureFields();
            string secret = smtpPassword.Text;
            smtpPassword.Text = string.Empty;
            await model.SavePasswordAsync(secret, MailProtocol.Smtp);
        };
        forgetSmtpPassword.Clicked += async (_, _) =>
        {
            SelectOutgoingTab();
            CaptureFields();
            smtpPassword.Text = string.Empty;
            await model.ForgetPasswordAsync(MailProtocol.Smtp);
        };
        savePassword.Clicked += async (_, _) =>
        {
            SelectIncomingTab();
            CaptureFields();
            string secret = password.Text;
            password.Text = string.Empty;
            await model.SavePasswordAsync(secret);
        };
        forgetPassword.Clicked += async (_, _) =>
        {
            SelectIncomingTab();
            CaptureFields();
            password.Text = string.Empty;
            await model.ForgetPasswordAsync();
        };
        test.Clicked += async (_, _) =>
        {
            SelectIncomingTab();
            CaptureFields();
            password.Text = string.Empty;
            await model.TestConnectionAsync();
        };
        testSmtp.Clicked += async (_, _) =>
        {
            SelectOutgoingTab();
            CaptureFields();
            smtpPassword.Text = string.Empty;
            await model.TestOutgoingConnectionAsync();
        };
        cancel.Clicked += (_, _) => model.CancelConnectionTest();
        RefreshState();
        return surface;
    }
}
