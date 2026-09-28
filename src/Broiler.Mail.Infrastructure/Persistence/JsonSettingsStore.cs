using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private readonly JsonConfigurationFile<ApplicationSettings> _file = new(path, () => new(), ConfigurationValidator.Validate);

    public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync(cancellationToken);

    public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(settings);
        return _file.UpdateAsync(_ => settings, cancellationToken);
    }
}
