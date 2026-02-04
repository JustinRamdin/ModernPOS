using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pos.Terminal.Converters;

public sealed class BooleanNegationConverter : IValueConverter
{
    public static readonly BooleanNegationConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
