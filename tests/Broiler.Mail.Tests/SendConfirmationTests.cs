// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.4b: Send on a draft that mentions a missing attachment asks first, inline: Attach file… or Send anyway.</summary>
[Collection("UI theme")]
public sealed class SendConfirmationTests
{
    private const string Mentioning = "Here is the report, see attached.";

    [Fact]
    public void SendWithReminderShowsInlineConfirmationAndDoesNotSend()
    {
        using var fixture = Fixture.Open();
        fixture.Write("Quarterly report", Mentioning);
        var compose = fixture.Shell.ActiveDialog;
        Assert.NotNull(compose);
        Assert.Equal(UiVisibility.Collapsed, fixture.Confirmation.Visibility);

        fixture.Session.SetFocus(fixture.Send);
        fixture.Send.Click();
        fixture.Render();

        Assert.Equal(0, fixture.Sender.Calls);
        Assert.True(fixture.Composer.ShowsSendConfirmation);
        Assert.Equal(DraftSubmissionState.Editing, fixture.Composer.SubmissionState);
        Assert.Equal(UiVisibility.Visible, fixture.Confirmation.Visibility);
        Assert.Equal("Attachment reminder before sending", fixture.Confirmation.GetSemanticNode().Name);
        Assert.Contains(Descendants(fixture.Confirmation).OfType<StandardLabel>(), label => label.Text == ComposerView.SendConfirmationText);
        // No extra dialog: the compose form stays the only modal element, and no file dialog opened.
        Assert.Same(compose, fixture.Shell.ActiveDialog);
        Assert.Same(compose, fixture.Session.ModalElement);
        Assert.Null(fixture.Shell.ActiveFileDialog);
        Assert.Empty(fixture.Picker.Requests);
        // Its first choice has focus and is in view; neither choice is a default button.
        Assert.Same(fixture.ConfirmAttach, fixture.Session.FocusedElement);
        Assert.Equal("Attach file before sending", fixture.ConfirmAttach.GetSemanticNode().Name);
        Assert.Equal("Attach file…", fixture.ConfirmAttach.Text);
        Assert.False(fixture.ConfirmAttach.IsDefault);
        Assert.False(fixture.SendAnyway.IsDefault);
        Assert.True(fixture.ConfirmAttach.Bounds.Height > 0);
        // The reminder line keeps saying plain text only.
        Assert.Contains("plain text only", fixture.Composer.AttachmentReminderText);

        // Clicking Send again still asks, and still sends nothing.
        fixture.Send.Click();
        fixture.Render();
        Assert.Equal(0, fixture.Sender.Calls);
        Assert.True(fixture.Composer.ShowsSendConfirmation);
    }

    [Fact]
    public void SendAnywaySendsOnceAndRetryAfterFailureDoesNotAskAgain()
    {
        using var fixture = Fixture.Open();
        fixture.Sender.Results.Enqueue(new SendResult(SubmissionStatus.Rejected, "Rejected by the test server."));
        fixture.Write("Quarterly report", Mentioning);
        fixture.Send.Click();
        fixture.Render();
        Assert.Equal(0, fixture.Sender.Calls);

        fixture.SendAnyway.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy);
        fixture.Render();
        Assert.Equal(1, fixture.Sender.Calls);
        Assert.Equal(DraftSubmissionState.Failed, fixture.Composer.SubmissionState);
        Assert.Equal(UiVisibility.Collapsed, fixture.Confirmation.Visibility);

        // A retry of the same, unchanged draft does not ask again.
        Assert.True(fixture.Composer.CanSend);
        Assert.False(fixture.Composer.RequestSendConfirmation());
        // The first send's click finishes on its own continuation; until then Send is still ignored.
        fixture.Dispatcher.DrainUntil(() =>
        {
            if (fixture.Sender.Calls < 2 && !fixture.Composer.IsBusy) fixture.Send.Click();
            return fixture.Sender.Calls == 2 && !fixture.Composer.IsBusy;
        });
        fixture.Render();
        Assert.Equal(2, fixture.Sender.Calls);
        Assert.Equal(DraftSubmissionState.Accepted, fixture.Composer.SubmissionState);
        Assert.False(fixture.Composer.ShowsSendConfirmation);
    }

    [Fact]
    public void AnEditReArmsTheConfirmation()
    {
        using var fixture = Fixture.Open();
        fixture.Sender.Results.Enqueue(new SendResult(SubmissionStatus.Rejected, "Rejected by the test server."));
        fixture.Write("Quarterly report", Mentioning);
        fixture.Send.Click();
        fixture.SendAnyway.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy);
        Assert.Equal(1, fixture.Sender.Calls);

        // The text changes and still mentions the file: Send asks again.
        fixture.Write("Quarterly report", Mentioning + " Two files, actually.");
        fixture.Send.Click();
        fixture.Render();
        Assert.Equal(1, fixture.Sender.Calls);
        Assert.True(fixture.Composer.ShowsSendConfirmation);

        // Changing only a recipient is not a new mention, so a dismissed reminder still asks on the next Send.
        fixture.Composer.DismissSendConfirmation();
        fixture.Composer.Edit("other@example.test", "", "", fixture.Composer.Subject, fixture.Composer.PlainText);
        Assert.True(fixture.Composer.RequestSendConfirmation());
        Assert.Equal(1, fixture.Sender.Calls);
    }

    [Fact]
    public void AttachFromConfirmationHidesBarAndFocusesSendWithoutSending()
    {
        using var fixture = Fixture.Open();
        fixture.Picker.OpenResults.Enqueue(MailFilePick<IMailReadableFile>.Picked(new ComposerAttachmentTests.TestReadableFile("report.pdf", [1, 2, 3, 4], length: 4)));
        fixture.Write("Quarterly report", Mentioning);
        fixture.Send.Click();
        fixture.Render();
        Assert.Same(fixture.ConfirmAttach, fixture.Session.FocusedElement);

        fixture.ConfirmAttach.Click();
        // The add finishes on the click's continuation, which then moves focus.
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.Attachments.Count == 1 && !fixture.Composer.IsBusy && fixture.Session.FocusedElement == fixture.Send);
        fixture.Render();

        var request = Assert.Single(fixture.Picker.Requests);
        Assert.Equal(("open", MailFilePurpose.Attachment), (request.Kind, request.Request.Purpose));
        Assert.Equal("report.pdf", Assert.Single(fixture.Composer.Attachments).FileName);
        Assert.Equal(0, fixture.Sender.Calls);
        Assert.False(fixture.Composer.ShowsSendConfirmation);
        Assert.Null(fixture.Composer.AttachmentReminderText);
        Assert.Equal(UiVisibility.Collapsed, fixture.Confirmation.Visibility);
        Assert.Same(fixture.Send, fixture.Session.FocusedElement);

        // Send now goes out with the file, without asking.
        fixture.Send.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy);
        Assert.Equal(1, fixture.Sender.Calls);
        Assert.Equal("report.pdf", Assert.Single(fixture.Sender.Drafts.Single().Attachments!).FileName);
    }

    [Fact]
    public void CanceledPickFromConfirmationKeepsItAndItsFocus()
    {
        using var fixture = Fixture.Open();
        fixture.Write("Quarterly report", Mentioning);
        fixture.Send.Click();
        fixture.Render();

        // The fake picker cancels when nothing is queued.
        fixture.ConfirmAttach.Click();
        fixture.Render();
        Assert.Single(fixture.Picker.Requests);
        Assert.True(fixture.Composer.ShowsSendConfirmation);
        Assert.Same(fixture.ConfirmAttach, fixture.Session.FocusedElement);
        Assert.Equal(0, fixture.Sender.Calls);
    }

    [Fact]
    public void EscapeDismissesConfirmationBeforeClosingCompose()
    {
        using var fixture = Fixture.Open();
        fixture.Write("Quarterly report", Mentioning);
        fixture.Send.Click();
        fixture.Render();
        var compose = fixture.Shell.ActiveDialog;
        Assert.True(fixture.Composer.ShowsSendConfirmation);

        Assert.True(fixture.Dispatch(Key(0x1B)));
        fixture.Render();
        Assert.False(fixture.Composer.ShowsSendConfirmation);
        Assert.Equal(UiVisibility.Collapsed, fixture.Confirmation.Visibility);
        Assert.Same(compose, fixture.Shell.ActiveDialog);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.Same(fixture.Send, fixture.Session.FocusedElement);
        Assert.Equal(0, fixture.Sender.Calls);

        // The next Escape closes Compose, as before.
        Assert.True(fixture.Dispatch(Key(0x1B)));
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.Equal(0, fixture.Sender.Calls);
    }

    [Fact]
    public void EscapeCancelsAttachmentReadBeforeClosingCompose()
    {
        using var fixture = Fixture.Open();
        var file = new ComposerAttachmentTests.TestReadableFile("slow.bin", new byte[3 * ComposerViewModel.AttachmentReadChunkBytes], 3 * ComposerViewModel.AttachmentReadChunkBytes) { Gated = true };
        fixture.Picker.OpenResults.Enqueue(MailFilePick<IMailReadableFile>.Picked(file));
        fixture.Write("Plain subject", "No mention here.");
        var attach = fixture.Bar.Descendant<StandardButton>(button => button.Text == "Attach file…");
        attach.Click();
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.IsAddingAttachment && file.BytesRead > 0);
        var compose = fixture.Shell.ActiveDialog;

        Assert.True(fixture.Dispatch(Key(0x1B)));
        file.Release();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsAddingAttachment);
        fixture.Render();
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("Attachment not added; reading was canceled.", fixture.Composer.Status);
        Assert.Same(compose, fixture.Shell.ActiveDialog);

        Assert.True(fixture.Dispatch(Key(0x1B)));
        Assert.Null(fixture.Shell.ActiveDialog);
    }

    [Fact]
    public void NoReminderSendsImmediately()
    {
        using var fixture = Fixture.Open();
        fixture.Write("Lunch", "See you at noon.");
        Assert.Null(fixture.Composer.AttachmentReminderText);
        Assert.False(fixture.Composer.RequestSendConfirmation());

        fixture.Send.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy);
        fixture.Render();
        Assert.Equal(1, fixture.Sender.Calls);
        Assert.Equal(DraftSubmissionState.Accepted, fixture.Composer.SubmissionState);
        Assert.Equal(UiVisibility.Collapsed, fixture.Confirmation.Visibility);
    }

    [Fact]
    public void EditingAwayTheMentionHidesBarAndMovesFocusToSend()
    {
        using var fixture = Fixture.Open();
        fixture.Write("Quarterly report", Mentioning);
        fixture.Send.Click();
        fixture.Render();
        Assert.Same(fixture.ConfirmAttach, fixture.Session.FocusedElement);

        // The body editor reports the change; with no mention left the bar hides and focus moves on to Send.
        fixture.Body.SetPlainText("Here is the summary.");
        fixture.Render();
        Assert.Null(fixture.Composer.AttachmentReminderText);
        Assert.False(fixture.Composer.ShowsSendConfirmation);
        Assert.Equal(UiVisibility.Collapsed, fixture.Confirmation.Visibility);
        Assert.Same(fixture.Send, fixture.Session.FocusedElement);
        Assert.Equal(0, fixture.Sender.Calls);
    }

    [Fact]
    public async Task TheViewModelGateNeverSendsWithoutSendAnyway()
    {
        var sender = new CountingSender();
        var account = Account();
        using var composer = new ComposerViewModel(sender: sender);
        composer.SetAccount(account);
        Assert.True(composer.StartNew());
        composer.Edit("to@example.test", "", "", "Report attached", "Body");

        Assert.True(composer.RequestSendConfirmation());
        Assert.True(composer.ShowsSendConfirmation);
        composer.DismissSendConfirmation();
        Assert.False(composer.ShowsSendConfirmation);
        Assert.True(composer.RequestSendConfirmation());
        Assert.Equal(0, sender.Calls);

        // Only Send anyway sends it as it is.
        await composer.SendAnywayAsync();
        Assert.Equal(1, sender.Calls);
        Assert.False(composer.ShowsSendConfirmation);
    }

    private static AccountProfile Account() =>
        TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test" } };

    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class CountingSender : IMailSender
    {
        public int Calls { get; private set; }
        public Queue<SendResult> Results { get; } = new();
        public List<MailDraft> Drafts { get; } = [];

        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
        {
            lock (Drafts) { Calls++; Drafts.Add(draft); }
            return Task.FromResult(Results.TryDequeue(out var result) ? result : new SendResult(SubmissionStatus.Accepted, "Accepted."));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestDirectory _directory;

        private Fixture(TestDirectory directory, TestQueueDispatcher dispatcher, MailShellViewModel model, MailShellView shell, UiSession session,
            FakeMailFilePicker picker, CountingSender sender)
        {
            _directory = directory; Dispatcher = dispatcher; Model = model; Shell = shell; Session = session; Picker = picker; Sender = sender;
            Keyboard = shell.CreateKeyboardNavigation(session);
        }

        public TestQueueDispatcher Dispatcher { get; }
        public MailShellViewModel Model { get; }
        public MailShellView Shell { get; }
        public UiSession Session { get; }
        public MailKeyboardNavigation Keyboard { get; }
        public FakeMailFilePicker Picker { get; }
        public CountingSender Sender { get; }
        public ComposerViewModel Composer => Model.Composer;
        private UiElement Content => Shell.GetContent("compose");
        public UiElement Confirmation => Shell.Composer.SendConfirmation!;
        public UiElement Bar => Shell.Composer.Attachments!;
        public StandardRichEdit Body => (StandardRichEdit)Shell.Composer.Body!;
        public StandardButton Send => Descendants(Content).OfType<StandardButton>().Single(button => button.Text == "Send");
        public StandardButton ConfirmAttach => Descendants(Confirmation).OfType<StandardButton>().Single(button => button.AccessibleName == "Attach file before sending");
        public StandardButton SendAnyway => Descendants(Confirmation).OfType<StandardButton>().Single(button => button.Text == "Send anyway");

        public static Fixture Open(int width = 1100, int height = 720)
        {
            var directory = new TestDirectory();
            var account = Account();
            var receiver = new TestMailReceiver();
            var dispatcher = new TestQueueDispatcher();
            var sender = new CountingSender();
            var picker = new FakeMailFilePicker();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher, sender: sender));
            var shell = new MailShellView(model, files: picker);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
            shell.Attach(session);
            shell.ShowView("inbox");
            Assert.True(model.Composer.StartNew());
            Assert.True(shell.ShowView("compose"));
            dispatcher.Drain();
            session.RenderFrame();
            return new(directory, dispatcher, model, shell, session, picker, sender);
        }

        /// <summary>Types a recipient, subject and text into the compose fields.</summary>
        public void Write(string subject, string text)
        {
            Field("To").Text = "to@example.test";
            Field("Subject").Text = subject;
            Body.SetPlainText(text);
            Render();
            Assert.Equal((subject, text), (Composer.Subject, Composer.PlainText));
        }

        private Broiler.UI.Edit.Standard.StandardEdit Field(string label) =>
            (Broiler.UI.Edit.Standard.StandardEdit)Descendants(Content).OfType<StandardLabel>().Single(item => item.Text == label).Target!;

        public bool Dispatch(UiInputEvent input) => Keyboard.Handle(input) || Session.DispatchInput(input);

        public void Render()
        {
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}

internal static class SendConfirmationTestExtensions
{
    public static T Descendant<T>(this UiElement root, Func<T, bool> match) where T : UiElement
    {
        static IEnumerable<UiElement> Walk(UiElement element)
        {
            yield return element;
            foreach (var child in element.Children)
                foreach (var item in Walk(child)) yield return item;
        }
        return Walk(root).OfType<T>().Single(match);
    }
}
