using System.Collections.Concurrent;
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
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class InboxWorkflowTests
{
    [Fact]
    public async Task RefreshReplacesPagesAndBodyIsFetchedOnlyWhenSelected()
    {
        var account = TestDirectory.Profile();
        var newest = Message(account, 2);
        var older = Message(account, 1);
        int bodyCalls = 0;
        var receiver = new TestMailReceiver
        {
            Inbox = (cursor, _) => Task.FromResult(cursor is null
                ? new MailInboxPage([newest], new(account.Id, 7, 3, 2, 0))
                : new MailInboxPage([older], null)),
            Body = (key, _) => { bodyCalls++; return Task.FromResult(new MailMessageBody(key, "Body")); },
        };
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        Assert.Equal(0, bodyCalls);
        Assert.True(model.CanLoadOlder);
        await model.LoadOlderAsync();
        Assert.Equal(2, model.Messages.Count);
        Assert.False(model.CanLoadOlder);
        await model.SelectAsync(newest.Key);
        Assert.Equal("Body", model.Body!.PlainText);
        Assert.Equal(1, bodyCalls);
        Assert.False(model.SelectedMessage!.IsRead);
        await model.ReceiveAsync();
        Assert.Single(model.Messages);
        Assert.Null(model.Body);
        Assert.Null(model.SelectedMessage);
    }

    [Fact]
    public async Task FailedRefreshPreservesLoadedMailAndRetryCanShowEmptyInbox()
    {
        var account = TestDirectory.Profile();
        var receiver = new TestMailReceiver { Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 1)], null)) };
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        await model.SelectAsync(model.Messages[0].Key);
        receiver.Inbox = (_, _) => throw new MailConnectionException("Connection interrupted.");
        await model.ReceiveAsync();
        Assert.Contains("Connection interrupted", model.Status);
        Assert.Single(model.Messages);
        Assert.NotNull(model.Body);
        Assert.True(model.CanReceive);
        receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage([], null));
        await model.ReceiveAsync();
        Assert.Empty(model.Messages);
        Assert.Contains("empty", model.Status);
    }

    [Fact]
    public async Task ResultsArePublishedOnlyThroughTheUiDispatcher()
    {
        var account = TestDirectory.Profile();
        var receiver = new TestMailReceiver { Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 1)], null)) };
        var dispatcher = new QueuedDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher);
        model.SetAccount(account);
        await model.ReceiveAsync();
        Assert.True(model.IsBusy);
        Assert.Empty(model.Messages);
        dispatcher.Drain();
        Assert.False(model.IsBusy);
        Assert.Single(model.Messages);
    }

    [Fact]
    public async Task OldBodyCompletionCannotReplaceANewerSelection()
    {
        var account = TestDirectory.Profile();
        var first = Message(account, 1);
        var second = Message(account, 2);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([second, first], null)),
            Body = (key, token) =>
            {
                if (key == second.Key) return Task.FromResult(new MailMessageBody(key, "Second body"));
                firstToken = token;
                firstStarted.SetResult();
                return firstResult.Task; // Deliberately ignores cancellation to check stale completion handling.
            },
        };
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        var oldRequest = model.SelectAsync(first.Key);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await model.SelectAsync(second.Key);
        Assert.True(firstToken.IsCancellationRequested);
        firstResult.SetResult(new(first.Key, "Wrong body"));
        await oldRequest;
        Assert.Equal(second, model.SelectedMessage);
        Assert.Equal("Second body", model.Body!.PlainText);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("dispose")]
    public async Task PendingPageCannotPublishAfterCancellationAccountEditOrClose(string action)
    {
        var account = TestDirectory.Profile();
        var dispatcher = new QueuedDispatcher();
        using var model = new InboxViewModel(new TestMailReceiver
        { Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 1)], null)) }, dispatcher);
        model.SetAccount(account);
        await model.ReceiveAsync(); // Result queued; cancellation must still prevent the pending commit.
        if (action == "cancel") model.Cancel();
        if (action == "account") model.SetAccount(account with { IncomingServer = account.IncomingServer with { Host = "other.example.test" } });
        if (action == "dispose") model.Dispose();
        dispatcher.Drain();
        Assert.Empty(model.Messages);
        Assert.False(model.IsBusy);
        if (action == "cancel") Assert.Contains("canceled", model.Status);
    }

    [Theory]
    [InlineData(1100, 720)]
    [InlineData(640, 480)]
    public async Task ControlsSelectMailAndRenderLongTextWithAWorkingScrollbar(int width, int height)
    {
        var account = TestDirectory.Profile();
        var message = Message(account, 1);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([message], null)),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join('\n', Enumerable.Repeat("Hello & welcome. This is a line of mail text.", 100))) { IsHtmlFallback = true }),
        };
        var dispatcher = new QueuedDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().Build(new HeadlessHost(width, height));
        session.AddRoot(content);
        await model.ReceiveAsync();
        dispatcher.Drain();
        var list = Descendants(content).OfType<StandardListView>().Single();
        Assert.Contains("Unread", Assert.Single(list.Items).Text);
        // Drive the selection event, then wait for the posted completion without touching controls off-thread.
        list.SelectIndex(0);
        await dispatcher.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        dispatcher.Drain();
        Assert.NotNull(model.Body);
        _ = session.RenderFrame();
        var scroll = Descendants(content).OfType<StandardScrollView>().Single();
        Assert.True(scroll.HasVerticalScrollbar);
        Assert.False(scroll.HasHorizontalScrollbar);
        Assert.True(scroll.ScrollToEnd());
        Assert.True(scroll.VerticalOffset > 0);
        var text = Descendants(content).OfType<ScrollableMessageText>().Single();
        Assert.Contains("Hello & welcome", text.Text);
        Assert.Contains("Text extracted from HTML", text.Text);
        Assert.Contains(Descendants(content).OfType<StandardLabel>(), label => label.DisplayText.Contains("Unread on server", StringComparison.Ordinal));
        Assert.True(Descendants(content).OfType<StandardButton>().Single(button => button.Text == "Receive mail").IsEnabled);
    }

    private static InboxViewModel Model(TestMailReceiver receiver, AccountProfile account)
    {
        var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account);
        return model;
    }

    private static MailMessageSummary Message(AccountProfile account, uint uid) => new()
    { Key = new(account.Id, "INBOX", 7, uid), Sender = "sender@example.test", Subject = "Subject & details", ReceivedAt = DateTimeOffset.UtcNow };

    private static IEnumerable<UiElement> Descendants(UiElement element) => element.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public TaskCompletionSource Posted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CheckAccess() => false;
        public void Post(Action callback) { _queue.Enqueue(callback); Posted.TrySetResult(); }
        public void Drain()
        {
            while (_queue.TryDequeue(out var callback)) callback();
            Posted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class HeadlessHost(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
