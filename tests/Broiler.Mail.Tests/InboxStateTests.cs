using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
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
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-08: inbox states are explained beside the affected pane, with the action that resolves them.</summary>
[Collection("UI theme")]
public sealed class InboxStateTests
{
    [Fact]
    public async Task ListExplainsWhyItIsEmptyBeforeAndAfterTheFirstReceive()
    {
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([], null)));
        Assert.False(fixture.Model.HasLoaded);
        Assert.Equal("Receive mail to load your inbox.", fixture.ListFeedback.Message);
        await fixture.ReceiveAsync();
        Assert.True(fixture.Model.HasLoaded);
        Assert.Equal("The inbox is empty.", fixture.ListFeedback.Message);
        Assert.Equal(FeedbackKind.Information, fixture.ListFeedback.Kind);
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
    }

    /// <summary>
    /// Once received, an empty inbox has nothing to select: the reader says that the inbox is empty, as
    /// the list does, and offers Receive mail to check again, not the wording for an inbox never received.
    /// While a receive runs, it does not ask for Receive mail, which is unavailable then.
    /// </summary>
    [Fact]
    public async Task TheReaderAgreesWithTheListAboutAnEmptyInbox()
    {
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([], null)));
        Assert.Equal("Receive mail to load your inbox.", fixture.Reader.Text);
        await fixture.ReceiveAsync();

        Assert.Equal("The inbox is empty.", fixture.ListFeedback.Message);
        Assert.Equal("The inbox is empty.", fixture.ReaderHeading.Text);
        Assert.Equal("Use Receive mail to check for new messages.", fixture.Reader.Text);
        Assert.True(fixture.Button("Receive mail").IsEnabled);

        // While it checks again, Receive mail is unavailable and the list shows the progress, so the reader
        // does not ask for it; nor while the first receive runs.
        var pending = new TaskCompletionSource<MailInboxPage>();
        fixture.Receiver.Inbox = (_, token) => pending.Task.WaitAsync(token);
        var receiving = fixture.Model.ReceiveAsync();
        fixture.Render();
        Assert.True(fixture.Model.IsLoadingList);
        Assert.Equal(FeedbackKind.Progress, fixture.ListFeedback.Kind);
        Assert.False(fixture.Button("Receive mail").IsEnabled);
        Assert.Equal("The inbox is empty.", fixture.ReaderHeading.Text);
        Assert.Equal("", fixture.Reader.Text);
        pending.SetResult(new MailInboxPage([], null));
        fixture.Settle();
        await receiving;
        Assert.Equal("Use Receive mail to check for new messages.", fixture.Reader.Text);

        using var first = new Fixture(token => new TaskCompletionSource<MailInboxPage>().Task.WaitAsync(token));
        _ = first.Model.ReceiveAsync();
        first.Render();
        Assert.True(first.Model.IsLoadingList);
        Assert.Equal("", first.Reader.Text);
        first.Model.Cancel();
        first.Settle();
    }

    [Fact]
    public async Task FailedRefreshKeepsMessagesAndOffersRetryAboveTheList()
    {
        var account = TestDirectory.Profile();
        var messages = new[] { Message(account, 2), Message(account, 1) };
        bool fail = false;
        using var fixture = new Fixture(_ => fail ? throw new MailConnectionException("The server did not respond.") : Task.FromResult(new MailInboxPage(messages, null)), account);
        await fixture.ReceiveAsync();
        fail = true;
        await fixture.ReceiveAsync();

        Assert.Equal(2, fixture.Model.Messages.Count);
        Assert.Equal(InboxProblemScope.List, fixture.Model.ProblemScope);
        Assert.Equal(FeedbackKind.Error, fixture.ListFeedback.Kind);
        Assert.Contains("The server did not respond.", fixture.ListFeedback.Message);
        Assert.Contains("from the last successful receive", fixture.ListFeedback.Message);
        var retry = fixture.Button("Retry receiving");
        Assert.True(retry.IsEnabled && fixture.IsShown(retry));

        fail = false;
        fixture.Session.SetFocus(retry);
        retry.Click();
        fixture.Settle();
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
        Assert.False(fixture.IsShown(retry));
        // Focus leaves the hidden Retry for the list it concerned.
        Assert.IsType<StandardListView>(fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task FailedMessageOffersRetryBesideItsHeaderInsteadOfAnInstruction()
    {
        var account = TestDirectory.Profile();
        var message = Message(account, 1);
        bool fail = true;
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([message], null)), account,
            key => fail ? throw new MailConnectionException("The connection closed.") : Task.FromResult(new MailMessageBody(key, "Loaded body")));
        await fixture.ReceiveAsync();
        await fixture.Model.SelectAsync(message.Key);
        fixture.Settle();

        Assert.Equal(InboxProblemScope.Message, fixture.Model.ProblemScope);
        Assert.Equal("The connection closed.", fixture.MessageFeedback.Message);
        Assert.Equal(FeedbackKind.Error, fixture.MessageFeedback.Kind);
        Assert.Equal("", fixture.Reader.Text);
        Assert.Equal("", fixture.ListFeedback.Message);
        var retry = fixture.Button("Retry loading");
        Assert.True(fixture.IsShown(retry));

        fail = false;
        retry.Click();
        fixture.Settle();
        Assert.Contains("Loaded body", fixture.Reader.Text);
        Assert.Equal("", fixture.MessageFeedback.Message);
        Assert.False(fixture.IsShown(retry));
    }

    [Fact]
    public async Task CancelIsInformationAndMovesFocusOffTheDisabledCancelButton()
    {
        var pending = new TaskCompletionSource<MailInboxPage>();
        using var fixture = new Fixture(token => pending.Task.WaitAsync(token));
        _ = fixture.Model.ReceiveAsync();
        fixture.Render();
        Assert.True(fixture.Model.IsLoadingList);
        Assert.Equal(FeedbackKind.Progress, fixture.ListFeedback.Kind);
        var cancel = fixture.Button("Cancel");
        fixture.Session.SetFocus(cancel);

        cancel.Click();
        fixture.Render();
        Assert.True(fixture.Model.ProblemIsCancellation);
        Assert.Equal(FeedbackKind.Information, fixture.ListFeedback.Kind);
        Assert.Equal("Receiving was canceled.", fixture.ListFeedback.Message);
        Assert.Equal("Receive mail", Assert.IsType<StandardButton>(fixture.Session.FocusedElement).Text);
    }

    [Fact]
    public async Task ChoosingAnotherMessageReplacesAnEarlierMessageProblem()
    {
        var account = TestDirectory.Profile();
        var first = Message(account, 2);
        var second = Message(account, 1);
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([first, second], null)), account,
            key => key.Uid == 2 ? throw new MailConnectionException("Broken message.") : Task.FromResult(new MailMessageBody(key, "Fine")));
        await fixture.ReceiveAsync();
        await fixture.Model.SelectAsync(first.Key);
        fixture.Settle();
        Assert.Equal(InboxProblemScope.Message, fixture.Model.ProblemScope);

        await fixture.Model.SelectAsync(second.Key);
        fixture.Settle();
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
        Assert.Equal("", fixture.MessageFeedback.Message);
    }

    /// <summary>
    /// A list problem (a failed or canceled receive, or a failed Load older) stays while a message is
    /// read: the rows are still from an earlier receive, so its notice and Retry stay above the list,
    /// also beside a message that fails to load. While a message loads, the list's Retry keeps its name
    /// (it was named Retry receiving after a failed Load older meanwhile). Each Retry repeats its own
    /// pane's operation, and a new page replaces both problems.
    /// </summary>
    [Theory]
    [InlineData("receive-error", "Retry receiving")]
    [InlineData("receive-canceled", "Retry receiving")]
    [InlineData("load-error", "Retry loading older")]
    public async Task ReadingAMessageKeepsTheListProblemAndItsRetry(string problem, string retryText)
    {
        var account = TestDirectory.Profile();
        MailMessageSummary[] newest = [Message(account, 3), Message(account, 2)];
        bool broken = true;
        TaskCompletionSource<MailMessageBody>? pendingBody = null;
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([], null)), account,
            key => pendingBody is { } pending ? pending.Task
                : key.Uid == 2 && broken ? throw new MailConnectionException("Broken message.") : Task.FromResult(new MailMessageBody(key, $"Body {key.Uid}")));
        // The newest page, then one older page, the last.
        Func<MailInboxCursor?, CancellationToken, Task<MailInboxPage>> working = (cursor, _) => Task.FromResult(cursor is null
            ? new MailInboxPage(newest, new(account.Id, 7, 4, 3, 2)) : new MailInboxPage([Message(account, 1)], null));
        fixture.Receiver.Inbox = working;
        await fixture.ReceiveAsync();
        int pages = 0;
        fixture.Receiver.Inbox = problem switch
        {
            "receive-error" => (_, _) => throw new MailConnectionException("The server did not respond."),
            "receive-canceled" => (_, token) => new TaskCompletionSource<MailInboxPage>().Task.WaitAsync(token),
            _ => (cursor, token) => cursor is null ? working(cursor, token) : throw new MailConnectionException("The server did not respond."),
        };
        var failing = problem == "load-error" ? fixture.Model.LoadOlderAsync() : fixture.Model.ReceiveAsync();
        if (problem == "receive-canceled") fixture.Model.Cancel();
        fixture.Settle();
        await failing;
        var listProblem = fixture.Model.ListProblem;
        Assert.NotNull(listProblem);
        var notice = (fixture.ListFeedback.Kind, fixture.ListFeedback.Message);
        Assert.Equal(listProblem.Text, notice.Message);
        var listRetry = fixture.Button(retryText);
        Assert.True(fixture.IsShown(listRetry) && listRetry.IsEnabled);

        // Reading a message keeps the list's explanation and its Retry.
        await fixture.Model.SelectAsync(newest[0].Key);
        fixture.Settle();
        Assert.Equal("Body 3", fixture.Reader.Text);
        Assert.Same(listProblem, fixture.Model.ListProblem);
        Assert.Equal(InboxProblemScope.List, fixture.Model.ProblemScope);
        Assert.Equal(notice, (fixture.ListFeedback.Kind, fixture.ListFeedback.Message));
        Assert.True(fixture.IsShown(listRetry) && listRetry.IsEnabled);

        // While a message loads, the list's Retry keeps its name, unavailable until the message has loaded.
        pendingBody = new TaskCompletionSource<MailMessageBody>();
        var loading = fixture.Model.SelectAsync(newest[0].Key);
        fixture.Render();
        Assert.True(fixture.Model.IsLoadingMessage);
        Assert.Equal(notice, (fixture.ListFeedback.Kind, fixture.ListFeedback.Message));
        Assert.Equal(retryText, listRetry.Text);
        Assert.True(fixture.IsShown(listRetry) && !listRetry.IsEnabled);
        pendingBody.SetResult(new MailMessageBody(newest[0].Key, "Body 3"));
        pendingBody = null;
        fixture.Settle();
        await loading;
        Assert.Equal(retryText, listRetry.Text);
        Assert.True(listRetry.IsEnabled);

        // A message that fails to load explains why under its header; the list's problem stays above the list.
        await fixture.Model.SelectAsync(newest[1].Key);
        fixture.Settle();
        Assert.Equal(InboxProblemScope.Message, fixture.Model.ProblemScope);
        Assert.Equal("Broken message.", fixture.MessageFeedback.Message);
        Assert.Equal(notice, (fixture.ListFeedback.Kind, fixture.ListFeedback.Message));
        var messageRetry = fixture.Button("Retry loading");
        Assert.True(fixture.IsShown(messageRetry) && messageRetry.IsEnabled);
        Assert.True(fixture.IsShown(listRetry) && listRetry.IsEnabled);

        // Retry loading repeats only the message.
        broken = false;
        fixture.Receiver.Inbox = (cursor, token) => { pages++; return working(cursor, token); };
        messageRetry.Click();
        fixture.Settle();
        Assert.Equal("Body 2", fixture.Reader.Text);
        Assert.Equal(0, pages);
        Assert.Null(fixture.Model.MessageProblem);
        Assert.Same(listProblem, fixture.Model.ListProblem);
        Assert.False(fixture.IsShown(messageRetry));

        // The list's Retry repeats its page, which replaces the problem.
        listRetry.Click();
        fixture.Settle();
        Assert.Equal(1, pages);
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
        Assert.Equal("", fixture.ListFeedback.Message);
        Assert.False(fixture.IsShown(listRetry));
        Assert.Equal(problem == "load-error" ? 3 : 2, fixture.Model.Messages.Count);
    }

    /// <summary>
    /// At the session limit with older mail on the server, Load older is unavailable; the notice above
    /// the list says why and how to go on. Reading a message replaces the status, not that notice, so it
    /// stays while a message loads and is read, until Receive mail returns to the newest page. The status,
    /// which the footer shows, does not repeat it. A mailbox loaded completely has nothing older and no notice.
    /// </summary>
    [Theory]
    [InlineData(600, true)]
    [InlineData(500, false)]
    public async Task TheSessionLimitStaysExplainedAboveTheListWhileAMessageIsRead(int total, bool limited)
    {
        const string limit = "Session limit reached (500 messages). Receive mail again to return to the newest page.";
        var account = TestDirectory.Profile();
        TaskCompletionSource<MailMessageBody>? pendingBody = null;
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([], null)), account,
            key => pendingBody?.Task ?? Task.FromResult(new MailMessageBody(key, $"Body {key.Uid}")));
        // Pages of the newest messages first, as the server numbers them; the last page has no older cursor.
        fixture.Receiver.Inbox = (cursor, _) =>
        {
            int end = cursor?.NextIndex ?? total - 1;
            int start = Math.Max(0, end - InboxViewModel.PageSize + 1);
            var messages = Enumerable.Range(start + 1, end - start + 1).Reverse().Select(uid => Message(account, (uint)uid)).ToArray();
            return Task.FromResult(new MailInboxPage(messages, start == 0 ? null : new(account.Id, 7, (uint)total + 1, total, start - 1)));
        };
        await fixture.ReceiveAsync();
        Assert.Equal("", fixture.ListFeedback.Message);
        var older = fixture.Button("Load older");
        while (fixture.Model.CanLoadOlder)
        {
            await fixture.Model.LoadOlderAsync();
            fixture.Settle();
        }
        Assert.Equal(InboxViewModel.MaximumLoadedMessages, fixture.Model.Messages.Count);
        Assert.False(older.IsEnabled);
        if (!limited)
        {
            Assert.Null(fixture.Model.SessionLimitNotice);
            Assert.Equal("", fixture.ListFeedback.Message);
            return;
        }
        Assert.Equal(limit, fixture.Model.SessionLimitNotice);
        var notice = (FeedbackKind.Information, limit);
        Assert.Equal(notice, (fixture.ListFeedback.Kind, fixture.ListFeedback.Message));
        Assert.True(fixture.IsShown(fixture.ListFeedback));
        Assert.DoesNotContain("Session limit", fixture.Model.Status);

        // While the newest message loads and once it is read, the notice stays and Load older stays unavailable.
        pendingBody = new TaskCompletionSource<MailMessageBody>();
        var loading = fixture.Model.SelectAsync(fixture.Model.Messages[0].Key);
        fixture.Render();
        Assert.True(fixture.Model.IsLoadingMessage);
        Assert.Equal(notice, (fixture.ListFeedback.Kind, fixture.ListFeedback.Message));
        pendingBody.SetResult(new MailMessageBody(fixture.Model.Messages[0].Key, $"Body {total}"));
        pendingBody = null;
        fixture.Settle();
        await loading;
        Assert.Equal($"Body {total}", fixture.Reader.Text);
        Assert.StartsWith("Reading plain text.", fixture.Model.Status);
        Assert.Equal(notice, (fixture.ListFeedback.Kind, fixture.ListFeedback.Message));
        Assert.False(older.IsEnabled);

        // Receive mail returns to the newest page, where Load older is available again.
        await fixture.ReceiveAsync();
        Assert.Equal(InboxViewModel.PageSize, fixture.Model.Messages.Count);
        Assert.Null(fixture.Model.SessionLimitNotice);
        Assert.Equal("", fixture.ListFeedback.Message);
        Assert.True(older.IsEnabled);
    }

    private static MailMessageSummary Message(AccountProfile account, uint uid) => new()
    { Key = new(account.Id, "INBOX", 7, uid), Sender = "sender@example.test", Subject = $"Subject {uid}" };

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UiElement _content;

        public Fixture(Func<CancellationToken, Task<MailInboxPage>> inbox, AccountProfile? account = null, Func<MailMessageKey, Task<MailMessageBody>>? body = null)
        {
            Dispatcher = new TestQueueDispatcher();
            var receiver = Receiver = new TestMailReceiver { Inbox = (_, token) => inbox(token) };
            if (body is not null) receiver.Body = (key, _) => body(key);
            Model = new InboxViewModel(receiver, Dispatcher);
            Model.SetAccount(account ?? TestDirectory.Profile());
            _content = new InboxView(Model).CreateContent();
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host());
            Session.AddRoot(_content);
            Render();
        }

        public TestQueueDispatcher Dispatcher { get; }
        public TestMailReceiver Receiver { get; }
        public InboxViewModel Model { get; }
        public UiSession Session { get; }
        public InlineFeedback ListFeedback => Descendants(_content).OfType<InlineFeedback>().First();
        public InlineFeedback MessageFeedback => Descendants(_content).OfType<InlineFeedback>().Skip(1).First();
        public ScrollableMessageText Reader => Descendants(_content).OfType<ScrollableMessageText>().Single();
        /// <summary>The reader's heading: the subject, or what to do without a message.</summary>
        public StandardLabel ReaderHeading => Descendants(_content).OfType<StandardLabel>().Single(label => label.TextStyle == StandardTextStyle.Title);
        public StandardButton Button(string text) => Descendants(_content).OfType<StandardButton>().Single(button => button.Text == text);

        public bool IsShown(UiElement element)
        {
            for (var current = element; current is not null; current = current.Parent)
                if (current.Visibility != UiVisibility.Visible) return false;
            return true;
        }

        public async Task ReceiveAsync()
        {
            await Model.ReceiveAsync();
            Settle();
        }

        public void Settle()
        {
            Dispatcher.DrainUntil(() => !Model.IsBusy);
            Render();
        }

        public void Render()
        {
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            _content.Dispose();
            Model.Dispose();
        }
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(1000, 700);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
