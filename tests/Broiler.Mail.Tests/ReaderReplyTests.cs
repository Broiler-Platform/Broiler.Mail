using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-03: a readable header and Reply/Reply all/Forward beside the open message.</summary>
[Collection("UI theme")]
public sealed class ReaderReplyTests
{
    [Theory]
    [InlineData("Reply", "Re: Plans & budget", "body")]
    [InlineData("Reply all", "Re: Plans & budget", "body")]
    [InlineData("Forward", "Fwd: Plans & budget", "to")]
    public async Task ReaderActionStartsTheDraftAndFocusesTheIntendedField(string action, string subject, string focus)
    {
        using var fixture = await Fixture.OpenAsync();
        Assert.True(fixture.ReaderButton(action).IsEnabled);

        fixture.ReaderButton(action).Click();

        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.True(fixture.Model.Composer.HasDraft);
        Assert.Equal(subject, fixture.Model.Composer.Subject);
        if (action == "Forward") Assert.Empty(fixture.Model.Composer.To);
        else Assert.Contains("author@example.test", fixture.Model.Composer.To);
        Assert.Same(focus == "body" ? fixture.ComposerBody : fixture.ComposerTo, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task ReaderActionRevealsAnExistingDraftWithoutReplacingIt()
    {
        using var fixture = await Fixture.OpenAsync();
        Assert.True(fixture.Model.Composer.StartNew());
        fixture.Model.Composer.Edit("kept@example.test", "", "", "Unsent thoughts", "Keep me");
        var draftId = fixture.Model.Composer.DraftId;

        Assert.True(fixture.ReaderButton("Reply").IsEnabled);
        fixture.ReaderButton("Reply").Click();

        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.Equal(draftId, fixture.Model.Composer.DraftId);
        Assert.Equal("Unsent thoughts", fixture.Model.Composer.Subject);
        Assert.Equal("Keep me", fixture.Model.Composer.PlainText);
        Assert.Contains("existing draft is retained", fixture.Model.Composer.Status);
        Assert.Same(fixture.ComposerBody, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task TheComposersOwnReplyMovesFocusOnceStraightToTheBody()
    {
        using var fixture = await Fixture.OpenAsync();
        fixture.Shell.ShowView("compose");
        fixture.Session.RenderFrame();
        var reply = fixture.ComposerButton("Reply");
        Assert.True(reply.IsEnabled);
        fixture.Session.SetFocus(reply);
        var moves = new List<UiElement>();
        fixture.Session.SemanticChanged += (_, e) =>
        {
            if (e.Change == UiSemanticChangeKind.FocusChanged) moves.Add(e.Element);
        };

        reply.Click();
        fixture.Dispatcher.Drain();
        fixture.Session.RenderFrame();

        // The starting row hides as the draft starts. A screen reader hears one move, to where
        // writing starts, not first the field after the hidden button.
        Assert.True(fixture.Model.Composer.HasDraft);
        Assert.Equal([fixture.ComposerBody], moves);
    }

    [Fact]
    public async Task ReturningToTheInboxResumesFocusOnTheReaderAction()
    {
        using var fixture = await Fixture.OpenAsync();
        var reply = fixture.ReaderButton("Reply");
        fixture.Session.SetFocus(reply);
        reply.Click();
        Assert.Equal("compose", fixture.Shell.ActiveViewId);

        fixture.Shell.ShowView("inbox");

        Assert.Same(reply, fixture.Session.FocusedElement);
        Assert.Equal(fixture.Message.Key, fixture.Model.Inbox.SelectedMessage?.Key);
    }

    [Fact]
    public async Task ReaderActionsNeedALoadedMessageAndAnAccount()
    {
        using var fixture = await Fixture.OpenAsync(select: false);
        Assert.False(fixture.ReaderButton("Reply").IsEnabled);
        await fixture.Model.Inbox.SelectAsync(fixture.Message.Key);
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.Inbox.IsBusy);
        Assert.True(fixture.ReaderButton("Reply").IsEnabled);
        fixture.Model.Composer.SetAccount(null);
        Assert.False(fixture.ReaderButton("Forward").IsEnabled);
        Assert.False(fixture.Model.Compose.Respond(CompositionKind.Forward));
        Assert.Equal("inbox", fixture.Shell.ActiveViewId);
    }

    [Fact]
    public async Task HeaderSeparatesSubjectSelectableAddressesAndQuietDate()
    {
        using var fixture = await Fixture.OpenAsync();
        var inbox = fixture.InboxContent;
        var labels = Descendants(inbox).OfType<StandardLabel>().ToArray();
        var subject = labels.Single(label => label.Text == "Plans & budget");
        // Literal text: an ampersand is shown once, never turned into an access key.
        Assert.Equal("Plans & budget", subject.DisplayText);
        Assert.Equal(StandardControlPaint.Theme.FontTitle, subject.Font);
        // The separator ends the date, after a non-breaking space, and the read state is one phrase joined by
        // non-breaking spaces: a line, and a header that ends between lines, may end after the separator, but
        // not on "Unread on", and no line starts with the separator.
        var meta = labels.Single(label => label.Text.StartsWith("Received ", StringComparison.Ordinal));
        Assert.EndsWith("\u00A0· Unread\u00A0on\u00A0server", meta.Text, StringComparison.Ordinal);
        Assert.Equal(StandardControlPaint.Theme.TextMuted, meta.Foreground);

        var details = Descendants(inbox).OfType<StandardRichEdit>().First(editor => editor.GetPlainText().StartsWith("From:", StringComparison.Ordinal));
        Assert.True(details.IsReadOnly);
        Assert.Equal("From: Author & Co <author@example.test>\nTo: test@example.test\nCc: copy@example.test", details.GetPlainText());
    }

    [Theory]
    [InlineData(1400, 24, ReadingColumn.DefaultMaximumWidth)]
    [InlineData(700, 12, 0)]
    public async Task MessageTextUsesMarginsAndABoundedColumn(int width, double margin, double maximum)
    {
        using var fixture = await Fixture.OpenAsync(width, 700);
        var reader = Descendants(fixture.InboxContent).OfType<ScrollableMessageText>().Single();
        fixture.Session.RenderFrame();
        double offset = reader.Editor.Bounds.X - reader.Bounds.X;
        if (maximum > 0)
        {
            Assert.Equal(margin, offset, 1);
            Assert.Equal(maximum, reader.Editor.Bounds.Width, 1);
        }
        else
        {
            // A narrow pane uses compact margins and never exceeds the available width.
            Assert.Equal(ReadingColumn.MarginFor(reader.Bounds.Width), offset, 1);
            Assert.True(reader.Editor.Bounds.Right <= reader.Bounds.Right);
        }
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

        private Fixture(TestDirectory directory, TestQueueDispatcher dispatcher, MailShellViewModel model, MailShellView shell, UiSession session, MailMessageSummary message)
        {
            _directory = directory;
            Dispatcher = dispatcher;
            Model = model;
            Shell = shell;
            Session = session;
            Message = message;
        }

        public TestQueueDispatcher Dispatcher { get; }
        public MailShellViewModel Model { get; }
        public MailShellView Shell { get; }
        public UiSession Session { get; }
        public MailMessageSummary Message { get; }
        public UiElement InboxContent => Shell.GetContent("inbox");
        private UiElement ComposeContent => Shell.GetContent("compose");
        public StandardRichEdit ComposerBody => Descendants(ComposeContent).OfType<StandardRichEdit>().Single();
        public StandardEdit ComposerTo => (StandardEdit)Descendants(ComposeContent).OfType<StandardLabel>().Single(label => label.Text == "To").Target!;
        public StandardButton ReaderButton(string text) => Descendants(InboxContent).OfType<StandardButton>().Single(button => button.Text == text);
        public StandardButton ComposerButton(string text) => Descendants(ComposeContent).OfType<StandardButton>().Single(button => button.Text == text);

        public static async Task<Fixture> OpenAsync(int width = 1100, int height = 720, bool select = true)
        {
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            var message = new MailMessageSummary { Key = new(account.Id, "INBOX", 7, 1), Sender = "Author & Co <author@example.test>", Subject = "Plans & budget" };
            var receiver = new TestMailReceiver
            {
                Inbox = (_, _) => Task.FromResult(new MailInboxPage([message], null)),
                Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join("\n", Enumerable.Repeat("Some message text that wraps across a wide reading pane.", 40)))
                {
                    Composition = new MailCompositionSource
                    {
                        Subject = "Plans & budget", From = ["author@example.test"], To = ["test@example.test"], Cc = ["copy@example.test"],
                        MessageId = "parent@example.test",
                    },
                }),
            };
            var dispatcher = new TestQueueDispatcher();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
            shell.Attach(session);
            shell.ShowView("inbox");
            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            if (select) await model.Inbox.SelectAsync(message.Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            session.RenderFrame();
            return new(directory, dispatcher, model, shell, session, message);
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
