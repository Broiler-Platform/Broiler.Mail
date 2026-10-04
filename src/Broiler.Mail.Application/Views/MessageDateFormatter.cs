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
        string[] forms = local.Year == now.Year
            ? [local.ToString(ShortMonthDay(format), format), NumericMonthDay(format) is { } numeric ? local.ToString(numeric, format) : ""]
            : [local.ToString("d", format), local.ToString("yyyy", format)];
        return forms.Where(form => form.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    // The culture's month-day pattern with the abbreviated month ("Sep 27", "27. Sep"), so the date
    // takes less of a row's first line than "September 27".
    private static string ShortMonthDay(CultureInfo culture) =>
        culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);

    /// <summary>
    /// The culture's short date without its year ("M/d/yyyy" gives "M/d", "dd.MM.yyyy" gives "dd.MM",
    /// "yyyy/MM/dd" gives "MM/dd"), or null when the pattern is not simply numbers and separators,
    /// for example when it carries a quoted word, so a stray year label never remains.
    /// </summary>
    private static string? NumericMonthDay(CultureInfo culture)
    {
        // Runs of one pattern letter, or of separator characters between them.
        var parts = new List<string>();
        foreach (char c in culture.DateTimeFormat.ShortDatePattern)
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
            parts.RemoveAt(Math.Max(year, separator));
            parts.RemoveAt(Math.Min(year, separator));
        }
        else parts.RemoveAt(year);
        string pattern = string.Concat(parts).Trim();
        return pattern.Contains('M', StringComparison.Ordinal) && pattern.Contains('d', StringComparison.Ordinal) && !pattern.Contains("MMM", StringComparison.Ordinal)
            ? pattern : null;
    }

    public string Detail(DateTimeOffset? timestamp) => timestamp is null ? "Unknown"
        : TimeZoneInfo.ConvertTime(timestamp.Value, _clock.LocalTimeZone).ToString("g", culture ?? CultureInfo.CurrentCulture);
}
