using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed class JsonOfflineFolderStore(string path) : IOfflineFolderStore, IOfflineFolderProgressStore
{
    private readonly JsonConfigurationFile<OfflineFolderState> _file = new(path, () => new([]), Validate, ConfigurationJsonContext.Storage.OfflineFolders, 512 * 1024);
    public async Task<IReadOnlyList<string>> LoadAsync(AccountId account, CancellationToken token = default)
    {
        CheckAccount(account); var state = await _file.ReadAsync(token).ConfigureAwait(false);
        return Array.AsReadOnly((state.Accounts.SingleOrDefault(row => row.AccountId == account)?.MailboxIds ?? []).ToArray());
    }
    public Task SaveAsync(AccountId account, IReadOnlyList<string> mailboxIds, CancellationToken token = default)
    {
        CheckAccount(account); ArgumentNullException.ThrowIfNull(mailboxIds); string[] snapshot = mailboxIds.ToArray();
        return _file.UpdateAsync(state => new(state.Accounts.Where(row => row.AccountId != account)
            .Concat(snapshot.Length == 0 ? [] : new[] { new OfflineFolderSelection(account, snapshot) }).ToArray()), token);
    }
    public async Task<IReadOnlyList<MailMessageKey>?> LoadProgressAsync(AccountId account, IReadOnlyList<string> selection, CancellationToken token = default)
    {
        CheckAccount(account); var state = await _file.ReadAsync(token).ConfigureAwait(false);
        var row = state.Accounts.SingleOrDefault(row => row.AccountId == account);
        if (!(row?.MailboxIds ?? []).SequenceEqual(selection, StringComparer.Ordinal))
            throw new InvalidDataException("Offline-folder selection changed; Reload before downloading.");
        return row?.Remaining is { } remaining ? Array.AsReadOnly(remaining.ToArray()) : null;
    }
    public Task SaveProgressAsync(AccountId account, IReadOnlyList<string> selection, IReadOnlyList<MailMessageKey>? remaining, CancellationToken token = default)
    {
        CheckAccount(account); string[] expected = selection.ToArray(); var snapshot = remaining?.ToArray();
        return _file.UpdateAsync(state =>
        {
            var row = state.Accounts.SingleOrDefault(row => row.AccountId == account);
            if (!(row?.MailboxIds ?? []).SequenceEqual(expected, StringComparer.Ordinal))
                throw new InvalidDataException("Offline-folder selection changed; progress was not advanced. Reload before downloading.");
            if (row is null) return state;
            return new(state.Accounts.Select(item => item.AccountId == account ? item with { Remaining = snapshot } : item).ToArray());
        }, token);
    }
    internal Task ResetProgressWithLockHeldAsync(CancellationToken token, AccountId? account = null) =>
        _file.UpdateWithLockHeldAsync(state => new(state.Accounts.Select(row => account is null || row.AccountId == account ? row with { Remaining = null } : row).ToArray()), token);
    internal async Task ValidateSnapshotAsync() => _ = await _file.ReadAsync(CancellationToken.None).ConfigureAwait(false);
    private static void CheckAccount(AccountId account) { if (account.Value == Guid.Empty) throw new ArgumentException("An offline-folder account is required."); }
    private static void Validate(OfflineFolderState state)
    {
        if (state.Accounts is null || state.Accounts.Count > 32) throw new ArgumentException("Use at most 32 offline-folder account selections.");
        var accounts = new HashSet<AccountId>();
        foreach (var row in state.Accounts)
        {
            if (row is null) throw new ArgumentException("Invalid offline-folder selection."); CheckAccount(row.AccountId);
            if (!accounts.Add(row.AccountId) || row.MailboxIds is null || row.MailboxIds.Count > 8) throw new ArgumentException("Choose at most eight distinct offline folders per account.");
            if (row.Remaining is { } remaining)
            {
                var folders = new HashSet<string>(StringComparer.Ordinal);
                if (remaining.Count > row.MailboxIds.Count || remaining.Any(key => key.AccountId != row.AccountId || key.UidValidity == 0 || key.Uid == 0 ||
                    !row.MailboxIds.Contains(key.MailboxId, StringComparer.Ordinal) || !folders.Add(key.MailboxId)))
                    throw new ArgumentException("Invalid offline-folder continuation checkpoint.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in row.MailboxIds)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || name.Any(char.IsControl) || !names.Add(name)) throw new ArgumentException("Invalid or duplicate offline-folder identity.");
                for (int index = 0; index < name.Length; index++)
                    if (char.IsSurrogate(name[index]) && (!char.IsHighSurrogate(name[index]) || ++index >= name.Length || !char.IsLowSurrogate(name[index])))
                        throw new ArgumentException("Offline-folder identities must contain valid Unicode.");
            }
        }
    }
}
