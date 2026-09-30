// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  1/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Settings;
using Broiler.UI.Standard;
using Microsoft.Win32;

namespace Broiler.Mail.Windows.Services;

// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=AF1F7D
// Broiler-Falsified-If: a denied or failing registry read while resolving the System theme escapes to startup instead of selecting the light tokens
// Broiler-Human:        PENDING
internal static class WindowsTheme
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=5E6D61
    // Broiler-Falsified-If: an AppsUseLightTheme DWORD of 0 under AppTheme.System resolves to the light tokens instead of the dark ones
    // Broiler-Human:        PENDING
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
