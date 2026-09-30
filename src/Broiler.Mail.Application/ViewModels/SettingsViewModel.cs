// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           5
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    Medium
// Criteria:         2/0
// Resource impact:  2/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Settings;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=757DD1
// Broiler-Falsified-If: a window width or height outside the validated range is written to the settings store
// Broiler-Human:        PENDING
public sealed class SettingsViewModel : SaveViewModel
{
    private readonly ISettingsStore _store;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=ACF155
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=9F7D12
    // Broiler-Falsified-If: a non-numeric window width, or one outside 640 to 7680, reaches the settings store
    // Broiler-Human:        PENDING
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
