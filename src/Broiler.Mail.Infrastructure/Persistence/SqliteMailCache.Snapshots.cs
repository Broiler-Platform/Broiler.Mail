using System.Buffers.Binary;
using System.Security.Cryptography;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class SqliteMailCache
{
    private const string SnapshotSchema = """
        CREATE TABLE mailbox_snapshots(account TEXT NOT NULL,mailbox TEXT NOT NULL,epoch INTEGER NOT NULL CHECK(epoch BETWEEN 1 AND 4294967295),observed TEXT NOT NULL,uid_count INTEGER NOT NULL CHECK(uid_count BETWEEN 0 AND 4096),uid_hash TEXT NOT NULL CHECK(length(uid_hash)=64),PRIMARY KEY(account,mailbox));
        CREATE TABLE mailbox_snapshot_uids(account TEXT NOT NULL,mailbox TEXT NOT NULL,epoch INTEGER NOT NULL CHECK(epoch BETWEEN 1 AND 4294967295),uid INTEGER NOT NULL CHECK(uid BETWEEN 1 AND 4294967295),PRIMARY KEY(account,mailbox,epoch,uid));
        """;

    public Task<MailMailboxCheckpoint> ApplyMailboxSnapshotAsync(MailMailboxSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(snapshot.Mailbox); ArgumentNullException.ThrowIfNull(snapshot.Messages);
        var box = snapshot.Mailbox;
        if (Broiler.Mail.Infrastructure.Protocols.Imap.ImapMailboxName.Validate(box.MailboxId) != box.MailboxId) throw new ArgumentException("Use the canonical mailbox identity.");
        var rows = snapshot.Messages.ToArray();
        if (snapshot.ObservedAt == default || rows.Length > MailMailboxSnapshot.MaximumMessages || rows.Any(row => row is null || !box.Contains(row.Key) || row.Sender is null || row.Subject is null || row.Sender.Length > 8192 || row.Subject.Length > 8192) || rows.Select(row => row.Key.Uid).Distinct().Count() != rows.Length)
            throw new ArgumentException("The complete snapshot must contain bounded unique headers from one mailbox epoch.");
        uint[] uids = rows.Select(row => row.Key.Uid).Order().ToArray(); string hash = SnapshotHash(uids);
        return RunAsync(connection =>
        {
            if (Scalar(connection, "PRAGMA user_version") < 5) throw new MailMailboxSnapshotUnavailableException("Increase the cache quota to enable complete mailbox checkpoints; saved mail was kept.");
            using var transaction = connection.BeginTransaction();
            using (var newer = Scoped(connection, box))
            {
                newer.Transaction = transaction; newer.CommandText = "SELECT received FROM mailbox_heads WHERE account=$a AND mailbox=$m UNION ALL SELECT observed FROM mailbox_snapshots WHERE account=$a AND mailbox=$m";
                using var reader = newer.ExecuteReader();
                while (reader.Read()) if (ParseDate(reader.GetString(0)) > snapshot.ObservedAt) throw new InvalidDataException("A newer saved observation exists; obtain a fresh complete snapshot.");
            }
            foreach (var row in rows) { token.ThrowIfCancellationRequested(); UpsertHeaderAndIndex(connection, transaction, box, row, token); }
            using var command = Scoped(connection, box); command.Transaction = transaction;
            command.CommandText = "DELETE FROM mailbox_snapshot_uids WHERE account=$a AND mailbox=$m"; command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO mailbox_snapshot_uids VALUES($a,$m,$e,$uid)"; var uidParameter = command.Parameters.Add("$uid", SqliteType.Integer);
            foreach (uint uid in uids) { token.ThrowIfCancellationRequested(); uidParameter.Value = (long)uid; command.ExecuteNonQuery(); }
            command.Parameters.Remove(uidParameter);
            command.CommandText = "INSERT INTO mailbox_snapshots VALUES($a,$m,$e,$date,$count,$hash) ON CONFLICT(account,mailbox) DO UPDATE SET epoch=excluded.epoch,observed=excluded.observed,uid_count=excluded.uid_count,uid_hash=excluded.uid_hash";
            command.Parameters.AddWithValue("$date", FormatDate(snapshot.ObservedAt)); command.Parameters.AddWithValue("$count", rows.Length); command.Parameters.AddWithValue("$hash", hash); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO mailbox_heads VALUES($a,$m,$e,$date) ON CONFLICT(account,mailbox) DO UPDATE SET epoch=excluded.epoch,received=excluded.received"; command.ExecuteNonQuery();
            long absent = SnapshotAbsentCount(connection, box, transaction);
            token.ThrowIfCancellationRequested(); transaction.Commit();
            return new MailMailboxCheckpoint(box, snapshot.ObservedAt, rows.Length, hash, absent);
        }, token);
    }

    public Task<MailMailboxCheckpoint?> ReadMailboxCheckpointAsync(AccountId account, string mailboxId, CancellationToken token = default)
    {
        _ = new MailboxKey(account, mailboxId, 1);
        return RunAsync<MailMailboxCheckpoint?>(connection =>
        {
            if (Scalar(connection, "PRAGMA user_version") < 5) return null;
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT epoch,observed,uid_count,uid_hash FROM mailbox_snapshots WHERE account=$a AND mailbox=$m";
            command.Parameters.AddWithValue("$a", account.ToString()); command.Parameters.AddWithValue("$m", mailboxId);
            MailboxKey box; DateTimeOffset date; long count; string hash;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) return null;
                box = new(account, mailboxId, checked((uint)reader.GetInt64(0))); date = ParseDate(reader.GetString(1)); count = reader.GetInt64(2); hash = reader.GetString(3);
                if (count is < 0 or > MailMailboxSnapshot.MaximumMessages || date == default || hash.Length != 64) throw new InvalidDataException("Invalid mailbox checkpoint.");
            }
            command.CommandText = "SELECT uid FROM mailbox_snapshot_uids WHERE account=$a AND mailbox=$m AND epoch=$e ORDER BY uid LIMIT 4097"; command.Parameters.AddWithValue("$e", (long)box.UidValidity);
            var uids = new List<uint>(); using (var reader = command.ExecuteReader()) while (reader.Read()) { token.ThrowIfCancellationRequested(); uint uid = checked((uint)reader.GetInt64(0)); if (uid == 0) throw new InvalidDataException("Invalid checkpoint UID."); uids.Add(uid); }
            if (uids.Count != count || SnapshotHash(uids) != hash) throw new InvalidDataException("Mailbox checkpoint membership is incomplete or changed.");
            var result = new MailMailboxCheckpoint(box, date, checked((int)count), hash, SnapshotAbsentCount(connection, box, transaction)); token.ThrowIfCancellationRequested(); transaction.Commit(); return result;
        }, token);
    }

    private static string SnapshotHash(IReadOnlyList<uint> uids)
    {
        byte[] bytes = new byte[checked(uids.Count * 4)];
        for (int i = 0; i < uids.Count; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4, 4), uids[i]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    private static long SnapshotAbsentCount(SqliteConnection connection, MailboxKey box, SqliteTransaction? transaction = null)
    {
        using var command = Scoped(connection, box); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM messages m WHERE m.account=$a AND m.mailbox=$m AND m.epoch=$e AND NOT EXISTS(SELECT 1 FROM mailbox_snapshot_uids u WHERE u.account=m.account AND u.mailbox=m.mailbox AND u.epoch=m.epoch AND u.uid=m.uid)";
        return (long)command.ExecuteScalar()!;
    }
    private static void UpsertHeaderAndIndex(SqliteConnection connection, SqliteTransaction transaction, MailboxKey box, MailMessageSummary row, CancellationToken token)
    {
        using var command = Scoped(connection, box); command.Transaction = transaction;
        command.CommandText = "INSERT INTO messages(account,mailbox,epoch,uid,sender,subject,received,is_read,is_starred) VALUES($a,$m,$e,$uid,$sender,$subject,$date,$read,$star) ON CONFLICT(account,mailbox,epoch,uid) DO UPDATE SET sender=excluded.sender,subject=excluded.subject,received=excluded.received,is_read=excluded.is_read,is_starred=excluded.is_starred";
        command.Parameters.AddWithValue("$uid", (long)row.Key.Uid); command.Parameters.AddWithValue("$sender", row.Sender); command.Parameters.AddWithValue("$subject", row.Subject);
        command.Parameters.AddWithValue("$date", row.ReceivedAt is {} date ? FormatDate(date) : DBNull.Value); command.Parameters.AddWithValue("$read", row.IsRead); command.Parameters.AddWithValue("$star", row.IsStarred); command.ExecuteNonQuery();
        SyncSearchDocument(connection, transaction, row.Key, token);
    }
}
