using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace ProjectCurator.Desktop.Converters;

public class HeatmapIntensityToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double intensity)
        {
            // 0.0 = dark, 1.0 = bright green
            var g = (byte)(intensity * 200);
            return new SolidColorBrush(Color.FromRgb(0, g, 0));
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
