using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit local navigation preferences; never starts server synchronization.</summary>
public interface IAccountNavigationStore
{
    Task<AccountNavigation> LoadAsync(AccountId account, CancellationToken token = default);
    Task SaveAsync(AccountNavigation navigation, CancellationToken token = default);
}
public sealed record AccountNavigation(AccountId AccountId, string StartMailboxId, IReadOnlyList<string> PinnedMailboxIds);
public sealed record AccountNavigationState(IReadOnlyList<AccountNavigation> Accounts);
