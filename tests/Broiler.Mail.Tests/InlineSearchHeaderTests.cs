using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// Search is a header at the top of the list pane, never a dialog: Ctrl+K, Ctrl+E, the toolbar and the menu focus
/// its query, Enter searches, and a results strip keeps the pages and Exit search in view while it is active.
/// </summary>
[Collection("UI theme")]
public sealed class InlineSearchHeaderTests
{
    [Theory]
    [InlineData(1100, 720)]
    [InlineData(640, 480)]
    public async Task CtrlKAndCtrlEFocusTheInlineQueryWithoutADialog(int width, int height)
    {
        using var fixture = await Fixture.OpenAsync(width, height);
        var (shell, session) = (fixture.Shell, fixture.Session);
        // AltGr is Ctrl+Alt: it types a character and never opens search.
        Assert.False(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control | KeyboardModifierState.Alt)));
        Assert.Equal(UiVisibility.Collapsed, fixture.Header.Visibility);
        foreach (char key in new[] { 'K', 'E' })
        {
            session.SetFocus(fixture.List);
            Assert.True(fixture.Keyboard.Handle(Key(key, KeyboardModifierState.Control)));
            fixture.Render();
            Assert.Null(shell.ActiveDialog);
            Assert.Null(session.ModalElement);
            Assert.Equal("inbox", shell.ActiveViewId);
            Assert.Equal(UiVisibility.Visible, fixture.Header.Visibility);
            Assert.Same(fixture.Query, session.FocusedElement);
            Assert.True(fixture.Query.IsDescendantOf(fixture.ListPane));
            Assert.True(fixture.Header.Bounds.Bottom <= fixture.List.Bounds.Top + 0.5);
            // Escape with an empty query closes the header and returns to the list.
            Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
            fixture.Render();
            Assert.Equal(UiVisibility.Collapsed, fixture.Header.Visibility);
            Assert.Same(fixture.List, session.FocusedElement);
        }
        Assert.False(shell.ShowView("search-mail"));
        Assert.Equal(0, fixture.InboxCalls);
    }

    [Theory]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    public async Task EnterSearchesAndTheResultsStripStaysWhileBrowsingPages(int width, int height, double scale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(scale));
        try
        {
            using var fixture = await Fixture.OpenAsync(width, height, rows: 501);
            var (shell, session, model) = (fixture.Shell, fixture.Session, fixture.Model.Inbox);
            Assert.True(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control)));
            fixture.Query.Text = "needle";
            fixture.Press(Key(0x0D, "Enter"));
            fixture.Drain();
            Assert.True(model.IsSearchActive);
            Assert.Equal(500, model.Messages.Count);
            Assert.Null(shell.ActiveDialog);
            Assert.Equal(UiVisibility.Visible, fixture.Strip.Visibility);
            Assert.Contains("Page 1", model.SearchSummary);
            // The summary's tooltip says why Receive is unavailable, as Receive's own tooltip does.
            Assert.Contains(Descendants(fixture.Strip).OfType<Broiler.UI.Label.Standard.StandardLabel>(),
                label => label.Text == model.SearchSummary && label.ToolTipText == InboxViewModel.SearchPausesReceiving);

            fixture.Named("Next saved results").Click();
            fixture.Drain();
            Assert.Equal(2, model.SearchPageNumber);
            Assert.Single(model.Messages);
            Assert.Equal("Page 2 · 1 match", model.SearchSummary);
            var viewport = new BRect(0, 0, width, height);
            foreach (var control in new UiElement[] { fixture.Query, fixture.Named("Previous saved results"), fixture.Named("Next saved results"), fixture.Button("Exit search") })
            {
                Assert.Equal(UiVisibility.Visible, control.Visibility);
                Assert.False(control.Bounds.IsEmpty, $"{control} was not laid out.");
                Assert.True(control.Bounds.Left >= viewport.Left - 0.5 && control.Bounds.Right <= viewport.Right + 0.5 &&
                    control.Bounds.Top >= viewport.Top - 0.5 && control.Bounds.Bottom <= viewport.Bottom + 0.5, $"{control} at {control.Bounds} is outside {viewport}.");
            }
            // The strip scrolls in its own area where the pane is too short for it; the area ends above the list.
            var stripArea = Descendants(fixture.ListPane).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Search results strip");
            Assert.True(stripArea.Bounds.Bottom <= fixture.List.Bounds.Top + 0.5, $"The strip's area at {stripArea.Bounds} covers the list at {fixture.List.Bounds}.");
            // The list keeps two rows below the header and the strip. A 640x480 window at 200 % text leaves the whole
            // list pane one row high, so there only the strip and its commands are checked to stay in view.
            if (fixture.ListPane.Bounds.Height >= fixture.Strip.Bounds.Height + 3 * fixture.List.EffectiveItemHeight)
                Assert.True(fixture.List.Bounds.Height >= 2 * fixture.List.EffectiveItemHeight - 0.5,
                    $"The list is {fixture.List.Bounds.Height} high of {fixture.ListPane.Bounds.Height}, below a header of {fixture.Header.Bounds.Height} with a strip of {fixture.Strip.Bounds.Height}; rows are {fixture.List.EffectiveItemHeight}.");
            else Assert.True(width == 640 && scale == 2.0, $"The list pane is only {fixture.ListPane.Bounds.Height} high.");
            var receive = fixture.Button("Receive mail");
            Assert.False(receive.IsEnabled);
            Assert.Equal("Exit search to receive mail", receive.ToolTipText);

            fixture.Named("Previous saved results").Click();
            fixture.Drain();
            Assert.Equal(500, model.Messages.Count);
            Assert.Equal(UiVisibility.Visible, fixture.Strip.Visibility);
            Assert.Equal(0, fixture.InboxCalls);
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    [Fact]
    public async Task EscapeClearsTheQueryThenExitsSearchAndRestoresFocus()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        var (session, model) = (fixture.Session, fixture.Model.Inbox);
        int rows = model.Messages.Count;
        session.SetFocus(fixture.List);
        Assert.True(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control)));
        fixture.Query.Text = "needle";
        fixture.Press(Key(0x0D, "Enter"));
        fixture.Drain();
        Assert.True(model.IsSearchActive);
        Assert.Same(fixture.Query, session.FocusedElement);

        // The first Escape clears the query; the search and its results stay.
        Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
        fixture.Render();
        Assert.Equal("", fixture.Query.Text);
        Assert.True(model.IsSearchActive);
        Assert.Equal(UiVisibility.Visible, fixture.Header.Visibility);
        Assert.Same(fixture.Query, session.FocusedElement);

        // The second exits search, reloads the folder's saved mail and returns focus to where it was.
        Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
        fixture.Drain();
        Assert.False(model.IsSearchActive);
        Assert.Equal(rows, model.Messages.Count);
        Assert.Equal(UiVisibility.Collapsed, fixture.Header.Visibility);
        Assert.Same(fixture.List, session.FocusedElement);
        Assert.Equal(0, fixture.InboxCalls);
    }

    [Fact]
    public async Task EscapeWhileSearchingCancelsFirst()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720, held: true);
        var (session, model) = (fixture.Shell, fixture.Model.Inbox);
        Assert.True(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control)));
        fixture.Query.Text = "needle";
        fixture.Press(Key(0x0D, "Enter"));
        fixture.Render();
        Assert.True(model.IsBusy);
        // The query stays enabled and focused while the search runs.
        Assert.Same(fixture.Query, fixture.Session.FocusedElement);

        Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
        fixture.Drain();
        Assert.False(model.IsBusy);
        Assert.True(model.IsSearchActive);
        Assert.Equal(UiVisibility.Visible, fixture.Header.Visibility);
        Assert.Equal("needle", fixture.Query.Text);
        Assert.Null(session.ActiveDialog);
    }

    [Fact]
    public async Task CompactReaderCtrlKReturnsToTheList()
    {
        using var fixture = await Fixture.OpenAsync(640, 480);
        var (shell, session, model) = (fixture.Shell, fixture.Session, fixture.Model.Inbox);
        await model.SelectAsync(model.Messages[0].Key);
        fixture.Drain();
        Assert.True(shell.Inbox.OpenSelected());
        fixture.Drain();
        Assert.False(shell.Inbox.ShowsPaneOf(InboxProblemScope.List));
        Assert.True(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control)));
        fixture.Render();
        Assert.True(shell.Inbox.ShowsPaneOf(InboxProblemScope.List));
        Assert.Same(fixture.Query, session.FocusedElement);
        Assert.False(fixture.List.Bounds.IsEmpty);
        Assert.Null(shell.ActiveDialog);
        // Closing the header goes to the list the reader left for it.
        Assert.True(fixture.Keyboard.Handle(Key(0x1B)));
        fixture.Render();
        Assert.Same(fixture.List, session.FocusedElement);
    }

    [Fact]
    public async Task CtrlFWithoutAMessageFallsBackToInlineSearch()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        Assert.Null(fixture.Model.Inbox.Body);
        Assert.True(fixture.Keyboard.Handle(Key('F', KeyboardModifierState.Control)));
        fixture.Render();
        Assert.Null(fixture.Shell.ActiveDialog);
        Assert.Same(fixture.Query, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task ClearButtonClearsTheTextThenExitsSearch()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        var model = fixture.Model.Inbox;
        fixture.Button("Search saved mail").Click();
        fixture.Query.Text = "needle";
        fixture.Button("Search").Click();
        fixture.Drain();
        Assert.True(model.IsSearchActive);
        var clear = fixture.Button("Clear search");
        clear.Click();
        fixture.Render();
        Assert.Equal("", fixture.Query.Text);
        Assert.Same(fixture.Query, fixture.Session.FocusedElement);
        Assert.True(model.IsSearchActive);
        clear.Click();
        fixture.Drain();
        Assert.False(model.IsSearchActive);
        Assert.Equal(UiVisibility.Collapsed, fixture.Header.Visibility);
        Assert.True(fixture.Session.FocusedElement?.CanFocus);
    }

    [Fact]
    public async Task MenuSearchItemOpensTheInlineHeader()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720);
        var (shell, session) = (fixture.Shell, fixture.Session);
        var file = shell.Menu.Items[0];
        int groupIndex = file.Children.ToList().FindIndex(item => item.Id == "mailbox-tools");
        var group = file.Children[groupIndex];
        Assert.DoesNotContain(group.Children, item => item.Id is "search-mail" or "message-actions" or "find-message");
        int searchIndex = group.Children.ToList().FindIndex(item => item.Id == "search");
        Assert.True(searchIndex >= 0);
        Assert.Equal("Search saved mail", group.Children[searchIndex].Text);
        Assert.Equal("Ctrl+K", group.Children[searchIndex].Accelerator);
        Assert.True(group.Children[searchIndex].IsEnabled);

        fixture.Keyboard.Execute(MailCommand.OpenMenu);
        Assert.True(shell.Menu.SetSelectedPath([0, groupIndex, searchIndex]));
        Assert.True(session.DispatchInput(Key(0x0D, "Enter")));
        fixture.Drain();
        Assert.Null(shell.ActiveDialog);
        Assert.Equal("inbox", shell.ActiveViewId);
        Assert.Same(fixture.Query, session.FocusedElement);
        Assert.False(shell.ShowView("search-mail"));
    }

    [Fact]
    public async Task FailedSearchKeepsExitSearchReachable()
    {
        using var fixture = await Fixture.OpenAsync(1100, 720, held: true);
        var model = fixture.Model.Inbox;
        fixture.Cache!.FailNext = true;
        Assert.True(fixture.Keyboard.Handle(Key('K', KeyboardModifierState.Control)));
        fixture.Query.Text = "needle";
        fixture.Press(Key(0x0D, "Enter"));
        fixture.Drain();
        Assert.False(model.IsBusy);
        Assert.True(model.IsSearchActive);
        Assert.NotNull(model.ListProblem);
        Assert.Equal(UiVisibility.Visible, fixture.Strip.Visibility);
        var exit = fixture.Button("Exit search");
        Assert.True(exit.IsEnabled);
        Assert.Equal("Retry saved search", fixture.Button("Retry saved search").Text);
        exit.Click();
        fixture.Drain();
        Assert.False(model.IsSearchActive);
        Assert.Equal(UiVisibility.Collapsed, fixture.Header.Visibility);
    }

    [Fact]
    public async Task StandaloneEnterSearches()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, Rows(account, 3), DateTimeOffset.UtcNow);
        var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        using var root = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(1100, 720));
        session.AddRoot(root); session.RenderFrame();
        Descendants(root).OfType<StandardButton>().Single(button => button.Text == "Search saved mail").Click();
        var query = Descendants(root).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Saved mail search text");
        Assert.Same(query, session.FocusedElement);
        query.Text = "needle";
        session.DispatchInput(Key(0x0D, "Enter"));
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        Assert.True(model.IsSearchActive);
        Assert.Equal(3, model.Messages.Count);
        // The standalone toolbar stays in view while searching.
        Assert.False(Descendants(root).OfType<StandardButton>().Single(button => button.Text == "Receive mail").Bounds.IsEmpty);
    }

    [Fact]
    public async Task SearchSummaryDescribesPageScopeAndPausedReceiving()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await cache.SaveSummariesAsync(new MailboxKey(account.Id, "INBOX", 7), Rows(account, 2), DateTimeOffset.UtcNow);
        var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        Assert.Null(model.SearchSummary);
        model.SetSearch("needle"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal("Page 1 · 2 matches", model.SearchSummary);
        Assert.Contains("Exit search to receive", model.SearchNotice);
        Assert.DoesNotContain("Next saved results", model.SearchNotice);
        model.SetSearchAllSavedFolders(true); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal("Page 1 · 2 matches in all folders", model.SearchSummary);
        await model.ClearSearchAsync(); dispatcher.Drain();
        Assert.Null(model.SearchSummary);
    }

    private static MailMessageSummary[] Rows(AccountProfile account, int count) =>
        Enumerable.Range(1, count).Select(uid => new MailMessageSummary
        {
            Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "sender@example.test", Subject = "needle",
            ReceivedAt = new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.Zero).AddMinutes(-uid),
        }).ToArray();

    private static UiInputEvent Key(char key, KeyboardModifierState modifiers) => Key(key, "VirtualKey:" + (int)key, modifiers);

    private static UiInputEvent Key(int code, string? name = null, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        ReaderMenus.Key(code, name ?? (code == 0x1B ? "Escape" : "VirtualKey:" + code), modifiers);

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>The shell over a saved INBOX, with no message selected.</summary>
    private sealed class Fixture(MailShellView shell, MailShellViewModel model, UiSession session, TestDirectory directory, Func<int> inboxCalls, HeldSearchCache? cache) : IDisposable
    {
        public MailShellView Shell { get; } = shell;
        public MailShellViewModel Model { get; } = model;
        public UiSession Session { get; } = session;
        public HeldSearchCache? Cache { get; } = cache;
        public int InboxCalls => inboxCalls();
        public MailKeyboardNavigation Keyboard { get; } = shell.CreateKeyboardNavigation(session);
        private UiElement Inbox => Shell.GetContent("inbox");
        public StandardListView List => Descendants(Inbox).OfType<StandardListView>().Single();
        public UiElement ListPane => List.Parent!;
        public StandardEdit Query => Descendants(Inbox).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Saved mail search text");
        public FirstTakesRestStack Header => Descendants(ListPane).OfType<FirstTakesRestStack>().Single(stack => Query.IsDescendantOf(stack));
        public StandardToolbar Strip => Descendants(Inbox).OfType<StandardToolbar>().Single(row => row.AccessibleName == "Saved search results");
        public StandardButton Button(string text) => Descendants(Inbox).OfType<StandardButton>().Single(button => button.Text == text);
        public StandardButton Named(string name) => Descendants(Inbox).OfType<StandardButton>().Single(button => button.AccessibleName == name);

        public void Render() { Session.RenderFrame(); Session.RenderFrame(); }

        public void Drain()
        {
            Cache?.Release.TrySetResult();
            ((TestQueueDispatcher)Session.Dispatcher).DrainUntil(() => !Model.Inbox.IsBusy);
            Render();
        }

        /// <summary>As the host does: a key the shell does not take reaches the focused control.</summary>
        public void Press(UiInputEvent input)
        {
            if (!Keyboard.Handle(input)) Session.DispatchInput(input);
        }

        public static async Task<Fixture> OpenAsync(int width, int height, int rows = 30, bool held = false)
        {
            var directory = new TestDirectory();
            var account = TestDirectory.Profile();
            var storage = new SqliteMailCache(directory.File("mail.db"));
            var saved = Rows(account, rows);
            var mailbox = new MailboxKey(account.Id, "INBOX", 7);
            for (int start = 0; start < saved.Length; start += 500)
                await storage.SaveSummariesAsync(mailbox, saved.Skip(start).Take(500).ToArray(), DateTimeOffset.UtcNow);
            int calls = 0;
            var receiver = new TestMailReceiver
            {
                Inbox = (_, _) => { calls++; return Task.FromResult(new MailInboxPage([], null)); },
                Body = (key, _) => Task.FromResult(new MailMessageBody(key, "A saved message body.")),
            };
            var cache = held ? new HeldSearchCache(storage) : null;
            var dispatcher = new TestQueueDispatcher();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher, (IMailCache?)cache ?? storage),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
            shell.Attach(session);
            await model.Inbox.CacheLoadTask;
            dispatcher.Drain();
            var fixture = new Fixture(shell, model, session, directory, () => calls, cache);
            fixture.Render();
            Assert.NotEmpty(model.Inbox.Messages);
            return fixture;
        }

        public void Dispose()
        {
            Session.Dispose();
            Shell.Dispose();
            directory.Dispose();
        }
    }

    /// <summary>A saved-mail cache whose search waits for <see cref="Release"/>, or fails once.</summary>
    internal sealed class HeldSearchCache(SqliteMailCache storage) : IMailCache, IMailLocalSearch
    {
        public bool FailNext { get; set; }
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MailHeaderSearchResult> SearchHeadersAsync(MailHeaderSearchQuery query, CancellationToken token = default)
        {
            if (FailNext) { FailNext = false; throw new IOException("Synthetic search read failure."); }
            var release = Release;
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return await storage.SearchHeadersAsync(query, token).ConfigureAwait(false);
        }
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account, string mailbox, CancellationToken token = default) => storage.GetLatestMailboxAsync(account, mailbox, token);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox, int count, uint? after = null, CancellationToken token = default) => storage.ReadSummariesAsync(mailbox, count, after, token);
        public Task SaveSummariesAsync(MailboxKey mailbox, IReadOnlyList<MailMessageSummary> rows, DateTimeOffset time, CancellationToken token = default) => storage.SaveSummariesAsync(mailbox, rows, time, token);
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox, uint uid, CancellationToken token = default) => storage.ReadBodyAsync(mailbox, uid, token);
        public Task SaveBodyAsync(MailboxKey mailbox, MailMessageBody body, CancellationToken token = default) => storage.SaveBodyAsync(mailbox, body, token);
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
