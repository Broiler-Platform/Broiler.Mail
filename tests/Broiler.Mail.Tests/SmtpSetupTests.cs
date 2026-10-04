using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// UI-06: the SMTP sign-in test in the Account tab. Sending stays optional and separate: its result stands
/// beside the outgoing step only, and receiving readiness never waits for it or changes because of it.
/// </summary>
[Collection("UI theme")]
public sealed class SmtpSetupTests
{
    private const string Secret = "synthetic-app-password";

    [Fact]
    public async Task SendingReadinessIsSeparateFromReceiving()
    {
        var (profile, credentials) = await SavedAccountAsync();
        var tester = new TestOutgoingTester();
        var receiver = new TestMailReceiver();
        using var fixture = new Fixture(profile, credentials, tester, receiver);
        Assert.Equal(OutgoingSetupStep.Test, fixture.Account.OutgoingStep);
        Assert.Equal("Optional — SMTP password saved. Test the SMTP sign-in below; no message is sent.", fixture.OutgoingLine);

        // The receiving checklist reaches Ready while SMTP is untested; its button never offers the SMTP test.
        fixture.Click(fixture.Next);
        Assert.Equal(1, receiver.Calls);
        Assert.Equal(AccountSetupStep.Ready, fixture.Account.NextStep);
        Assert.Equal("Open Inbox", fixture.Next.Text);
        Assert.Equal(0, tester.Calls);

        fixture.Click(fixture.TestSmtp);
        Assert.Equal(1, tester.Calls);
        Assert.Equal(profile, tester.Account);
        Assert.Equal(ConnectionCheck.Passed, fixture.Account.OutgoingCheck);
        Assert.Equal(OutgoingSetupStep.Ready, fixture.Account.OutgoingStep);
        Assert.Equal("The SMTP server accepted the sign-in over an encrypted connection. No message was sent.", fixture.Account.Status);
        // Both kinds of readiness read separately.
        Assert.Equal(["Ready — this account can receive mail.", "Done — Outgoing sign-in tested; no message was sent."], fixture.StepLines);
        Assert.Equal(ConnectionCheck.Passed, fixture.Account.ConnectionCheck);
        Assert.Equal(AccountSetupStep.Ready, fixture.Account.NextStep);
        Assert.Equal(1, receiver.Calls);
        Assert.DoesNotContain(fixture.AllText(), text => text.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedSmtpTestIsExplainedBesideTheOutgoingStepOnly()
    {
        var (profile, credentials) = await SavedAccountAsync();
        var tester = new TestOutgoingTester
        {
            Test = _ => throw new MailConnectionException("The SMTP server rejected the sign-in.", MailConnectionFailure.AuthenticationRejected),
        };
        using var fixture = new Fixture(profile, credentials, tester);
        var receiving = fixture.StepLines[..3];

        fixture.Click(fixture.TestSmtp);
        Assert.Equal(ConnectionCheck.Failed, fixture.Account.OutgoingCheck);
        Assert.Equal(MailConnectionFailure.AuthenticationRejected, fixture.Account.OutgoingFailureKind);
        Assert.Equal("Optional — SMTP sign-in test failed: The SMTP server rejected the sign-in.", fixture.OutgoingLine);
        Assert.Single(fixture.StepLines, line => line.Contains("rejected the sign-in", StringComparison.Ordinal));
        Assert.Equal(FeedbackKind.Error, fixture.Account.StatusKind);
        Assert.Equal("SMTP sign-in test failed. Details are below the buttons.", fixture.Footer.Text);
        // The receiving steps are untouched, and the test can be repeated.
        Assert.Equal(receiving, fixture.StepLines[..3]);
        Assert.Equal(ConnectionCheck.NotRun, fixture.Account.ConnectionCheck);
        Assert.Equal(AccountSetupStep.TestConnection, fixture.Account.NextStep);
        Assert.True(fixture.TestSmtp.IsEnabled);

        // A tester that finds no password bound to these details corrects the saved-password state.
        tester.Test = _ => throw new MailConnectionException("No SMTP password is saved for these server details.", MailConnectionFailure.MissingPassword);
        fixture.Click(fixture.TestSmtp);
        Assert.Equal(false, fixture.Account.HasSmtpPassword);
        Assert.Equal(OutgoingSetupStep.SavePassword, fixture.Account.OutgoingStep);
        Assert.Equal("Optional — SMTP sign-in test failed: No SMTP password is saved for these server details.", fixture.OutgoingLine);
        Assert.False(fixture.TestSmtp.IsEnabled);
    }

    [Fact]
    public async Task ACanceledSmtpTestProvesNothingAndFocusReturnsToIt()
    {
        var (profile, credentials) = await SavedAccountAsync();
        var tester = new TestOutgoingTester { Test = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        using var fixture = new Fixture(profile, credentials, tester);
        var cancel = fixture.Button("Cancel test");

        foreach (bool escape in new[] { false, true })
        {
            fixture.Click(fixture.TestSmtp, settle: false);
            fixture.Session.RenderFrame();
            Assert.Equal(ConnectionCheck.Running, fixture.Account.OutgoingCheck);
            Assert.Equal("Testing the SMTP sign-in… No message is sent.", fixture.OutgoingLine);
            Assert.Equal("Signing in to the SMTP server… No message is sent.", fixture.Account.Status);
            Assert.Equal(UiVisibility.Visible, cancel.Visibility);
            Assert.True(cancel.IsEnabled);
            Assert.False(fixture.Next.IsEnabled);
            fixture.Session.SetFocus(cancel);

            // The bottom bar's Cancel test and Escape stop either test.
            if (escape) Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
            else cancel.Click();
            fixture.Settle();
            Assert.Equal(ConnectionCheck.NotRun, fixture.Account.OutgoingCheck);
            Assert.Null(fixture.Account.OutgoingFailure);
            Assert.Equal("SMTP sign-in test canceled.", fixture.Account.Status);
            Assert.Equal(FeedbackKind.Information, fixture.Account.StatusKind);
            Assert.Equal(UiVisibility.Collapsed, cancel.Visibility);
            Assert.Equal("Optional — SMTP password saved. Test the SMTP sign-in below; no message is sent.", fixture.OutgoingLine);
            // Focus goes back to the test that ran, not to Test connection.
            Assert.Same(fixture.TestSmtp, fixture.Session.FocusedElement);
        }
        Assert.Equal(2, tester.Calls);
        Assert.Equal(ConnectionCheck.NotRun, fixture.Account.ConnectionCheck);
    }

    [Fact]
    public async Task TheTestIsOfferedOnlyForASavedPasswordWithAnEmptyPasswordBox()
    {
        var (profile, credentials) = await SavedAccountAsync();
        // Without a tester there is no button, and the outgoing line keeps its earlier wording.
        using (var without = new Fixture(profile, credentials, tester: null))
        {
            Assert.Equal(UiVisibility.Collapsed, without.TestSmtp.Visibility);
            Assert.DoesNotContain(without.TestSmtp, MailKeyboardNavigation.TabStops(without.Content));
            Assert.Equal("Done — Outgoing mail set up with a saved password.", without.OutgoingLine);
        }

        var tester = new TestOutgoingTester();
        using var fixture = new Fixture(profile, credentials, tester);
        Assert.True(fixture.TestSmtp.IsEnabled);
        var stops = MailKeyboardNavigation.TabStops(fixture.Content).ToList();
        // After the SMTP password buttons, before the bottom bar.
        Assert.Equal(stops.IndexOf(fixture.Button("Forget SMTP password")) + 1, stops.IndexOf(fixture.TestSmtp));
        Assert.Equal("Test connection", ((StandardButton)stops[^1]).Text);

        // Typed text is never replaced by the saved password: it has to be saved first.
        fixture.SmtpPassword.Text = "typed-but-not-saved";
        Assert.False(fixture.TestSmtp.IsEnabled);
        fixture.SmtpPassword.Text = "";
        Assert.True(fixture.TestSmtp.IsEnabled);

        // Without a saved SMTP password there is nothing to sign in with.
        await fixture.Account.ForgetPasswordAsync(MailProtocol.Smtp);
        fixture.Settle();
        Assert.False(fixture.TestSmtp.IsEnabled);
        Assert.Equal("Optional — Outgoing mail is set up; save its SMTP password to send messages.", fixture.OutgoingLine);

        // Outgoing mail switched off: the button is inside the hidden SMTP fields, so it is no Tab stop.
        fixture.Combo("Outgoing mail setup").SelectedIndex = 0;
        fixture.Session.RenderFrame();
        Assert.DoesNotContain(fixture.TestSmtp, MailKeyboardNavigation.TabStops(fixture.Content));
        Assert.Equal(0, tester.Calls);
    }

    [Fact]
    public async Task WithoutOutgoingSetupThereIsNothingToTest()
    {
        var profile = TestDirectory.Profile();
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        var tester = new TestOutgoingTester();
        using var fixture = new Fixture(profile, credentials, tester);
        Assert.False(fixture.Account.CanTestOutgoing);
        Assert.Equal(OutgoingSetupStep.NotConfigured, fixture.Account.OutgoingStep);
        Assert.Equal("Optional — Outgoing mail is not set up; add it below to send messages.", fixture.OutgoingLine);
        Assert.DoesNotContain(fixture.TestSmtp, MailKeyboardNavigation.TabStops(fixture.Content));
        await fixture.Account.TestOutgoingConnectionAsync();
        fixture.Settle();
        Assert.Equal(0, tester.Calls);
        Assert.Contains("Configure and save SMTP first.", fixture.Account.Status);
    }

    [Fact]
    public async Task OnlyOneTestRunsAtATime()
    {
        var (profile, credentials) = await SavedAccountAsync();
        var receiver = new TestMailReceiver { Test = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        var tester = new TestOutgoingTester();
        using var fixture = new Fixture(profile, credentials, tester, receiver);
        var receiving = fixture.Account.TestConnectionAsync();
        Assert.False(fixture.TestSmtp.IsEnabled);
        await fixture.Account.TestOutgoingConnectionAsync();
        Assert.Equal(0, tester.Calls);
        Assert.Equal(ConnectionCheck.NotRun, fixture.Account.OutgoingCheck);
        // Cancel still reaches the test that is running.
        fixture.Account.CancelConnectionTest();
        await receiving;
        fixture.Settle();
        Assert.Equal("Connection test canceled.", fixture.Account.Status);
        Assert.Equal(ConnectionCheck.NotRun, fixture.Account.ConnectionCheck);
    }

    [Fact]
    public async Task UnsavedEditsRefuseEitherTestAndKeepTheSavedProfilesResults()
    {
        var (profile, credentials) = await SavedAccountAsync();
        var tester = new TestOutgoingTester();
        var receiver = new TestMailReceiver();
        using var fixture = new Fixture(profile, credentials, tester, receiver);
        var account = fixture.Account;
        await PassBothAsync(fixture);

        fixture.Field("SMTP server (hostname only)").Text = "changed.example.test";
        await account.TestOutgoingConnectionAsync();
        fixture.Settle();
        Assert.Equal(1, tester.Calls);
        Assert.Contains("Save your account changes", account.Status);
        Assert.Equal(ConnectionCheck.Passed, account.OutgoingCheck);
        fixture.Field("SMTP server (hostname only)").Text = profile.OutgoingServer!.Host;

        // A refusal is no test result: once an unrelated change is saved, both results still stand.
        fixture.Field("Display name").Text = "Renamed account";
        await account.TestConnectionAsync();
        fixture.Settle();
        Assert.Equal(1, receiver.Calls);
        Assert.Contains("Save your account changes", account.Status);
        fixture.Click(fixture.Button("Save account"));
        Assert.Equal("Renamed account", account.Profile!.DisplayName);
        Assert.Equal((ConnectionCheck.Passed, ConnectionCheck.Passed), (account.ConnectionCheck, account.OutgoingCheck));
        Assert.Equal(AccountSetupStep.Ready, account.NextStep);
    }

    [Fact]
    public async Task EachResultIsResetOnlyByItsOwnServerDetailsAndPassword()
    {
        var (profile, credentials) = await SavedAccountAsync();
        using var fixture = new Fixture(profile, credentials, new TestOutgoingTester());
        var account = fixture.Account;

        // Saving or forgetting the SMTP password resets only the SMTP result.
        await PassBothAsync(fixture);
        await account.SavePasswordAsync("another-smtp-password", MailProtocol.Smtp);
        fixture.Settle();
        Assert.Equal((ConnectionCheck.Passed, ConnectionCheck.NotRun), (account.ConnectionCheck, account.OutgoingCheck));
        await PassBothAsync(fixture);
        await account.ForgetPasswordAsync(MailProtocol.Smtp);
        fixture.Settle();
        Assert.Equal((ConnectionCheck.Passed, ConnectionCheck.NotRun), (account.ConnectionCheck, account.OutgoingCheck));

        // New outgoing details reset only the SMTP result; receiving stays ready.
        await account.SavePasswordAsync(Secret, MailProtocol.Smtp);
        fixture.Settle();
        await PassBothAsync(fixture);
        fixture.Field("SMTP port").Text = "465";
        fixture.Click(fixture.Button("Save account"));
        Assert.Equal((ConnectionCheck.Passed, ConnectionCheck.NotRun), (account.ConnectionCheck, account.OutgoingCheck));
        Assert.Equal((true, false), (account.HasPassword, account.HasSmtpPassword));
        Assert.Equal(AccountSetupStep.Ready, account.NextStep);

        // New incoming details reset only the IMAP result.
        await account.SavePasswordAsync(Secret, MailProtocol.Smtp);
        fixture.Settle();
        await PassBothAsync(fixture);
        fixture.Field("IMAP server (hostname only)").Text = "other.example.test";
        fixture.Click(fixture.Button("Save account"));
        Assert.Equal((ConnectionCheck.NotRun, ConnectionCheck.Passed), (account.ConnectionCheck, account.OutgoingCheck));
        Assert.Equal((false, true), (account.HasPassword, account.HasSmtpPassword));
        Assert.Equal("Done — Outgoing sign-in tested; no message was sent.", fixture.OutgoingLine);
    }

    [Fact]
    public async Task AddingOutgoingMailKeepsAPassedReceivingTest()
    {
        var profile = TestDirectory.Profile();
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        using var fixture = new Fixture(profile, credentials, new TestOutgoingTester());
        fixture.Click(fixture.Next);
        Assert.Equal(AccountSetupStep.Ready, fixture.Account.NextStep);

        fixture.Combo("Outgoing mail setup").SelectedIndex = 1;
        fixture.Field("SMTP server (hostname only)").Text = "smtp.example.test";
        fixture.Field("SMTP username").Text = "test";
        fixture.Click(fixture.Button("Save account"));
        Assert.NotNull(fixture.Account.Profile!.OutgoingServer);
        Assert.Equal(ConnectionCheck.Passed, fixture.Account.ConnectionCheck);
        Assert.Equal(AccountSetupStep.Ready, fixture.Account.NextStep);
        Assert.Equal(OutgoingSetupStep.SavePassword, fixture.Account.OutgoingStep);
        Assert.Equal(["Ready — this account can receive mail.", "Optional — Outgoing mail is set up; save its SMTP password to send messages."], fixture.StepLines);
    }

    private static async Task PassBothAsync(Fixture fixture)
    {
        await fixture.Account.TestConnectionAsync();
        fixture.Settle();
        await fixture.Account.TestOutgoingConnectionAsync();
        fixture.Settle();
        Assert.Equal((ConnectionCheck.Passed, ConnectionCheck.Passed), (fixture.Account.ConnectionCheck, fixture.Account.OutgoingCheck));
    }

    private static async Task<(AccountProfile, TestCredentialStore)> SavedAccountAsync()
    {
        var profile = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test", Security = TransportSecurity.StartTls } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Smtp), Secret);
        return (profile, credentials);
    }

    private static UiInputEvent Key(int code) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, KeyboardModifierState.None, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>The whole shell, so the footer, the tab and Escape take part, on a queued dispatcher.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly TestDirectory _directory = new();

        public Fixture(AccountProfile profile, TestCredentialStore credentials, TestOutgoingTester? tester, TestMailReceiver? receiver = null)
        {
            Dispatcher = new TestQueueDispatcher();
            receiver ??= new TestMailReceiver();
            Model = new MailShellViewModel(
                new(new JsonAccountStore(_directory.File("accounts.json")), credentials, receiver, Dispatcher, profile, null, tester),
                new(new JsonSettingsStore(_directory.File("settings.json")), Dispatcher, new(), null),
                new(receiver, Dispatcher), new ComposerViewModel(dispatcher: Dispatcher));
            Shell = new MailShellView(Model);
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host());
            Session.AddRoot(Shell.Window);
            Shell.Navigation.SelectTab("account");
            Keyboard = Shell.CreateKeyboardNavigation(Session);
            Settle();
        }

        public TestQueueDispatcher Dispatcher { get; }
        public MailShellViewModel Model { get; }
        public AccountProfileViewModel Account => Model.Account;
        public MailShellView Shell { get; }
        public UiSession Session { get; }
        public MailKeyboardNavigation Keyboard { get; }
        public UiElement Content => Shell.Navigation.Tabs.Single(tab => tab.Id == "account").Content!;
        private StandardPanel Checklist => Descendants(Content).OfType<FormSection>().First().Content;
        public StandardLabel Footer => (StandardLabel)Shell.Window.Children[0].Children[0];
        public StandardButton Next => Descendants(Content).OfType<StandardButton>().Single(button => button.Text.StartsWith("Next:", StringComparison.Ordinal) || button.Text == "Open Inbox");
        public StandardButton TestSmtp => Button("Test SMTP sign-in");
        public StandardEdit SmtpPassword => Field("SMTP password / app password");
        public string[] StepLines => Checklist.Children.OfType<StandardLabel>().Where(label => label.Visibility == UiVisibility.Visible).Select(label => label.Text).ToArray();
        public string OutgoingLine => Checklist.Children.OfType<StandardLabel>().ElementAt(3).Text;
        public StandardButton Button(string text) => Descendants(Content).OfType<StandardButton>().Single(button => button.Text == text);
        public StandardEdit Field(string label) => (StandardEdit)Descendants(Content).OfType<StandardLabel>().Single(item => item.Text == label).Target!;
        public StandardComboBox Combo(string label) => (StandardComboBox)Descendants(Content).OfType<StandardLabel>().Single(item => item.Text == label).Target!;
        public IEnumerable<string> AllText() => Descendants(Shell.Window).OfType<StandardLabel>().Select(label => label.Text).Append(Account.Status);

        public void Click(StandardButton button, bool settle = true)
        {
            button.Click();
            if (settle) Settle();
        }

        public void Settle()
        {
            Dispatcher.DrainUntil(() => !Account.IsBusy && Account.HasPassword is not null
                && (Account.Profile?.OutgoingServer is null || Account.HasSmtpPassword is not null));
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(1100, 720);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
