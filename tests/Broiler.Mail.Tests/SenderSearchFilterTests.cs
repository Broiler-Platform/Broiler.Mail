using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class SenderSearchFilterTests
{
    [Theory]
    [InlineData(MailHeaderSearchField.IndexedRetainedBody)]
    [InlineData(MailHeaderSearchField.IndexedHeadersOrRetainedBody)]
    [InlineData(MailHeaderSearchField.BodyPreview)]
    public async Task SenderIsAnIndependentLiteralFilterCombinedWithScopeDateAndReadState(MailHeaderSearchField mode)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = AccountId.New(); var now = DateTimeOffset.UtcNow;
        var box = new MailboxKey(account, "INBOX", 1);
        var rows = new[] { Row(account, "INBOX", 1, "Ålice+%_@example.test", now), Row(account, "INBOX", 2, "Bob@example.test", now), Row(account, "INBOX", 3, "Ålice+%_@example.test", now.AddDays(-20)) with { IsRead = true } };
        await cache.SaveSummariesAsync(box, rows, now);
        foreach (var row in rows) await cache.SaveBodyAsync(box, new(row.Key, "matching body phrase") { RetainedBodyText = "matching body phrase" });
        var archive = Row(account, "Archive", 1, rows[0].Sender, now); await cache.SaveSummariesAsync(new(account, "Archive", 1), [archive], now); await cache.SaveBodyAsync(new(account, "Archive", 1), new(archive.Key, "matching body phrase") { RetainedBodyText = "matching body phrase" });
        var foreign = Row(AccountId.New(), "INBOX", 1, rows[0].Sender, now); await cache.SaveSummariesAsync(new(foreign.Key.AccountId, "INBOX", 1), [foreign], now);
        await cache.RebuildSearchIndexAsync();
        var query = new MailHeaderSearchQuery(account, "matching body phrase") { Field = mode, SenderContains = "åLICE+%_@", MailboxId = "INBOX", IsRead = false, ReceivedFrom = now.AddDays(-1), ReceivedBefore = now.AddDays(1) };
        Assert.Equal(rows[0].Key, Assert.Single((await cache.SearchHeadersAsync(query)).Messages).Key);
        Assert.Equal(2, (await cache.SearchHeadersAsync(query with { MailboxId = null })).Messages.Count);
        Assert.Equal(rows[1].Key, Assert.Single((await cache.SearchHeadersAsync(query with { SenderContains = "BOB" })).Messages).Key);
        Assert.Empty((await cache.SearchHeadersAsync(query with { SenderContains = "alice+%_@" })).Messages); // Accent-sensitive ordinal matching.
        Assert.Empty((await cache.SearchHeadersAsync(query with { SenderContains = "*" })).Messages);
    }

    [Theory]
    [InlineData(MailHeaderSearchField.SenderOrSubject)]
    [InlineData(MailHeaderSearchField.IndexedHeadersOrRetainedBody)]
    public async Task SenderOnlyQueryNeedsNoKeywordAndDoesNotRequireBodyDownload(MailHeaderSearchField mode)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = AccountId.New(); var box = new MailboxKey(account, "INBOX", 1);
        await cache.SaveSummariesAsync(box, [Row(account, "INBOX", 1, "Alice@example.test", DateTimeOffset.UtcNow), Row(account, "INBOX", 2, "Bob@example.test", DateTimeOffset.UtcNow)], DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        Assert.Equal(1u, Assert.Single((await cache.SearchHeadersAsync(new(account, "") { SenderContains = "ALICE", Field = mode })).Messages).Key.Uid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid\r\nheader")]
    public async Task InvalidSenderFiltersAreRejected(string sender)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(new(AccountId.New(), "needle") { SenderContains = sender }));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(new(AccountId.New(), "needle") { SenderContains = new string('x', 257) }));
    }

    [Fact]
    public async Task PagingPinsSubmittedSenderWhileEditsAndAccountResetRemainIndependent()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db")); var account = TestDirectory.Profile() with { IsEnabled = false }; var box = new MailboxKey(account.Id, "INBOX", 1);
        var rows = Enumerable.Range(1, 501).Select(uid => Row(account.Id, "INBOX", (uint)uid, "Alice@example.test", DateTimeOffset.UtcNow)).ToArray();
        await cache.SaveSummariesAsync(box, rows.Take(500).ToArray(), DateTimeOffset.UtcNow); await cache.SaveSummariesAsync(box, rows.Skip(500).Append(Row(account.Id, "INBOX", 502, "Bob@example.test", DateTimeOffset.UtcNow)).ToArray(), DateTimeOffset.UtcNow); await cache.RebuildSearchIndexAsync();
        using var model = new InboxViewModel(new InboxSearchTests.NoNetworkReceiver(), new ImmediateUiDispatcher(), cache); model.SetAccount(account); await model.CacheLoadTask;
        model.SetSearch("match"); model.SetSearchField(MailHeaderSearchField.IndexedHeadersOrRetainedBody); model.SetSearchSenderText(" Alice@example.test "); await model.SearchSavedHeadersAsync();
        Assert.Equal(500, model.Messages.Count); Assert.True(model.CanLoadNextSearchPage);
        model.SetSearchSenderText("Bob@example.test"); await model.LoadNextSearchPageAsync(); Assert.Equal(1u, Assert.Single(model.Messages).Key.Uid); Assert.Contains("Alice@example.test", model.SearchNotice); Assert.DoesNotContain("Bob@example.test", model.SearchNotice);
        await model.SearchSavedHeadersAsync(); Assert.Equal(502u, Assert.Single(model.Messages).Key.Uid);
        model.SetAccount(account with { Id = AccountId.New() }); await model.CacheLoadTask; Assert.Equal("", model.SearchSenderText);
        model.SetSearchText(""); model.SetSearchSenderText("Alice"); Assert.True(model.CanSearch); model.SetSearchSenderText(new string('x', 257)); Assert.False(model.CanSearch); model.SetSearchSenderText("bad\rheader"); Assert.False(model.CanSearch);
    }

    private static MailMessageSummary Row(AccountId account, string folder, uint uid, string sender, DateTimeOffset date) => new() { Key = new(account, folder, 1, uid), Sender = sender, Subject = "match", ReceivedAt = date };
}
