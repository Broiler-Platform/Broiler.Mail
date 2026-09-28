using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

public interface IAccountStore
{
    Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default);
    Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default);
}
