using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Text;
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
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Splitter.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

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
    public void ResultsArePublishedOnlyThroughTheUiDispatcher()
    {
        var account = TestDirectory.Profile();
        int uiThread = Environment.CurrentManagedThreadId;
        int receiverThread = uiThread;
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) =>
            {
                receiverThread = Environment.CurrentManagedThreadId;
                return Task.FromResult(new MailInboxPage([Message(account, 1)], null));
            },
        };
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(receiver, ui.Dispatcher);
        var changedOn = new List<int>();
        model.Changed += (_, _) => changedOn.Add(Environment.CurrentManagedThreadId);
        model.SetAccount(account);
        _ = model.ReceiveAsync();
        ui.WaitForPost();
        Assert.True(model.IsBusy);
        Assert.Empty(model.Messages);
        Assert.Equal(1, ui.Dispatcher.Drain());
        Assert.False(model.IsBusy);
        Assert.Single(model.Messages);
        // The receiver ran on a worker, and every change was published on the thread that owns the dispatcher.
        Assert.NotEqual(uiThread, receiverThread);
        Assert.All(changedOn, thread => Assert.Equal(uiThread, thread));
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
    public void PendingPageCannotPublishAfterCancellationAccountEditOrClose(string action)
    {
        var account = TestDirectory.Profile();
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(new TestMailReceiver
        { Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 1)], null)) }, ui.Dispatcher);
        model.SetAccount(account);
        _ = model.ReceiveAsync();
        ui.WaitForPost(); // Result queued; cancellation must still prevent the pending commit.
        if (action == "cancel") model.Cancel();
        if (action == "account") model.SetAccount(account with { IncomingServer = account.IncomingServer with { Host = "other.example.test" } });
        if (action == "dispose") model.Dispose();
        Assert.Equal(1, ui.Dispatcher.Drain());
        Assert.Empty(model.Messages);
        Assert.False(model.IsBusy);
        if (action == "cancel") Assert.Contains("canceled", model.Status);
    }

    [Theory]
    [InlineData(1100, 720)]
    [InlineData(640, 480)]
    public void ControlsSelectMailAndRenderLongTextWithAWorkingScrollbar(int width, int height)
    {
        var account = TestDirectory.Profile();
        var message = Message(account, 1);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([message], null)),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join('\n', Enumerable.Repeat("Hello & welcome. This is a line of mail text.", 100))) { IsHtmlFallback = true }),
        };
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(receiver, ui.Dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        // One dispatcher for the session and the view models, as in the native host.
        using var session = new StandardUiSessionBuilder().WithDispatcher(ui.Dispatcher).Build(new HeadlessHost(width, height));
        session.AddRoot(content);
        _ = model.ReceiveAsync();
        ui.WaitForPost();
        ui.Dispatcher.Drain();
        var list = Descendants(content).OfType<StandardListView>().Single();
        Assert.Contains("Unread", Assert.Single(list.Items).Text);
        // Drive the selection event, then wait for the posted completion without touching controls off-thread.
        list.SelectIndex(0);
        ui.WaitForPost();
        ui.Dispatcher.Drain();
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

    [Fact]
    public void SelectingMessageShowsDistinctLoadingStateWhileFetchingBody()
    {
        var account = TestDirectory.Profile();
        var message = Message(account, 1);
        var bodyTask = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([message], null)),
            Body = (_, _) => bodyTask.Task,
        };
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(receiver, ui.Dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(ui.Dispatcher).Build(new HeadlessHost(800, 600));
        session.AddRoot(content);
        _ = model.ReceiveAsync();
        ui.WaitForPost();
        ui.Dispatcher.Drain();

        var list = Descendants(content).OfType<StandardListView>().Single();
        list.SelectIndex(0);
        // While body is fetching, model is busy and text view shows distinct loading message
        Assert.True(model.IsBusy);
        var text = Descendants(content).OfType<ScrollableMessageText>().Single();
        Assert.Equal("Loading message body…", text.Text);

        bodyTask.SetResult(new MailMessageBody(message.Key, "Loaded text content"));
        ui.WaitForPost();
        ui.Dispatcher.Drain();
        Assert.False(model.IsBusy);
        Assert.Contains("Loaded text content", text.Text);
    }

    [Fact]
    public void InboxView_Splitter_Supports_Resizing_Keyboard_Navigation_And_Minima()
    {
        var account = TestDirectory.Profile();
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(new TestMailReceiver(), ui.Dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(ui.Dispatcher).Build(new HeadlessHost(1000, 600));
        session.AddRoot(content);
        _ = session.RenderFrame();

        var split = Descendants(content).OfType<StandardSplitContainer>().Single();
        Assert.Equal(UiSplitterOrientation.Vertical, split.Orientation);
        Assert.Equal(0.35, split.SplitterFraction, 2);
        Assert.Equal(0.35, model.SplitterFraction, 2);

        // Keyboard navigation on the splitter grip
        session.SetFocus(split.Splitter);
        var route = new StandardInputRoute(session);
        route.Dispatch(Key("Right", BVirtualKey.Right));
        _ = session.RenderFrame();

        Assert.True(split.SplitterFraction > 0.35);
        Assert.Equal(split.SplitterFraction, model.SplitterFraction);

        // Minima enforcement
        split.SplitterFraction = 0.01;
        _ = session.RenderFrame();
        Assert.True(split.FirstPane!.Bounds.Width >= split.FirstPaneMinimumSize - 1);

        split.SplitterFraction = 0.99;
        _ = session.RenderFrame();
        Assert.True(split.SecondPane!.Bounds.Width >= split.SecondPaneMinimumSize - 1);
    }

    [Fact]
    public void InboxView_Splitter_Collapsed_Pane_Behavior()
    {
        var account = TestDirectory.Profile();
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(new TestMailReceiver(), ui.Dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(ui.Dispatcher).Build(new HeadlessHost(1000, 600));
        session.AddRoot(content);
        _ = session.RenderFrame();

        var split = Descendants(content).OfType<StandardSplitContainer>().Single();

        // Collapse first pane (list)
        split.CollapseFirstPane();
        _ = session.RenderFrame();
        Assert.True(split.IsFirstPaneCollapsed);
        Assert.Equal(BRect.Empty, split.FirstPane!.Bounds);
        Assert.Equal(BRect.Empty, split.Splitter.Bounds);
        Assert.True(split.SecondPane!.Bounds.Width >= 900);

        // Collapse second pane (reading)
        split.CollapseSecondPane();
        _ = session.RenderFrame();
        Assert.True(split.IsSecondPaneCollapsed);
        Assert.False(split.IsFirstPaneCollapsed);
        Assert.True(split.FirstPane!.Bounds.Width >= 900);
        Assert.Equal(BRect.Empty, split.SecondPane!.Bounds);

        // Restore
        split.RestorePanes();
        _ = session.RenderFrame();
        Assert.False(split.IsFirstPaneCollapsed);
        Assert.False(split.IsSecondPaneCollapsed);
        Assert.True(split.FirstPane!.Bounds.Width > 0);
        Assert.True(split.SecondPane!.Bounds.Width > 0);
    }

    [Fact]
    public void InboxView_Toolbar_Wraps_When_Narrow()
    {
        var account = TestDirectory.Profile();
        using var ui = new TestUiQueue();
        using var model = new InboxViewModel(new TestMailReceiver(), ui.Dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(ui.Dispatcher).Build(new HeadlessHost(300, 600));
        session.AddRoot(content);
        _ = session.RenderFrame();

        var toolbar = Descendants(content).OfType<StandardToolbar>().Single();
        Assert.Equal(UiToolbarOverflow.Wrap, toolbar.Overflow);
        Assert.Empty(toolbar.OverflowItems);

        var buttons = Descendants(toolbar).OfType<StandardButton>().ToArray();
        Assert.Equal(4, buttons.Length);
        // Under 300px width, 4 buttons wrap into at least 2 rows
        Assert.True(buttons.Select(b => b.Bounds.Top).Distinct().Count() >= 2);
    }

    [Fact]
    public async Task MessageReader_Is_Selectable_Readable_And_Supports_Copy()
    {
        var account = TestDirectory.Profile();
        var msg = Message(account, 1);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([msg], null)),
            Body = (_, _) => Task.FromResult(new MailMessageBody(msg.Key, "Hello, this is a selectable message body!")),
        };
        var dispatcher = new ImmediateUiDispatcher();
        using var model = new InboxViewModel(receiver, dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model).CreateContent();
        var host = new HeadlessHost(1000, 600);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        session.AddRoot(content);
        _ = session.RenderFrame();

        await model.ReceiveAsync();
        await model.SelectAsync(msg.Key);
        _ = session.RenderFrame();

        var reader = Descendants(content).OfType<ScrollableMessageText>().Single();
        Assert.NotNull(reader.Editor);
        Assert.True(reader.Editor.IsReadOnly);
        Assert.Contains("selectable message body", reader.Text);

        // Select all text via Ctrl+A
        session.SetFocus(reader.Editor);
        var route = new StandardInputRoute(session);
        route.Dispatch(Key("A", BVirtualKey.A, KeyboardModifierState.Control));

        Assert.False(reader.Editor.Selection.IsEmpty);
        Assert.Equal(reader.Editor.Document.End, reader.Editor.Selection.Focus);

        // Copy via Ctrl+C
        route.Dispatch(Key("C", BVirtualKey.C, KeyboardModifierState.Control));
        Assert.Equal("Hello, this is a selectable message body!", host.ClipboardText);

        // Typing does not modify read-only reader
        route.Dispatch(new TextInputEvent(Header(), "mutated text", Source: InputEventSource.Synthetic));
        Assert.Equal("Hello, this is a selectable message body!", reader.Text);

        // Tab key yields to focus navigation (returns false)
        bool tabHandled = route.Dispatch(Key("Tab", BVirtualKey.Tab));
        Assert.False(tabHandled);
    }

    private static InputEventHeader Header() =>
        new(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);

    private static KeyboardKeyEvent Key(string name, int native, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        new(Header(), KeyboardKey.FromName(name), KeyboardKeyTransition.Down,
            modifiers, native, 0, 0, false, false, Source: InputEventSource.Synthetic);

    private static InboxViewModel Model(TestMailReceiver receiver, AccountProfile account)
    {
        var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account);
        return model;
    }

    private static MailMessageSummary Message(AccountProfile account, uint uid) => new()
    { Key = new(account.Id, "INBOX", 7, uid), Sender = "sender@example.test", Subject = "Subject & details", ReceivedAt = DateTimeOffset.UtcNow };

    private static IEnumerable<UiElement> Descendants(UiElement element) => element.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    /// <summary>The host's queued dispatcher, owned by the test thread as the native window owns its own.</summary>
    private sealed class TestUiQueue : IDisposable
    {
        private readonly SemaphoreSlim _woken = new(0);
        public TestUiQueue() => Dispatcher = new(() => _woken.Release());
        public StandardQueuedUiDispatcher Dispatcher { get; }

        // Block instead of awaiting: an await can resume on another thread, and only the owner may drain.
        public void WaitForPost() => Assert.True(_woken.Wait(TimeSpan.FromSeconds(5)), "Nothing was posted to the UI dispatcher.");
        public void Dispose() => _woken.Dispose();
    }

    private sealed class HeadlessHost(int width, int height) : IUiHost, IUiClipboardHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public string ClipboardText { get; set; } = string.Empty;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
        public bool TryGetText(out string text) { text = ClipboardText; return true; }
        public void SetText(string text) => ClipboardText = text;
    }
}
