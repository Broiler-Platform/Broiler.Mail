using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Recovery-backed configuration removal. Cached/server mail is never deleted.</summary>
public interface IAccountRemovalService
{
    Task RemoveAsync(AccountProfile expected, bool forgetPasswords, Action<string> backupCreated,
        CancellationToken cancellationToken = default);
}
