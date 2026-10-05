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
using Broiler.UI.ListView.Standard;
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

    /// <summary>
    /// The page that reaches the session limit is announced with its count, and then the notice that
    /// explains why Load older is now unavailable, once. Moving through the messages afterwards is
    /// silent: that notice stays as it is.
    /// </summary>
    [Fact]
    public async Task ReachingTheSessionLimitIsAnnouncedOnceAfterTheCount()
    {
        using var fixture = Fixture.Open();
        var inbox = fixture.Model.Inbox;
        var account = fixture.Model.Account.Profile!;
        const int total = InboxViewModel.MaximumLoadedMessages + InboxViewModel.PageSize;
        fixture.Receiver.Inbox = (cursor, _) =>
        {
            int end = cursor?.NextIndex ?? total - 1;
            int start = end - InboxViewModel.PageSize + 1;
            var page = Enumerable.Range(start + 1, InboxViewModel.PageSize).Reverse().Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "author@example.test", Subject = $"Message {uid}",
            }).ToArray();
            return Task.FromResult(new MailInboxPage(page, start == 0 ? null : new(account.Id, 7, total + 1, total, start - 1)));
        };
        await fixture.ReceiveAsync();
        while (inbox.Messages.Count < InboxViewModel.MaximumLoadedMessages - InboxViewModel.PageSize)
        {
            await inbox.LoadOlderAsync();
            fixture.Settle();
        }

        fixture.Announced.Clear();
        await inbox.LoadOlderAsync();
        fixture.Settle();
        Assert.False(inbox.CanLoadOlder);
        Assert.Equal(["Progress: Loading older messages…", "500 messages loaded.",
            "Information: Session limit reached (500 messages): older messages cannot be loaded now. Receive mail to start again from the newest page."], fixture.Announced);

        fixture.Announced.Clear();
        foreach (var message in inbox.Messages.Take(5))
        {
            await inbox.SelectAsync(message.Key);
            fixture.Settle();
        }
        Assert.Equal(inbox.Messages[4].Key, inbox.Body?.Key);
        Assert.Empty(fixture.Announced);
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
    public async Task AnSmtpTestAnnouncesProgressAndTheResultOnceAndNeverTakesFocus()
    {
        var tester = new TestOutgoingTester();
        using var fixture = Fixture.Open(outgoing: tester);
        var account = fixture.Model.Account;
        fixture.Shell.Navigation.SelectTab("account");
        fixture.Settle();
        fixture.Announced.Clear();

        await account.TestOutgoingConnectionAsync();
        fixture.Settle();
        // Progress and the result once each; the checklist line that changes with them is silent.
        Assert.Equal(["Progress: Signing in to the SMTP server… No message is sent.",
            "Success: The SMTP server accepted the sign-in over an encrypted connection. No message was sent."], fixture.Announced);
        Assert.Equal(account.Status, fixture.Footer.Text);

        // The confirmation goes away unannounced; the checklist keeps the result.
        fixture.Announced.Clear();
        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime);
        fixture.Settle();
        Assert.Equal("", account.Status);
        Assert.Empty(fixture.Announced);
        Assert.Equal(ConnectionCheck.Passed, account.OutgoingCheck);

        // A failure that arrives after the user moved on keeps their tab and focus.
        var pending = new TaskCompletionSource();
        tester.Test = async token =>
        {
            await pending.Task.WaitAsync(token);
            throw new MailConnectionException("The SMTP server rejected the sign-in.", MailConnectionFailure.AuthenticationRejected);
        };
        fixture.Model.Composer.StartNew();
        var testing = account.TestOutgoingConnectionAsync();
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Session.SetFocus(fixture.ComposerBody);
        fixture.Dispatcher.Drain();
        fixture.Session.RenderFrame();
        fixture.Announced.Clear();
        pending.SetResult();
        await testing;
        fixture.Settle();
        Assert.Equal("compose", fixture.Shell.Navigation.SelectedTab?.Id);
        Assert.Same(fixture.ComposerBody, fixture.Session.FocusedElement);
        Assert.Equal(["Error: SMTP sign-in test failed: The SMTP server rejected the sign-in."], fixture.Announced);

        // The footer names the problem and points to the details instead of repeating them.
        fixture.Shell.Navigation.SelectTab("account");
        fixture.Settle();
        Assert.Equal("SMTP sign-in test failed. Details are below the buttons.", fixture.Footer.Text);
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

    [Fact]
    public void ADraftCheckConfirmationIsAnnouncedOnceAndGoesAwayQuietly()
    {
        using var fixture = Fixture.Open();
        var composer = fixture.Model.Composer;
        composer.StartNew();
        composer.Edit("to@example.test", "", "", "Plans", "Body");
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        fixture.Announced.Clear();

        fixture.Button("Check draft").Click();
        fixture.Settle();
        const string passed = "Draft fields are valid. No mail was sent.";
        Assert.Equal(["Success: " + passed], fixture.Announced);
        Assert.Equal(passed, fixture.ComposerStatus.Message);

        // A second check restarts the time the confirmation stays.
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Settle();
        fixture.Button("Check draft").Click();
        fixture.Settle();
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Settle();
        Assert.Equal(FeedbackKind.Success, composer.StatusKind);
        Assert.Equal(passed, fixture.ComposerStatus.Message);

        fixture.Announced.Clear();
        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime);
        fixture.Settle();
        Assert.Equal("", composer.Status);
        Assert.Equal("", fixture.ComposerStatus.Message);
        Assert.Equal(UiVisibility.Collapsed, fixture.ComposerStatus.Visibility);
        // Going away is not announced; the footer returns to the draft's storage state.
        Assert.Empty(fixture.Announced);
        Assert.Equal(composer.StorageStatus, fixture.Footer.Text);
    }

    [Fact]
    public void AFailedDraftCheckStaysAndAnEarlierConfirmationNeverClearsANewerStatus()
    {
        using var fixture = Fixture.Open();
        var composer = fixture.Model.Composer;
        composer.StartNew();
        composer.Edit("team.example.test", "", "", "Plans", "Body");
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();

        fixture.Button("Check draft").Click();
        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime * 2);
        fixture.Settle();
        Assert.Equal(FeedbackKind.Error, composer.StatusKind);
        Assert.Equal("Enter valid email addresses separated by commas.", fixture.ComposerStatus.Message);

        // A passed check is replaced by the retention notice before its time is up; the notice stays.
        composer.Edit("to@example.test", "", "", "Plans", "Body");
        fixture.Button("Check draft").Click();
        fixture.Settle();
        composer.SetAccount(fixture.Model.Account.Profile! with { DisplayName = "Renamed" });
        fixture.Settle();
        string retained = composer.Status;
        Assert.Contains("retained", retained, StringComparison.Ordinal);
        fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime);
        fixture.Settle();
        Assert.Equal(retained, composer.Status);
    }

    private const string Wide = "1100x720";
    // Compact inbox, and forms that scroll.
    private const string SmallLargeText = "640x480 at 200 % text";

    public static TheoryData<string, string> Outcomes()
    {
        var data = new TheoryData<string, string>();
        foreach (string layout in new[] { Wide, SmallLargeText })
            foreach (string outcome in new[]
            {
                "settings validation", "account validation in a collapsed section", "account section collapsed",
                "composer copies collapsed", "composer check failed", "composer check failed in collapsed Cc", "composer check failed without a field",
                "account test canceled", "account SMTP test found no password",
                "inbox receive canceled", "inbox retry succeeded", "inbox load older reached the last page",
                "inbox load older reached the last page in the reader",
                "composer draft discarded", "composer send accepted", "background results while typing",
            })
                data.Add(outcome, layout);
        return data;
    }

    /// <summary>
    /// After validation, cancel, retry, disclosure collapse, and a command that disables itself, on
    /// every surface, focus is on a control that can take it, in the tab the user is on, scrolled
    /// into view, and Tab continues from there instead of restarting at the tabs. Later results
    /// never move it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task FocusLandsOnAUsableControlAfterEveryOutcome(string outcome, string layout)
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(layout == Wide ? 1 : 2));
        try { await FocusLandsOnAUsableControl(outcome, layout); }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    private static async Task FocusLandsOnAUsableControl(string outcome, string layout)
    {
        // Only the SMTP outcome offers the SMTP test; the account form of the others stays as it was.
        var outgoing = outcome == "account SMTP test found no password" ? new TestOutgoingTester() : null;
        using var fixture = layout == Wide ? Fixture.Open(outgoing: outgoing) : Fixture.Open(outgoing: outgoing, width: 640, height: 480);
        var model = fixture.Model;
        var session = fixture.Session;
        void Show(string id) { fixture.Shell.Navigation.SelectTab(id); fixture.Settle(); }
        void Press(StandardButton button) { session.SetFocus(button); button.Click(); fixture.Settle(); }
        // Tab there, as a user would, which also scrolls the form to it.
        void TabTo(UiElement target)
        {
            var navigation = fixture.Shell.CreateKeyboardNavigation(session);
            for (int step = 0; step < 40 && session.FocusedElement != target; step++) navigation.MoveFocus(1);
            Assert.Same(target, session.FocusedElement);
        }
        FormSection SentCopy() => Descendants(fixture.Tab("account")).OfType<FormSection>().Single(section => section.Toggle is not null);
        FormSection Copies() => Descendants(fixture.Tab("compose")).OfType<FormSection>().Single(section => section.Toggle is not null);
        void AppendSentCopies()
        {
            // Chosen in the form, as a user would; the folder field is then required.
            var handling = Descendants(SentCopy()).OfType<Broiler.UI.ComboBox.Standard.StandardComboBox>().Single();
            handling.SelectedIndex = (int)SentCopyMode.AppendToFolder;
            fixture.Settle();
        }

        string tab;
        UiElement expected;
        // Where focus moved, the control is entirely in view.
        bool entirelyInView = true;
        switch (outcome)
        {
            case "settings validation":
                Show(tab = "settings");
                var width = fixture.Field(tab, "Initial window width");
                width.Text = model.Settings.WindowWidth = "wide";
                Press(fixture.Button("Save settings", tab));
                expected = width;
                break;
            case "account validation in a collapsed section":
                Show(tab = "account");
                AppendSentCopies();
                SentCopy().IsExpanded = false;
                fixture.Settle();
                Press(fixture.Button("Save account", tab));
                Assert.True(SentCopy().IsExpanded);
                expected = fixture.Field(tab, "Sent folder path");
                break;
            case "account section collapsed":
                Show(tab = "account");
                AppendSentCopies();
                SentCopy().IsExpanded = true;
                fixture.Settle();
                TabTo(fixture.Field(tab, "Sent folder path"));
                SentCopy().Toggle!.Click();
                fixture.Settle();
                expected = SentCopy().Toggle!;
                break;
            case "composer copies collapsed":
                model.Composer.StartNew();
                Show(tab = "compose");
                Copies().Toggle!.Click();
                fixture.Settle();
                TabTo(Descendants(Copies().Content).OfType<StandardEdit>().First());
                Copies().Toggle!.Click();
                fixture.Settle();
                expected = Copies().Toggle!;
                break;
            case "composer check failed":
                // The error names To, so To takes focus, as a refused account or settings field does.
                model.Composer.StartNew();
                model.Composer.Edit("team.example.test", "", "", "Plans", "Body");
                Show(tab = "compose");
                Press(fixture.Button("Check draft"));
                Assert.Equal(FeedbackKind.Error, model.Composer.StatusKind);
                expected = fixture.Field(tab, "To");
                break;
            case "composer check failed in collapsed Cc":
                model.Composer.StartNew();
                model.Composer.Edit("to@example.test", "copy.example.test", "", "Plans", "Body");
                Show(tab = "compose");
                Assert.False(Copies().IsExpanded);
                Press(fixture.Button("Check draft"));
                Assert.True(Copies().IsExpanded);
                expected = fixture.Field(tab, "Cc");
                break;
            case "composer check failed without a field":
                // The error is about the subject, which names no recipient field: focus stays on the command and
                // the error is announced.
                model.Composer.StartNew();
                model.Composer.Edit("to@example.test", "", "", new string('x', MailComposition.MaximumSubjectLength + 1), "Body");
                Show(tab = "compose");
                expected = fixture.Button("Check draft");
                Press((StandardButton)expected);
                Assert.Equal(FeedbackKind.Error, model.Composer.StatusKind);
                Assert.Null(model.Composer.InvalidField);
                break;
            case "account test canceled":
                fixture.Receiver.Test = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
                Show(tab = "account");
                _ = model.Account.TestConnectionAsync();
                session.RenderFrame();
                Press(fixture.Button("Cancel test", tab));
                expected = fixture.Button("Test connection", tab);
                break;
            case "account SMTP test found no password":
                // A password was saved, but the tester finds none for these server details. The test
                // then stays disabled until one is saved, so focus moves on to the SMTP password.
                Show(tab = "account");
                await model.Account.SavePasswordAsync("synthetic-app-password", MailProtocol.Smtp);
                fixture.Settle();
                outgoing!.Test = _ => throw new MailConnectionException("No SMTP password is saved for these server details.", MailConnectionFailure.MissingPassword);
                Press(fixture.Button("Test SMTP sign-in", tab));
                Assert.Equal(false, model.Account.HasSmtpPassword);
                Assert.False(fixture.Button("Test SMTP sign-in", tab).IsEnabled);
                expected = fixture.Field(tab, "SMTP password");
                break;
            case "inbox receive canceled":
                fixture.Receiver.Inbox = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null!; };
                tab = "inbox";
                _ = model.Inbox.ReceiveAsync();
                session.RenderFrame();
                Press(fixture.Button("Cancel", tab));
                expected = fixture.Button("Receive mail", tab);
                break;
            case "inbox retry succeeded":
            {
                tab = "inbox";
                var pages = fixture.Receiver.Inbox;
                fixture.Receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
                await fixture.ReceiveAsync();
                fixture.Receiver.Inbox = pages;
                Press(fixture.Button("Retry receiving", tab));
                expected = Descendants(fixture.Tab(tab)).OfType<StandardListView>().Single();
                break;
            }
            case "inbox load older reached the last page":
                tab = "inbox";
                fixture.PageOlderMessages();
                await fixture.ReceiveAsync();
                Press(fixture.Button("Load older", tab));
                Assert.False(model.Inbox.CanLoadOlder);
                expected = Descendants(fixture.Tab(tab)).OfType<StandardListView>().Single();
                break;
            case "inbox load older reached the last page in the reader":
            {
                tab = "inbox";
                fixture.PageOlderMessages();
                await fixture.ReceiveAsync();
                await model.Inbox.SelectAsync(model.Inbox.Messages[0].Key);
                fixture.Settle();
                Assert.True(fixture.Shell.Inbox.OpenSelected());
                fixture.Settle();
                var inbox = Descendants(fixture.Tab(tab)).OfType<AdaptiveInboxLayout>().Single();
                Assert.Equal(layout != Wide, inbox.ShowsReaderOnly);
                Press(fixture.Button("Load older", tab));
                Assert.False(model.Inbox.CanLoadOlder);
                // The list it extended, or while the reader is shown alone (the list pane hidden),
                // Back to inbox, which leads there.
                expected = inbox.ShowsReaderOnly ? fixture.Button("Back to inbox", tab) : Descendants(fixture.Tab(tab)).OfType<StandardListView>().Single();
                break;
            }
            case "composer draft discarded":
            {
                model.Composer.StartNew();
                Show(tab = "compose");
                // Tab to the body as when writing; at a small size the form scrolls, and New message,
                // which returns above the fields after the discard, starts out of view.
                TabTo(fixture.ComposerBody);
                var form = Descendants(fixture.Tab(tab)).OfType<FormSurface>().Single().Content.Scroll;
                Assert.Equal(layout != Wide, form.VerticalOffset > 0);
                Press(fixture.Button("Discard draft"));
                Assert.False(model.Composer.HasDraft);
                expected = fixture.Button("New message");
                break;
            }
            case "composer send accepted":
                model.Composer.StartNew();
                model.Composer.Edit("to@example.test", "", "", "Plans", "Body");
                Show(tab = "compose");
                Press(fixture.Button("Send"));
                Assert.Equal(DraftSubmissionState.Accepted, model.Composer.SubmissionState);
                // Check draft no longer applies, so the next enabled action takes focus.
                expected = fixture.Button("Save draft");
                break;
            case "background results while typing":
                model.Composer.StartNew();
                model.Composer.Edit("to@example.test", "", "", "Plans", "Body");
                Show(tab = "compose");
                expected = fixture.ComposerBody;
                fixture.Field(tab, "To").Text = "to@example.test";
                TabTo(expected);
                fixture.ComposerBody.SetPlainText("Still typing");
                fixture.Settle();
                // Checked, then left alone while other work finishes: the confirmation goes away by
                // itself, not because of an edit.
                fixture.Button("Check draft").Click();
                fixture.Settle();
                Assert.Equal(FeedbackKind.Success, model.Composer.StatusKind);
                model.Settings.WindowWidth = "wide";
                await model.Settings.SaveAsync();
                await model.Inbox.ReceiveAsync();
                fixture.Clock.Advance(SaveViewModel.SuccessDisplayTime);
                fixture.Settle();
                Assert.Equal("WindowWidth", model.Settings.ValidationField);
                Assert.Equal("", model.Composer.Status);
                Assert.Equal("Still typing", model.Composer.PlainText);
                // Nothing moved focus here. Lines shown below the form's buttons shorten its view, so
                // at a small size part of the body can go out of view (the form surface's layout).
                entirelyInView = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        Assert.Equal(tab, fixture.Shell.Navigation.SelectedTab?.Id);
        Assert.Same(expected, session.FocusedElement);
        Assert.True(expected.CanFocus);
        AssertScrolledIntoView(session, expected, entirelyInView);
        var stops = MailKeyboardNavigation.TabStops(fixture.Tab(tab)).ToList();
        int index = stops.IndexOf(expected);
        Assert.True(index >= 0, "The focused control is not a tab stop of its tab.");
        fixture.Shell.CreateKeyboardNavigation(session).MoveFocus(1);
        Assert.Same(index + 1 < stops.Count ? stops[index + 1] : fixture.Shell.Navigation, session.FocusedElement);
    }

    /// <summary>
    /// Within every scroll view around it, the control is entirely in view, or for one taller than
    /// the view (such as the body editor), or when <paramref name="entirely"/> is false, at least
    /// part of it is.
    /// </summary>
    private static void AssertScrolledIntoView(UiSession session, UiElement element, bool entirely)
    {
        session.RenderFrame();
        BRect bounds = element.Bounds;
        Assert.True(bounds.Width > 0 && bounds.Height > 0, $"The focused control has no area: {bounds}.");
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not Broiler.UI.ScrollView.Standard.StandardScrollView scroll) continue;
            BRect view = scroll.ContentBounds;
            bool shown = entirely && bounds.Height <= view.Height + 0.5
                ? bounds.Top >= view.Top - 0.5 && bounds.Bottom <= view.Bottom + 0.5
                : bounds.Top < view.Bottom && bounds.Bottom > view.Top;
            Assert.True(shown, $"The focused control at {bounds} is outside its scroll view's visible area {view}.");
        }
    }

    [Fact]
    public async Task FocusStaysOnACommandWhileItRunsAndTabContinuesAfterIt()
    {
        using var fixture = Fixture.Open();
        fixture.PageOlderMessages();
        await fixture.ReceiveAsync();
        var older = fixture.Button("Load older", "inbox");
        var pending = new TaskCompletionSource<MailInboxPage>();
        var pages = fixture.Receiver.Inbox;
        fixture.Receiver.Inbox = (cursor, token) => cursor is null ? pages(cursor, token) : pending.Task.WaitAsync(token);
        fixture.Session.SetFocus(older);
        older.Click();
        fixture.Session.RenderFrame();

        Assert.True(fixture.Model.Inbox.IsLoadingList);
        Assert.False(older.CanFocus);
        Assert.Same(older, fixture.Session.FocusedElement);
        // Read message is unavailable while loading, so Tab reaches Cancel rather than the tabs.
        fixture.Shell.CreateKeyboardNavigation(fixture.Session).MoveFocus(1);
        Assert.Equal("Cancel", Assert.IsType<StandardButton>(fixture.Session.FocusedElement).Text);
        fixture.Model.Inbox.Cancel();
    }

    [Fact]
    public async Task ALoadOlderProblemNamesTheOlderPageAndItsRetryLoadsThatPage()
    {
        using var fixture = Fixture.Open();
        var inbox = fixture.Model.Inbox;
        fixture.PageOlderMessages();
        await fixture.ReceiveAsync();
        var pages = fixture.Receiver.Inbox;
        fixture.Receiver.Inbox = (cursor, token) => cursor is null ? pages(cursor, token) : throw new MailConnectionException("The server did not respond.");
        await inbox.LoadOlderAsync();
        fixture.Settle();

        Assert.True(inbox.ProblemIsOlderPage);
        Assert.Equal("Older messages could not be loaded. Details and Retry are above the list.", fixture.Footer.Text);
        var retry = fixture.Button("Retry loading older", "inbox");
        Assert.True(retry.IsEnabled);
        fixture.Receiver.Inbox = pages;
        retry.Click();
        fixture.Settle();
        Assert.Equal(4, inbox.Messages.Count);
        Assert.False(inbox.CanLoadOlder);

        // Canceled, it says which page it was.
        await fixture.ReceiveAsync();
        var pending = new TaskCompletionSource<MailInboxPage>();
        fixture.Receiver.Inbox = (cursor, token) => cursor is null ? pages(cursor, token) : pending.Task.WaitAsync(token);
        _ = inbox.LoadOlderAsync();
        inbox.Cancel();
        fixture.Settle();
        Assert.Equal("Loading older messages was canceled.", inbox.Problem);
        Assert.True(fixture.Button("Retry loading older", "inbox").IsEnabled);

        // Receiving the newest messages keeps its own wording.
        fixture.Receiver.Inbox = (_, _) => throw new MailConnectionException("The server did not respond.");
        await fixture.ReceiveAsync();
        Assert.False(inbox.ProblemIsOlderPage);
        Assert.Equal("Mail could not be received. Details and Retry are above the list.", fixture.Footer.Text);
        Assert.True(fixture.Button("Retry receiving", "inbox").IsEnabled);
    }

    [Fact]
    public void TabFromAControlThatIsNoLongerAStopFollowsTabOrder()
    {
        using var fixture = Fixture.Open();
        fixture.Model.Composer.StartNew();
        fixture.Shell.Navigation.SelectTab("compose");
        fixture.Settle();
        var check = fixture.Button("Check draft");
        var save = fixture.Button("Save draft");
        // Save draft goes last in Tab order; the other actions keep their order (Send, Check draft, Discard draft).
        save.TabIndex = 1;
        fixture.Session.SetFocus(check);
        check.IsEnabled = false;

        fixture.Shell.CreateKeyboardNavigation(fixture.Session).MoveFocus(1);
        Assert.Same(fixture.Button("Discard draft"), fixture.Session.FocusedElement);
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
            ComposerStatus = Descendants(Tab("compose")).OfType<InlineFeedback>().Last();
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
        public UiElement Tab(string id) => Shell.Navigation.Tabs.Single(tab => tab.Id == id).Content!;
        public StandardRichEdit ComposerBody => Descendants(Tab("compose")).OfType<StandardRichEdit>().Single();
        public StandardButton Button(string text, string tab = "compose") => Descendants(Tab(tab)).OfType<StandardButton>().Single(button => button.Text == text);
        /// <summary>
        /// The composer's line for check results, warnings, and errors: the last of its feedback lines as
        /// they are created. Errors and warnings move to the top, so it is found before there are any.
        /// </summary>
        public InlineFeedback ComposerStatus { get; }
        public StandardEdit Field(string tab, string label) =>
            Descendants(Tab(tab)).OfType<FormField>().Where(field => field.Label.Text.StartsWith(label, StringComparison.Ordinal)).Select(field => field.Control).OfType<StandardEdit>().Single();
        public InlineFeedback SettingsFeedback => Descendants(Tab("settings")).OfType<InlineFeedback>().Single();
        public StandardLabel Footer => Shell.Footer;

        public static Fixture Open(SubmissionStatus sendResult = SubmissionStatus.Accepted, string emailAddress = "test@example.test",
            TestOutgoingTester? outgoing = null, double width = 1100, double height = 720)
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
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null, outgoing) { Clock = clock },
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null) { Clock = clock },
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher, sender: sender) { Clock = clock });
            var shell = new MailShellView(model);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
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

        /// <summary>Two pages: the newest, then one older page, the last.</summary>
        public void PageOlderMessages()
        {
            var account = Model.Account.Profile!;
            MailMessageSummary Message(uint uid) => new() { Key = new(account.Id, "INBOX", 7, uid), Sender = "author@example.test", Subject = $"Message {uid}" };
            Receiver.Inbox = (cursor, _) => Task.FromResult(cursor is null
                ? new MailInboxPage([Message(4), Message(3)], new(account.Id, 7, 5, 4, 1))
                : new MailInboxPage([Message(2), Message(1)], null));
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

    private sealed class Host(double width, double height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
