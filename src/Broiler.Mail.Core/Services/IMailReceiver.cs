using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Version 1 read-only IMAP operations. Implementations must not mark messages read.</summary>
public interface IMailReceiver
{
    Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default);
    Task<MailInboxPage> GetInboxAsync(
        AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default);
    Task<MailMessageBody> GetBodyAsync(
        AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default);
}
