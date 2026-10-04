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
    private readonly SemaphoreSlim _layoutWrites = new(1, 1);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=ACF155
    // Broiler-Human:        PENDING
    public SettingsViewModel(ISettingsStore store, IUiDispatcher dispatcher, ApplicationSettings settings, string? loadError)
        : base(dispatcher, loadError)
    {
        _store = store;
        Settings = settings;
        Theme = settings.Theme;
        InboxDensity = settings.InboxDensity;
        WindowWidth = settings.WindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WindowHeight = settings.WindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public ApplicationSettings Settings { get; private set; }

    /// <summary>The last background layout save failure, if any. It is never shown while the user works.</summary>
    public string? LayoutSaveError { get; private set; }

    /// <summary>
    /// Remembers window geometry and the inbox split without touching Status, validation, or the busy
    /// state, so resizing never interrupts typing. Writes are serialized and always store the newest
    /// settings. A later explicit save keeps the remembered layout because it starts from Settings.
    /// </summary>
    public Task RememberLayoutAsync(WindowPlacement? window, double inboxSplitterFraction)
    {
        if (HasLoadError || !double.IsFinite(inboxSplitterFraction)) return Task.CompletedTask;
        var candidate = Settings with { Window = window, InboxSplitterFraction = Math.Clamp(inboxSplitterFraction, 0.05, 0.95) };
        if (candidate == Settings) return Task.CompletedTask;
        try { ConfigurationValidator.Validate(candidate); }
        catch (ConfigurationValidationException error) { LayoutSaveError = error.Message; return Task.CompletedTask; }
        Settings = candidate;
        return WriteLayoutAsync();
    }

    private async Task WriteLayoutAsync()
    {
        await _layoutWrites.WaitAsync().ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(Settings).ConfigureAwait(false);
            LayoutSaveError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ConfigurationValidationException)
        {
            LayoutSaveError = error.Message;
        }
        finally { _layoutWrites.Release(); }
    }
    public AppTheme Theme { get; set; }
    public InboxDensity InboxDensity { get; set; }
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
            if (!int.TryParse(WindowWidth, out int width))
                throw new ConfigurationValidationException("WindowWidth", "Window width must be a whole number.");
            if (!int.TryParse(WindowHeight, out int height))
                throw new ConfigurationValidationException("WindowHeight", "Window height must be a whole number.");
            candidate = Settings with { Theme = Theme, InboxDensity = InboxDensity, WindowWidth = width, WindowHeight = height };
            // A newly entered size replaces the remembered geometry, so the next start uses it.
            if (width != Settings.WindowWidth || height != Settings.WindowHeight) candidate = candidate with { Window = null };
            ConfigurationValidator.Validate(candidate);
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        }, () => Settings = candidate!, "Settings saved. Appearance and inbox spacing are applied; a new window size is used the next time Broiler.Mail starts.");
    }
}
