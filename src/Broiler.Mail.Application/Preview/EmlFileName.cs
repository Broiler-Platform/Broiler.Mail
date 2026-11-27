// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   8
// Annotated:        0/8
// Exempt:           0
// Human-reviewed:   0/8
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;
using System.Text;

namespace Broiler.Mail.Application.Preview;

/// <summary>
/// The file name Save as .eml… proposes for a message: its subject and date, safe on Windows, macOS, Linux and
/// Android file systems. The person can still change it in the file dialog.
/// </summary>
public static class EmlFileName
{
    /// <summary>The longest subject part, in UTF-16 units, before the date and extension are added.</summary>
    public const int MaximumSubjectLength = 80;

    /// <summary>The name used when the subject has nothing left to name the file with.</summary>
    public const string Fallback = "message";

    private const string Invalid = "<>:\"/\\|?*";

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// "{subject} yyyy-MM-dd.eml": printable characters are kept; the characters Windows forbids and every control,
    /// formatting or unpaired surrogate character become '_'; runs of white space become one space; leading and
    /// trailing dots and spaces go; a reserved device name is prefixed with '_'; the subject is cut to
    /// <see cref="MaximumSubjectLength"/> units without splitting a surrogate pair; and an empty result is
    /// <see cref="Fallback"/>. Without a date the name is "{subject}.eml".
    /// </summary>
    public static string Suggest(string? subject, DateTimeOffset? date)
    {
        var text = new StringBuilder();
        bool space = false;
        string value = subject ?? "";
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                if (space && text.Length > 0) text.Append(' ');
                space = false;
                text.Append(c).Append(value[++i]);
                continue;
            }
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && text.Length > 0) text.Append(' ');
            space = false;
            text.Append(Invalid.Contains(c) || !IsPrintable(c) ? '_' : c);
        }
        string name = Trim(text.ToString());
        if (name.Length > MaximumSubjectLength)
        {
            int cut = MaximumSubjectLength;
            if (char.IsLowSurrogate(name[cut]) && char.IsHighSurrogate(name[cut - 1])) cut--;
            name = Trim(name[..cut]);
        }
        if (name.Length == 0) name = Fallback;
        string stem = name.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        if (date is { } when) name += " " + when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return name + ".eml";
    }

    private static string Trim(string name) => name.Trim('.', ' ');

    private static bool IsPrintable(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Control
        or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
}
