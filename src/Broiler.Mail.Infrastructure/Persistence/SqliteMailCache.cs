using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Versioned, bounded local mail cache. Never owns credentials, drafts or server mutations.</summary>
public sealed partial class SqliteMailCache : IMailCache, IMailFolderCache, IMailReadStateCache, IMailStarStateCache, IMailMoveCache, IMailDeleteCache, IMailLocalSearch, IMailCacheRecovery, IMailSearchIndex, IMailMailboxSnapshotCache
{
    public const long DefaultMaximumBytes = 256L * 1024 * 1024;
    public const int MaximumBodyBytes = 8 * 1024 * 1024;
    private const int ApplicationId = 0x424D4149;
    private readonly string _path;
    private readonly CacheRecoveryJournal _recovery;
    private readonly MutationIntentJournal _intents;
    private readonly SemaphoreSlim _repairGate = new(1);
    private int _pendingRecoveryCount;
    public int PendingRecoveryCount { get => Math.Max(_pendingRecoveryCount, Math.Max(_intents.ActiveCount, _intents.PendingCount)); private set => _pendingRecoveryCount = value; }
    public async Task<int> GetPendingRecoveryCountAsync(CancellationToken token = default)
    {
        try
        {
            var receipts = await _recovery.ReadAsync(token).ConfigureAwait(false);
            var intents = await _intents.ReadAsync(token).ConfigureAwait(false);
            MutationIntentJournal.ValidateReceiptLinks(intents, receipts);
            PendingRecoveryCount = receipts.Count + intents.Count(intent => !receipts.Any(receipt => receipt.Id == intent.Id));
            return PendingRecoveryCount;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { PendingRecoveryCount = Math.Max(1, PendingRecoveryCount); throw; }
    }
    public async Task<IReadOnlyList<MailUncertainAction>> ReadUncertainActionsAsync(CancellationToken token = default)
    {
        var receipts = await _recovery.ReadAsync(token).ConfigureAwait(false); var intents = await _intents.ReadAsync(token).ConfigureAwait(false);
        MutationIntentJournal.ValidateReceiptLinks(intents, receipts);
        var actions = new List<MailUncertainAction>();
        foreach (var intent in intents.Where(intent => !receipts.Any(receipt => receipt.Id == intent.Id)))
        {
            var key = intent.Source;
            var rows = await ReadSummariesAsync(new(key.AccountId, key.MailboxId, key.UidValidity), 1, key.Uid == uint.MaxValue ? null : key.Uid + 1, token).ConfigureAwait(false);
            string subject = rows.FirstOrDefault(message => message.Key == key)?.Subject ?? $"UID {key.Uid} (saved header unavailable)";
            actions.Add(new(intent.Id, key, intent.DestinationMailbox, $"{(intent.Operation == "flag" ? (intent.Flag == "seen" ? (intent.IsSet == true ? "Mark read" : "Mark unread") : (intent.IsSet == true ? "Star" : "Unstar")) : intent.DestinationMailbox is { } destination ? "Move to " + destination : "Permanent deletion")} — {key.MailboxId} — {subject}"));
        }
        return actions.AsReadOnly();
    }
    public async Task DismissUncertainActionAsync(Guid id, CancellationToken token = default)
    {
        if ((await _recovery.ReadAsync(token).ConfigureAwait(false)).Any(entry => entry.Id == id)) throw new InvalidOperationException("This action is confirmed; repair its local cache instead.");
        await _intents.DismissAsync(id, token).ConfigureAwait(false); await GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false);
    }
    public async Task<MailCacheRecoveryReport> RepairAsync(CancellationToken token = default)
    {
        await _repairGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            int repaired = 0;
            var entries = await _recovery.ReadAsync(token).ConfigureAwait(false); await GetPendingRecoveryCountAsync(token).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (entry.Operation == "flag") await UpdateFlagCoreAsync(entry.Source, entry.IsSet!.Value, entry.Flag == "flagged", token).ConfigureAwait(false);
                    else if (entry.Destination is { } target) await ReconcileMoveCoreAsync(entry.Source, target, token).ConfigureAwait(false);
                    else await ReconcileDeleteCoreAsync(entry.Source, token).ConfigureAwait(false);
                    await _intents.CompleteIfPresentAsync(entry.Id, entry.Source, entry.Destination?.MailboxId, token, entry.Flag, entry.IsSet).ConfigureAwait(false);
                    await _recovery.CompleteAsync(entry.Id, token).ConfigureAwait(false); _intents.ForgetPending(entry.Id); repaired++;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            return new(repaired, await GetPendingRecoveryCountAsync(token).ConfigureAwait(false));
        }
        finally
        {
            try { await GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { PendingRecoveryCount = Math.Max(1, PendingRecoveryCount); }
            _repairGate.Release();
        }
    }
    public Task ReconcilePermanentDeleteAsync(MailMessageKey key, CancellationToken token = default) => RecordAndReconcileAsync(key, null, token);
    public Task ReconcileMoveAsync(MailMessageKey source, MailMessageKey destination, CancellationToken token = default) => RecordAndReconcileAsync(source, destination, token);
    private async Task RecordAndReconcileAsync(MailMessageKey source, MailMessageKey? destination, CancellationToken token)
    {
        _ = new MailboxKey(source.AccountId, source.MailboxId, source.UidValidity);
        if (source.Uid == 0) throw new ArgumentException("A confirmed message UID is required.");
        if (destination is { } checkedTarget)
        {
            _ = new MailboxKey(checkedTarget.AccountId, checkedTarget.MailboxId, checkedTarget.UidValidity);
            if (checkedTarget.Uid == 0 || checkedTarget.AccountId != source.AccountId || checkedTarget.MailboxId == source.MailboxId)
                throw new ArgumentException("A verified same-account move between different mailboxes is required.");
        }
        await _repairGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var id = await _recovery.RecordAsync(source, destination, token).ConfigureAwait(false);
            await GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false);
            if (destination is { } target) await ReconcileMoveCoreAsync(source, target, token).ConfigureAwait(false);
            else await ReconcileDeleteCoreAsync(source, token).ConfigureAwait(false);
            await _intents.CompleteIfPresentAsync(id, source, destination?.MailboxId, token).ConfigureAwait(false);
            await _recovery.CompleteAsync(id, token).ConfigureAwait(false); _intents.ForgetPending(id);
            await GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally { _repairGate.Release(); }
    }
    private long _maximumBytes;
    private readonly SemaphoreSlim _gate = new(1);

    public SqliteMailCache(string path, long maximumBytes = DefaultMaximumBytes)
    {
        if (maximumBytes < 256 * 1024 || maximumBytes > 16L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _path = Path.GetFullPath(path); _recovery = new(_path + ".recovery.json"); _intents = new(_path);
        _maximumBytes = maximumBytes;
    }

    public Task<MailHeaderSearchResult> SearchHeadersAsync(MailHeaderSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.AccountId.Value == Guid.Empty) throw new ArgumentException("An account is required.", nameof(query));
        if (query.Text is null || query.Text.Length > 256 || query.Text.Any(char.IsControl) || !Enum.IsDefined(query.Field) ||
            (string.IsNullOrWhiteSpace(query.Text) && query.SenderContains is null && query.IsRead is null && query.IsStarred is null && query.IsBodyCached is null && query.ReceivedFrom is null && query.ReceivedBefore is null) ||
            query.SenderContains is {} senderFilter && (string.IsNullOrWhiteSpace(senderFilter) || senderFilter.Length > 256 || senderFilter.Any(char.IsControl)))
            throw new ArgumentException("Choose bounded search text or at least one sender/flag/date filter and a supported search field.", nameof(query));
        if (query.MailboxId is { } name) _ = new MailboxKey(query.AccountId, name, 1);
        if (query.ReceivedFrom is { } start && query.ReceivedBefore is { } end && start >= end)
            throw new ArgumentException("The received-date interval must have a positive duration.", nameof(query));
        if (query.AccountMailboxSnapshot is {} snapshot && (query.MailboxId is not null || snapshot.Length != 64 || !snapshot.All(Uri.IsHexDigit)))
            throw new ArgumentException("Invalid all-folder mailbox snapshot.", nameof(query));
        if (query.BeforeMessage is { } cursor)
        {
            _ = new MailboxKey(cursor.AccountId, cursor.MailboxId, cursor.UidValidity);
            if (cursor.Uid == 0 || cursor.AccountId != query.AccountId || query.MailboxId is {} explicitFolder && explicitFolder != cursor.MailboxId || query.MailboxId is null && query.AccountMailboxSnapshot is null)
                throw new ArgumentException("Search continuation must belong to the explicit account and mailbox.", nameof(query));
        }
        if (query.MaximumCount is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(query));
        return RunAsync(connection =>
        {
            string? accountSnapshot = query.MailboxId is null ? AccountMailboxSnapshot(connection, query.AccountId, cancellationToken) : null;
            if (query.AccountMailboxSnapshot is {} expected && !string.Equals(expected,accountSnapshot,StringComparison.OrdinalIgnoreCase))
                throw new MailSearchScopeChangedException("Saved account folders or UIDVALIDITY changed. Start a new search with Find saved headers.");
            bool indexed = (int)query.Field >= 5;
            if (indexed && !HasSearchIndex(connection)) throw new MailSearchIndexUnavailableException(_indexIssue);
            if (query.BeforeMessage is { } continuation)
            {
                using var head = connection.CreateCommand();
                head.CommandText = "SELECT epoch FROM mailbox_heads WHERE account=$a AND mailbox=$m";
                head.Parameters.AddWithValue("$a", query.AccountId.ToString()); head.Parameters.AddWithValue("$m", continuation.MailboxId);
                if (head.ExecuteScalar() is not long epoch || epoch != continuation.UidValidity)
                    throw new InvalidDataException("The saved mailbox epoch changed. Start a new search.");
            }
            // Managed comparison gives consistent Unicode semantics without SQLite extensions, dynamic code or SQL wildcards.
            connection.CreateFunction<string, string, bool>("mail_contains", (value, text) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return value.Contains(text, StringComparison.OrdinalIgnoreCase);
            }, isDeterministic: true);
            connection.CreateFunction<bool>("mail_bad_body", () => throw new InvalidDataException("Search body exceeds supported bounds."));
            connection.CreateFunction<byte[]?, string, long, long, bool>("mail_body_matches", (bytes, mailbox, epoch, uid) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (bytes is null) return false;
                if (bytes.Length > MaximumBodyBytes) throw new InvalidDataException("Search body exceeds supported bounds.");
                var body = JsonSerializer.Deserialize(bytes, CacheJsonContext.Default.MailMessageBody)
                    ?? throw new InvalidDataException("Search body is missing.");
                if (body.Key != new MailMessageKey(query.AccountId, mailbox, checked((uint)epoch), checked((uint)uid)))
                    throw new InvalidDataException("Search body identity is invalid.");
                ValidateBody(body);
                cancellationToken.ThrowIfCancellationRequested();
                return body.PlainText.Contains(query.Text, StringComparison.OrdinalIgnoreCase);
            }, isDeterministic: true);
            using var command = connection.CreateCommand();
            string matchCondition = indexed
                ? "$empty=1 OR EXISTS(SELECT 1 FROM search.docs WHERE docs MATCH $match AND account=m.account AND mailbox=m.mailbox AND epoch=m.epoch AND uid=m.uid)"
                : "$empty=1 OR ($field IN (0,1,4) AND mail_contains(m.sender,$text)) OR ($field IN (0,2,4) AND mail_contains(m.subject,$text)) OR ($field IN (3,4) AND CASE WHEN length(m.body)>$maximumBody THEN mail_bad_body() ELSE mail_body_matches(m.body,m.mailbox,m.epoch,m.uid) END)";
            string coverageCondition = query.Field == MailHeaderSearchField.IndexedRetainedBody
                ? "EXISTS(SELECT 1 FROM search.docs WHERE account=m.account AND mailbox=m.mailbox AND epoch=m.epoch AND uid=m.uid AND retained IS NOT NULL)" : "1";
            command.CommandText = $$"""
                SELECT m.mailbox,m.epoch,m.uid,m.sender,m.subject,m.received,m.is_read,m.is_starred,m.body IS NOT NULL
                FROM messages m JOIN mailbox_heads h ON h.account=m.account AND h.mailbox=m.mailbox AND h.epoch=m.epoch
                WHERE m.account=$account AND ($mailbox IS NULL OR m.mailbox=$mailbox)
                  AND CASE WHEN $senderFilter IS NULL THEN 1 ELSE mail_contains(m.sender,$senderFilter) END
                  AND ($read IS NULL OR m.is_read=$read) AND ($star IS NULL OR m.is_starred=$star)
                  AND ($cached IS NULL OR (m.body IS NOT NULL)=$cached)
                  AND ($epoch IS NULL OR m.epoch=$epoch)
                  AND ($cursorMailbox IS NULL OR m.mailbox COLLATE BINARY < $cursorMailbox OR (m.mailbox=$cursorMailbox AND m.uid<$uid))
                  AND ($from IS NULL OR m.received >= $from) AND ($before IS NULL OR m.received < $before)
                  AND ($field<>3 OR m.body IS NOT NULL)
                  AND ({{coverageCondition}})
                  AND ({{matchCondition}})
                ORDER BY m.mailbox COLLATE BINARY DESC,m.uid DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$account", query.AccountId.ToString());
            command.Parameters.AddWithValue("$mailbox", (object?)query.MailboxId ?? DBNull.Value);
            command.Parameters.AddWithValue("$senderFilter", (object?)query.SenderContains ?? DBNull.Value);
            command.Parameters.AddWithValue("$read", (object?)query.IsRead ?? DBNull.Value);
            command.Parameters.AddWithValue("$star", (object?)query.IsStarred ?? DBNull.Value);
            command.Parameters.AddWithValue("$cached", (object?)query.IsBodyCached ?? DBNull.Value);
            command.Parameters.AddWithValue("$from", query.ReceivedFrom is { } from ? FormatDate(from) : DBNull.Value);
            command.Parameters.AddWithValue("$before", query.ReceivedBefore is { } before ? FormatDate(before) : DBNull.Value);
            command.Parameters.AddWithValue("$epoch", query.MailboxId is not null && query.BeforeMessage is { } prior ? (long)prior.UidValidity : DBNull.Value);
            command.Parameters.AddWithValue("$uid", query.BeforeMessage is { } preceding ? preceding.Uid : (long)uint.MaxValue + 1);
            command.Parameters.AddWithValue("$cursorMailbox", (object?)query.BeforeMessage?.MailboxId ?? DBNull.Value);
            command.Parameters.AddWithValue("$maximumBody", MaximumBodyBytes);
            command.Parameters.AddWithValue("$empty", string.IsNullOrWhiteSpace(query.Text));
            command.Parameters.AddWithValue("$field", (int)query.Field % 5);
            if (indexed)
            {
                string column = query.Field switch
                {
                    MailHeaderSearchField.IndexedHeaders => "{sender subject} : ", MailHeaderSearchField.IndexedSender => "sender : ",
                    MailHeaderSearchField.IndexedSubject => "subject : ", MailHeaderSearchField.IndexedBodyPreview => "preview : ",
                    MailHeaderSearchField.IndexedHeadersOrBodyPreview => "{sender subject preview} : ",
                    MailHeaderSearchField.IndexedRetainedBody => "retained : ", MailHeaderSearchField.IndexedHeadersOrRetainedBody => "{sender subject retained} : ",
                    _ => throw new ArgumentException("Unsupported indexed search field."),
                };
                command.Parameters.AddWithValue("$match", column + "\"" + (string.IsNullOrWhiteSpace(query.Text) ? "unused" : query.Text.Trim()).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
            }
            command.Parameters.AddWithValue("$text", query.Text);
            command.Parameters.AddWithValue("$limit", query.MaximumCount + 1);
            using var registration = cancellationToken.Register(command.Cancel);
            using var reader = command.ExecuteReader();
            var matches = new List<MailMessageSummary>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matches.Count == query.MaximumCount) return new MailHeaderSearchResult(matches.AsReadOnly(), true) { AccountMailboxSnapshot = accountSnapshot };
                matches.Add(new()
                {
                    Key = new(query.AccountId, reader.GetString(0), checked((uint)reader.GetInt64(1)), checked((uint)reader.GetInt64(2))),
                    Sender = reader.GetString(3), Subject = reader.GetString(4),
                    ReceivedAt = reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
                    IsRead = reader.GetBoolean(6), IsStarred = reader.GetBoolean(7), IsBodyCached = reader.GetBoolean(8),
                });
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new MailHeaderSearchResult(matches.AsReadOnly(), false) { AccountMailboxSnapshot = accountSnapshot };
        }, cancellationToken);
    }

    private Task ReconcileDeleteCoreAsync(MailMessageKey key, CancellationToken cancellationToken)
    {
        var mailbox = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        if (key.Uid == 0) throw new ArgumentException("A confirmed message UID is required.", nameof(key));
        return RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = Scoped(connection, mailbox); command.Transaction = transaction;
            command.CommandText = "DELETE FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
            command.Parameters.AddWithValue("$uid", (long)key.Uid); command.ExecuteNonQuery();
            SyncSearchDocument(connection, transaction, key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); transaction.Commit(); return true;
        }, cancellationToken);
    }

    private Task ReconcileMoveCoreAsync(MailMessageKey source, MailMessageKey destination, CancellationToken cancellationToken)
    {
        var from = new MailboxKey(source.AccountId, source.MailboxId, source.UidValidity);
        _ = new MailboxKey(destination.AccountId, destination.MailboxId, destination.UidValidity);
        if (source.AccountId != destination.AccountId || source.MailboxId == destination.MailboxId || source.Uid == 0 || destination.Uid == 0)
            throw new ArgumentException("A verified same-account move between different mailboxes is required.");
        return RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            byte[]? movedBody = null;
            using (var read = Scoped(connection, from))
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT body,length(body) FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
                read.Parameters.AddWithValue("$uid", (long)source.Uid);
                using var reader = read.ExecuteReader();
                if (reader.Read() && !reader.IsDBNull(0))
                {
                    if (reader.GetInt64(1) > MaximumBodyBytes) throw new InvalidDataException("Cached move body exceeds supported bounds.");
                    var body = JsonSerializer.Deserialize((byte[])reader.GetValue(0), CacheJsonContext.Default.MailMessageBody)
                        ?? throw new InvalidDataException("Cached move body is missing.");
                    if (body.Key != source) throw new InvalidDataException("Cached move body identity is invalid.");
                    ValidateBody(body);
                    movedBody = JsonSerializer.SerializeToUtf8Bytes(body with { Key = destination }, CacheJsonContext.Default.MailMessageBody);
                    if (movedBody.Length > MaximumBodyBytes) throw new InvalidDataException("Rekeyed body exceeds supported bounds.");
                }
            }
            using (var existing = Scoped(connection, new(destination.AccountId, destination.MailboxId, destination.UidValidity)))
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT body,length(body) FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
                existing.Parameters.AddWithValue("$uid", (long)destination.Uid);
                using var reader = existing.ExecuteReader();
                if (reader.Read() && !reader.IsDBNull(0))
                {
                    if (reader.GetInt64(1) > MaximumBodyBytes) throw new InvalidDataException("Destination body exceeds supported bounds.");
                    var body = JsonSerializer.Deserialize((byte[])reader.GetValue(0), CacheJsonContext.Default.MailMessageBody);
                    if (body is null || body.Key != destination) throw new InvalidDataException("Destination body identity is invalid.");
                    ValidateBody(body);
                }
            }
            using var write = Scoped(connection, from); write.Transaction = transaction;
            write.Parameters.AddWithValue("$uid", (long)source.Uid);
            write.Parameters.AddWithValue("$dm", destination.MailboxId); write.Parameters.AddWithValue("$de", (long)destination.UidValidity);
            write.Parameters.AddWithValue("$duid", (long)destination.Uid); write.Parameters.AddWithValue("$body", movedBody is null ? DBNull.Value : movedBody);
            // A different known destination epoch needs a fresh receive, not an assumed epoch ordering.
            write.CommandText = "SELECT count(*) FROM mailbox_heads WHERE account=$a AND mailbox=$dm AND epoch<>$de";
            if ((long)write.ExecuteScalar()! != 0) throw new InvalidDataException("Receive the destination mailbox before reconciling its changed epoch.");
            write.CommandText = "INSERT INTO messages(account,mailbox,epoch,uid,sender,subject,received,is_read,is_starred,body) SELECT account,$dm,$de,$duid,sender,subject,received,is_read,is_starred,$body FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid ON CONFLICT(account,mailbox,epoch,uid) DO UPDATE SET body=COALESCE(messages.body,excluded.body)";
            write.ExecuteNonQuery();
            // Require destination availability before removing any source data. Repeating this local operation is safe.
            write.CommandText = "SELECT count(*) FROM messages WHERE account=$a AND mailbox=$dm AND epoch=$de AND uid=$duid";
            if ((long)write.ExecuteScalar()! != 1) throw new InvalidDataException("The cached source and destination are unavailable.");
            // Preserve existing destination receive time; a newly introduced head keeps the source's original receive time.
            write.CommandText = "INSERT INTO mailbox_heads(account,mailbox,epoch,received) SELECT account,$dm,$de,received FROM mailbox_heads WHERE account=$a AND mailbox=$m AND epoch=$e ON CONFLICT(account,mailbox) DO NOTHING";
            write.ExecuteNonQuery();
            write.CommandText = "SELECT count(*) FROM mailbox_heads WHERE account=$a AND mailbox=$dm AND epoch=$de";
            if ((long)write.ExecuteScalar()! != 1) throw new InvalidDataException("The destination cache head is unavailable.");
            write.CommandText = "DELETE FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
            write.ExecuteNonQuery();
            SyncSearchDocument(connection, transaction, source, cancellationToken); SyncSearchDocument(connection, transaction, destination, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); transaction.Commit(); return true;
        }, cancellationToken);
    }

    public Task UpdateReadStateAsync(MailMessageKey key, bool isRead, CancellationToken cancellationToken = default) =>
        UpdateFlagAsync(key, isRead, starred: false, cancellationToken);

    public Task UpdateStarStateAsync(MailMessageKey key, bool isStarred, CancellationToken cancellationToken = default) =>
        UpdateFlagAsync(key, isStarred, starred: true, cancellationToken);

    private async Task UpdateFlagAsync(MailMessageKey key, bool isRead, bool starred, CancellationToken cancellationToken)
    {
        _ = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        if (key.Uid == 0) throw new ArgumentException("A confirmed message UID is required.");
        string flag = starred ? "flagged" : "seen";
        await _repairGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var id = await _recovery.RecordFlagAsync(key, flag, isRead, token: cancellationToken).ConfigureAwait(false);
            await GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false);
            await UpdateFlagCoreAsync(key, isRead, starred, cancellationToken).ConfigureAwait(false);
            await _intents.CompleteIfPresentAsync(id, key, null, cancellationToken, flag, isRead).ConfigureAwait(false);
            await _recovery.CompleteAsync(id, cancellationToken).ConfigureAwait(false); _intents.ForgetPending(id);
            await GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally { _repairGate.Release(); }
    }
    private Task UpdateFlagCoreAsync(MailMessageKey key, bool isRead, bool starred, CancellationToken cancellationToken)
    {
        var mailbox = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        if (key.Uid == 0) throw new ArgumentException("A message UID is required.", nameof(key));
        return RunAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = Scoped(connection, mailbox);
            command.CommandText = $"UPDATE messages SET {(starred ? "is_starred" : "is_read")}=$read WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
            command.Parameters.AddWithValue("$read", isRead); command.Parameters.AddWithValue("$uid", (long)key.Uid);
            if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("The cached message is no longer available.");
            return true;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetCachedMailboxIdsAsync(AccountId account, CancellationToken cancellationToken = default)
    {
        _ = new MailboxKey(account, "INBOX", 1);
        return RunAsync<IReadOnlyList<string>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT mailbox FROM mailbox_heads WHERE account=$a ORDER BY mailbox COLLATE BINARY LIMIT 513";
            command.Parameters.AddWithValue("$a", account.ToString());
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = reader.GetString(0);
                if (name.Length is < 1 or > 1024 || name.Any(char.IsControl) || names.Count >= 512)
                    throw new InvalidDataException("Saved mailbox catalog exceeds supported bounds.");
                names.Add(name);
            }
            return names.AsReadOnly();
        }, cancellationToken);
    }

    public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account, string mailboxId, CancellationToken cancellationToken = default)
    {
        _ = new MailboxKey(account, mailboxId, 1);
        return RunAsync<CachedMailbox?>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT epoch,received FROM mailbox_heads WHERE account=$a AND mailbox=$m";
            command.Parameters.AddWithValue("$a", account.ToString()); command.Parameters.AddWithValue("$m", mailboxId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(new(account, mailboxId, checked((uint)reader.GetInt64(0))), ParseDate(reader.GetString(1))) : null;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox, int maximumCount,
        uint? afterUid = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        if (maximumCount is < 1 or > 501) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (afterUid == 0) throw new ArgumentOutOfRangeException(nameof(afterUid));
        return RunAsync<IReadOnlyList<MailMessageSummary>>(connection =>
        {
            using var command = Scoped(connection, mailbox);
            command.CommandText = "SELECT uid,sender,subject,received,is_read,body IS NOT NULL,is_starred FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid<$before ORDER BY uid DESC LIMIT $limit";
            command.Parameters.AddWithValue("$before", afterUid is null ? (long)uint.MaxValue + 1 : afterUid.Value);
            command.Parameters.AddWithValue("$limit", maximumCount);
            using var reader = command.ExecuteReader();
            var messages = new List<MailMessageSummary>();
            while (reader.Read()) messages.Add(new()
            {
                Key = new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, checked((uint)reader.GetInt64(0))),
                Sender = reader.GetString(1), Subject = reader.GetString(2),
                ReceivedAt = reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)), IsRead = reader.GetBoolean(4), IsBodyCached = reader.GetBoolean(5), IsStarred = reader.GetBoolean(6),
            });
            return messages;
        }, cancellationToken);
    }

    public Task SaveSummariesAsync(MailboxKey mailbox, IReadOnlyList<MailMessageSummary> messages,
        DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailbox); ArgumentNullException.ThrowIfNull(messages);
        var batch = messages.ToArray();
        if (batch.Length > 500 || batch.Any(message => !mailbox.Contains(message.Key) ||
            message.Sender is null || message.Subject is null || message.Sender.Length > 8192 || message.Subject.Length > 8192))
            throw new ArgumentException("The bounded header batch must belong to one mailbox epoch.", nameof(messages));
        return RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            foreach (var message in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpsertHeaderAndIndex(connection, transaction, mailbox, message, cancellationToken);
            }
            using (var command = Scoped(connection, mailbox))
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO mailbox_heads VALUES($a,$m,$e,$date) ON CONFLICT(account,mailbox) DO UPDATE SET epoch=excluded.epoch,received=excluded.received";
                command.Parameters.AddWithValue("$date", FormatDate(receivedAt)); command.ExecuteNonQuery();
            }
            cancellationToken.ThrowIfCancellationRequested(); transaction.Commit(); return true;
        }, cancellationToken);
    }

    public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox, uint uid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        if (uid == 0) throw new ArgumentOutOfRangeException(nameof(uid));
        return RunAsync<MailMessageBody?>(connection =>
        {
            using var command = Scoped(connection, mailbox);
            command.CommandText = "SELECT body,length(body) FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
            command.Parameters.AddWithValue("$uid", (long)uid);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0)) return null;
            if (reader.GetInt64(1) > MaximumBodyBytes) throw new InvalidDataException("Cached body exceeds its supported limit.");
            var body = JsonSerializer.Deserialize((byte[])reader.GetValue(0), CacheJsonContext.Default.MailMessageBody)
                ?? throw new InvalidDataException("Cached message is incomplete.");
            if (!mailbox.Contains(body.Key) || body.Key.Uid != uid) throw new InvalidDataException("Cached message identity is invalid.");
            ValidateBody(body);
            return body;
        }, cancellationToken);
    }

    public Task SaveBodyAsync(MailboxKey mailbox, MailMessageBody body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailbox); ArgumentNullException.ThrowIfNull(body);
        if (!mailbox.Contains(body.Key)) throw new ArgumentException("Body belongs to another mailbox epoch.", nameof(body));
        ValidateBody(body);
        return RunAsync(connection =>
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body, CacheJsonContext.Default.MailMessageBody);
            if (body.RetainedBodyText is not null && Scalar(connection, "PRAGMA user_version") < 4)
                throw new InvalidOperationException("Increase the cache quota so its format can be upgraded before retaining complete body text. No saved body was replaced.");
            if (bytes.Length > MaximumBodyBytes) throw new InvalidDataException("Message body exceeds the local cache limit.");
            using var transaction = connection.BeginTransaction();
            using var command = Scoped(connection, mailbox); command.Transaction = transaction;
            command.CommandText = "UPDATE messages SET body=$body WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
            command.Parameters.AddWithValue("$uid", (long)body.Key.Uid); command.Parameters.AddWithValue("$body", bytes);
            if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("Save the message header before its body.");
            SyncSearchDocument(connection, transaction, body.Key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); transaction.Commit(); return true;
        }, cancellationToken);
    }

    private async Task<T> RunAsync<T>(Func<SqliteConnection, T> operation, CancellationToken token, bool attachIndex = true)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                try { using var connection = Open(attachIndex); token.ThrowIfCancellationRequested(); return operation(connection); }
                catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
                catch (SqliteException error) { throw new IOException("Local mail storage could not be read or saved. Check free space, the cache limit, and profile recovery instructions.", error); }
                catch (JsonException error) { throw new InvalidDataException("Cached body is corrupt; it was not reset.", error); }
                catch (Exception error) when (error is FormatException or OverflowException)
                { throw new InvalidDataException("Cached metadata is invalid; it was not reset.", error); }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // Maintenance shares the same gate as every receive/read/write operation.
    internal Task<T> WithExclusiveConnectionAsync<T>(Func<SqliteConnection, T> operation, CancellationToken token) => RunAsync(operation, token);

    internal void ApplyStoragePolicy(SqliteConnection connection, ProfileStoragePolicy policy, Action persist)
    {
        if (policy.MaximumBytes < 1024 * 1024 || policy.MaximumBytes > 16L * 1024 * 1024 * 1024 || !Enum.IsDefined(policy.Downloads))
            throw new ArgumentOutOfRangeException(nameof(policy), "Choose a cache limit between 1 and 16384 MiB and a supported download policy.");
        long pageSize = Scalar(connection, "PRAGMA page_size"), pages = policy.MaximumBytes / pageSize;
        if (Scalar(connection, "PRAGMA page_count") > pages)
            throw new InvalidOperationException("The existing database allocation exceeds that limit. Choose a larger quota or use recovery-backed cleanup first; no downloaded mail was removed.");
        long oldLimit = _maximumBytes;
        long actual = Scalar(connection, $"PRAGMA max_page_count={pages}");
        if (actual != pages) throw new InvalidOperationException("SQLite could not apply the requested quota.");
        try { persist(); _maximumBytes = policy.MaximumBytes; }
        catch
        {
            // Memory remains on the previous policy if manifest publication fails.
            try { Scalar(connection, $"PRAGMA max_page_count={oldLimit / pageSize}"); } catch (SqliteException) { }
            throw;
        }
    }

    internal static void ValidateSnapshot(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString());
            connection.Open();
            long version = Scalar(connection, "PRAGMA user_version");
            if (version is not (1 or 2 or 3 or 4 or 5) || Scalar(connection, "PRAGMA application_id") != ApplicationId)
                throw new InvalidDataException("The backup cache has an unsupported schema/application identity.");
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA quick_check";
            if ((string?)command.ExecuteScalar() != "ok") throw new InvalidDataException("The backup cache failed its integrity check.");
            command.CommandText = "SELECT account,mailbox,epoch,uid,sender,subject,received,is_read,body FROM messages LIMIT 0";
            using (command.ExecuteReader()) { }
            if (version >= 2)
            {
                command.CommandText = "SELECT is_starred FROM messages LIMIT 0";
                using (command.ExecuteReader()) { }
            }
            if (version >= 3) _ = SearchRevision(connection);
            if (version >= 5)
            {
                command.CommandText = "SELECT account,mailbox,epoch,observed,uid_count,uid_hash FROM mailbox_snapshots LIMIT 0"; using (command.ExecuteReader()) { }
                command.CommandText = "SELECT account,mailbox,epoch,uid FROM mailbox_snapshot_uids LIMIT 0"; using (command.ExecuteReader()) { }
            }
            command.CommandText = "SELECT account,mailbox,epoch,received FROM mailbox_heads LIMIT 0";
            using (command.ExecuteReader()) { }
        }
        catch (SqliteException error) { throw new InvalidDataException("The backup cache could not be verified. It may be corrupt or use an unsupported schema.", error); }
    }

    private SqliteConnection Open(bool attachIndex = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            connection.Open();
            long version = Scalar(connection, "PRAGMA user_version");
            long application = Scalar(connection, "PRAGMA application_id");
            if (version is < 0 or > 5 || (application != 0 && application != ApplicationId) || (version > 0 && application != ApplicationId))
                throw new InvalidDataException("The mail cache uses an unsupported schema/application. It has not been reset.");
            if (version == 0)
            {
                if (Scalar(connection, "SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'") != 0)
                    throw new InvalidDataException("Unrecognized cache database; no tables were changed.");
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"CREATE TABLE mailbox_heads(account TEXT NOT NULL,mailbox TEXT NOT NULL,epoch INTEGER NOT NULL,received TEXT NOT NULL,PRIMARY KEY(account,mailbox)); CREATE TABLE messages(account TEXT NOT NULL,mailbox TEXT NOT NULL,epoch INTEGER NOT NULL,uid INTEGER NOT NULL,sender TEXT NOT NULL,subject TEXT NOT NULL,received TEXT,is_read INTEGER NOT NULL,is_starred INTEGER NOT NULL DEFAULT 0 CHECK(is_starred IN (0,1)),body BLOB,PRIMARY KEY(account,mailbox,epoch,uid)); PRAGMA application_id={ApplicationId}; PRAGMA user_version=5;" + RevisionSchema + SnapshotSchema;
                command.ExecuteNonQuery(); transaction.Commit();
            }
            if (version == 1)
            {
                BackupBeforeStarMigration(connection);
                // Enforce the existing quota and durable commit before the transactional upgrade.
                using (var policy = connection.CreateCommand())
                {
                    policy.CommandText = $"PRAGMA synchronous=FULL; PRAGMA max_page_count={_maximumBytes / Scalar(connection, "PRAGMA page_size")};";
                    policy.ExecuteNonQuery();
                }
                using var transaction = connection.BeginTransaction();
                using var migration = connection.CreateCommand(); migration.Transaction = transaction;
                migration.CommandText = "ALTER TABLE messages ADD COLUMN is_starred INTEGER NOT NULL DEFAULT 0 CHECK(is_starred IN (0,1)); PRAGMA user_version=2;";
                migration.ExecuteNonQuery(); transaction.Commit(); version = 2;
            }
            if (version == 2)
            {
                long pageSize = Scalar(connection, "PRAGMA page_size");
                if (Scalar(connection, "PRAGMA page_count") + 4 <= _maximumBytes / pageSize)
                {
                    BackupBeforeStarMigration(connection, 2);
                    using var upgrade = connection.BeginTransaction(); using var migration = connection.CreateCommand(); migration.Transaction = upgrade;
                    migration.CommandText = RevisionSchema + " PRAGMA user_version=3;"; migration.ExecuteNonQuery(); upgrade.Commit(); version = 3;
                }
                else _indexIssue = "Increase the cache quota or clear downloaded mail with recovery backup before enabling the search revision schema.";
            }
            using (var command = connection.CreateCommand())
            {
                long pageSize = Scalar(connection, "PRAGMA page_size");
                command.CommandText = $"PRAGMA synchronous=FULL; PRAGMA max_page_count={_maximumBytes / pageSize};";
                command.ExecuteNonQuery();
            }
            if (version == 3)
            {
                // Format v4 permits retained-body JSON. Older builds must refuse it rather than overwrite unfamiliar body data.
                BackupBeforeStarMigration(connection, 3);
                using var upgrade = connection.BeginTransaction(); using var migration = connection.CreateCommand(); migration.Transaction = upgrade;
                migration.CommandText = "PRAGMA user_version=4;"; migration.ExecuteNonQuery(); upgrade.Commit(); version = 4;
            }
            if (version == 4 && Scalar(connection, "PRAGMA page_count") + 4 <= _maximumBytes / Scalar(connection, "PRAGMA page_size"))
            {
                BackupBeforeStarMigration(connection, 4);
                using var upgrade = connection.BeginTransaction(); using var migration = connection.CreateCommand(); migration.Transaction = upgrade;
                migration.CommandText = SnapshotSchema + " PRAGMA user_version=5;"; migration.ExecuteNonQuery(); upgrade.Commit();
            }
            if (attachIndex) AttachSearchIndex(connection);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private void BackupBeforeStarMigration(SqliteConnection source, int version = 1)
    {
        string backupPath = _path + $".schema-v{version}-" + Guid.NewGuid().ToString("N") + ".backup";
        string stage = backupPath + ".partial";
        try
        {
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = stage, Pooling = false, DefaultTimeout = 2 }.ToString()))
            { backup.Open(); source.BackupDatabase(backup); }
            ValidateSnapshot(stage);
            File.Move(stage, backupPath);
        }
        finally
        {
            try { if (File.Exists(stage)) File.Delete(stage); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void ValidateBody(MailMessageBody body)
    {
        if (body.PlainText is null || body.PlainText.Length > 32000 || body.RetainedBodyText?.Length > MailMessageBody.MaximumRetainedTextCharacters || body.HtmlText?.Length > 1_280_000 ||
            body.EmbeddedImages is null || body.EmbeddedImages.Count > 16 ||
            body.EmbeddedImages.Any(pair => pair.Key.Length > 4096 || pair.Value is null || pair.Value.Data is null || pair.Value.Data.Length > 1_048_576) ||
            body.EmbeddedImages.Sum(pair => (long)pair.Value.Data.Length) > 2_097_152)
            throw new InvalidDataException("Cached body exceeds supported text/image bounds.");
        if (body.Composition is { } composition)
        {
            IReadOnlyList<string>?[] headers = [composition.From, composition.ReplyTo, composition.To, composition.Cc, composition.References, composition.InReplyTo];
            if (composition.Subject is null || composition.Subject.Length > 8192 || composition.MessageId?.Length > 8192 ||
                headers.Any(values => values is null || values.Count > 256 || values.Any(value => value is null || value.Length > 8192)) ||
                headers.Sum(values => values!.Sum(value => (long)value.Length)) > 256 * 1024)
                throw new InvalidDataException("Cached reply headers exceed supported bounds.");
        }
    }

    private static SqliteCommand Scoped(SqliteConnection connection, MailboxKey mailbox)
    {
        var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$a", mailbox.AccountId.ToString()); command.Parameters.AddWithValue("$m", mailbox.MailboxId);
        command.Parameters.AddWithValue("$e", (long)mailbox.UidValidity); return command;
    }
    private static long Scalar(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); }
    private static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MailMessageBody))]
internal partial class CacheJsonContext : JsonSerializerContext;
