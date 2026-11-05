using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class UnifiedSavedInboxTests
{
    [Fact]
    public async Task RealCacheCombinesOnlyCurrentInboxEpochsWithAccountAttributionAndBoundedCoverage()
    {
        using var directory = new TestDirectory(); directory.Create(); var cache = new SqliteMailCache(directory.File("cache.db"));
        var first = TestDirectory.Profile(); var second = TestDirectory.Profile() with { IsEnabled = false };
        foreach (var account in new[] { first, second })
        {
            await cache.SaveSummariesAsync(new(account.Id, "INBOX", 7), [Row(account, 99)], DateTimeOffset.UtcNow);
            await cache.SaveSummariesAsync(new(account.Id, "Archive", 9), [Row(account, 99) with { Key = new(account.Id, "Archive", 9, 99) }], DateTimeOffset.UtcNow);
            var current = Enumerable.Range(1, 60).Select(i => Row(account, (uint)i) with { Key = new(account.Id, "INBOX", 8, (uint)i), ReceivedAt = DateTimeOffset.UnixEpoch.AddMinutes(i) }).ToArray();
            await cache.SaveSummariesAsync(new(account.Id, "INBOX", 8), current, DateTimeOffset.UtcNow);
            await cache.SaveBodyAsync(new(account.Id, "INBOX", 8), new(new(account.Id, "INBOX", 8, 60), account.Id.ToString()));
        }
        var snapshot = await UnifiedSavedInbox.ReadAsync(cache, [first, second]);
        Assert.Empty(snapshot.Failures); Assert.Equal(2, snapshot.Accounts); Assert.Equal(100, snapshot.Rows.Count);
        Assert.All(snapshot.Rows, row => { Assert.Equal("INBOX", row.Message.Key.MailboxId); Assert.Equal(8u, row.Message.Key.UidValidity); Assert.InRange(row.Message.Key.Uid, 11u, 60u); Assert.Equal(row.Account.Id, row.Message.Key.AccountId); });
        Assert.Equal(50, snapshot.Rows.Count(row => row.Account.Id == second.Id));
        Assert.Equal(snapshot.Rows.OrderByDescending(row => row.Message.ReceivedAt).ThenBy(row => row.Account.Id.Value).ThenByDescending(row => row.Message.Key.Uid), snapshot.Rows);
        foreach (var account in new[] { first, second })
        {
            var selected = snapshot.Rows.Single(row => row.Account.Id == account.Id && row.Message.Key.Uid == 60);
            Assert.Equal(account.Id.ToString(), (await UnifiedSavedInbox.ReadBodyAsync(cache, selected))!.PlainText);
        }
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("epoch")]
    [InlineData("duplicates")]
    [InlineData("order")]
    [InlineData("oversized")]
    [InlineData("failure")]
    public async Task OneInvalidOrFailedAccountNeverSuppressesAnotherAccountsRows(string mode)
    {
        var a = TestDirectory.Profile(); var b = TestDirectory.Profile(); var cache = new FakeCache();
        cache.Headers = (key, _) =>
        {
            if (key.AccountId == b.Id) return Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(b, 1)]);
            if (mode == "failure") throw new IOException("private diagnostic must not become UI feedback");
            IReadOnlyList<MailMessageSummary> rows = mode switch
            {
                "foreign" => [Row(b, 2)], "epoch" => [Row(a, 2) with { Key = new(a.Id, "INBOX", 8, 2) }],
                "duplicates" => [Row(a, 2), Row(a, 2)], "order" => [Row(a, 1), Row(a, 2)],
                _ => Enumerable.Range(1, 51).Reverse().Select(i => Row(a, (uint)i)).ToArray()
            };
            return Task.FromResult(rows);
        };
        var snapshot = await UnifiedSavedInbox.ReadAsync(cache, [a, b]); Assert.Equal(b.Id, Assert.Single(snapshot.Rows).Account.Id);
        Assert.Equal(a.Id, Assert.Single(snapshot.Failures).Account.Id); Assert.DoesNotContain("private", snapshot.Failures[0].Code);
    }
    [Fact]
    public async Task ChangedEpochDuringInventoryRejectsOnlyThatAccount()
    {
        var a = TestDirectory.Profile(); int heads = 0; var cache = new FakeCache { Headers = (_, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(a, 1)]) };
        cache.Head = (account, token) => Task.FromResult<CachedMailbox?>(new(new(account, "INBOX", Interlocked.Increment(ref heads) == 1 ? 7u : 8u), DateTimeOffset.UtcNow));
        var result = await UnifiedSavedInbox.ReadAsync(cache, [a]); Assert.Empty(result.Rows); Assert.Single(result.Failures);
    }
    [Fact]
    public async Task CallerCancellationReturnsEvenWhenACacheOperationIgnoresItsToken()
    {
        var a = TestDirectory.Profile(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CachedMailbox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache { Head = (_, _) => { entered.TrySetResult(); return release.Task; } }; using var cancellation = new CancellationTokenSource();
        var reading = UnifiedSavedInbox.ReadAsync(cache, [a], cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { release.TrySetResult(null); }
    }
    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("wrong-body")]
    public async Task BodyReadsRejectEpochChangesAndAnotherMessagesBytes(string mode)
    {
        var a = TestDirectory.Profile(); var row = new UnifiedSavedInboxRow(a, Row(a, 1)); int heads = 0;
        var cache = new FakeCache
        {
            Head = (account, _) => Task.FromResult<CachedMailbox?>(new(new(account, "INBOX", mode == "before" || mode == "after" && Interlocked.Increment(ref heads) > 1 ? 8u : 7u), DateTimeOffset.UtcNow)),
            Body = (key, uid, _) => Task.FromResult<MailMessageBody?>(new(new(key.AccountId, "INBOX", key.UidValidity, mode == "wrong-body" ? 2u : uid), "Cached"))
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => UnifiedSavedInbox.ReadBodyAsync(cache, row));
    }
    [Fact]
    public async Task MissingBodyIsNotFetchedAndInvalidAccountSetsAreRefused()
    {
        var a = TestDirectory.Profile(); var cache = new FakeCache();
        Assert.Null(await UnifiedSavedInbox.ReadBodyAsync(cache, new(a, Row(a, 1))));
        await Assert.ThrowsAsync<ArgumentException>(() => UnifiedSavedInbox.ReadAsync(cache, [a, a]));
        await Assert.ThrowsAsync<ArgumentException>(() => UnifiedSavedInbox.ReadAsync(cache, Enumerable.Range(0, 33).Select(_ => TestDirectory.Profile()).ToArray()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UnifiedSavedInbox.ReadAsync(cache, [a], new(true)));
    }
    internal static MailMessageSummary Row(AccountProfile account, uint uid) => new() { Key = new(account.Id, "INBOX", 7, uid), Sender = "sender@example.test", Subject = "Saved " + uid, ReceivedAt = DateTimeOffset.UnixEpoch.AddMinutes(uid) };
    internal sealed class FakeCache : IMailCache
    {
        public Func<AccountId, CancellationToken, Task<CachedMailbox?>> Head = (account, _) => Task.FromResult<CachedMailbox?>(new(new(account, "INBOX", 7), DateTimeOffset.UtcNow));
        public Func<MailboxKey, CancellationToken, Task<IReadOnlyList<MailMessageSummary>>> Headers = (_, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([]);
        public Func<MailboxKey, uint, CancellationToken, Task<MailMessageBody?>> Body = (_, _, _) => Task.FromResult<MailMessageBody?>(null);
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account, string mailbox, CancellationToken token = default) => Head(account, token);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey key, int count, uint? afterUid = null, CancellationToken cancellationToken = default) => Headers(key, cancellationToken);
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey key, uint uid, CancellationToken token = default) => Body(key, uid, token);
        public Task SaveSummariesAsync(MailboxKey key, IReadOnlyList<MailMessageSummary> messages, DateTimeOffset receivedAt, CancellationToken token = default) => throw new InvalidOperationException("Unified view must not write headers.");
        public Task SaveBodyAsync(MailboxKey key, MailMessageBody body, CancellationToken token = default) => throw new InvalidOperationException("Unified view must not write bodies.");
    }
}
