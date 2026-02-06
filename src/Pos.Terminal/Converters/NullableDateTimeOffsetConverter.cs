using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pos.Terminal.Converters;

public sealed class NullableDateTimeOffsetConverter : IValueConverter
{
    public static readonly NullableDateTimeOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            DateTime dateTime => new DateTimeOffset(dateTime),
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            DateTimeOffset dateTimeOffset => dateTimeOffset.DateTime,
            DateTime dateTime => dateTime,
            _ => null
        };
    }
}
