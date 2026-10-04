using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.ComboBox.Standard;
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
    private bool _disposed;

    public AppearanceController(UiSession session, SettingsViewModel settings, IUiSystemSettingsHost? system)
    {
        _session = session;
        _settings = settings;
        _system = system;
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

    public void Apply()
    {
        if (_disposed) return;
        var tokens = AppearancePolicy.Resolve(_settings.Settings.Theme, _system?.Settings ?? UiSystemSettings.Default);
        if (tokens == Current) return;
        StandardThemeController.Apply(_session, tokens);
        // UI preview.17 scales the combo font but retains fixed control/popup heights.
        // Size Mail's choices from the applied font until the shared control does so itself.
        foreach (var root in _session.Roots)
            foreach (var choice in StandardTreeTraversal.PreOrder(root).OfType<StandardComboBox>())
            {
                double lineHeight = BTextMeasurer.GetLineHeight(choice.Font);
                choice.PreferredSize = new BSize(choice.PreferredSize.Width, Math.Max(32, lineHeight + 12));
                choice.ItemHeight = Math.Max(28, lineHeight + 8);
            }
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
