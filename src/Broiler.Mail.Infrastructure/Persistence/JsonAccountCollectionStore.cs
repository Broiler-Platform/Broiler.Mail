using System.Diagnostics;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Versioned multi-account foundation. Legacy accounts.json is preserved as the import source.</summary>
public sealed class JsonAccountCollectionStore : IAccountCollectionStore
{
    public const int MaximumAccounts = 32;
    private readonly string _path;
    private readonly string _legacyPath;
    private readonly JsonConfigurationFile<AccountCollectionState> _file;

    public JsonAccountCollectionStore(string directory)
    {
        directory = Path.GetFullPath(directory);
        _path = Path.Combine(directory, "account-collection.json");
        _legacyPath = Path.Combine(directory, "accounts.json");
        _file = new(_path, () => new([], null), Validate, ConfigurationJsonContext.Storage.AccountCollection);
    }

    // Maintenance callers hold configuration locks: read the authoritative collection without importing/writing legacy state.
    internal async Task<IReadOnlyList<AccountProfile>> ReadExistingAccountsAsync(CancellationToken token)
        => File.Exists(_path) ? (await _file.ReadAsync(token).ConfigureAwait(false)).Accounts
            : await new JsonAccountStore(_legacyPath).LoadAsync(token).ConfigureAwait(false);

    public async Task<AccountCollectionState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        return await _file.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
        (await LoadStateAsync(cancellationToken).ConfigureAwait(false)).Accounts;

    public async Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default)
        => await SaveProfileAsync(profile, false, cancellationToken).ConfigureAwait(false);

    public async Task SaveAndSelectAsync(AccountProfile profile, CancellationToken cancellationToken = default)
        => await SaveProfileAsync(profile, true, cancellationToken).ConfigureAwait(false);

    private async Task SaveProfileAsync(AccountProfile profile, bool select, CancellationToken cancellationToken)
    {
        ConfigurationValidator.Validate(profile);
        await EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        await _file.UpdateAsync(state =>
        {
            int index = Array.FindIndex(state.Accounts, account => account.Id == profile.Id);
            var accounts = state.Accounts.ToArray();
            if (index < 0) accounts = [.. accounts, profile]; else accounts[index] = profile;
            return new(accounts, select ? profile.Id : state.SelectedAccountId ?? profile.Id);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        await _file.UpdateAsync(state =>
        {
            if (!state.Accounts.Any(account => account.Id == accountId)) throw new InvalidOperationException("The selected account no longer exists.");
            return state with { SelectedAccountId = accountId };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        await _file.UpdateAsync(state =>
        {
            var remaining = state.Accounts.Where(account => account.Id != accountId).ToArray();
            return new(remaining, state.SelectedAccountId == accountId ? remaining.FirstOrDefault()?.Id : state.SelectedAccountId);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveIfUnchangedAsync(AccountProfile expected, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(expected);
        await EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        await _file.UpdateAsync(state =>
        {
            if (state.SelectedAccountId != expected.Id || !AccountProfileConfiguration.Matches(expected, state.Accounts.SingleOrDefault(profile => profile.Id == expected.Id)))
                throw new InvalidOperationException("The selected account changed. Review it before removing it.");
            var remaining = state.Accounts.Where(profile => profile.Id != expected.Id).ToArray();
            return new(remaining, remaining.FirstOrDefault()?.Id);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderAsync(IReadOnlyList<AccountId> accountIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        var order = accountIds.ToArray();
        await EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        await _file.UpdateAsync(state =>
        {
            if (order.Length != state.Accounts.Length || order.Distinct().Count() != order.Length ||
                order.Any(id => !state.Accounts.Any(account => account.Id == id)))
                throw new InvalidOperationException("Account order must contain every saved account exactly once.");
            return state with { Accounts = order.Select(id => state.Accounts.Single(account => account.Id == id)).ToArray() };
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureImportedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(_path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Share the old writer's lock while taking the import snapshot. Collection writers
        // serialize independently; the winner's published collection is never replaced by import.
        var elapsed = Stopwatch.StartNew();
        FileStream lease;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { lease = new(_legacyPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(3)) { await Task.Delay(50, token).ConfigureAwait(false); }
        }
        await using (lease)
        {
            if (File.Exists(_path)) return;
            var legacy = (await new JsonAccountStore(_legacyPath).LoadAsync(token).ConfigureAwait(false)).ToArray();
            await _file.UpdateAsync(existing => File.Exists(_path) ? existing : new(legacy, legacy.FirstOrDefault()?.Id), token).ConfigureAwait(false);
        }
    }

    private static void Validate(AccountCollectionState state)
    {
        if (state.Accounts is null || state.Accounts.Length > MaximumAccounts || state.Accounts.Any(account => account is null) ||
            state.Accounts.Select(account => account.Id).Distinct().Count() != state.Accounts.Length)
            throw new InvalidDataException("The account collection is invalid or exceeds the 32-account limit.");
        foreach (var account in state.Accounts) ConfigurationValidator.Validate(account);
        if (state.Accounts.Length == 0 ? state.SelectedAccountId is not null :
            state.SelectedAccountId is null || !state.Accounts.Any(account => account.Id == state.SelectedAccountId))
            throw new InvalidDataException("The selected account must identify a saved profile.");
    }
}
