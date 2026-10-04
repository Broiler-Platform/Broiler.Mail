using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// UI-08: what the user is told and where focus goes. Routine work (typing, autosave, moving through
/// messages) is silent; progress, outcomes, and problems are announced once each; success
/// confirmations go away; results arriving later never take focus or the tab from the user.
/// </summary>
[Collection("UI theme")]
public sealed class FeedbackPolicyTests
{
    [Fact]
    public void TypingWithAutosaveAnnouncesNothing()
    {
        using var fixture = Fixture.Open();
        fixture.Model.Composer.StartNew();
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        fixture.Announced.Clear();

        string text = "";
        foreach (char c in "Dear team, the draft is ready.\nRegards")
        {
            text += c;
            fixture.ComposerBody.SetPlainText(text);
            fixture.Settle();
        }

        Assert.Equal(text, fixture.Model.Composer.PlainText);
        Assert.Empty(fixture.Announced);
    }

    [Fact]
    public async Task ReceivingAnnouncesProgressAndTheResultAndMovingThroughMessagesIsSilent()
    {
        using var fixture = Fixture.Open();
        await fixture.ReceiveAsync();
        Assert.Equal(["Progress: Receiving newest messages…", "2 messages loaded."], fixture.Announced);

        fixture.Announced.Clear();
        foreach (var message in fixture.Model.Inbox.Messages)
        {
            await fixture.Model.Inbox.SelectAsync(message.Key);
            fixture.Settle();
        }
        Assert.Empty(fixture.Announced);

        fixture.Receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await fixture.ReceiveAsync();
        Assert.Equal("Progress: Receiving newest messages…", fixture.Announced[0]);
        Assert.StartsWith("Error: The server did not respond.", Assert.Single(fixture.Announced.Skip(1)));
    }

    [Fact]
    public async Task SendingAnnouncesEachOutcomeOnceWithoutARepeatedBusyLine()
    {
        using var fixture = Fixture.Open(SubmissionStatus.Rejected);
        fixture.Model.Composer.StartNew();
        fixture.Model.Composer.Edit("to@example.test", "", "", "Plans", "Body");
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        fixture.Announced.Clear();

        await fixture.SendAsync();
        Assert.Equal(["Progress: Sending… Do not close the app.",
            "Error: Sending failed before acceptance. The draft is retained; retry only when ready."], fixture.Announced);

        fixture.Announced.Clear();
        fixture.Sender.Result = SubmissionStatus.Accepted;
        await fixture.SendAsync();
        Assert.Equal(["Progress: Sending… Do not close the app.",
            "Success: Accepted by the server. Delivery is not guaranteed. This draft will not be sent again.",
            "Information: Sent copy: not configured; no app copy was attempted."], fixture.Announced);
        // An accepted draft cannot change, so it offers no check.
        Assert.False(fixture.Button("Check draft").IsEnabled);
    }

    [Fact]
    public async Task AServerReasonForARejectionIsAnnouncedBesideTheOutcome()
    {
        using var fixture = Fixture.Open(SubmissionStatus.Rejected);
        fixture.Sender.Message = "550 Mailbox unavailable.";
        fixture.Model.Composer.StartNew();
        fixture.Model.Composer.Edit("to@example.test", "", "", "Plans", "Body");
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        fixture.Announced.Clear();

        await fixture.SendAsync();
        Assert.Equal(["Progress: Sending… Do not close the app.",
            "Error: Sending failed before acceptance. The draft is retained; retry only when ready.",
            "Error: 550 Mailbox unavailable."], fixture.Announced);
    }

    [Fact]
    public async Task SaveConfirmationsGoAwayButFailuresStay()
    {
        using var fixture = Fixture.Open();
        var settings = fixture.Model.Settings;
        fixture.Shell.Navigation.SelectTab("settings");
        fixture.Settle();
        fixture.Announced.Clear();

        await settings.SaveAsync();
        fixture.Settle();
        Assert.Equal(FeedbackKind.Success, settings.StatusKind);
        Assert.Equal(["Progress: Saving…", "Success: " + settings.Status], fixture.Announced);
        Assert.Equal(settings.Status, fixture.SettingsFeedback.Message);

        // A second save restarts the time the confirmation stays.
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Settle();
        await settings.SaveAsync();
        fixture.Settle();
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Settle();
        Assert.Equal(FeedbackKind.Success, settings.StatusKind);

        fixture.Announced.Clear();
        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime);
        fixture.Settle();
        Assert.Equal("", settings.Status);
        Assert.Equal("", fixture.SettingsFeedback.Message);
        Assert.Equal(UiVisibility.Collapsed, fixture.SettingsFeedback.Visibility);
        // Going away is not announced, and the footer returns to its hint.
        Assert.Empty(fixture.Announced);
        Assert.StartsWith("Saved appearance and inbox spacing apply immediately.", fixture.Footer.Text);

        settings.WindowWidth = "wide";
        await settings.SaveAsync();
        fixture.Settle();
        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime * 2);
        fixture.Settle();
        Assert.Equal(FeedbackKind.Error, settings.StatusKind);
        Assert.Contains("Window width must be a whole number.", fixture.SettingsFeedback.Message);
        // The footer names the problem and points to it instead of repeating it.
        Assert.Equal("Not saved. Details are below the buttons.", fixture.Footer.Text);
    }

    [Fact]
    public void TheFooterShowsAStatusWithAnAmpersandAsWritten()
    {
        // An address may contain '&'. The footer is a literal label: no doubled text, no access key.
        using var fixture = Fixture.Open(emailAddress: "r&d@example.test");
        string status = fixture.Model.Inbox.Status;
        Assert.Contains("r&d@example.test", status);

        Assert.Equal(status, fixture.Footer.Text);
        Assert.Equal(status, fixture.Footer.DisplayText);
        Assert.Equal(status, fixture.Footer.GetSemanticNode().Name);
        Assert.Null(fixture.Footer.EffectiveAccessKey);
    }

    [Fact]
    public async Task AResultArrivingAfterTheUserMovedOnDoesNotTakeFocusOrTheTab()
    {
        using var fixture = Fixture.Open();
        fixture.Model.Composer.StartNew();
        var settings = fixture.Model.Settings;
        fixture.Shell.Navigation.SelectTab("settings");
        fixture.Settle();
        settings.WindowWidth = "wide";

        // The save fails validation only after the user has moved to the composer and started typing.
        var saving = settings.SaveAsync();
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Session.SetFocus(fixture.ComposerBody);
        fixture.Settle();
        await saving;
        fixture.Settle();
        await fixture.ReceiveAsync();

        Assert.Equal("WindowWidth", settings.ValidationField);
        Assert.Equal("compose", fixture.Shell.Navigation.SelectedTab?.Id);
        Assert.Same(fixture.ComposerBody, fixture.Session.FocusedElement);

        // On the form itself, the same failure takes focus to the field it concerns, from the Save
        // button or from the tab strip showing the form.
        fixture.Shell.Navigation.SelectTab("settings");
        var save = Descendants(fixture.Shell.Navigation.Tabs.Single(tab => tab.Id == "settings").Content!).OfType<StandardButton>().Single(button => button.Text == "Save settings");
        foreach (var start in new UiElement[] { save, fixture.Shell.Navigation })
        {
            fixture.Session.SetFocus(start);
            await settings.SaveAsync();
            fixture.Settle();
            Assert.IsType<StandardEdit>(fixture.Session.FocusedElement);
        }
    }

    [Fact]
    public void CollapsingCopiesWithFocusInsideMovesFocusToTheirToggle()
    {
        using var fixture = Fixture.Open();
        fixture.Model.Composer.StartNew();
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        var copies = Descendants(fixture.Shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!).OfType<FormSection>().Single(section => section.Toggle is not null);
        copies.Toggle!.Click();
        fixture.Settle();
        var cc = Descendants(copies.Content).OfType<StandardEdit>().First();
        fixture.Session.SetFocus(cc);

        copies.Toggle.Click();
        fixture.Settle();
        Assert.False(copies.IsExpanded);
        Assert.Same(copies.Toggle, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task AReadyAccountFooterSaysSoOnceTheConfirmationGoes()
    {
        using var fixture = Fixture.Open();
        var account = fixture.Model.Account;
        fixture.Shell.Navigation.SelectTab("account");
        await account.SavePasswordAsync("app password");
        fixture.Settle();
        await account.TestConnectionAsync();
        fixture.Settle();
        Assert.Equal(AccountSetupStep.Ready, account.NextStep);
        Assert.Equal(account.Status, fixture.Footer.Text);

        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime);
        fixture.Settle();
        Assert.Equal("This account is ready to receive mail.", fixture.Footer.Text);
    }

    [Fact]
    public void AFormsFeedbackIsATabStopOnlyWhileItScrollsAndIsThenNamed()
    {
        // Short feedback: Tab goes from the last action back to the tabs, not into the feedback area.
        using var fixture = Fixture.Open();
        fixture.Model.Composer.StartNew();
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        var stops = MailKeyboardNavigation.TabStops(fixture.Shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!);
        Assert.IsType<StandardButton>(stops[^1]);

        // Feedback taller than its area scrolls, so the keyboard needs a stop there, with a name to announce.
        var dispatcher = new TestQueueDispatcher();
        var settings = new SettingsViewModel(new FailingSettingsStore(), dispatcher, new(), null);
        var content = new SettingsView(settings).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new SmallHost());
        session.AddRoot(content);
        _ = settings.SaveAsync();
        dispatcher.DrainUntil(() => !settings.IsBusy);
        session.RenderFrame();
        session.RenderFrame();
        Assert.Equal(FeedbackKind.Error, settings.StatusKind);
        var last = MailKeyboardNavigation.TabStops(content)[^1];
        Assert.IsType<Broiler.UI.ScrollView.Standard.StandardScrollView>(last);
        Assert.Equal("Status and errors", last.GetSemanticNode().Name);
        content.Dispose();
    }

    private sealed class FailingSettingsStore : ISettingsStore
    {
        public Task<Broiler.Mail.Core.Settings.ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new Broiler.Mail.Core.Settings.ApplicationSettings());
        public Task SaveAsync(Broiler.Mail.Core.Settings.ApplicationSettings settings, CancellationToken cancellationToken = default) =>
            throw new IOException(string.Join(" ", Enumerable.Repeat("The settings file could not be written because the folder is read-only.", 8)));
    }

    private sealed class SmallHost : IUiHost
    {
        public BSize ViewportSize => new(640, 480);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestDirectory _directory;

        private Fixture(TestDirectory directory, TestQueueDispatcher dispatcher, TestMailReceiver receiver, Sender sender,
            ManualClock clock, MailShellViewModel model, MailShellView shell, UiSession session)
        {
            _directory = directory; Dispatcher = dispatcher; Receiver = receiver; Sender = sender; Clock = clock;
            Model = model; Shell = shell; Session = session;
            session.SemanticChanged += (_, e) =>
            {
                if (e.Change == UiSemanticChangeKind.StatusAnnounced) Announced.Add(e.Message ?? "");
            };
        }

        public List<string> Announced { get; } = [];
        public TestQueueDispatcher Dispatcher { get; }
        public TestMailReceiver Receiver { get; }
        public Sender Sender { get; }
        public ManualClock Clock { get; }
        public MailShellViewModel Model { get; }
        public MailShellView Shell { get; }
        public UiSession Session { get; }
        private UiElement Tab(string id) => Shell.Navigation.Tabs.Single(tab => tab.Id == id).Content!;
        public StandardRichEdit ComposerBody => Descendants(Tab("compose")).OfType<StandardRichEdit>().Single();
        public StandardButton Button(string text) => Descendants(Tab("compose")).OfType<StandardButton>().Single(button => button.Text == text);
        public InlineFeedback SettingsFeedback => Descendants(Tab("settings")).OfType<InlineFeedback>().Single();
        public StandardLabel Footer => (StandardLabel)Shell.Window.Children[0].Children[0];

        public static Fixture Open(SubmissionStatus sendResult = SubmissionStatus.Accepted, string emailAddress = "test@example.test")
        {
            var directory = new TestDirectory();
            var account = TestDirectory.Profile() with
            {
                EmailAddress = emailAddress, OutgoingServer = new() { Host = "smtp.example.test", Port = 465, UserName = "test" },
            };
            var messages = new[] { 2u, 1u }.Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 7, uid), Sender = "author@example.test", Subject = $"Message {uid}",
            }).ToArray();
            var receiver = new TestMailReceiver { Inbox = (_, _) => Task.FromResult(new MailInboxPage(messages, null)) };
            var dispatcher = new TestQueueDispatcher();
            var clock = new ManualClock();
            var sender = new Sender { Result = sendResult };
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null) { Clock = clock },
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null) { Clock = clock },
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher, sender: sender));
            var shell = new MailShellView(model);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());
            session.AddRoot(shell.Window);
            shell.Navigation.SelectTab("inbox");
            session.RenderFrame();
            return new Fixture(directory, dispatcher, receiver, sender, clock, model, shell, session);
        }

        /// <summary>Sending completes on the UI dispatcher, so it is drained while the send runs.</summary>
        public async Task SendAsync()
        {
            var sending = Model.Composer.SendAsync();
            Settle();
            await sending;
            Settle();
        }

        public async Task ReceiveAsync()
        {
            await Model.Inbox.ReceiveAsync();
            Settle();
        }

        public void Settle()
        {
            Dispatcher.DrainUntil(() => !Model.Inbox.IsBusy && !Model.Composer.IsBusy && !Model.Settings.IsBusy && !Model.Account.IsBusy);
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class Sender : IMailSender
    {
        public SubmissionStatus Result { get; set; }
        public string? Message { get; set; }
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SendResult(Result, Message));
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
