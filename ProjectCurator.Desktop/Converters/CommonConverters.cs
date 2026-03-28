using Avalonia.Data.Converters;
using System.Globalization;

namespace ProjectCurator.Desktop.Converters;

/// <summary>bool を反転する (WPFのInverseBoolConverterと同等)</summary>
public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>bool -> "●" (dirty) or "" (clean)</summary>
public class DirtyConverter : IValueConverter
{
    public static readonly DirtyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "●" : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>"Show All" / "Show Active" ラベル切替</summary>
public class ShowAllLabelConverter : IValueConverter
{
    public static readonly ShowAllLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "Show Active" : "Show All";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
