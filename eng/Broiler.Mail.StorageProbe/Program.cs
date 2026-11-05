using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.StorageProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--interrupted-writer", var database, var ready])
        {
            using var connection = Open(database);
            using var transaction = connection.BeginTransaction();
            Execute(connection, "INSERT INTO messages VALUES ('account-a','INBOX',7,999999,'uncommitted')", transaction);
            File.WriteAllText(ready, "ready");
            Thread.Sleep(TimeSpan.FromSeconds(30)); // Parent kills only this isolated child after its uncommitted write.
            return 2;
        }
        if (args.Length != 0) return 2;

        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Broiler.Mail.StorageProbe")) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(Path.Combine(root, Guid.NewGuid().ToString("N")));
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid probe path.");
        Directory.CreateDirectory(directory);
        try
        {
            var report = Run(directory);
            Console.WriteLine(JsonSerializer.Serialize(report, ProbeJsonContext.Default.ProbeReport));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProbeReport Run(string directory)
    {
        string database = Path.Combine(directory, "mail.db");
        var checks = new List<string>();
        using var connection = Open(database);
        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA user_version=1;" +
            "CREATE TABLE messages(account TEXT NOT NULL, mailbox TEXT NOT NULL, epoch INTEGER NOT NULL, uid INTEGER NOT NULL," +
            "subject TEXT NOT NULL, PRIMARY KEY(account,mailbox,epoch,uid));");

        var watch = Stopwatch.StartNew();
        using (var transaction = connection.BeginTransaction())
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO messages VALUES ('account-a','INBOX',7,$uid,$subject)";
            var uid = insert.Parameters.Add("$uid", SqliteType.Integer);
            var subject = insert.Parameters.Add("$subject", SqliteType.Text);
            insert.Prepare();
            for (int i = 1; i <= 20000; i++) { uid.Value = i; subject.Value = $"Synthetic message {i}"; insert.ExecuteNonQuery(); }
            transaction.Commit();
        }
        long insertMs = watch.ElapsedMilliseconds;
        Execute(connection, "INSERT INTO messages VALUES ('account-b','INBOX',7,1,'Other account'),('account-a','INBOX',8,1,'Other epoch');");
        watch.Restart();
        using (var page = connection.CreateCommand())
        {
            page.CommandText = "SELECT uid FROM messages WHERE account=$account AND mailbox=$mailbox AND epoch=$epoch AND uid<$before ORDER BY uid DESC LIMIT 50";
            page.Parameters.AddWithValue("$account", "account-a"); page.Parameters.AddWithValue("$mailbox", "INBOX");
            page.Parameters.AddWithValue("$epoch", 7); page.Parameters.AddWithValue("$before", 15000);
            using var reader = page.ExecuteReader();
            int count = 0;
            while (reader.Read()) { Require(reader.GetInt64(0) == 14999 - count, "Scoped keyset page mismatch."); count++; }
            Require(count == 50, "Bounded page missing rows.");
        }
        long pageMs = watch.ElapsedMilliseconds;
        checks.Add("20000-row keyset paging; account and UIDVALIDITY isolation");

        Execute(connection, "CREATE VIRTUAL TABLE search USING fts5(subject); INSERT INTO search SELECT subject FROM messages;");
        Require(Scalar(connection, "SELECT count(*) FROM search WHERE search MATCH '\"Synthetic message\"'") == 20000, "FTS5 search mismatch.");
        checks.Add("FTS5 search");

        using (var migration = connection.BeginTransaction())
        {
            Execute(connection, "ALTER TABLE messages ADD COLUMN migrated INTEGER; PRAGMA user_version=2;", migration);
            migration.Rollback();
        }
        Require(Scalar(connection, "PRAGMA user_version") == 1, "Rolled-back migration advanced schema.");
        using (var migration = connection.BeginTransaction())
        {
            Execute(connection, "CREATE TABLE schema2_marker(value INTEGER); PRAGMA user_version=2;", migration);
            migration.Commit();
        }
        Require(Scalar(connection, "PRAGMA user_version") == 2, "Committed migration did not persist.");
        Execute(connection, "PRAGMA user_version=99");
        bool refused = false;
        try { CheckSchema(connection, 2); } catch (InvalidDataException) { refused = true; }
        Require(refused && Scalar(connection, "PRAGMA user_version") == 99, "Newer schema was not preserved.");
        Execute(connection, "PRAGMA user_version=2");
        checks.Add("transactional migration commit/rollback; newer-schema refusal without reset");

        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, "INSERT INTO messages VALUES ('account-a','INBOX',7,999998,'rollback')", transaction);
            transaction.Rollback();
        }
        Require(Scalar(connection, "SELECT count(*) FROM messages WHERE uid=999998") == 0, "Rollback lost isolation.");
        using (var writer = connection.BeginTransaction())
        using (var contender = Open(database))
        {
            bool locked = false;
            try { Execute(contender, "INSERT INTO messages VALUES ('account-a','INBOX',7,999997,'contender')"); }
            catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6) { locked = true; }
            Require(locked, "Second writer bypassed transaction lock.");
            writer.Rollback();
        }
        checks.Add("explicit transaction rollback; concurrent writer rejection");

        // Kill a separate published process after it confirms its uncommitted write.
        string ready = Path.Combine(directory, "writer-ready");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        start.ArgumentList.Add("--interrupted-writer"); start.ArgumentList.Add(database); start.ArgumentList.Add(ready);
        using (var child = Process.Start(start) ?? throw new IOException("Could not start interrupted writer."))
        {
            try
            {
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(ready) && !child.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(20);
                Require(File.Exists(ready), "Interrupted writer did not reach its write.");
            }
            finally { if (!child.HasExited) child.Kill(); child.WaitForExit(); }
        }
        using (var reopened = Open(database))
        {
            Require(Scalar(reopened, "SELECT count(*) FROM messages WHERE uid=999999") == 0, "Uncommitted child write survived.");
            Require(Scalar(reopened, "SELECT count(*) FROM messages WHERE account='account-a' AND epoch=7") == 20000, "Crash lost committed rows.");
            using var integrity = reopened.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
            Require((string?)integrity.ExecuteScalar() == "ok", "Crash recovery integrity check failed.");
        }
        checks.Add("process-kill before commit; reopen and integrity_check preserve committed rows");

        ProbeDiskFull(Path.Combine(directory, "quota.db"));
        checks.Add("SQLITE_FULL via max_page_count; rejected write preserves committed data");

        // Both candidate blob shapes use the same bounded 64 KiB chunk, never a whole-message buffer.
        const int bytes = 8 * 1024 * 1024;
        byte[] chunk = new byte[64 * 1024]; RandomNumberGenerator.Fill(chunk);
        string blobPath = Path.Combine(directory, "message.mime");
        watch.Restart();
        using (var file = new FileStream(blobPath, FileMode.CreateNew, FileAccess.Write))
        { for (int i = 0; i < bytes / chunk.Length; i++) file.Write(chunk); file.Flush(flushToDisk: true); }
        long fileWriteMs = watch.ElapsedMilliseconds;
        Execute(connection, $"CREATE TABLE bodies(id INTEGER PRIMARY KEY, data BLOB NOT NULL); INSERT INTO bodies VALUES (1,zeroblob({bytes}));");
        watch.Restart();
        using (var transaction = connection.BeginTransaction())
        {
            using (var blob = new SqliteBlob(connection, "bodies", "data", 1))
                for (int i = 0; i < bytes / chunk.Length; i++) blob.Write(chunk);
            transaction.Commit();
        }
        long sqliteWriteMs = watch.ElapsedMilliseconds;
        byte[] fileHash;
        using (var file = File.OpenRead(blobPath)) fileHash = SHA256.HashData(file);
        using (var blob = new SqliteBlob(connection, "bodies", "data", 1, readOnly: true))
            Require(SHA256.HashData(blob).SequenceEqual(fileHash), "Streamed SQLite blob hash mismatch.");
        checks.Add("8 MiB file/SQLite blob round trip with bounded 64 KiB writes and matching SHA-256");

        // Split-store commit discipline: rename completed blob first, then metadata reference.
        // A crash in between leaves an orphan; scan only the dedicated blob directory in M1.
        string orphan = Path.Combine(directory, "orphan.mime"); File.WriteAllText(orphan, "completed but unreferenced");
        using (var transaction = connection.BeginTransaction())
        { Execute(connection, "INSERT INTO messages VALUES ('account-a','INBOX',7,999996,'orphan reference')", transaction); transaction.Rollback(); }
        Require(File.Exists(orphan) && Scalar(connection, "SELECT count(*) FROM messages WHERE uid=999996") == 0, "Split-store orphan simulation failed.");
        File.Delete(orphan);
        checks.Add("split-store orphan classification after metadata rollback (simulation; no recovery engine)");

        using var sqliteVersion = connection.CreateCommand(); sqliteVersion.CommandText = "SELECT sqlite_version()";
        return new ProbeReport(RuntimeInformation.RuntimeIdentifier, RuntimeInformation.FrameworkDescription,
            (string)sqliteVersion.ExecuteScalar()!, 20000, insertMs, pageMs, fileWriteMs, sqliteWriteMs,
            Process.GetCurrentProcess().PeakWorkingSet64, checks.ToArray());
    }

    private static void ProbeDiskFull(string path)
    {
        using var connection = Open(path);
        Execute(connection, "PRAGMA journal_mode=DELETE; PRAGMA page_size=4096; CREATE TABLE quota(id INTEGER PRIMARY KEY,data BLOB); INSERT INTO quota VALUES(1,x'01'); PRAGMA max_page_count=8;");
        bool full = false;
        try { Execute(connection, "INSERT INTO quota VALUES(2,zeroblob(1048576));"); }
        catch (SqliteException error) when (error.SqliteErrorCode == 13) { full = true; }
        Require(full && Scalar(connection, "SELECT count(*) FROM quota") == 1, "Disk quota failure lost committed data.");
    }

    private static void CheckSchema(SqliteConnection connection, int supported)
    { if (Scalar(connection, "PRAGMA user_version") > supported) throw new InvalidDataException("Newer schema; no writes permitted."); }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open(); return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }
    private static long Scalar(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar()); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal sealed record ProbeReport(string RuntimeIdentifier, string Framework, string SqliteVersion, int Rows,
    long InsertMilliseconds, long PageMilliseconds, long FileBlobWriteMilliseconds, long SqliteBlobWriteMilliseconds,
    long PeakWorkingSetBytes, string[] Checks);

[JsonSerializable(typeof(ProbeReport))]
internal partial class ProbeJsonContext : JsonSerializerContext;
