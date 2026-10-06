using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-06: the account form states each setup step and offers exactly one next action.</summary>
[Collection("UI theme")]
public sealed class AccountSetupTests
{
    private const string Secret = "synthetic-app-password";

    [Fact]
    public void FirstAccountIsGuidedFromDetailsToPasswordToConnectionTest()
    {
        using var fixture = new Fixture(profile: null);
        Assert.Equal(AccountSetupStep.SaveDetails, fixture.Model.NextStep);
        Assert.Equal("Next: save account details", fixture.Next.Text);
        Assert.StartsWith("Next — Enter your email address", fixture.StepLines[0]);

        fixture.Field("Display name").Text = "Reader";
        fixture.Field("Email address").Text = "reader@example.test";
        fixture.Field("IMAP server (hostname only)").Text = "imap.example.test";
        fixture.Field("Username").Text = "reader";
        fixture.Click(fixture.Next);
        Assert.NotNull(fixture.Model.Profile);
        Assert.Equal(AccountSetupStep.SavePassword, fixture.Model.NextStep);
        Assert.Equal("Next: enter password", fixture.Next.Text);

        // The step takes the user to the password field; it never types or reuses a secret itself.
        fixture.Click(fixture.Next);
        Assert.Same(fixture.ImapPassword, fixture.Session.FocusedElement);
        fixture.ImapPassword.Text = Secret;
        fixture.Click(fixture.Button("Save password"));
        Assert.Equal(AccountSetupStep.TestConnection, fixture.Model.NextStep);
        Assert.Equal("Next: test connection", fixture.Next.Text);

        fixture.Click(fixture.Next);
        Assert.Equal(AccountSetupStep.Ready, fixture.Model.NextStep);
        Assert.Equal("Open Inbox", fixture.Next.Text);
        Assert.StartsWith("Ready", fixture.StepLines[0]);
        Assert.DoesNotContain(fixture.AllText(), text => text.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoredPasswordIsDetectedWithoutExposingIt()
    {
        var credentials = new TestCredentialStore();
        var profile = TestDirectory.Profile();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        using var fixture = new Fixture(profile, credentials);
        Assert.Equal(true, fixture.Model.HasPassword);
        Assert.Equal(AccountSetupStep.TestConnection, fixture.Model.NextStep);
        Assert.Contains("Done — Password saved on this device.", fixture.StepLines);
        Assert.DoesNotContain(fixture.AllText(), text => text.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public void EditingASavedAccountShowsUnsavedChangesUntilSaved()
    {
        using var fixture = new Fixture(TestDirectory.Profile());
        Assert.False(fixture.Model.HasUnsavedChanges);
        fixture.Field("IMAP server (hostname only)").Text = "changed.example.test";
        Assert.True(fixture.Model.HasUnsavedChanges);
        Assert.Equal(AccountSetupStep.SaveDetails, fixture.Model.NextStep);
        Assert.StartsWith("Next — You have unsaved changes", fixture.StepLines[0]);
        fixture.Field("IMAP server (hostname only)").Text = "imap.example.test";
        Assert.False(fixture.Model.HasUnsavedChanges);
    }

    [Fact]
    public async Task FailedTestIsExplainedBesideTheStepAndCanceledTestProvesNothing()
    {
        var credentials = new TestCredentialStore();
        var profile = TestDirectory.Profile();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        var receiver = new TestMailReceiver { Test = _ => throw new MailConnectionException("The server rejected the sign-in.") };
        using var fixture = new Fixture(profile, credentials, receiver);

        fixture.Click(fixture.Next);
        Assert.Equal(ConnectionCheck.Failed, fixture.Model.ConnectionCheck);
        Assert.Contains(fixture.StepLines, line => line.StartsWith("Next — Connection test failed: The server rejected the sign-in.", StringComparison.Ordinal));
        Assert.Equal("Next: test connection", fixture.Next.Text);

        var pending = new TaskCompletionSource();
        receiver.Test = token => pending.Task.WaitAsync(token);
        fixture.Click(fixture.Next, settle: false);
        Assert.Equal(ConnectionCheck.Running, fixture.Model.ConnectionCheck);
        Assert.False(fixture.Next.IsEnabled);
        fixture.Model.CancelConnectionTest();
        fixture.Settle();
        Assert.Equal(ConnectionCheck.NotRun, fixture.Model.ConnectionCheck);
        Assert.Null(fixture.Model.ConnectionFailure);
    }

    [Fact]
    public async Task SavingNewServerDetailsStartsTheCredentialAndTestStepsOver()
    {
        var credentials = new TestCredentialStore();
        var profile = TestDirectory.Profile();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        using var fixture = new Fixture(profile, credentials);
        fixture.Click(fixture.Next);
        Assert.Equal(AccountSetupStep.Ready, fixture.Model.NextStep);

        fixture.Field("IMAP server (hostname only)").Text = "other.example.test";
        fixture.Click(fixture.Next);
        Assert.Equal(ConnectionCheck.NotRun, fixture.Model.ConnectionCheck);
        // The password was bound to the previous server, so it has to be saved again.
        Assert.Equal(false, fixture.Model.HasPassword);
        Assert.Equal(AccountSetupStep.SavePassword, fixture.Model.NextStep);
    }

    [Fact]
    public async Task ReadyAccountOpensTheInboxAndReceives()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore();
        var profile = TestDirectory.Profile();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), Secret);
        int receives = 0;
        var receiver = new TestMailReceiver { Inbox = (_, _) => { receives++; return Task.FromResult(new Broiler.Mail.Core.Messages.MailInboxPage([], null)); } };
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), credentials, receiver, dispatcher, profile, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher), new ComposerViewModel(dispatcher: dispatcher));
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());
        shell.Attach(session);
        shell.ShowView("account");
        dispatcher.DrainUntil(() => model.Account.HasPassword is not null);
        await model.Account.TestConnectionAsync();
        dispatcher.DrainUntil(() => !model.Account.IsBusy);
        var open = Descendants(shell.Window).OfType<StandardButton>().Single(button => button.Text == "Open Inbox");

        open.Click();
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        Assert.Equal("inbox", shell.ActiveViewId);
        Assert.Equal(1, receives);
    }

    [Fact]
    public void SetupHintsCanBeDismissedAndRestored()
    {
        using var fixture = new Fixture(profile: null);
        Assert.NotEmpty(fixture.StepLines);
        var dismissButton = fixture.Button("Dismiss hints");
        Assert.NotNull(dismissButton);

        fixture.Click(dismissButton);
        Assert.Empty(fixture.StepLines);
        Assert.Equal("Show hints", dismissButton.Text);

        fixture.Click(dismissButton);
        Assert.NotEmpty(fixture.StepLines);
        Assert.Equal("Dismiss hints", dismissButton.Text);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestDirectory _directory = new();
        private readonly FormSurface _form;

        public Fixture(AccountProfile? profile, TestCredentialStore? credentials = null, TestMailReceiver? receiver = null)
        {
            Dispatcher = new TestQueueDispatcher();
            Model = new AccountProfileViewModel(new JsonAccountStore(_directory.File("accounts.json")), credentials ?? new TestCredentialStore(),
                receiver ?? new TestMailReceiver(), Dispatcher, profile, null);
            _form = (FormSurface)new AccountProfileView(Model).CreateContent();
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host());
            Session.AddRoot(_form);
            Dispatcher.DrainUntil(() => Model.HasPassword is not null || Model.Profile is null);
            Session.RenderFrame();
        }

        public TestQueueDispatcher Dispatcher { get; }
        public AccountProfileViewModel Model { get; }
        public UiSession Session { get; }
        public StandardButton Next => Descendants(_form).OfType<StandardButton>().Single(button => button.Text.StartsWith("Next:", StringComparison.Ordinal) || button.Text == "Open Inbox");
        public StandardEdit ImapPassword => Field("Password / app password");
        public string[] StepLines => Descendants(_form).OfType<FormSection>().First().Content.Children.OfType<StandardLabel>()
            .Where(label => label.Visibility == UiVisibility.Visible).Select(label => label.Text).ToArray();
        public StandardEdit Field(string label) => (StandardEdit)Descendants(_form).OfType<StandardLabel>().Single(item => item.Text == label).Target!;
        public StandardButton Button(string text) => Descendants(_form).OfType<StandardButton>().Single(button => button.Text == text);
        public IEnumerable<string> AllText() => Descendants(_form).OfType<StandardLabel>().Select(label => label.Text).Append(Model.Status);

        public void Click(StandardButton button, bool settle = true)
        {
            button.Click();
            if (settle) Settle();
        }

        public void Settle()
        {
            Dispatcher.DrainUntil(() => !Model.IsBusy && Model.HasPassword is not null);
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            _form.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(1000, 800);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
