using Android.Content.Res;
using Broiler.Hosting.Android;
using Broiler.UI;

namespace Broiler.Mail.Android;

internal sealed class MailAndroidUiHost(AndroidBroilerView view, Configuration configuration,
    Action<UiTextCaretInfo?> caretChanged)
    : AndroidUiHost(() => view.ViewportSize, () => view.Scale, _ => view.InvalidateFrame(),
        view.Present, view.GetClipboardText, view.SetClipboardText, caretChanged, view.Dispose), IUiSystemSettingsHost
{
    public UiSystemSettings Settings { get; private set; } = Read(configuration);
    public event EventHandler<UiSystemSettingsChangedEventArgs>? SettingsChanged;

    public void Refresh(Configuration configuration)
    {
        var settings = Read(configuration);
        if (settings == Settings) return;
        Settings = settings;
        SettingsChanged?.Invoke(this, new UiSystemSettingsChangedEventArgs(settings));
    }

    private static UiSystemSettings Read(Configuration configuration) => UiSystemSettings.Default with
    {
        TextScale = configuration.FontScale,
        ColorScheme = (configuration.UiMode & UiMode.NightMask) == UiMode.NightYes
            ? UiColorScheme.Dark : UiColorScheme.Light,
    };
}
