using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Core.Services;

public interface ISettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
