using System.Globalization;
using Avalonia.Data.Converters;

namespace Bobrlog.App.Converters;

public static class DateConverters
{
    /// <summary>CalendarDatePicker works with DateTime?, the view models with DateTimeOffset?.</summary>
    public static readonly IValueConverter OffsetToDateTime = new OffsetDateTimeConverter();

    private sealed class OffsetDateTimeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is DateTimeOffset d ? d.LocalDateTime.Date : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is DateTime d ? new DateTimeOffset(d.Date) : null;
    }
}
