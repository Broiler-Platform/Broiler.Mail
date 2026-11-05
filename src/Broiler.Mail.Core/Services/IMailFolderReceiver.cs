using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Read-only access to explicitly identified server mailboxes.</summary>
public interface IMailFolderReceiver : IMailReceiver
{
    Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default);

    Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount,
        MailInboxCursor? older = null, CancellationToken cancellationToken = default);
}
