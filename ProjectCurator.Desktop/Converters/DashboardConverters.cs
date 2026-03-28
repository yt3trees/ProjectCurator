using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ProjectCurator.Desktop.Converters;

/// <summary>
/// Converts bool to double opacity.
/// ConverterParameter: "falseValue|trueValue" (e.g. "0.45|1.0")
/// Default: false=0.45, true=1.0
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                double falseVal = double.TryParse(parts[0], System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double f) ? f : 1.0;
                double trueVal  = double.TryParse(parts[1], System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double t) ? t : 1.0;
                return flag ? trueVal : falseVal;
            }
        }
        return flag ? 1.0 : 0.45;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts bool to IBrush.
/// ConverterParameter: "falseColor|trueColor" in hex (e.g. "#3B4252|#A3BE8C")
/// Default: false=#3B4252 (dim gray), true=#A3BE8C (green)
/// </summary>
public class BoolToColorBrushConverter : IValueConverter
{
    public static readonly BoolToColorBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        string falseColor = "#3B4252";
        string trueColor = "#A3BE8C";

        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                falseColor = parts[0];
                trueColor = parts[1];
            }
        }

        var hex = flag ? trueColor : falseColor;
        return new SolidColorBrush(Color.Parse(hex));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if the string value equals the ConverterParameter string.
/// </summary>
public class StringEqualsToBoolConverter : IValueConverter
{
    public static readonly StringEqualsToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p && s == p;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns one of two strings based on a bool value.
/// ConverterParameter: "falseString|trueString" (e.g. "Show All|Show Less")
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public static readonly BoolToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
                return flag ? parts[1] : parts[0];
        }
        return flag.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
