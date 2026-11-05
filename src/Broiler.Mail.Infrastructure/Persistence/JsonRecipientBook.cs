using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Bounded account-partitioned recipient book, using the profile's atomic configuration writer.</summary>
public sealed class JsonRecipientBook(string path) : IRecipientBook
{
    private readonly JsonConfigurationFile<RecipientBookState> _file = new(path, () => new([]), Validate, ConfigurationJsonContext.Storage.Recipients);
    internal async Task ValidateSnapshotAsync() => _ = await _file.ReadAsync(CancellationToken.None).ConfigureAwait(false);
    public async Task<IReadOnlyList<SavedRecipient>> LoadAsync(AccountId account, CancellationToken token = default)
    {
        if (account.Value == Guid.Empty) throw new ArgumentException("A recipient account is required.");
        var state = await _file.ReadAsync(token).ConfigureAwait(false);
        return Array.AsReadOnly(state.Recipients.Where(row => row.AccountId == account).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Address, StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public Task SaveAsync(SavedRecipient recipient, CancellationToken token = default)
    {
        var value = SavedRecipient.Create(recipient.AccountId, recipient.Address, recipient.Name);
        return _file.UpdateAsync(state => new(state.Recipients.Where(row => row.AccountId != value.AccountId || !Same(row.Address, value.Address)).Append(value).ToArray()), token);
    }
    public Task RemoveAsync(AccountId account, string address, CancellationToken token = default)
    {
        var value = SavedRecipient.Create(account, address, "");
        return _file.UpdateAsync(state => new(state.Recipients.Where(row => row.AccountId != account || !Same(row.Address, value.Address)).ToArray()), token);
    }
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static void Validate(RecipientBookState state)
    {
        if (state.Recipients is null || state.Recipients.Count > 2000) throw new ArgumentException("Use at most 2,000 saved recipients per profile.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in state.Recipients)
        {
            if (row is null || row != SavedRecipient.Create(row.AccountId, row.Address, row.Name) || !keys.Add(row.AccountId + ":" + row.Address))
                throw new ArgumentException("Invalid or duplicate saved recipient.");
        }
        if (state.Recipients.GroupBy(row => row.AccountId).Any(group => group.Count() > 500)) throw new ArgumentException("Use at most 500 saved recipients per account.");
    }
}
