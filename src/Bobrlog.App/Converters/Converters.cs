using System.Globalization;
using Avalonia.Data.Converters;
using Bobrlog.App.Resources;
using Bobrlog.Core.Models;

namespace Bobrlog.App.Converters;

public static class Converters
{
    public static readonly IValueConverter CategoryName =
        new FuncValueConverter<EventCategory, string>(EventCategoryInfo.DisplayName);

    public static readonly IValueConverter SeverityName =
        new FuncValueConverter<Severity, string>(SeverityMapper.DisplayName);

    public static readonly IValueConverter ShutdownKindName =
        new FuncValueConverter<ShutdownKind, string>(BootSession.DisplayName);

    public static readonly IValueConverter Timestamp =
        new FuncValueConverter<DateTimeOffset, string>(t => Format(t, Strings.Fmt_DateTimeSeconds));

    public static readonly IValueConverter Duration =
        new FuncValueConverter<TimeSpan, string>(FormatDuration);

    public static string FormatDuration(TimeSpan d) =>
        d.TotalDays >= 1 ? string.Format(CultureInfo.CurrentCulture, Strings.Duration_DaysHours, (int)d.TotalDays, d.Hours)
        : d.TotalHours >= 1 ? string.Format(CultureInfo.CurrentCulture, Strings.Duration_HoursMinutes, (int)d.TotalHours, d.Minutes)
        : d.TotalMinutes >= 1 ? string.Format(CultureInfo.CurrentCulture, Strings.Duration_Minutes, (int)d.TotalMinutes)
        : string.Format(CultureInfo.CurrentCulture, Strings.Duration_Seconds, (int)d.TotalSeconds);

    /// <summary>Formats a timestamp with one of the per-language patterns (<c>Strings.Fmt_*</c>).</summary>
    public static string Format(DateTimeOffset t, string pattern) => t.ToString(pattern, CultureInfo.CurrentCulture);

    public static string Format(DateOnly d, string pattern) => d.ToString(pattern, CultureInfo.CurrentCulture);
}
