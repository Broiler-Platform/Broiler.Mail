using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class ComposerTests
{
    private const string Headers = "From: Sender <sender@example.test>\r\nReply-To: Reply Desk <reply@example.test>\r\nTo: test@example.test, other@example.test, reply@example.test\r\nCc: other@example.test, cc@example.test, TEST@example.test\r\nBcc: hidden@example.test\r\nMessage-Id: <parent@example.test>\r\nReferences: <root@example.test> <middle@example.test>\r\nSubject: Topic & details\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nFirst line\r\nGrüße & hello";

    [Fact]
    public async Task ReplyAllUsesParsedReplyToDeduplicatesVisibleRecipientsAndCarriesThread()
    {
        var account = TestDirectory.Profile();
        var body = await Decode(account, Headers);
        var reply = MailComposition.Create(account, body, CompositionKind.ReplyAll);
        Assert.Equal(new[] { "reply@example.test", "other@example.test" }, reply.To);
        Assert.Equal(new[] { "cc@example.test" }, reply.Cc);
        Assert.Empty(reply.Bcc);
        Assert.Equal(account.Id, reply.AccountId);
        Assert.Equal(account.EmailAddress, reply.FromAddress);
        Assert.Equal("Re: Topic & details", reply.Subject);
        Assert.Equal("parent@example.test", reply.InReplyTo);
        Assert.Equal(new[] { "root@example.test", "middle@example.test", "parent@example.test" }, reply.References);
        Assert.Contains("> First line\n> Grüße & hello", reply.PlainText);
        Assert.DoesNotContain("hidden@example.test", reply.PlainText);
        var one = MailComposition.Create(account, body, CompositionKind.Reply);
        Assert.Equal(new[] { "reply@example.test" }, one.To);
        Assert.Empty(one.Cc);
        var sender = await Decode(account, Headers.Replace("Reply-To: Reply Desk <reply@example.test>\r\n", ""));
        Assert.Equal(new[] { "sender@example.test" }, MailComposition.Create(account, sender, CompositionKind.Reply).To);
    }

    [Theory]
    [InlineData("In-Reply-To: <older@example.test>\r\n", "parent@example.test", "older@example.test,parent@example.test")]
    [InlineData("In-Reply-To: <one@example.test> <two@example.test>\r\n", "parent@example.test", "parent@example.test")]
    [InlineData("", "parent@example.test", "parent@example.test")]
    [InlineData("", null, "")]
    public async Task ReplyThreadFallbackFollowsParentHeadersWithoutInventingIds(string header, string? parentId, string expected)
    {
        var account = TestDirectory.Profile();
        string raw = "From: sender@example.test\r\nSubject: Re: Existing\r\n" + header +
            (parentId is null ? "" : $"Message-Id: <{parentId}>\r\n") + "\r\nBody";
        var reply = MailComposition.Create(account, await Decode(account, raw), CompositionKind.Reply);
        Assert.Equal(parentId, reply.InReplyTo);
        Assert.Equal(expected, string.Join(",", reply.References));
        Assert.Equal("Re: Existing", reply.Subject);
    }

    [Fact]
    public async Task ForwardStartsUnaddressedWithoutThreadOrBccAndLabelsPartialQuote()
    {
        var account = TestDirectory.Profile();
        var body = await Decode(account, Headers);
        var forward = MailComposition.Create(account, body with { IsTruncated = true, IsHtmlFallback = true }, CompositionKind.Forward);
        Assert.Empty(forward.To); Assert.Empty(forward.Cc); Assert.Empty(forward.Bcc);
        Assert.Null(forward.InReplyTo); Assert.Empty(forward.References);
        Assert.Equal("Fwd: Topic & details", forward.Subject);
        Assert.Contains("From: sender@example.test", forward.PlainText);
        Assert.Contains("Quoted preview is truncated", forward.PlainText);
        Assert.Contains("extracted from HTML", forward.PlainText);
        Assert.DoesNotContain("hidden@example.test", forward.PlainText);
    }

    [Fact]
    public async Task OversizedReplyHeadersLeaveTheBodyReadableButDisableComposition()
    {
        var account = TestDirectory.Profile();
        var body = await Decode(account, "From: sender@example.test\r\nSubject: " + new string('x', 999) + "\r\n\r\nReadable");
        Assert.Equal("Readable", body.PlainText);
        Assert.Null(body.Composition);
        Assert.NotNull(body.CompositionUnavailableReason);
        Assert.Throws<ArgumentException>(() => MailComposition.Create(account, body, CompositionKind.Reply));
    }

    [Fact]
    public async Task NewDraftAndInboxChangesCannotOverwriteActiveCompositionOrChangeItsSender()
    {
        var account = TestDirectory.Profile();
        var model = new ComposerViewModel();
        model.SetAccount(account);
        Assert.True(model.StartNew());
        model.Edit("recipient@example.test", "copy@example.test", "private@example.test", "Draft", "line 1\nline 2");
        var original = model.BuildDraft();
        Assert.False(model.StartFromMessage(await Decode(account, Headers), CompositionKind.Reply));
        Assert.False(model.StartNew());
        Assert.Equal(original.Id, model.BuildDraft().Id);
        Assert.Equal(original.PlainText, model.PlainText);
        model.SetAccount(account with { EmailAddress = "changed@example.test" });
        Assert.Equal(account.EmailAddress, model.FromAddress);
        Assert.Throws<InvalidOperationException>(() => model.BuildDraft());
        model.SetAccount(account);
        Assert.Equal(original.Id, model.BuildDraft().Id);
        Assert.Equal(new[] { "private@example.test" }, model.BuildDraft().Bcc);
        Assert.True(await model.DiscardAsync());
        Assert.True(model.StartNew());
        Assert.NotEqual(original.Id, model.DraftId);
        Assert.Empty(model.PlainText);
    }

    [Theory]
    [InlineData("invalid address", "Subject", "Body")]
    [InlineData("a@example.test\r\nBcc: injected@example.test", "Subject", "Body")]
    [InlineData("a@example.test", "Injected\nSubject", "Body")]
    [InlineData("a@example.test", "Subject", "Body\0control")]
    [InlineData("", "Subject", "Body")]
    public void InvalidDraftCheckRetainsTheUserText(string to, string subject, string body)
    {
        var model = new ComposerViewModel();
        model.SetAccount(TestDirectory.Profile()); model.StartNew();
        model.Edit(to, "", "", subject, body);
        Assert.Throws<ArgumentException>(() => model.BuildDraft());
        model.CheckDraft();
        Assert.Equal(to, model.To); Assert.Equal(subject, model.Subject); Assert.Equal(body, model.PlainText);
        Assert.True(model.HasDraft);
    }

    /// <summary>
    /// A draft refused for one recipient field marks that field, as the account form marks its fields: the field
    /// shows the error and reports Invalid with the error in its name, and its edit, which takes focus, reports
    /// Invalid with a description that starts with the error, for a screen reader on the field and not only on the
    /// status line. Refusals of the whole draft mark none, and the next edit clears the mark.
    /// </summary>
    [Theory]
    [InlineData("team.example.test", "", "", "Plans", "To")]
    [InlineData("", "", "", "Plans", "To")]
    [InlineData("to@example.test", "copy.example.test", "", "Plans", "Cc")]
    [InlineData("to@example.test", "", "hidden.example.test", "Plans", "Bcc")]
    [InlineData("to@example.test", "", "", "Injected\nSubject", null)]
    public async Task ARefusedRecipientFieldCarriesTheErrorForAssistiveTechnology(string to, string cc, string bcc, string subject, string? refused)
    {
        var account = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test" } };
        using var inbox = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher());
        inbox.SetAccount(account);
        var sender = new CountingSender();
        // Queued, so a draft write finishing on its own thread cannot refresh the view in the middle of a step.
        var composer = new ComposerViewModel(dispatcher: new TestQueueDispatcher(), sender: sender);
        composer.SetAccount(account);
        Assert.True(composer.StartNew());
        composer.Edit(to, cc, bcc, subject, "Body");
        using var view = new ComposerView(composer, inbox).CreateContent();
        var fields = Descendants(view).OfType<FormField>().Where(field => field.Label.Text is "To" or "Cc" or "Bcc").ToDictionary(field => field.Label.Text);

        composer.CheckDraft();
        Assert.Equal(FeedbackKind.Error, composer.StatusKind);
        Assert.Equal(refused, composer.InvalidField);
        foreach (var (name, field) in fields)
        {
            bool marked = name == refused;
            Assert.Equal(marked ? composer.Status : "", field.Error);
            Assert.Equal(marked, field.Control.ErrorMessage is not null);
            var control = field.Control.GetSemanticNode();
            Assert.Equal(marked, control.State.HasFlag(UiSemanticState.Invalid));
            Assert.Equal(marked, control.Description?.StartsWith("Error: " + composer.Status, StringComparison.Ordinal) == true);
            var node = field.GetSemanticNode();
            Assert.Equal(marked, node.State.HasFlag(UiSemanticState.Invalid));
            Assert.Equal(marked, node.Name.Contains(composer.Status, StringComparison.Ordinal));
        }
        if (refused is null) return;

        // An edit clears the mark; sending is then refused the same way, before the sender is asked.
        composer.Edit(to, cc, bcc, subject, "Body edited");
        Assert.Null(composer.InvalidField);
        Assert.All(fields.Values, field => Assert.Equal("", field.Error));
        Assert.True(composer.CanSend);
        await composer.SendAsync();
        Assert.Equal(0, sender.Calls);
        Assert.Equal(refused, composer.InvalidField);
        Assert.Equal(composer.Status, fields[refused].Error);

        // Typing in the field clears its mark, and the draft's.
        string error = fields[refused].Error;
        var edit = (StandardEdit)fields[refused].Control;
        edit.Text += " ";
        Assert.Null(composer.InvalidField);
        Assert.Equal("", fields[refused].Error);
        Assert.False(edit.GetSemanticNode().State.HasFlag(UiSemanticState.Invalid));
        Assert.False(edit.GetSemanticNode().Description?.Contains(error, StringComparison.Ordinal) == true);
        Assert.False(fields[refused].GetSemanticNode().State.HasFlag(UiSemanticState.Invalid));
    }

    [Fact]
    public void EveryDisclosureTogglesAPartNamedForWhatItHolds()
    {
        using var directory = new TestDirectory();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, new ImmediateUiDispatcher(), TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), new ImmediateUiDispatcher(), new(), null),
            new(receiver, new ImmediateUiDispatcher()));
        using var shell = new MailShellView(model);
        var sections = Descendants(shell.Window).OfType<FormSection>().Where(section => section.Toggle is not null).ToArray();
        Assert.Equal(["Cc and Bcc", "Keyboard shortcuts", "Sent-copy settings"], sections.Select(section => section.Content.GetSemanticNode().Name).Order());
        // A screen reader that follows a toggle's "controls" relation lands on the section's name ("Show Cc and
        // Bcc" controls "Cc and Bcc"), not on an unnamed pane.
        foreach (var section in sections)
        {
            Assert.Same(section.Content, section.Toggle!.Controls);
            Assert.EndsWith(" " + section.Content.GetSemanticNode().Name, section.Toggle.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Each disclosure says Show or Hide in sentence case, as every other command does ("Show plain text", "Save
    /// settings"), rather than "Show " and the section's title; abbreviations and the Sent folder's name keep their capitals.
    /// </summary>
    [Fact]
    public void EveryDisclosureToggleSaysShowOrHideInSentenceCase()
    {
        using var directory = new TestDirectory();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, new ImmediateUiDispatcher(), TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), new ImmediateUiDispatcher(), new(), null),
            new(receiver, new ImmediateUiDispatcher()));
        using var shell = new MailShellView(model);
        var expected = new Dictionary<string, (string Show, string Hide)>
        {
            ["Cc and Bcc"] = ("Show Cc and Bcc", "Hide Cc and Bcc"),
            ["Keyboard shortcuts"] = ("Show keyboard shortcuts", "Hide keyboard shortcuts"),
            ["Sent-copy settings"] = ("Show Sent-copy settings", "Hide Sent-copy settings"),
        };
        var sections = Descendants(shell.Window).OfType<FormSection>().Where(section => section.Toggle is not null).ToArray();
        Assert.Equal(expected.Keys.Order(), sections.Select(section => section.Content.GetSemanticNode().Name).Order());
        foreach (var section in sections)
        {
            var (show, hide) = expected[section.Content.GetSemanticNode().Name];
            section.Collapse();
            Assert.Equal(show, section.Toggle!.Text);
            Assert.Equal(show, section.Toggle.GetSemanticNode().Name);
            section.Expand();
            Assert.Equal(hide, section.Toggle.Text);
            Assert.Equal(hide, section.Toggle.GetSemanticNode().Name);
        }
    }

    [Fact]
    public void RecipientParserHandlesQuotedNamesAndBccOnlyDrafts()
    {
        var model = new ComposerViewModel();
        model.SetAccount(TestDirectory.Profile()); model.StartNew();
        model.Edit("", "", "\"Last, First\" <private@example.test>, second@example.test", "", "Body");
        Assert.Equal(new[] { "private@example.test", "second@example.test" }, model.BuildDraft().Bcc);
        model.CheckDraft();
        Assert.Contains("No mail was sent", model.Status);
        model.Edit("a@example.test", "", "", "", new string('x', MailComposition.MaximumBodyLength + 1));
        Assert.Throws<ArgumentException>(() => model.BuildDraft());
        Assert.Equal(MailComposition.MaximumBodyLength + 1, model.PlainText.Length);
    }

    [Fact]
    public async Task ImapBodyFetchSuppliesTheComposerWithOriginalThreadHeaders()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, Headers));
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test" } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var receiver = new ImapMailReceiver(credentials, () => new MailKit.Net.Imap.ImapClient
        { ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString() }, TimeSpan.FromSeconds(5));
        var body = await receiver.GetBodyAsync(account, new(account.Id, "INBOX", server.UidValidity, 1));
        Assert.Equal("parent@example.test", body.Composition!.MessageId);
        Assert.Equal("reply@example.test", Assert.Single(body.Composition.ReplyTo));
        Assert.DoesNotContain(server.Commands, command => command is "SELECT" or "STORE" or "EXPUNGE");
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1100, 720)]
    public async Task ComposerEditsMultilineBodyAndNavigatesWithoutInsertingTab(int width, int height)
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var credentials = new TestCredentialStore();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), credentials, receiver, new ImmediateUiDispatcher(), account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), new ImmediateUiDispatcher(), new(), null),
            new(receiver, new ImmediateUiDispatcher()));
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().Build(new Host(width, height));
        session.AddRoot(shell.Window);
        var keyboard = shell.CreateKeyboardNavigation(session);
        bool Dispatch(UiInputEvent input) => keyboard.Handle(input) || session.DispatchInput(input);
        Assert.True(Dispatch(Key(0x34, control: true)));
        Assert.Equal("compose", shell.Navigation.SelectedTab!.Id);
        var content = shell.Navigation.SelectedTab.Content!;
        Button(content, "New message").Click();
        var fields = Descendants(content).OfType<StandardLabel>().Where(label => label.Target is StandardEdit).ToDictionary(label => label.Text);
        Assert.IsType<StandardEdit>(fields["To"].Target).Text = "to@example.test";
        Assert.IsType<StandardEdit>(fields["Cc"].Target).Text = "cc@example.test";
        Assert.IsType<StandardEdit>(fields["Bcc"].Target).Text = "bcc@example.test";
        Assert.IsType<StandardEdit>(fields["Subject"].Target).Text = "Editable subject";
        var body = Descendants(content).OfType<StandardRichEdit>().Single();
        body.SetPlainText("First line");
        session.RenderFrame();
        session.SetFocus(shell.Navigation);
        for (int attempt = 0; attempt < 20 && session.FocusedElement != body; attempt++) Dispatch(Key(9));
        Assert.Same(body, session.FocusedElement);
        Assert.True(Dispatch(Key(13)));
        Assert.Contains('\n', model.Composer.PlainText);
        string beforeTab = model.Composer.PlainText;
        Assert.True(Dispatch(Key(9)));
        Assert.Equal(beforeTab, model.Composer.PlainText);
        Assert.Equal("Check draft", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        session.RenderFrame();
        var scroll = Descendants(content).OfType<FormSurface>().Single().Content.Scroll;
        Assert.False(scroll.HasHorizontalScrollbar);
        Assert.InRange(session.FocusedElement.Bounds.Top, scroll.Bounds.Bottom, height);
        Assert.InRange(session.FocusedElement.Bounds.Bottom, session.FocusedElement.Bounds.Top, height);
        Button(content, "Check draft").Click();
        Assert.Contains("No mail was sent", model.Composer.Status);
        Assert.Equal(new[] { "bcc@example.test" }, model.Composer.BuildDraft().Bcc);
        Assert.False(Button(content, "New message").IsEnabled);
        Assert.True(Dispatch(Key(9, control: true)));
        Assert.Equal("inbox", shell.Navigation.SelectedTab!.Id);
        Assert.True(Dispatch(Key(9, shift: true, control: true)));
        Assert.Equal("compose", shell.Navigation.SelectedTab!.Id);
        Assert.Equal(beforeTab, body.GetPlainText());
        await DiscardUsingButton(content, model.Composer);
        Assert.False(model.Composer.HasDraft);
        Assert.Empty(body.GetPlainText());
    }

    [Fact]
    public async Task ReplyButtonsUseLoadedSelectionAndNeverReplaceAnExistingDraft()
    {
        var account = TestDirectory.Profile();
        var first = await Decode(account, Headers);
        var second = first with { Key = first.Key with { Uid = 2 }, Composition = first.Composition! with { Subject = "Other message" } };
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage(new[] { first, second }.Select(body => new MailMessageSummary
            { Key = body.Key, Sender = "sender@example.test", Subject = body.Composition!.Subject }).ToArray(), null)),
            Body = (key, _) => Task.FromResult(key.Uid == 1 ? first : second),
        };
        using var inbox = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        inbox.SetAccount(account);
        var composer = new ComposerViewModel();
        composer.SetAccount(account);
        using var view = new ComposerView(composer, inbox).CreateContent();
        Assert.False(Button(view, "Reply all").IsEnabled);
        await inbox.ReceiveAsync();
        await inbox.SelectAsync(first.Key);
        Assert.True(Button(view, "Reply all").IsEnabled);
        Button(view, "Reply all").Click();
        var draft = composer.BuildDraft();
        Assert.Equal("parent@example.test", draft.InReplyTo);
        Assert.False(Button(view, "Forward").IsEnabled);
        await inbox.SelectAsync(second.Key);
        Assert.Equal(draft.Id, composer.DraftId);
        Assert.Equal(draft.Subject, composer.Subject);
        await DiscardUsingButton(view, composer);
        Assert.True(Button(view, "Forward").IsEnabled);
        Button(view, "Forward").Click();
        Assert.Equal("Fwd: Other message", composer.Subject);
        Assert.Empty(composer.To);
        Assert.Throws<ArgumentException>(() => MailComposition.Create(TestDirectory.Profile(), first, CompositionKind.Reply));
    }

    private static StandardButton Button(UiElement root, string text) => Descendants(root).OfType<StandardButton>().Single(button => button.Text == text);

    private sealed class CountingSender : IMailSender
    {
        public int Calls { get; private set; }
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SendResult(SubmissionStatus.Rejected, null));
        }
    }
    private static async Task DiscardUsingButton(UiElement view, ComposerViewModel composer)
    {
        var discarded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composer.Changed += (_, _) => { if (!composer.HasDraft) discarded.TrySetResult(); };
        Button(view, "Discard draft").Click();
        await discarded.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static async Task<MailMessageBody> Decode(AccountProfile account, string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await MessageTextDecoder.DecodeAsync(new(account.Id, "INBOX", 1, 1), stream, CancellationToken.None);
    }
    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }
    private static UiInputEvent Key(int code, bool shift = false, bool control = false)
    {
#pragma warning disable CS0618
        return new StandardLegacyGraphicsInputAdapter("composer-test").FromKey(new BKeyEventArgs(code, control, shift, false), KeyboardKeyTransition.Down);
#pragma warning restore CS0618
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
