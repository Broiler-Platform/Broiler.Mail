using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Local downloaded-mail maintenance only. Caller must quiesce receive and flush drafts before cleanup.</summary>
public interface IAccountDownloadedMailStorage
{
    string ProfileDirectory { get; }
    Task<AccountDownloadedMailInfo> GetAccountDownloadedMailInfoAsync(AccountId account, CancellationToken token = default);
    Task<AccountMailCleanupResult> ClearAccountDownloadedMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated = null, CancellationToken token = default);
}
public sealed record AccountDownloadedMailInfo(AccountId AccountId, long HeaderCount, long DownloadedBodyCount, long MailboxCount);
public sealed record AccountMailCleanupResult(AccountId AccountId, string BackupDirectory, bool Compacted, long RemovedHeaderCount, long RemovedBodyCount);
