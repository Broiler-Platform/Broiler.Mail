// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        0/5
// Exempt:           8
// Human-reviewed:   0/5
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Keeps a running session's palette in step with the saved theme preference and the operating
/// system's appearance. Re-theming goes through <see cref="StandardThemeController"/>, which updates
/// existing controls in place, so text, caret, selection, and scroll positions are untouched.
/// </summary>
public sealed class AppearanceController : IDisposable
{
    private readonly UiSession _session;
    private readonly SettingsViewModel _settings;
    private readonly IUiSystemSettingsHost? _system;
    private readonly Func<UiSystemSettings, StandardThemeTokens?>? _highContrast;
    private bool _disposed;

    /// <param name="highContrast">The system's own contrast palette; see <see cref="AppearancePolicy.Resolve"/>.</param>
    public AppearanceController(UiSession session, SettingsViewModel settings, IUiSystemSettingsHost? system,
        Func<UiSystemSettings, StandardThemeTokens?>? highContrast = null)
    {
        _session = session;
        _settings = settings;
        _system = system;
        _highContrast = highContrast;
        settings.Changed += OnSettingsChanged;
        if (system is not null) system.SettingsChanged += OnSystemChanged;
        Apply();
    }

    /// <summary>The palette currently applied to the session.</summary>
    public StandardThemeTokens? Current { get; private set; }

    /// <summary>Raised after a different palette was applied, for host chrome such as the title bar.</summary>
    public event EventHandler? Applied;

    /// <summary>Only a saved preference counts; an unsaved selection in Settings changes nothing.</summary>
    private void OnSettingsChanged(object? sender, EventArgs e) => Apply();
    private void OnSystemChanged(object? sender, UiSystemSettingsChangedEventArgs e) => Apply();

    /// <summary>
    /// Resolves the palette again and applies it if it changed. Hosts also call this when the system's
    /// colors change while its settings do not, such as a switch between two contrast themes.
    /// </summary>
    public void Apply()
    {
        if (_disposed) return;
        var tokens = AppearancePolicy.Resolve(_settings.Settings.Theme, _system?.Settings ?? UiSystemSettings.Default, _highContrast);
        if (tokens == Current) return;
        // Combo boxes and their rows size themselves from the applied font (Broiler.UI preview.18); Mail sets
        // no size on them, so a text-scaled theme makes them taller without help here.
        // Buttons pick their focus ring against the fill they draw (Broiler.UI ADR 0032), so a default button
        // rings itself visibly on its accent even where the palette's ring is that accent.
        StandardThemeController.Apply(_session, tokens);
        Current = tokens;
        Applied?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        if (_system is not null) _system.SettingsChanged -= OnSystemChanged;
    }
}
