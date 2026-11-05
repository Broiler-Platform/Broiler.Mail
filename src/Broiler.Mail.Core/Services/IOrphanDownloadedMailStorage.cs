using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit maintenance of downloaded mail whose account is no longer configured.</summary>
public interface IOrphanDownloadedMailStorage
{
    Task<OrphanDownloadedMailPage> FindOrphanDownloadedMailAsync(CancellationToken token = default);
    Task<AccountMailCleanupResult> ClearOrphanDownloadedMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated = null, CancellationToken token = default);
}
public sealed record OrphanDownloadedMailPage(IReadOnlyList<AccountDownloadedMailInfo> Accounts, bool HasMore);
