using Broiler.Mail.Core.Settings;
using Broiler.UI.Standard;
using Microsoft.Win32;

namespace Broiler.Mail.Windows.Services;

internal static class WindowsTheme
{
    public static StandardThemeTokens Resolve(AppTheme theme)
    {
        if (theme == AppTheme.System)
        {
            try
            {
                using var personalization = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                theme = personalization?.GetValue("AppsUseLightTheme") is int value && value == 0 ? AppTheme.Dark : AppTheme.Light;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                theme = AppTheme.Light;
            }
        }
        return theme == AppTheme.Dark ? StandardThemeTokens.Dark : StandardThemeTokens.Light;
    }
}
