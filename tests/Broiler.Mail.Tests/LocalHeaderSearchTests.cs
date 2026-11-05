using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class LocalHeaderSearchTests
{
    [Fact]
    public async Task ReopenedSearchUsesCurrentEpochsAndNeverLeaksOtherAccounts()
    {
        using var directory = new TestDirectory();
        var path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var account = AccountId.New();
        var inbox = new MailboxKey(account, "INBOX", 7);
        var archive = new MailboxKey(account, "Archive", 9);
        await cache.SaveSummariesAsync(inbox, [Row(inbox, 1, "old match")], DateTimeOffset.UtcNow);
        inbox = new(account, "INBOX", 8);
        await cache.SaveSummariesAsync(inbox, [Row(inbox, 2, "match current")], DateTimeOffset.UtcNow);
        await cache.SaveSummariesAsync(archive, [Row(archive, 3, "match archive")], DateTimeOffset.UtcNow);
        var foreign = new MailboxKey(AccountId.New(), "INBOX", 8);
        await cache.SaveSummariesAsync(foreign, [Row(foreign, 4, "match private")], DateTimeOffset.UtcNow);
        var result = await new SqliteMailCache(path).SearchHeadersAsync(new(account, "MATCH"));
        Assert.Equal(new uint[] { 2, 3 }, result.Messages.Select(row => row.Key.Uid));
        Assert.All(result.Messages, row => Assert.Equal(account, row.Key.AccountId));
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task FiltersAndBoundedResultsPreserveFlagsBodyAvailabilityAndLiteralText()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "100%_ ' OR 1=1 --"),
            Row(mailbox, 2, "100%_ ' OR 1=1 --") with { IsRead = true },
            Row(mailbox, 3, "100%_ ' OR 1=1 --")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, 3), "body"));
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "%_ ' OR 1=1 --")
            { MailboxId = "INBOX", IsRead = false, IsStarred = true, MaximumCount = 1 };
        var first = await cache.SearchHeadersAsync(query);
        Assert.True(first.HasMore); Assert.Equal(3u, Assert.Single(first.Messages).Key.Uid);
        Assert.True(first.Messages[0].IsBodyCached); Assert.True(first.Messages[0].IsStarred);
        var all = await cache.SearchHeadersAsync(query with { MaximumCount = 2 });
        Assert.Equal(2, all.Messages.Count); Assert.False(all.HasMore);
        Assert.Empty((await cache.SearchHeadersAsync(query with { MailboxId = "inbox" })).Messages);
    }

    [Fact]
    public async Task UnicodeSenderMatchingIsOrdinalIgnoreCaseAndBodiesAreNotSearched()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "subject") with { Sender = "Grüße@example.test" }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, 1), "body-only"));
        Assert.Single((await cache.SearchHeadersAsync(new(mailbox.AccountId, "GRÜßE"))).Messages);
        Assert.Empty((await cache.SearchHeadersAsync(new(mailbox.AccountId, "body-only"))).Messages);
    }

    [Fact]
    public async Task InvalidAndCancelledQueriesDoNotReturnSuccessfulEmptyResults()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var query = new MailHeaderSearchQuery(AccountId.New(), "match");
        foreach (var invalid in new[] { query with { Text = " " }, query with { Text = "a\nb" },
            query with { Text = new string('a', 257) }, query with { AccountId = default } })
            await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(invalid));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.SearchHeadersAsync(query with { MaximumCount = 501 }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SearchHeadersAsync(query, cancellation.Token));
    }

    [Fact]
    public async Task DateIntervalUsesUtcInclusiveStartExclusiveEndAndExcludesUnknownDates()
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero); var end = start.AddDays(1);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "match") with { ReceivedAt = start.AddTicks(-1) },
            Row(mailbox, 2, "match") with { ReceivedAt = start.ToOffset(TimeSpan.FromHours(5)) },
            Row(mailbox, 3, "match") with { ReceivedAt = end.AddTicks(-1) },
            Row(mailbox, 4, "match") with { ReceivedAt = end }, Row(mailbox, 5, "match")], DateTimeOffset.UtcNow);
        cache = new SqliteMailCache(path);
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "match") { ReceivedFrom = start, ReceivedBefore = end };
        Assert.Equal(new uint[] { 3, 2 }, (await cache.SearchHeadersAsync(query)).Messages.Select(row => row.Key.Uid));
        Assert.Equal(new uint[] { 4, 3, 2 }, (await cache.SearchHeadersAsync(query with { ReceivedBefore = null })).Messages.Select(row => row.Key.Uid));
        Assert.Equal(new uint[] { 3, 2, 1 }, (await cache.SearchHeadersAsync(query with { ReceivedFrom = null })).Messages.Select(row => row.Key.Uid));
        Assert.Equal(5, (await cache.SearchHeadersAsync(query with { ReceivedFrom = null, ReceivedBefore = null })).Messages.Count);
        Assert.Equal(new uint[] { 3, 2 }, (await cache.SearchHeadersAsync(query with
            { ReceivedFrom = start.ToOffset(TimeSpan.FromHours(-7)), ReceivedBefore = end.ToOffset(TimeSpan.FromHours(9)) })).Messages.Select(row => row.Key.Uid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidDateIntervalsAreRejected(int days)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var start = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(new(AccountId.New(), "match")
            { ReceivedFrom = start, ReceivedBefore = start.AddDays(days) }));
    }

    [Fact]
    public async Task ContinuationPagesUseExclusiveUidAndKeepEveryMatchOnceAfterReopen()
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, Enumerable.Range(1, 7).Select(uid => Row(mailbox, (uint)uid, "match")).ToArray(), DateTimeOffset.UtcNow);
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "match") { MailboxId = "INBOX", MaximumCount = 2 };
        var uids = new List<uint>();
        while (true)
        {
            var page = await new SqliteMailCache(path).SearchHeadersAsync(query);
            Assert.InRange(page.Messages.Count, 1, 2); uids.AddRange(page.Messages.Select(message => message.Key.Uid));
            if (!page.HasMore) break;
            query = query with { BeforeMessage = page.Messages[^1].Key };
        }
        Assert.Equal(new uint[] { 7, 6, 5, 4, 3, 2, 1 }, uids);
    }

    [Fact]
    public async Task ContinuationRejectsForeignOrUnscopedKeysAndChangedMailboxEpochs()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "match")], DateTimeOffset.UtcNow);
        var key = new MailMessageKey(mailbox.AccountId, "INBOX", 7, 1);
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "match") { MailboxId = "INBOX", BeforeMessage = key };
        foreach (var invalid in new[] { query with { MailboxId = null }, query with { MailboxId = "Archive" },
            query with { BeforeMessage = key with { AccountId = AccountId.New() } }, query with { BeforeMessage = key with { Uid = 0 } } })
            await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(invalid));
        await cache.SaveSummariesAsync(new(mailbox.AccountId, "INBOX", 8), [], DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.SearchHeadersAsync(query));
    }

    [Theory]
    [InlineData(MailHeaderSearchField.SenderOrSubject, 2)]
    [InlineData(MailHeaderSearchField.Sender, 1)]
    [InlineData(MailHeaderSearchField.Subject, 1)]
    public async Task HeaderFieldSelectsOnlyTheRequestedLiteralSource(MailHeaderSearchField field, int count)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "unrelated") with { Sender = "NEEDLE_%@example.test" },
            Row(mailbox, 2, "needle_%"), Row(mailbox, 3, "unrelated")], DateTimeOffset.UtcNow);
        var result = await cache.SearchHeadersAsync(new(mailbox.AccountId, "needle_%") { Field = field });
        Assert.Equal(count, result.Messages.Count);
        if (field == MailHeaderSearchField.Sender) Assert.Equal(1u, result.Messages.Single().Key.Uid);
        if (field == MailHeaderSearchField.Subject) Assert.Equal(2u, result.Messages.Single().Key.Uid);
    }

    [Fact]
    public async Task BlankKeywordRequiresAFilterAndStillHonorsDatesFlagsAndScope()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7); var date = DateTimeOffset.UtcNow.AddDays(-1);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "any") with { ReceivedAt = date },
            Row(mailbox, 2, "any") with { IsRead = true, ReceivedAt = date }, Row(mailbox, 3, "any")], DateTimeOffset.UtcNow);
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "   ") { IsRead = false, ReceivedFrom = date, MailboxId = "INBOX" };
        Assert.Equal(1u, (await cache.SearchHeadersAsync(query)).Messages.Single().Key.Uid);
        Assert.Equal(3, (await cache.SearchHeadersAsync(query with { IsRead = null, ReceivedFrom = null, IsStarred = true })).Messages.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Field = (MailHeaderSearchField)99 }));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SearchHeadersAsync(query with { Text = "a\nb" }));
    }

    [Fact]
    public async Task BodyPreviewSearchUsesSavedPlainTextAndNeverRawHtmlOrUncachedContent()
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        var old = new MailboxKey(mailbox.AccountId, "INBOX", 6);
        await cache.SaveSummariesAsync(old, [Row(old, 9, "ordinary")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(old, new(Row(old, 9, "").Key, "body needle"));
        var foreign = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(foreign, [Row(foreign, 9, "ordinary")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(foreign, new(Row(foreign, 9, "").Key, "body needle"));
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "ordinary"), Row(mailbox, 2, "body needle"), Row(mailbox, 3, "ordinary")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(Row(mailbox, 1, "").Key, "BODY NEEDLE", "<p>raw-html-only</p>") { IsTruncated = true, IsHtmlFallback = true });
        await cache.SaveBodyAsync(mailbox, new(Row(mailbox, 3, "").Key, "unrelated"));
        cache = new SqliteMailCache(path);
        var query = new MailHeaderSearchQuery(mailbox.AccountId, "body needle") { Field = MailHeaderSearchField.BodyPreview };
        Assert.Equal(1u, (await cache.SearchHeadersAsync(query)).Messages.Single().Key.Uid);
        Assert.Equal(new uint[] { 2, 1 }, (await cache.SearchHeadersAsync(query with { Field = MailHeaderSearchField.HeadersOrBodyPreview })).Messages.Select(row => row.Key.Uid));
        Assert.Empty((await cache.SearchHeadersAsync(query with { Text = "raw-html-only" })).Messages);
        Assert.Equal(new uint[] { 3, 1 }, (await cache.SearchHeadersAsync(query with { Text = "", IsRead = false })).Messages.Select(row => row.Key.Uid));
        Assert.Equal(3, (await cache.SearchHeadersAsync(query with { Text = "", IsRead = false, Field = MailHeaderSearchField.HeadersOrBodyPreview })).Messages.Count);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("identity")]
    [InlineData("oversized")]
    public async Task InvalidBodyCannotProduceSuccessfulSearchResults(string kind)
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db");
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "ordinary"), Row(mailbox, 2, "ordinary")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(Row(mailbox, 1, "").Key, "needle"));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = kind switch
            {
                "identity" => "UPDATE messages SET body=(SELECT body FROM messages WHERE uid=1) WHERE uid=2",
                "oversized" => $"UPDATE messages SET body=zeroblob({SqliteMailCache.MaximumBodyBytes + 1}) WHERE uid=2",
                _ => "UPDATE messages SET body=x'7B' WHERE uid=2",
            };
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAnyAsync<IOException>(() => cache.SearchHeadersAsync(new(mailbox.AccountId, "needle") { Field = MailHeaderSearchField.BodyPreview }));
    }

    [Theory]
    [InlineData(null, null, 4)]
    [InlineData(false, null, 2)]
    [InlineData(true, null, 2)]
    [InlineData(null, false, 2)]
    [InlineData(null, true, 2)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    public async Task ReadAndStarFiltersCoverBothStatesAndTheirCombinations(bool? read, bool? starred, int count)
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, Enumerable.Range(0, 4).Select(i => Row(mailbox, (uint)i + 1, "match") with
            { IsRead = (i & 1) != 0, IsStarred = (i & 2) != 0 }).ToArray(), DateTimeOffset.UtcNow);
        var result = await cache.SearchHeadersAsync(new(mailbox.AccountId, "match") { IsRead = read, IsStarred = starred });
        Assert.Equal(count, result.Messages.Count);
        Assert.All(result.Messages, message => { if (read.HasValue) Assert.Equal(read.Value, message.IsRead); if (starred.HasValue) Assert.Equal(starred.Value, message.IsStarred); });
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task BodyAvailabilityIsAnIndependentFilterWithoutFetchingOrRequiringKeyword(bool cached, uint expectedUid)
    {
        using var directory = new TestDirectory(); var path = directory.File("mail.db"); var cache = new SqliteMailCache(path);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Row(mailbox, 1, "match"), Row(mailbox, 2, "match")], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(Row(mailbox, 1, "").Key, "body"));
        var result = await new SqliteMailCache(path).SearchHeadersAsync(new(mailbox.AccountId, "") { IsBodyCached = cached });
        Assert.Equal(expectedUid, result.Messages.Single().Key.Uid); Assert.Equal(cached, result.Messages[0].IsBodyCached);
    }

    private static MailMessageSummary Row(MailboxKey mailbox, uint uid, string subject) => new()
    {
        Key = new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, uid),
        Sender = "sender@example.test", Subject = subject, IsStarred = true,
    };
}
