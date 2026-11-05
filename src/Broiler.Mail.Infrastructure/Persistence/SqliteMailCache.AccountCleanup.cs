using Broiler.Mail.Core.Accounts;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class SqliteMailCache
{
    // Called only under the cache gate and profile maintenance locks, after a verified recovery snapshot.
    internal void ClearAccountDownloadedMail(SqliteConnection connection, AccountId account, CancellationToken token)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.Parameters.AddWithValue("$a", account.ToString());
        command.CommandText = "DELETE FROM messages WHERE account=$a; DELETE FROM mailbox_heads WHERE account=$a";
        command.ExecuteNonQuery(); token.ThrowIfCancellationRequested();
        if (Scalar(connection, "PRAGMA user_version") >= 5)
        {
            command.CommandText = "DELETE FROM mailbox_snapshots WHERE account=$a; DELETE FROM mailbox_snapshot_uids WHERE account=$a"; command.ExecuteNonQuery();
        }
        if (HasSearchIndex(connection))
        {
            command.CommandText = "DELETE FROM search.docs WHERE account=$a"; command.ExecuteNonQuery();
            SetIndexRevision(connection, transaction);
        }
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
}
