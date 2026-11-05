using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Mailbox identities with saved headers, including successfully received empty mailboxes.</summary>
public interface IMailFolderCache
{
    Task<IReadOnlyList<string>> GetCachedMailboxIdsAsync(AccountId account, CancellationToken cancellationToken = default);
}
