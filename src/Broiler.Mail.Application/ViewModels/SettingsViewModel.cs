using Broiler.Mail.Core.Settings;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

public sealed class SettingsViewModel : SaveViewModel
{
    private readonly ISettingsStore _store;

    public SettingsViewModel(ISettingsStore store, IUiDispatcher dispatcher, ApplicationSettings settings, string? loadError)
        : base(dispatcher, loadError)
    {
        _store = store;
        Settings = settings;
        Theme = settings.Theme;
        WindowWidth = settings.WindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WindowHeight = settings.WindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public ApplicationSettings Settings { get; private set; }
    public AppTheme Theme { get; set; }
    public string WindowWidth { get; set; }
    public string WindowHeight { get; set; }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ApplicationSettings? candidate = null;
        return SaveAsync(async () =>
        {
            if (!int.TryParse(WindowWidth, out int width) || !int.TryParse(WindowHeight, out int height))
                throw new ArgumentException("Window width and height must be whole numbers.");
            candidate = new ApplicationSettings { Theme = Theme, WindowWidth = width, WindowHeight = height };
            ConfigurationValidator.Validate(candidate);
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        }, () => Settings = candidate!, "Settings saved. Theme and window size apply the next time Broiler.Mail starts.");
    }
}
