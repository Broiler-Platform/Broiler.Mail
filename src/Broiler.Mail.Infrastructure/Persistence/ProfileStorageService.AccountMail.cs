using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class ProfileStorageService
{
    private static void CheckCleanupAccount(AccountId account)
    { if (account.Value == Guid.Empty) throw new ArgumentException("A stable account identity is required."); }
    public Task<AccountDownloadedMailInfo> GetAccountDownloadedMailInfoAsync(AccountId account, CancellationToken token = default)
    {
        CheckCleanupAccount(account);
        return cache.WithExclusiveConnectionAsync(connection => ReadAccountMailInfo(connection, account), token);
    }
    private static AccountDownloadedMailInfo ReadAccountMailInfo(SqliteConnection connection, AccountId account)
    {
        using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$a", account.ToString());
        command.CommandText = "SELECT count(*),count(body),(SELECT count(*) FROM mailbox_heads WHERE account=$a) FROM messages WHERE account=$a";
        using var rows = command.ExecuteReader(); rows.Read(); return new(account, rows.GetInt64(0), rows.GetInt64(1), rows.GetInt64(2));
    }
    public Task<AccountMailCleanupResult> ClearAccountDownloadedMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated = null, CancellationToken token = default)
        => ClearLocalAccountMailAsync(account, recoveryDirectory, backupCreated, false, token);
    public Task<AccountMailCleanupResult> ClearOrphanDownloadedMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated = null, CancellationToken token = default)
        => ClearLocalAccountMailAsync(account, recoveryDirectory, backupCreated, true, token);
    private Task<AccountMailCleanupResult> ClearLocalAccountMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated, bool requireOrphan, CancellationToken token)
    {
        CheckCleanupAccount(account);
        return cache.WithExclusiveConnectionAsync(connection =>
        {
            using var locks = AcquireConfigurationLocks(token);
            if (requireOrphan && new JsonAccountCollectionStore(ProfileDirectory).ReadExistingAccountsAsync(token).GetAwaiter().GetResult().Any(row => row.Id == account))
                throw new InvalidOperationException("This account is configured again. Reload the removed-account mail list; use the configured account controls for its mail.");
            if (cache.GetPendingRecoveryCountAsync(token).GetAwaiter().GetResult() != 0)
                throw new InvalidOperationException("Resolve pending confirmed or uncertain mailbox changes before clearing downloaded mail. No server commands were sent.");
            var before = ReadAccountMailInfo(connection, account);
            string backup = CreateSnapshot(connection, recoveryDirectory, token); backupCreated?.Invoke(backup);
            token.ThrowIfCancellationRequested();
            string offline = Path.Combine(ProfileDirectory, "offline-folders.json");
            if (File.Exists(offline)) new JsonOfflineFolderStore(offline).ResetProgressWithLockHeldAsync(token, account).GetAwaiter().GetResult();
            cache.ClearAccountDownloadedMail(connection, account, token);
            // Once deletion commits, cancellation/compaction cannot turn it into an unconfirmed failure.
            bool compacted = true;
            try { using var vacuum = connection.CreateCommand(); vacuum.CommandText = "VACUUM main"; vacuum.ExecuteNonQuery(); }
            catch (SqliteException) { compacted = false; }
            return new AccountMailCleanupResult(account, backup, compacted, before.HeaderCount, before.DownloadedBodyCount);
        }, token);
    }
}
