using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Application.Persistence;

/// <summary>Serializes application account writes with receive-result validation/cache publication.
/// Network I/O never holds this gate. External configuration writers still require normal profile ownership.</summary>
public sealed class CoordinatedAccountCollectionStore(IAccountCollectionStore inner) : IAccountCollectionStore
{
    private readonly SemaphoreSlim _gate = new(1);
    private async Task<T> Read<T>(Func<Task<T>> action, CancellationToken token)
    { await _gate.WaitAsync(token).ConfigureAwait(false); try { return await action().ConfigureAwait(false); } finally { _gate.Release(); } }
    private async Task Write(Func<Task> action, CancellationToken token)
    { await _gate.WaitAsync(token).ConfigureAwait(false); try { token.ThrowIfCancellationRequested(); await action().ConfigureAwait(false); } finally { _gate.Release(); } }
    public Task<AccountCollectionState> LoadStateAsync(CancellationToken cancellationToken = default) => Read(() => inner.LoadStateAsync(cancellationToken), cancellationToken);
    public Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) => Read(() => inner.LoadAsync(cancellationToken), cancellationToken);
    public Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default) => Write(() => inner.SaveAsync(profile, cancellationToken), cancellationToken);
    public Task SaveAndSelectAsync(AccountProfile profile, CancellationToken cancellationToken = default) => Write(() => inner.SaveAndSelectAsync(profile, cancellationToken), cancellationToken);
    public Task SelectAsync(AccountId accountId, CancellationToken cancellationToken = default) => Write(() => inner.SelectAsync(accountId, cancellationToken), cancellationToken);
    public Task RemoveAsync(AccountId id, CancellationToken cancellationToken = default) => Write(() => inner.RemoveAsync(id, cancellationToken), cancellationToken);
    public Task RemoveIfUnchangedAsync(AccountProfile expected, CancellationToken cancellationToken = default) => Write(() => inner.RemoveIfUnchangedAsync(expected, cancellationToken), cancellationToken);
    public Task ReorderAsync(IReadOnlyList<AccountId> accountIds, CancellationToken cancellationToken = default) => Write(() => inner.ReorderAsync(accountIds, cancellationToken), cancellationToken);
    public Task WithStableProfileAsync(AccountProfile expected, Func<CancellationToken, Task> publish, CancellationToken token) => Write(async () =>
    {
        var state = await inner.LoadStateAsync(token).ConfigureAwait(false);
        if (state.Accounts is null || state.Accounts.Length > 32 || state.Accounts.Any(profile => profile is null) || state.Accounts.Select(profile => profile.Id).Distinct().Count() != state.Accounts.Length)
            throw new InvalidDataException("Account collection is invalid; no receive result was published.");
        var actual = state.Accounts.SingleOrDefault(profile => profile.Id == expected.Id);
        if (actual is not { IsEnabled: true } || !AccountProfileConfiguration.Matches(expected, actual)) throw new AccountReceiveConfigurationChangedException();
        ConfigurationValidator.Validate(actual); token.ThrowIfCancellationRequested();
        await publish(token).ConfigureAwait(false);
    }, token);
}
public sealed class AccountReceiveConfigurationChangedException() : InvalidOperationException("Account was changed, disabled or removed. Queue a new receive with its current saved configuration.");
