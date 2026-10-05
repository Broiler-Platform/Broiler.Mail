using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-05: writing occupies the composer, and feedback never disturbs the text being written.</summary>
[Collection("UI theme")]
public sealed class ComposerLayoutTests
{
    [Fact]
    public void BodyFillsTheRemainingHeightWithoutOuterScrolling()
    {
        using var fixture = new Fixture(1100, 720);
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();

        var scroll = fixture.Surface.Content.Scroll;
        Assert.False(scroll.HasVerticalScrollbar);
        // The body reaches down to the persistent action bar instead of stopping at a fixed height.
        Assert.True(fixture.Body.Bounds.Height > 300, $"Body is {fixture.Body.Bounds.Height} DIP tall.");
        Assert.InRange(fixture.Surface.Actions.Bounds.Top - fixture.Body.Bounds.Bottom, 0, 40);
    }

    [Fact]
    public void MinimumSizeKeepsTheBodyUsableAndEveryActionVisible()
    {
        using var fixture = new Fixture(640, 480);
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();

        Assert.True(fixture.Body.Bounds.Height >= 120);
        foreach (var text in new[] { "Send", "Check draft", "Save draft", "Discard draft" })
        {
            var button = fixture.Button(text);
            Assert.True(button.Bounds.Width > 0, $"{text} is not laid out.");
            Assert.InRange(button.Bounds.Bottom, button.Bounds.Top, 480);
        }
    }

    [Fact]
    public void StatusAndAutosaveUpdatesKeepTheCaretAndSelection()
    {
        using var fixture = new Fixture(1100, 720);
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();
        fixture.Body.SetPlainText("First line\nSecond line");
        fixture.Dispatcher.Drain();
        Assert.True(fixture.Body.SetEditorSelection(3, 8));
        var selection = fixture.Body.Selection;

        fixture.Composer.CheckDraft();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy);
        fixture.Composer.SetAccount(fixture.Account with { DisplayName = "Renamed" });
        fixture.Render();

        Assert.Equal("First line\nSecond line", fixture.Body.GetPlainText());
        Assert.Equal(selection, fixture.Body.Selection);
    }

    [Fact]
    public async Task TypingKeepsItsUndoHistoryThroughStatusAutosaveAccountInboxAndDisclosureUpdates()
    {
        using var fixture = new Fixture(1100, 720);
        Assert.True(fixture.Composer.StartNew());
        // Addressed, so the draft check below passes: a refused check takes focus to the field it names.
        ((Broiler.UI.Edit.Standard.StandardEdit)Descendants(fixture.Surface).OfType<FormField>().Single(field => field.Label.Text == "To").Control).Text = "team@example.test";
        fixture.Render();
        // Text that was there before typing, such as a recovered draft, is not part of the history.
        const string loaded = "Dear all,\n";
        fixture.Body.SetPlainText(loaded);
        Assert.True(fixture.Body.SetEditorSelection(loaded.Length, loaded.Length));
        fixture.Session.SetFocus(fixture.Body);
        const string typed = "the agenda is ready";
        foreach (char character in typed)
            Assert.True(fixture.Session.DispatchInput(Text(character)));
        Assert.Equal(loaded + typed, fixture.Composer.PlainText);

        // Everything that refreshes the composer while the user writes.
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.StorageKind != FeedbackKind.Progress);
        fixture.Composer.CheckDraft();
        Assert.Equal(FeedbackKind.Success, fixture.Composer.StatusKind);
        fixture.Composer.SetAccount(fixture.Account with { DisplayName = "Renamed" });
        await fixture.Inbox.ReceiveAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Inbox.IsBusy);
        var copies = Descendants(fixture.Surface).OfType<FormSection>().Single();
        copies.Toggle!.Click();
        fixture.Render();
        copies.Toggle.Click();
        fixture.Render();
        Assert.Same(fixture.Body, fixture.Session.FocusedElement);
        Assert.Equal(loaded + typed, fixture.Body.GetPlainText());
        Assert.True(fixture.Body.GetCommandState(RichEditCommand.Undo).IsEnabled);

        // Ctrl+Z, through the editor's own keys, takes back the typing and nothing before it; the caret
        // stays where the text was removed, and the draft follows.
        for (int press = 0; press < typed.Length && fixture.Body.GetCommandState(RichEditCommand.Undo).IsEnabled; press++)
        {
            Assert.True(fixture.Session.DispatchInput(Key(0x5A, control: true)));
            string text = fixture.Body.GetPlainText();
            Assert.StartsWith(text, loaded + typed, StringComparison.Ordinal);
            Assert.True(text.Length >= loaded.Length);
            var caret = fixture.Body.GetTextEditorMetrics();
            Assert.Equal((text.Length, text.Length), (caret.SelectionStart, caret.SelectionEnd));
        }
        Assert.Equal(loaded, fixture.Body.GetPlainText());
        Assert.False(fixture.Body.GetCommandState(RichEditCommand.Undo).IsEnabled);
        fixture.Render();
        Assert.Equal(loaded, fixture.Composer.PlainText);

        // Ctrl+Y restores it, with the caret after the restored text.
        for (int press = 0; press < typed.Length && fixture.Body.GetCommandState(RichEditCommand.Redo).IsEnabled; press++)
            Assert.True(fixture.Session.DispatchInput(Key(0x59, control: true)));
        Assert.Equal(loaded + typed, fixture.Body.GetPlainText());
        var end = fixture.Body.GetTextEditorMetrics();
        Assert.Equal(((loaded + typed).Length, (loaded + typed).Length), (end.SelectionStart, end.SelectionEnd));
        fixture.Render();
        Assert.Equal(loaded + typed, fixture.Composer.PlainText);
    }

    [Fact]
    public void RoutineInformationLeavesTheComposerForTheFooterWhileResultsStayInline()
    {
        using var fixture = new Fixture(1100, 720);
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();
        Assert.Equal(FeedbackKind.Information, fixture.Composer.StatusKind);
        Assert.DoesNotContain(fixture.Feedback, item => item.Message == fixture.Composer.Status);

        fixture.Composer.CheckDraft();
        Assert.Contains(fixture.Feedback, item => item.Kind == FeedbackKind.Error && item.Message == fixture.Composer.Status);

        // Editing clears the stale check result rather than announcing every change.
        fixture.Composer.Edit("to@example.test", "", "", "Subject", "Body");
        Assert.Equal("", fixture.Composer.Status);
        Assert.DoesNotContain(fixture.Feedback, item => item.Kind == FeedbackKind.Error);
        // The quiet autosave state follows the save through to completion.
        // The save state is read live, but the label follows only when the posted notification is drained.
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.StorageKind != FeedbackKind.Progress && fixture.SenderLine.Text.Contains(fixture.Composer.StorageStatus));
        Assert.DoesNotContain("Saving", fixture.SenderLine.Text);
    }

    /// <summary>
    /// The feedback below the buttons is capped and scrolls at a large text size, so it starts with a
    /// problem: a recipient error is shown above the hint that sending is unavailable, which keeps its
    /// text below it. Moving the lines does not announce them again. Without a problem, the lines keep
    /// their usual order. An area scrolled down to a line below, such as a passed check's result, brings a
    /// new problem into view, where it was put out of view before.
    /// </summary>
    [Fact]
    public void AnErrorIsShownAboveAHintInTheCappedFeedbackArea()
    {
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        try
        {
            using var fixture = new Fixture(640, 480);
            var announced = new List<string>();
            fixture.Session.SemanticChanged += (_, e) =>
            {
                if (e.Change == UiSemanticChangeKind.StatusAnnounced) announced.Add(e.Message ?? "");
            };
            Assert.True(fixture.Composer.StartNew());
            fixture.Composer.Edit("team.example.test", "", "", "Plans", "Body");
            fixture.Render();
            const string hint = "Sending is not available in this mode.";
            Assert.Equal([(FeedbackKind.Information, hint)], fixture.Feedback.Select(line => (line.Kind, line.Message)));
            announced.Clear();

            fixture.Button("Check draft").Click();
            fixture.Render();
            string error = fixture.Composer.Status;
            Assert.Equal(FeedbackKind.Error, fixture.Composer.StatusKind);
            Assert.Equal([(FeedbackKind.Error, error), (FeedbackKind.Information, hint)], fixture.Feedback.Select(line => (line.Kind, line.Message)));
            Assert.Equal(["Error: " + error], announced);
            var area = fixture.Surface.Children.OfType<FormViewport>().Last().Scroll;
            BRect shown = fixture.Feedback.First().Bounds;
            Assert.True(area.HasVerticalScrollbar, "The feedback fits its area; the text is not large enough.");
            Assert.Equal(area.ContentBounds.Top, shown.Top, 0.5);
            Assert.True(shown.Bottom <= fixture.Feedback.Last().Bounds.Top + 0.5, $"The error is at {shown}, the hint at {fixture.Feedback.Last().Bounds}.");

            // A passed check is a result, not a problem: it stays below the hint, as before.
            fixture.Composer.Edit("team@example.test", "", "", "Plans", "Body");
            fixture.Button("Check draft").Click();
            fixture.Render();
            Assert.Equal([(FeedbackKind.Information, hint), (FeedbackKind.Success, fixture.Composer.Status)], fixture.Feedback.Select(line => (line.Kind, line.Message)));

            // Scrolled down to read that result, the area shows a new problem, which comes first, by scrolling
            // to its top, without moving focus; once shown, the user may scroll it away again.
            Assert.True(area.HasVerticalScrollbar, "The feedback fits its area; the text is not large enough.");
            Assert.True(area.ScrollToEnd());
            fixture.Render();
            var focused = fixture.Session.FocusedElement;
            fixture.Composer.Edit("team.example.test", "", "", "Plans", "Body");
            fixture.Button("Check draft").Click();
            fixture.Render();
            var problem = fixture.Feedback.First();
            Assert.Equal((FeedbackKind.Error, error), (problem.Kind, problem.Message));
            Assert.True(problem.Bounds.Top >= area.ContentBounds.Top - 0.5 && problem.Bounds.Bottom <= area.ContentBounds.Bottom + 0.5,
                $"The error is at {problem.Bounds}, the area shows {area.ContentBounds}.");
            Assert.Same(focused, fixture.Session.FocusedElement);
            Assert.True(area.ScrollToEnd());
            fixture.Button("Check draft").Click();
            fixture.Render();
            Assert.True(area.VerticalOffset > 0, "The same error was brought into view again.");
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    [Fact]
    public void StartingRowGivesWayWhileADraftExistsAndReturnsAfterDiscard()
    {
        using var fixture = new Fixture(1100, 720);
        Assert.Equal(UiVisibility.Visible, fixture.Button("New message").Parent!.Visibility);
        Assert.True(fixture.Composer.StartNew());
        Assert.Equal(UiVisibility.Collapsed, fixture.Button("New message").Parent!.Visibility);

        _ = fixture.Composer.DiscardAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.HasDraft && !fixture.Composer.IsBusy);
        Assert.Equal(UiVisibility.Visible, fixture.Button("New message").Parent!.Visibility);
    }

    [Fact]
    public void CollapsedCopiesKeepTheirValuesAndExpandedCopiesDropTheSummary()
    {
        using var fixture = new Fixture(1100, 720);
        Assert.True(fixture.Composer.StartNew());
        fixture.Composer.Edit("to@example.test", "cc@example.test", "", "Subject", "Body");
        var copies = Descendants(fixture.Surface).OfType<FormSection>().Single();
        Assert.False(copies.IsExpanded);
        Assert.Equal("Cc recipients included", copies.Summary);

        copies.Toggle!.Click();
        Assert.True(copies.IsExpanded);
        Assert.Equal("", copies.Summary);
        copies.Toggle.Click();
        Assert.Equal("cc@example.test", fixture.Composer.Cc);
        Assert.Equal("Cc recipients included", copies.Summary);
    }

    [Fact]
    public void SendHintExplainsOnlyWhatPreventsSending()
    {
        var withoutSmtp = TestDirectory.Profile();
        var withSmtp = withoutSmtp with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test", Security = TransportSecurity.StartTls } };
        string? Reason(AccountProfile account, bool available)
        {
            var composer = new ComposerViewModel(new MemoryDraftStore(), dispatcher: new TestQueueDispatcher(), sender: new Sender(available));
            composer.SetAccount(account);
            Assert.True(composer.StartNew());
            return composer.SendUnavailableReason;
        }
        Assert.Contains("SMTP", Reason(withoutSmtp, true));
        Assert.Contains("not available", Reason(withSmtp, false));
        Assert.Null(Reason(withSmtp, true));
    }

    [Fact]
    public async Task FirstAccountKeepsTheRecoveryMessageAndALaterChangeExplainsRetention()
    {
        var account = TestDirectory.Profile();
        var store = new MemoryDraftStore();
        var snapshot = new DraftSnapshot
        {
            Draft = new MailDraft { AccountId = account.Id, FromAddress = account.EmailAddress, Subject = "Kept" },
            ToText = "", CcText = "", BccText = "", State = DraftSubmissionState.Editing,
        };
        var initial = await store.SaveAsync(0, snapshot);
        var composer = new ComposerViewModel(store, initial, new TestQueueDispatcher());
        composer.SetAccount(account);
        Assert.Equal("Recovered your saved draft.", composer.Status);
        composer.SetAccount(account with { DisplayName = "Changed" });
        Assert.Contains("retained", composer.Status);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private static UiInputEvent Text(char character)
    {
#pragma warning disable CS0618
        return new StandardLegacyGraphicsInputAdapter("composer-layout").FromText(new BTextInputEventArgs(character));
#pragma warning restore CS0618
    }

    private static UiInputEvent Key(int code, bool control = false)
    {
#pragma warning disable CS0618
        return new StandardLegacyGraphicsInputAdapter("composer-layout").FromKey(new BKeyEventArgs(code, control, false, false), KeyboardKeyTransition.Down);
#pragma warning restore CS0618
    }

    private sealed class Sender(bool available) : IMailSender
    {
        public bool IsAvailable => available;
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SendResult(SubmissionStatus.Rejected, "Not in tests."));
    }

    private sealed class Fixture : IDisposable
    {

        public Fixture(int width, int height)
        {
            Account = TestDirectory.Profile();
            Dispatcher = new TestQueueDispatcher();
            Composer = new ComposerViewModel(new MemoryDraftStore(), dispatcher: Dispatcher);
            Composer.SetAccount(Account);
            Inbox = new InboxViewModel(new TestMailReceiver(), Dispatcher);
            Inbox.SetAccount(Account);
            Surface = (FormSurface)new ComposerView(Composer, Inbox).CreateContent();
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host(width, height));
            Session.AddRoot(Surface);
            Render();
        }

        public AccountProfile Account { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public ComposerViewModel Composer { get; }
        public InboxViewModel Inbox { get; }
        public UiSession Session { get; }
        public FormSurface Surface { get; }
        public StandardRichEdit Body => Descendants(Surface).OfType<StandardRichEdit>().Single();
        public StandardLabel SenderLine => Descendants(Surface).OfType<StandardLabel>().Single(label => label.Text.StartsWith("From:", StringComparison.Ordinal));
        public IEnumerable<InlineFeedback> Feedback => Descendants(Surface).OfType<InlineFeedback>().Where(item => item.Message.Length > 0);
        public StandardButton Button(string text) => Descendants(Surface).OfType<StandardButton>().Single(button => button.Text == text);

        public void Render()
        {
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            Surface.Dispose();
            Inbox.Dispose();
            Composer.Dispose();
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
