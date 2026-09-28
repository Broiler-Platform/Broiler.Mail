using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Version 1 single-account persistence; profiles never contain credentials.</summary>
public sealed class JsonAccountStore(string path) : IAccountStore
{
    private readonly JsonConfigurationFile<AccountProfile[]> _file = new(path, () => [], ValidateAccounts);

    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
        await _file.ReadAsync(cancellationToken).ConfigureAwait(false);

    public Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(profile);
        return _file.UpdateAsync(existing =>
        {
            if (existing.Length > 0 && existing[0].Id != profile.Id)
                throw new InvalidOperationException("Version 1 supports one account. Reload the saved account before editing it.");
            return [profile];
        }, cancellationToken);
    }

    public Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
        _file.UpdateAsync(existing => existing.Where(profile => profile.Id != accountId).ToArray(), cancellationToken);

    private static void ValidateAccounts(AccountProfile[] profiles)
    {
        if (profiles.Length > 1)
            throw new InvalidDataException("This version supports only one saved account. The file has not been changed.");
        foreach (var profile in profiles)
            ConfigurationValidator.Validate(profile);
    }
}
