using System.Globalization;

namespace Broiler.Mail.Application.Views;

/// <summary>Shared row/header date formatting; the demo can pin clock, zone, and culture.</summary>
public sealed class MessageDateFormatter(TimeProvider? clock = null, CultureInfo? culture = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public static MessageDateFormatter Default { get; } = new();

    public string List(DateTimeOffset? timestamp)
    {
        if (timestamp is null) return string.Empty;
        var local = TimeZoneInfo.ConvertTime(timestamp.Value, _clock.LocalTimeZone);
        var now = _clock.GetLocalNow();
        CultureInfo format = culture ?? CultureInfo.CurrentCulture;
        return local.ToString(local.Date == now.Date ? "t" : local.Year == now.Year ? ShortMonthDay(format) : "d", format);
    }

    // The culture's month-day pattern with the abbreviated month ("Sep 27", "27. Sep"), so the date
    // takes less of a row's first line than "September 27".
    private static string ShortMonthDay(CultureInfo culture) =>
        culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);

    public string Detail(DateTimeOffset? timestamp) => timestamp is null ? "Unknown"
        : TimeZoneInfo.ConvertTime(timestamp.Value, _clock.LocalTimeZone).ToString("g", culture ?? CultureInfo.CurrentCulture);
}
