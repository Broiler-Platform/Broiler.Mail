// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   9
// Annotated:        0/9
// Exempt:           2
// Human-reviewed:   0/9
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Collections.Concurrent;
using System.Globalization;

namespace Broiler.Mail.Application.Views;

/// <summary>Shared row/header date formatting; the demo can pin clock, zone, and culture.</summary>
public sealed class MessageDateFormatter(TimeProvider? clock = null, CultureInfo? culture = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public static MessageDateFormatter Default { get; } = new();

    public string List(DateTimeOffset? timestamp) => ListForms(timestamp)[0];

    /// <summary>
    /// The row date from the longest form to the shortest, for rows too narrow for the first: today's
    /// time; this year's abbreviated month and day ("Sep 27"), then the numeric month and day ("9/27");
    /// an older year's short date ("9/27/2025"), then the year alone ("2025").
    /// </summary>
    public IReadOnlyList<string> ListForms(DateTimeOffset? timestamp)
    {
        if (timestamp is null) return [string.Empty];
        var local = TimeZoneInfo.ConvertTime(timestamp.Value, _clock.LocalTimeZone);
        var now = _clock.GetLocalNow();
        CultureInfo format = culture ?? CultureInfo.CurrentCulture;
        if (local.Date == now.Date) return [local.ToString("t", format)];
        string first, second;
        if (local.Year == now.Year)
        {
            var patterns = Patterns(format.DateTimeFormat);
            first = local.ToString(patterns.MonthDay, format);
            second = patterns.NumericMonthDay is { } numeric ? local.ToString(numeric, format) : "";
        }
        else
        {
            first = local.ToString("d", format);
            second = local.ToString("yyyy", format);
        }
        return second.Length == 0 || string.Equals(first, second, StringComparison.Ordinal) ? [first] : [first, second];
    }

    // Rows format every visible date on every frame, so a culture's patterns are derived once.
    private static readonly ConcurrentDictionary<(string ShortDate, string MonthDay), (string MonthDay, string? NumericMonthDay)> PatternCache = new();

    private static (string MonthDay, string? NumericMonthDay) Patterns(DateTimeFormatInfo format) =>
        PatternCache.GetOrAdd((format.ShortDatePattern, format.MonthDayPattern),
            static key => (ShortMonthDay(key.MonthDay), NumericMonthDay(key.ShortDate, key.MonthDay)));

    // The culture's month-day pattern with the abbreviated month ("Sep 27", "27. Sep"), so the date
    // takes less of a row's first line than "September 27".
    private static string ShortMonthDay(string monthDayPattern) =>
        monthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);

    /// <summary>
    /// The culture's short date without its year ("M/d/yyyy" gives "M/d", "yyyy/MM/dd" gives "MM/dd",
    /// "dd.MM.yyyy" gives "dd.MM." where the language writes "27. September" and "dd.MM" where it
    /// writes "27 September"), or null when the pattern is not simply numbers and separators, for
    /// example when it carries a quoted word, so a stray year label never remains.
    /// </summary>
    private static string? NumericMonthDay(string shortDatePattern, string monthDayPattern)
    {
        // Runs of one pattern letter, or of separator characters between them.
        var parts = new List<string>();
        foreach (char c in shortDatePattern)
        {
            bool letter = char.IsLetter(c);
            if ((letter && c is not ('d' or 'M' or 'y')) || c is '\'' or '"' or '\\' or '%') return null;
            if (parts.Count > 0 && (letter ? parts[^1][0] == c : !char.IsLetter(parts[^1][0]))) parts[^1] += c;
            else parts.Add(c.ToString());
        }
        int year = parts.FindIndex(part => part[0] == 'y');
        if (year < 0 || parts.Count(part => char.IsLetter(part[0])) != 3) return null;
        // Drop the year with the separator that joined it to the rest: the one after it when it
        // comes first, otherwise the one before it.
        int separator = year == 0 ? 1 : year - 1;
        if (separator < parts.Count && !char.IsLetter(parts[separator][0]))
        {
            // A period right after the day or month is an ordinal mark in languages that write
            // "27. September"; it stays when the year after it goes ("27.09.", "27. 9.").
            bool ordinal = year == parts.Count - 1 && parts[separator][0] == '.' && WritesOrdinalDay(monthDayPattern);
            parts.RemoveAt(Math.Max(year, separator));
            parts.RemoveAt(Math.Min(year, separator));
            if (ordinal) parts.Add(".");
        }
        else parts.RemoveAt(year);
        string pattern = string.Concat(parts).Trim();
        return pattern.Contains('M', StringComparison.Ordinal) && pattern.Contains('d', StringComparison.Ordinal) && !pattern.Contains("MMM", StringComparison.Ordinal)
            ? pattern : null;
    }

    // Whether the culture's month-day pattern follows the day with a period, as in "d. MMMM".
    private static bool WritesOrdinalDay(string monthDayPattern)
    {
        int end = monthDayPattern.IndexOf('d', StringComparison.Ordinal);
        if (end < 0) return false;
        while (end < monthDayPattern.Length && monthDayPattern[end] == 'd') end++;
        return end < monthDayPattern.Length && monthDayPattern[end] == '.';
    }

    public string Detail(DateTimeOffset? timestamp) => timestamp is null ? "Unknown"
        : TimeZoneInfo.ConvertTime(timestamp.Value, _clock.LocalTimeZone).ToString("g", culture ?? CultureInfo.CurrentCulture);
}
