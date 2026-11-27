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
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

/// <summary>The three human search scopes, the engine the cache picks for them, and the orthogonal filters.</summary>
public sealed class SearchScopeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EverywhereUsesIndexWhenReadyAndLiteralOtherwise(bool indexed)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = AccountId.New();
        var box = new MailboxKey(account, "INBOX", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1, "zebra tail"), Row(box, 2, "other"), Row(box, 3, "other")], DateTimeOffset.UtcNow);
        // Uid 2 matches only in its retained text, beyond the 32,000-character preview; uid 3 has headers only.
        await cache.SaveBodyAsync(box, new(Key(box, 2), "short preview") { RetainedBodyText = new string('x', 34000) + " zebra tail" });
        if (indexed) await cache.RebuildSearchIndexAsync();
        var query = new MailHeaderSearchQuery(account, "zebra tail") { Scope = MailSearchScope.Everywhere, MailboxId = "INBOX" };
        var result = await cache.SearchHeadersAsync(query);
        Assert.Equal([2u, 1u], result.Messages.Select(row => row.Key.Uid));
        Assert.Equal(indexed ? MailSearchEngine.Indexed : MailSearchEngine.Literal, result.Engine); Assert.True(result.HasHeaderOnlyMessages);
        Assert.Equal(MailSearchEngine.Literal, (await cache.SearchHeadersAsync(query with { Engine = MailSearchEngine.Literal })).Engine);
        if (!indexed)
        {
            await Assert.ThrowsAsync<MailSearchIndexUnavailableException>(() => cache.SearchHeadersAsync(query with { Engine = MailSearchEngine.Indexed }));
            return;
        }
        await cache.DisableSearchIndexAsync();
        var literal = await cache.SearchHeadersAsync(query);
        Assert.Equal([2u, 1u], literal.Messages.Select(row => row.Key.Uid)); Assert.Equal(MailSearchEngine.Literal, literal.Engine);
        await Assert.ThrowsAsync<MailSearchIndexUnavailableException>(() => cache.SearchHeadersAsync(query with { Engine = MailSearchEngine.Indexed }));
    }

    [Fact]
    public async Task EverywhereKeepsSubstringHeaderMatchingWithTheIndex()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1, "Invoice-2026"), Row(box, 2, "other")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 2), "the invoice is attached"));
        var query = new MailHeaderSearchQuery(box.AccountId, "voice") { Scope = MailSearchScope.Everywhere };
        // All folders list newest first (uid 1 is the newer). Without the index, both the subject and the body match as substrings.
        Assert.Equal([1u, 2u], (await cache.SearchHeadersAsync(query)).Messages.Select(row => row.Key.Uid));
        await cache.RebuildSearchIndexAsync();
        // With it, the subject still matches as a substring, but the body only as a whole indexed word.
        var indexed = await cache.SearchHeadersAsync(query);
        Assert.Equal(MailSearchEngine.Indexed, indexed.Engine); Assert.Equal(1u, Assert.Single(indexed.Messages).Key.Uid);
        Assert.Equal([1u, 2u], (await cache.SearchHeadersAsync(query with { Text = "invoice" })).Messages.Select(row => row.Key.Uid));
        // A punctuation-only phrase has no indexed words: it neither fails nor matches text, and headers still match it.
        Assert.Equal([1u, 2u], (await cache.SearchHeadersAsync(query with { Text = "@" })).Messages.Select(row => row.Key.Uid));
    }

    [Fact]
    public async Task MessageBodyScopeExcludesHeaderOnlyRowsAndIgnoresHeaders()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1, "needle"), Row(box, 2, "other"), Row(box, 3, "other") with { IsRead = true }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 2), "a needle in the text"));
        var body = new MailHeaderSearchQuery(box.AccountId, "needle") { Scope = MailSearchScope.MessageBody };
        var result = await cache.SearchHeadersAsync(body);
        Assert.Equal(2u, Assert.Single(result.Messages).Key.Uid); Assert.True(result.HasHeaderOnlyMessages);
        Assert.Equal(2u, Assert.Single((await cache.SearchHeadersAsync(body with { Text = "", IsRead = false })).Messages).Key.Uid);
        var everywhere = await cache.SearchHeadersAsync(body with { Text = "", IsRead = false, Scope = MailSearchScope.Everywhere });
        Assert.Equal([1u, 2u], everywhere.Messages.Select(row => row.Key.Uid)); Assert.False(everywhere.HasHeaderOnlyMessages);
        Assert.False((await cache.SearchHeadersAsync(body with { Scope = MailSearchScope.SenderOrSubject })).HasHeaderOnlyMessages);
    }

    [Fact]
    public async Task SenderOrSubjectScopeNeverReadsBodies()
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var box = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1, "ordinary"), Row(box, 2, "ordinary")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 1), "needle"));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE messages SET body=x'7B' WHERE uid=2"; command.ExecuteNonQuery();
        }
        var query = new MailHeaderSearchQuery(box.AccountId, "needle") { Scope = MailSearchScope.SenderOrSubject };
        var headers = await cache.SearchHeadersAsync(query);
        Assert.Empty(headers.Messages); Assert.Equal(MailSearchEngine.Literal, headers.Engine);
        await Assert.ThrowsAnyAsync<IOException>(() => cache.SearchHeadersAsync(query with { Scope = MailSearchScope.Everywhere }));
    }

    [Fact]
    public async Task InvalidScopeAndEngineCombinationsAreRejected()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var query = new MailHeaderSearchQuery(AccountId.New(), "needle");
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Scope = MailSearchScope.Everywhere, Field = MailHeaderSearchField.Subject }));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Engine = MailSearchEngine.Literal }));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Scope = MailSearchScope.SenderOrSubject, Engine = MailSearchEngine.Indexed }));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Scope = (MailSearchScope)9 }));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Scope = MailSearchScope.Everywhere, Engine = (MailSearchEngine)9 }));
        Assert.Empty((await cache.SearchHeadersAsync(query with { Scope = MailSearchScope.SenderOrSubject, Engine = MailSearchEngine.Literal })).Messages);
    }

    [Fact]
    public async Task PagingPinsTheIndexedEngineAndFailsCleanlyWhenTheIndexDisappears()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile() with { IsEnabled = false };
        var box = new MailboxKey(account.Id, "INBOX", 7);
        var rows = Enumerable.Range(1, 501).Select(uid => Row(box, (uint)uid, "needle")).ToArray();
        await cache.SaveSummariesAsync(box, rows.Take(500).ToArray(), DateTimeOffset.UtcNow); await cache.SaveSummariesAsync(box, rows.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        await cache.RebuildSearchIndexAsync();
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        Assert.Equal(MailSearchScope.Everywhere, model.SearchScope);
        model.SetSearch("needle"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(500, model.Messages.Count); Assert.Equal(MailSearchEngine.Indexed, model.SearchEngineUsed); Assert.True(model.CanLoadNextSearchPage);
        // Every saved message has headers only, which the strip notes even though the index ran.
        Assert.Contains("indexed word phrase", model.SearchNotice); Assert.DoesNotContain("Search index is off", model.SearchCoverageNote);
        Assert.Contains("they matched on sender and subject only", model.SearchCoverageNote); Assert.Equal("Some headers only", model.SearchCoverageSummary);
        await cache.DisableSearchIndexAsync();
        await model.LoadNextSearchPageAsync(); dispatcher.Drain();
        Assert.NotNull(model.ListProblem); Assert.Empty(model.Messages); Assert.True(model.CanRetryIn(InboxProblemScope.List));
        // Retry would repeat the pinned engine; Refresh results drops it and runs the submitted search again from page 1.
        Assert.Contains("Refresh results", model.ListProblem!.Text); Assert.True(model.CanRefreshSearchResults);
        model.SetSearch("edited, not submitted"); await model.RefreshSearchResultsAsync(); dispatcher.Drain();
        Assert.Null(model.ListProblem); Assert.Equal(1, model.SearchPageNumber); Assert.Equal(500, model.Messages.Count);
        Assert.Equal(MailSearchEngine.Literal, model.SearchEngineUsed); Assert.False(model.CanRefreshSearchResults); Assert.False(model.CanLoadPreviousSearchPage);
        // A new search picks the engine again, now a direct scan, and says so.
        model.SetSearch("needle"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Null(model.ListProblem); Assert.Equal(500, model.Messages.Count); Assert.Equal(MailSearchEngine.Literal, model.SearchEngineUsed);
        Assert.Contains("Search index is off", model.SearchCoverageNote); Assert.Equal("Index off", model.SearchCoverageSummary);
        Assert.Contains("headers only", model.SearchCoverageNote);
        await model.LoadNextSearchPageAsync(); dispatcher.Drain(); Assert.Equal(1u, Assert.Single(model.Messages).Key.Uid);
    }

    [Fact]
    public async Task OrthogonalReadStarOfflineFiltersMapIndependently()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile();
        var box = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1, "needle"), Row(box, 2, "needle") with { IsRead = true, IsStarred = true }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 2), "body"));
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearchReadState(true); Assert.Equal(true, model.SearchReadState); Assert.Null(model.SearchStarState); Assert.Null(model.SearchBodyCached);
        model.SetSearchStarState(false); Assert.Equal(true, model.SearchReadState); Assert.Equal(false, model.SearchStarState); Assert.Null(model.SearchBodyCached);
        model.SetSearchBodyAvailability(false); Assert.Equal(true, model.SearchReadState); Assert.Equal(false, model.SearchStarState); Assert.Equal(false, model.SearchBodyCached);
        // Everywhere with headers-only mail is allowed; message-body search over it can find nothing, so it is refused with a reason.
        Assert.Null(model.SearchFilterConflict); Assert.True(model.CanSearch);
        model.SetSearchScope(MailSearchScope.MessageBody); Assert.NotNull(model.SearchFilterConflict); Assert.False(model.CanSearch);
        // The reason names the real control and its options.
        Assert.Contains("Offline availability", model.SearchFilterConflict); Assert.Contains("Downloaded or not", model.SearchFilterConflict);
        Assert.Contains("Headers only", model.SearchFilterConflict); Assert.DoesNotContain("either", model.SearchFilterConflict);
        model.SetSearchBodyAvailability(true); Assert.Null(model.SearchFilterConflict); Assert.True(model.CanSearch);
        model.SetSearchScope(MailSearchScope.Everywhere); model.SetSearchReadState(null); model.SetSearchStarState(true); model.SetSearchText("needle"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(2u, Assert.Single(model.Messages).Key.Uid); Assert.Contains("Starred only", model.SearchNotice); Assert.Contains("Downloaded bodies only", model.SearchNotice);
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; dispatcher.Drain();
        Assert.Equal(MailSearchScope.Everywhere, model.SearchScope); Assert.Null(model.SearchReadState); Assert.Null(model.SearchStarState); Assert.Null(model.SearchBodyCached);
        Assert.Equal(MailSearchEngine.Literal, model.SearchEngineUsed); Assert.False(model.SearchHasHeaderOnlyMessages); Assert.Null(model.SearchFilterConflict);
    }

    [Fact]
    public async Task HeaderOnlyCoverageFollowsTheSubmittedFilters()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var box = new MailboxKey(AccountId.New(), "INBOX", 7);
        // Uid 1 is recent, read, starred and downloaded; uid 2 is an old, unread, unstarred headers-only message from someone else.
        await cache.SaveSummariesAsync(box, [Row(box, 1, "contract") with { IsRead = true, IsStarred = true },
            Row(box, 2, "contract") with { Sender = "other@example.test", ReceivedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 1), "the contract text"));
        var query = new MailHeaderSearchQuery(box.AccountId, "contract") { Scope = MailSearchScope.MessageBody, MailboxId = "INBOX" };
        Assert.True((await cache.SearchHeadersAsync(query)).HasHeaderOnlyMessages);
        Assert.True((await cache.SearchHeadersAsync(query with { Scope = MailSearchScope.Everywhere })).HasHeaderOnlyMessages);
        // Each filter that already leaves the headers-only message out of scope also leaves it out of the coverage note.
        foreach (var filtered in new[]
        {
            query with { IsBodyCached = true }, query with { IsRead = true }, query with { IsStarred = true },
            query with { SenderContains = "sender@" }, query with { ReceivedFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            query with { ReceivedBefore = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) }, query with { MailboxId = "Archive" },
            query with { Scope = MailSearchScope.Everywhere, IsBodyCached = true },
        })
            Assert.False((await cache.SearchHeadersAsync(filtered)).HasHeaderOnlyMessages);
        Assert.True((await cache.SearchHeadersAsync(query with { IsRead = false, ReceivedBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) })).HasHeaderOnlyMessages);
    }

    [Fact]
    public async Task AnEnabledIndexThatFailsToAttachIsNotCalledOff()
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db"); var cache = new SqliteMailCache(path); var account = TestDirectory.Profile() with { IsEnabled = false };
        var box = new MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(box, [Row(box, 1, "needle"), Row(box, 2, "other")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(box, new(Key(box, 2), "a needle in the text"));
        var query = new MailHeaderSearchQuery(account.Id, "needle") { Scope = MailSearchScope.Everywhere };
        // Disabled: no index file, so a direct scan carries no issue and the note says the index is off.
        Assert.Null((await cache.SearchHeadersAsync(query)).SearchIndexIssue);
        await cache.RebuildSearchIndexAsync();
        Assert.Null((await cache.SearchHeadersAsync(query)).SearchIndexIssue);
        // Enabled but unreadable: the index file is still there, so the direct scan says why it was not used.
        await File.WriteAllBytesAsync(path + ".search.db", Enumerable.Repeat((byte)0x5A, 4096).ToArray());
        var broken = await cache.SearchHeadersAsync(query);
        Assert.Equal(MailSearchEngine.Literal, broken.Engine); Assert.StartsWith("Search index unavailable", broken.SearchIndexIssue);
        var dispatcher = new TestQueueDispatcher(); using var model = new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(), dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        model.SetSearch("needle"); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Equal(MailSearchEngine.Literal, model.SearchEngineUsed);
        Assert.Contains("Search index is on but was not used", model.SearchCoverageNote); Assert.Contains("Search index unavailable", model.SearchCoverageNote);
        Assert.DoesNotContain("Search index is off", model.SearchCoverageNote); Assert.DoesNotContain("Turn it on", model.SearchCoverageNote);
        Assert.Equal("Index not used", model.SearchCoverageSummary);
        await cache.DisableSearchIndexAsync(); await model.SearchSavedHeadersAsync(); dispatcher.Drain();
        Assert.Null(model.SearchIndexIssue); Assert.Contains("Search index is off", model.SearchCoverageNote); Assert.Equal("Index off", model.SearchCoverageSummary);
    }

    private static MailMessageKey Key(MailboxKey box, uint uid) => new(box.AccountId, box.MailboxId, box.UidValidity, uid);
    private static MailMessageSummary Row(MailboxKey box, uint uid, string subject) => new()
        { Key = Key(box, uid), Sender = "sender@example.test", Subject = subject, ReceivedAt = new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.Zero).AddMinutes(-uid) };
}

[Collection("UI theme")]
public sealed class SearchScopeViewTests
{
    [Theory]
    [InlineData(640, 480, 1d)]
    [InlineData(640, 480, 2d)]
    [InlineData(1100, 720, 1d)]
    [InlineData(1100, 720, 2d)]
    public async Task FiltersOfferFourShortCombosBesideTheDateThatFollowTheModel(int width, int height, double scale)
    {
        using var f = await AccountSearchTests.Fixture.Create(); using var model = await f.Model();
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host(width, height)); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        Button(root, "Search saved mail").Click(); session.RenderFrame();
        Button(root, "Filters").Click(); session.RenderFrame();
        var searchIn = Combo(root, "Search in");
        var row = (StandardToolbar)searchIn.Parent!;
        Assert.Equal(["Search in", "Read state", "Starred state", "Offline availability", "Saved search received date"],
            row.Children.OfType<StandardComboBox>().Select(combo => combo.AccessibleName));
        Assert.All(row.Children.OfType<StandardComboBox>(), combo => Assert.InRange(combo.Items.Count, 3, 5));
        Assert.Equal(["Everywhere (headers & body)", "Sender or subject", "Message body"], searchIn.Items.Select(item => item.Text));
        Assert.Equal(0, searchIn.SelectedIndex);
        Assert.DoesNotContain(All(root).OfType<StandardComboBox>(), combo => combo.AccessibleName is "Saved search header field" or "Saved search filter");
        // The combos and the model follow each other without echoing back.
        searchIn.SelectedIndex = 2; Assert.Equal(MailSearchScope.MessageBody, model.SearchScope);
        Combo(root, "Starred state").SelectedIndex = 1; Assert.Equal(true, model.SearchStarState); Assert.Null(model.SearchReadState);
        model.SetSearchReadState(false); model.SetSearchBodyAvailability(true); model.SetSearchScope(MailSearchScope.SenderOrSubject); session.RenderFrame();
        Assert.Equal(1, Combo(root, "Read state").SelectedIndex); Assert.Equal(1, Combo(root, "Offline availability").SelectedIndex);
        Assert.Equal(1, searchIn.SelectedIndex); Assert.Equal(1, Combo(root, "Starred state").SelectedIndex);
        model.SetSearchFilters(null, null, null); model.SetSearchScope(MailSearchScope.Everywhere); session.RenderFrame();
        Assert.All(row.Children.OfType<StandardComboBox>(), combo => Assert.Equal(0, combo.SelectedIndex));
        var frame = All(root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Saved search controls");
        if (height == 480) Assert.InRange(frame.Bounds.Height, 1, 168);
        Assert.True(All(root).OfType<StandardListView>().Single().Bounds.Height >= 40);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task TheResultsStripNotesTheDirectScanAndAConflictExplainsWhySearchWaits(double scale)
    {
        using var f = await AccountSearchTests.Fixture.Create(); using var model = await f.Model();
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host(640, 480)); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        Button(root, "Search saved mail").Click(); Button(root, "Filters").Click(); session.RenderFrame();
        model.SetSearch("needle"); await model.SearchSavedHeadersAsync(); session.RenderFrame();
        var strip = All(root).OfType<StandardToolbar>().Single(toolbar => toolbar.AccessibleName == "Saved search results");
        var coverage = All(strip).OfType<StandardLabel>().Single(label => label.AccessibleName == "Search coverage");
        Assert.Equal(UiVisibility.Visible, coverage.Visibility); Assert.Equal("Index off", coverage.Text);
        Assert.Contains("Settings", coverage.ToolTipText); Assert.Contains("headers only", coverage.ToolTipText); Assert.True(coverage.Bounds.Height > 0);
        Assert.True(strip.Bounds.Bottom <= All(root).OfType<StandardListView>().Single().Bounds.Top + 0.5);
        Combo(root, "Search in").SelectedIndex = 1; await model.SearchSavedHeadersAsync(); session.RenderFrame();
        Assert.Equal(UiVisibility.Collapsed, coverage.Visibility);
        // Message body search over headers-only mail cannot find anything: Search waits and the filters say why.
        Combo(root, "Search in").SelectedIndex = 2; Combo(root, "Offline availability").SelectedIndex = 2; session.RenderFrame();
        Assert.False(Button(root, "Search").IsEnabled);
        Assert.Contains(All(root).OfType<StandardLabel>(), label => label.Text.Contains("Message body search needs downloaded mail") && label.Bounds.Height > 0);
        Combo(root, "Offline availability").SelectedIndex = 1; session.RenderFrame();
        Assert.True(Button(root, "Search").IsEnabled);
        Assert.DoesNotContain(All(root).OfType<StandardLabel>(), label => label.Text.Contains("Message body search needs downloaded mail") && label.Bounds.Height > 0);
    }

    private static StandardButton Button(UiElement root, string text) => All(root).OfType<StandardButton>().Single(button => button.Text == text);
    private static StandardComboBox Combo(UiElement root, string name) => All(root).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == name);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }

    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation value) { }
        public void Present(BRenderList value) { }
    }
}
