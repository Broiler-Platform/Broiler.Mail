using System.Globalization;
using System.Text.Json;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class SqliteMailCache
{
    private const long IndexMaximumBytes = 256L * 1024 * 1024;
    private const int IndexApplicationId = 0x424D5345;
    private string IndexPath => _path + ".search.db";
    private string _indexIssue = "Search index is disabled. Rebuild it in Settings → Storage to use indexed phrase search.";
    private const string RevisionSchema = """
        CREATE TABLE cache_search_state(id INTEGER PRIMARY KEY CHECK(id=1),revision INTEGER NOT NULL CHECK(typeof(revision)='integer' AND revision>=0));
        INSERT INTO cache_search_state VALUES(1,0);
        CREATE TRIGGER search_insert AFTER INSERT ON messages BEGIN UPDATE cache_search_state SET revision=revision+1 WHERE id=1; END;
        CREATE TRIGGER search_update AFTER UPDATE OF sender,subject,body ON messages BEGIN UPDATE cache_search_state SET revision=revision+1 WHERE id=1; END;
        CREATE TRIGGER search_delete AFTER DELETE ON messages BEGIN UPDATE cache_search_state SET revision=revision+1 WHERE id=1; END;
        """;
    private static long SearchRevision(SqliteConnection connection, bool index = false)
    {
        using var command = connection.CreateCommand();
        command.CommandText = index ? "SELECT revision FROM search.state WHERE id=1" : "SELECT revision FROM main.cache_search_state WHERE id=1";
        return command.ExecuteScalar() is long revision && revision >= 0 ? revision : throw new InvalidDataException("Search revision is missing or invalid.");
    }
    private static bool HasSearchIndex(SqliteConnection connection) => Scalar(connection, "SELECT count(*) FROM pragma_database_list WHERE name='search'") == 1;
    private void AttachSearchIndex(SqliteConnection connection)
    {
        if (!File.Exists(IndexPath)) { _indexIssue = "Search index is disabled. Rebuild it in Settings → Storage."; return; }
        try
        {
            if (Scalar(connection, "PRAGMA user_version") is not (4 or 5)) throw new InvalidDataException("Search/body-retention schema is not ready; increase the cache quota.");
            using var mode = connection.CreateCommand(); mode.CommandText = "PRAGMA main.journal_mode";
            if (!string.Equals((string?)mode.ExecuteScalar(), "delete", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Indexed maintenance requires SQLite DELETE journal mode.");
            using var attach = connection.CreateCommand(); attach.CommandText = "ATTACH DATABASE $path AS search"; attach.Parameters.AddWithValue("$path", IndexPath); attach.ExecuteNonQuery();
            if (Scalar(connection, "PRAGMA search.application_id") != IndexApplicationId || Scalar(connection, "PRAGMA search.user_version") != 2)
                throw new InvalidDataException("Search index version/identity is unsupported.");
            if (SearchRevision(connection, index: true) != SearchRevision(connection))
                throw new InvalidDataException("Search index is stale; rebuild it from saved mail.");
            attach.Parameters.Clear(); attach.CommandText = "SELECT account,mailbox,epoch,uid,sender,subject,preview,retained FROM search.docs LIMIT 0"; using (attach.ExecuteReader()) { }
            attach.CommandText = "PRAGMA search.synchronous=FULL; PRAGMA search.max_page_count=" + IndexMaximumBytes / Scalar(connection, "PRAGMA search.page_size"); attach.ExecuteNonQuery();
            _indexIssue = "Index ready: word phrases over saved headers, previews and complete retained decoded bodies.";
        }
        catch (Exception error) when (error is SqliteException or InvalidDataException)
        {
            _indexIssue = "Search index unavailable: " + error.Message + " Literal search remains available; rebuild or disable the index in Storage.";
            if (HasSearchIndex(connection)) { using var detach = connection.CreateCommand(); detach.CommandText = "DETACH DATABASE search"; detach.ExecuteNonQuery(); }
        }
    }
    public Task<MailSearchIndexInfo> GetSearchIndexInfoAsync(CancellationToken token = default) => RunAsync(connection =>
    {
        bool ready = HasSearchIndex(connection);
        long documents = ready ? Scalar(connection, "SELECT count(*) FROM search.docs") : 0;
        long retained = ready ? Scalar(connection, "SELECT count(*) FROM search.docs WHERE retained IS NOT NULL") : 0;
        return new MailSearchIndexInfo(ready, documents, File.Exists(IndexPath) ? new FileInfo(IndexPath).Length : 0,
            _indexIssue + (ready ? $" Complete retained body text: {retained} of {documents} indexed messages; others have headers/preview coverage only. Older, incomplete or over-limit bodies need reacquisition where supported; rebuilding cannot recover discarded text." : "")) { RetainedBodyDocuments = retained };
    }, token);
    internal void RemoveSearchIndex(SqliteConnection connection)
    {
        if (HasSearchIndex(connection)) { using var detach = connection.CreateCommand(); detach.CommandText = "DETACH DATABASE search"; detach.ExecuteNonQuery(); }
        File.Delete(IndexPath); _indexIssue = "Search index disabled; saved mail is unchanged.";
    }
    public Task DisableSearchIndexAsync(CancellationToken token = default) => RunAsync(connection =>
    { token.ThrowIfCancellationRequested(); File.Delete(IndexPath); _indexIssue = "Search index disabled. Saved mail is unchanged; literal search remains available."; return true; }, token, attachIndex: false);
    public Task RebuildSearchIndexAsync(CancellationToken token = default) => RunAsync(connection =>
    {
        if (Scalar(connection, "PRAGMA user_version") is not (4 or 5)) throw new MailSearchIndexUnavailableException(_indexIssue);
        _ = SearchRevision(connection);
        string stage = IndexPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE $path AS search"; attach.Parameters.AddWithValue("$path", stage); attach.ExecuteNonQuery();
                attach.Parameters.Clear(); attach.CommandText = $"PRAGMA search.synchronous=FULL; PRAGMA search.max_page_count={IndexMaximumBytes / Scalar(connection, "PRAGMA search.page_size")}; PRAGMA search.application_id={IndexApplicationId}; PRAGMA search.user_version=2; CREATE TABLE search.state(id INTEGER PRIMARY KEY,revision INTEGER NOT NULL); INSERT INTO search.state VALUES(1,0); CREATE VIRTUAL TABLE search.docs USING fts5(account UNINDEXED,mailbox UNINDEXED,epoch UNINDEXED,uid UNINDEXED,sender,subject,preview,retained,tokenize='unicode61 remove_diacritics 0');";
                attach.ExecuteNonQuery();
            }
            using (var transaction = connection.BeginTransaction())
            {
                using var read = connection.CreateCommand(); read.Transaction = transaction; read.CommandText = "SELECT account,mailbox,epoch,uid FROM messages";
                using (var reader = read.ExecuteReader())
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        SyncSearchDocument(connection, transaction, new(new AccountId(Guid.Parse(reader.GetString(0))), reader.GetString(1), checked((uint)reader.GetInt64(2)), checked((uint)reader.GetInt64(3))), token, replace: false);
                    }
                SetIndexRevision(connection, transaction); token.ThrowIfCancellationRequested(); transaction.Commit();
            }
            using (var detach = connection.CreateCommand()) { detach.CommandText = "DETACH DATABASE search"; detach.ExecuteNonQuery(); }
            token.ThrowIfCancellationRequested(); File.Move(stage, IndexPath, overwrite: true); _indexIssue = "Index rebuilt from saved headers, previews and complete retained decoded bodies."; return true;
        }
        finally
        {
            if (HasSearchIndex(connection)) { using var detach = connection.CreateCommand(); detach.CommandText = "DETACH DATABASE search"; detach.ExecuteNonQuery(); }
            if (File.Exists(stage)) File.Delete(stage);
        }
    }, token, attachIndex: false);
    private static void SetIndexRevision(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var state = connection.CreateCommand(); state.Transaction = transaction;
        state.CommandText = "UPDATE search.state SET revision=(SELECT revision FROM main.cache_search_state WHERE id=1) WHERE id=1"; state.ExecuteNonQuery();
    }
    private static void SyncSearchDocument(SqliteConnection connection, SqliteTransaction transaction, MailMessageKey key, CancellationToken token, bool replace = true)
    {
        if (!HasSearchIndex(connection)) return;
        token.ThrowIfCancellationRequested(); var mailbox = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        using var command = Scoped(connection, mailbox); command.Transaction = transaction; command.Parameters.AddWithValue("$uid", (long)key.Uid);
        if (replace) { command.CommandText = "DELETE FROM search.docs WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid"; command.ExecuteNonQuery(); }
        string? sender = null, subject = null, retained = null; string preview = "";
        command.CommandText = "SELECT sender,subject,length(body),body FROM messages WHERE account=$a AND mailbox=$m AND epoch=$e AND uid=$uid";
        using (var reader = command.ExecuteReader())
            if (reader.Read())
            {
                sender = reader.GetString(0); subject = reader.GetString(1);
                if (sender.Length > 8192 || subject.Length > 8192) throw new InvalidDataException("Indexed headers exceed bounds.");
                if (!reader.IsDBNull(2))
                {
                    if (reader.GetInt64(2) > MaximumBodyBytes) throw new InvalidDataException("Indexed body exceeds bounds.");
                    var body = JsonSerializer.Deserialize((byte[])reader.GetValue(3), CacheJsonContext.Default.MailMessageBody) ?? throw new InvalidDataException("Indexed body missing.");
                    if (body.Key != key) throw new InvalidDataException("Indexed body identity mismatch."); ValidateBody(body); preview = body.PlainText; retained = body.RetainedBodyText;
                }
            }
        if (sender is not null)
        {
            command.CommandText = "INSERT INTO search.docs(account,mailbox,epoch,uid,sender,subject,preview,retained) VALUES($a,$m,$e,$uid,$sender,$subject,$preview,$retained)";
            command.Parameters.AddWithValue("$sender", sender); command.Parameters.AddWithValue("$subject", subject!); command.Parameters.AddWithValue("$preview", preview); command.Parameters.AddWithValue("$retained", (object?)retained ?? DBNull.Value); command.ExecuteNonQuery();
        }
        SetIndexRevision(connection, transaction);
    }
}
