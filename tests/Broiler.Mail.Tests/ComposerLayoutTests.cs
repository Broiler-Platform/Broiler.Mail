using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
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

    private sealed class Sender(bool available) : IMailSender
    {
        public bool IsAvailable => available;
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SendResult(SubmissionStatus.Rejected, "Not in tests."));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UiSession _session;
        private readonly InboxViewModel _inbox;

        public Fixture(int width, int height)
        {
            Account = TestDirectory.Profile();
            Dispatcher = new TestQueueDispatcher();
            Composer = new ComposerViewModel(new MemoryDraftStore(), dispatcher: Dispatcher);
            Composer.SetAccount(Account);
            _inbox = new InboxViewModel(new TestMailReceiver(), Dispatcher);
            Surface = (FormSurface)new ComposerView(Composer, _inbox).CreateContent();
            _session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host(width, height));
            _session.AddRoot(Surface);
            Render();
        }

        public AccountProfile Account { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public ComposerViewModel Composer { get; }
        public FormSurface Surface { get; }
        public StandardRichEdit Body => Descendants(Surface).OfType<StandardRichEdit>().Single();
        public StandardLabel SenderLine => Descendants(Surface).OfType<StandardLabel>().Single(label => label.Text.StartsWith("From:", StringComparison.Ordinal));
        public IEnumerable<InlineFeedback> Feedback => Descendants(Surface).OfType<InlineFeedback>().Where(item => item.Message.Length > 0);
        public StandardButton Button(string text) => Descendants(Surface).OfType<StandardButton>().Single(button => button.Text == text);

        public void Render()
        {
            Dispatcher.Drain();
            _session.RenderFrame();
        }

        public void Dispose()
        {
            _session.Dispose();
            Surface.Dispose();
            _inbox.Dispose();
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
