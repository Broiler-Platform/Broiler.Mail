using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed class JsonAccountNavigationStore(string path) : IAccountNavigationStore
{
    private readonly JsonConfigurationFile<AccountNavigationState> _file = new(path, () => new([]), Validate, ConfigurationJsonContext.Storage.NavigationPreferences, 512 * 1024);
    public async Task<AccountNavigation> LoadAsync(AccountId account, CancellationToken token = default)
    {
        CheckAccount(account); var state = await _file.ReadAsync(token).ConfigureAwait(false);
        var row = state.Accounts.SingleOrDefault(row => row.AccountId == account);
        return row is null ? new(account, "INBOX", []) : row with { PinnedMailboxIds = Array.AsReadOnly(row.PinnedMailboxIds.ToArray()) };
    }
    public Task SaveAsync(AccountNavigation navigation, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(navigation); ArgumentNullException.ThrowIfNull(navigation.PinnedMailboxIds);
        var snapshot = navigation with { PinnedMailboxIds = navigation.PinnedMailboxIds.ToArray() };
        Validate(new([snapshot]));
        return _file.UpdateAsync(state => new(state.Accounts.Where(row => row.AccountId != snapshot.AccountId)
            .Concat(snapshot.StartMailboxId == "INBOX" && snapshot.PinnedMailboxIds.Count == 0 ? [] : new[] { snapshot }).ToArray()), token);
    }
    internal async Task ValidateSnapshotAsync() => _ = await _file.ReadAsync(CancellationToken.None).ConfigureAwait(false);
    private static void CheckAccount(AccountId account) { if (account.Value == Guid.Empty) throw new ArgumentException("A navigation account is required."); }
    private static void CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || name.Any(char.IsControl)) throw new ArgumentException("Invalid navigation folder identity.");
        for (int index = 0; index < name.Length; index++)
            if (char.IsSurrogate(name[index]) && (!char.IsHighSurrogate(name[index]) || ++index >= name.Length || !char.IsLowSurrogate(name[index])))
                throw new ArgumentException("Navigation folders must contain valid Unicode.");
    }
    private static void Validate(AccountNavigationState state)
    {
        if (state.Accounts is null || state.Accounts.Count > 32) throw new ArgumentException("Use at most 32 account navigation preferences.");
        var accounts = new HashSet<AccountId>();
        foreach (var row in state.Accounts)
        {
            if (row is null) throw new ArgumentException("Invalid account navigation preferences.");
            CheckAccount(row.AccountId); CheckName(row.StartMailboxId);
            if (!accounts.Add(row.AccountId) || row.PinnedMailboxIds is null || row.PinnedMailboxIds.Count > 8) throw new ArgumentException("Use at most eight pinned folders per account.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in row.PinnedMailboxIds) { CheckName(name); if (!names.Add(name)) throw new ArgumentException("Duplicate pinned folder identity."); }
        }
    }
}
