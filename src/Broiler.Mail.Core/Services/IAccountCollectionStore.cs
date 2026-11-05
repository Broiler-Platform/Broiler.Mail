using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Ordered, non-secret account configuration with a durable selected identity.</summary>
public sealed record AccountCollectionState(AccountProfile[] Accounts, AccountId? SelectedAccountId);

/// <summary>Storage only. UI removal must separately resolve drafts, secrets and local mail.</summary>
public interface IAccountCollectionStore : IAccountStore
{
    Task<AccountCollectionState> LoadStateAsync(CancellationToken cancellationToken = default);
    Task SelectAsync(AccountId accountId, CancellationToken cancellationToken = default);
    Task SaveAndSelectAsync(AccountProfile profile, CancellationToken cancellationToken = default);
    Task RemoveIfUnchangedAsync(AccountProfile expected, CancellationToken cancellationToken = default);
    Task ReorderAsync(IReadOnlyList<AccountId> accountIds, CancellationToken cancellationToken = default);
}
