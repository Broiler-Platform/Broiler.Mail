using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class SqliteMailCacheTests
{
    [Fact]
    public async Task HeadersAndCompleteDecodedBodySurviveReopenWithoutLosingReplyOrCidData()
    {
        using var directory = new TestDirectory();
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        var body = new MailMessageBody(Key(mailbox, 1), "Grüße", "<p>Grüße<img src='cid:one'></p>")
        { IsTruncated = true, Composition = new() { From = ["sender@example.test"], References = ["<parent@example.test>"], MessageId = "<one@example.test>" },
            EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["one"] = new("one", "image/png", [1, 2, 3]) } };
        await cache.SaveBodyAsync(mailbox, body);
        var reopened = new SqliteMailCache(directory.File("mail.db"));
        var loaded = await reopened.ReadBodyAsync(mailbox, 1);
        Assert.Equal(body.PlainText, loaded!.PlainText); Assert.Equal(body.HtmlText, loaded.HtmlText); Assert.True(loaded.IsTruncated);
        Assert.Equal("<parent@example.test>", Assert.Single(loaded.Composition!.References));
        Assert.Equal(new byte[] { 1, 2, 3 }, loaded.EmbeddedImages["one"].Data);
        Assert.True(Assert.Single(await reopened.ReadSummariesAsync(mailbox, 50)).IsBodyCached);
        Assert.Null(await reopened.ReadBodyAsync(new(mailbox.AccountId, "Archive", 7), 1));
        Assert.Null(await reopened.ReadBodyAsync(new(AccountId.New(), "INBOX", 7), 1));
        Assert.Null(await reopened.ReadBodyAsync(new(mailbox.AccountId, "INBOX", 8), 1));
    }

    [Fact]
    public async Task NewEmptyEpochBecomesLatestButCannotReadOldBodyUnderNewIdentity()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(Key(mailbox, 1), "old epoch"));
        var reset = new MailboxKey(mailbox.AccountId, "INBOX", 8);
        await cache.SaveSummariesAsync(reset, [], DateTimeOffset.UtcNow);
        Assert.Equal(reset, (await cache.GetLatestMailboxAsync(mailbox.AccountId, "INBOX"))!.Key);
        Assert.Empty(await cache.ReadSummariesAsync(reset, 50)); Assert.Null(await cache.ReadBodyAsync(reset, 1));
    }

    [Fact]
    public async Task InvalidBatchAndCancellationLeaveThePreviousTimestampAndRowsUntouched()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7); var time = DateTimeOffset.UtcNow.AddHours(-1);
        await cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 1)], time);
        await Assert.ThrowsAsync<ArgumentException>(() => cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 2), Summary(new(AccountId.New(), "INBOX", 7), 3)], DateTimeOffset.UtcNow));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 2)], DateTimeOffset.UtcNow, cancellation.Token));
        Assert.Single(await cache.ReadSummariesAsync(mailbox, 50));
        Assert.Equal(time, (await cache.GetLatestMailboxAsync(mailbox.AccountId, "INBOX"))!.ReceivedAt);
    }

    [Fact]
    public async Task QuotaFailureDoesNotLoseTheExistingBodyOrPublishTheFailedBodyAsOffline()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"), 256 * 1024);
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 1), Summary(mailbox, 2)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(Key(mailbox, 1), "retained"));
        var tooLarge = new MailMessageBody(Key(mailbox, 2), "large")
        { EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["big"] = new("big", "image/png", new byte[300 * 1024]) } };
        await Assert.ThrowsAsync<IOException>(() => cache.SaveBodyAsync(mailbox, tooLarge));
        Assert.Equal("retained", (await cache.ReadBodyAsync(mailbox, 1))!.PlainText);
        Assert.Null(await cache.ReadBodyAsync(mailbox, 2));
        Assert.False((await cache.ReadSummariesAsync(mailbox, 50)).Single(row => row.Key.Uid == 2).IsBodyCached);
    }

    [Fact]
    public async Task UnsupportedDatabaseVersionIsNotReplaced()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        using (var connection = new SqliteConnection($"Data Source={directory.File("mail.db")};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery(); }
        byte[] before = File.ReadAllBytes(directory.File("mail.db"));
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.ReadSummariesAsync(mailbox, 50));
        Assert.Equal(before, File.ReadAllBytes(directory.File("mail.db")));
    }

    [Fact]
    public async Task CorruptDatabaseIsReportedWithoutSilentlyCreatingAnEmptyCache()
    {
        using var directory = new TestDirectory(); directory.Create();
        byte[] corrupt = [1, 2, 3, 4, 5]; File.WriteAllBytes(directory.File("mail.db"), corrupt);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        await Assert.ThrowsAsync<IOException>(() => cache.GetLatestMailboxAsync(AccountId.New(), "INBOX"));
        Assert.Equal(corrupt, File.ReadAllBytes(directory.File("mail.db")));
    }

    [Fact]
    public async Task TamperedBodyCannotBeReadUnderAnotherUid()
    {
        using var directory = new TestDirectory(); var cache = new SqliteMailCache(directory.File("mail.db"));
        var mailbox = new MailboxKey(AccountId.New(), "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [Summary(mailbox, 1)], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(Key(mailbox, 1), "original"));
        using (var connection = new SqliteConnection($"Data Source={directory.File("mail.db")};Pooling=False"))
        {
            connection.Open(); using var read = connection.CreateCommand(); read.CommandText = "SELECT body FROM messages";
            string json = System.Text.Encoding.UTF8.GetString((byte[])read.ExecuteScalar()!);
            using var write = connection.CreateCommand(); write.CommandText = "UPDATE messages SET body=$body";
            write.Parameters.AddWithValue("$body", System.Text.Encoding.UTF8.GetBytes(json.Replace("\"Uid\":1", "\"Uid\":2")));
            write.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.ReadBodyAsync(mailbox, 1));
    }

    public static MailMessageSummary Summary(MailboxKey mailbox, uint uid) => new()
    { Key = Key(mailbox, uid), Sender = "Sender", Subject = $"Message {uid}", ReceivedAt = DateTimeOffset.UtcNow };
    public static MailMessageKey Key(MailboxKey mailbox, uint uid) => new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, uid);
}
