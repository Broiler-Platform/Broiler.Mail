using System.Text;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

/// <summary>Rewrites a freshly created (current-format) cache into an older on-disk schema, as an older build would have left it.</summary>
internal static class CacheSchemaFixture
{
    public const int CurrentVersion = 6;

    public static string DowngradeScript(int version)
    {
        if (version is < 1 or >= CurrentVersion) throw new ArgumentOutOfRangeException(nameof(version));
        var sql = new StringBuilder();
        // v6 added the attachment mark.
        sql.Append("ALTER TABLE messages DROP COLUMN has_attachments; ");
        // v5 added complete mailbox checkpoints.
        if (version < 5) sql.Append("DROP TABLE mailbox_snapshots; DROP TABLE mailbox_snapshot_uids; ");
        // v3 added the search revision schema (v4 only changed what body JSON may carry).
        if (version < 3) sql.Append("DROP TRIGGER search_insert; DROP TRIGGER search_update; DROP TRIGGER search_delete; DROP TABLE cache_search_state; ");
        // v2 added the star flag.
        if (version < 2) sql.Append("ALTER TABLE messages DROP COLUMN is_starred; ");
        sql.Append("PRAGMA user_version=").Append(version).Append(';');
        return sql.ToString();
    }

    public static void DowngradeTo(string path, int version)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt64(command.ExecuteScalar()) != CurrentVersion) throw new InvalidOperationException("Only a current-format cache can be downgraded.");
        command.CommandText = DowngradeScript(version);
        command.ExecuteNonQuery();
    }

    /// <summary>Pads the database past the smallest quota and returns a quota with a few free pages for ordinary writes but
    /// fewer than the four spare pages a schema migration needs, so opening the cache with it defers the migration.</summary>
    public static long FillSoMigrationDefers(string path)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE test_padding(data BLOB); INSERT INTO test_padding VALUES(zeroblob(300000));";
            command.ExecuteNonQuery();
        }
        return Scalar(path, "PRAGMA page_count") * Scalar(path, "PRAGMA page_size") + 3 * Scalar(path, "PRAGMA page_size");
    }

    public static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
