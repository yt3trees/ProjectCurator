using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace Curia.Helpers;

/// <summary>bool を反転するコンバーター。IsEnabled="{Binding IsRunning, Converter={StaticResource InverseBoolConverter}}" で使用。</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>bool (Dirty状態) を "●" または空文字に変換する。</summary>
[ValueConversion(typeof(bool), typeof(string))]
public class DirtyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "●" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>bool (ShowAllTasks) を "Show All" または "Show Top 10" に変換する。</summary>
[ValueConversion(typeof(bool), typeof(string))]
public class ShowAllLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "Show Top 10" : "Show All";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>bool の逆値を Visibility に変換する (false → Visible, true → Collapsed)。</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>bool (IsDirectory) を SymbolRegular アイコンに変換する。</summary>
public class DirIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isDir = value is bool b && b;
        return isDir ? Wpf.Ui.Controls.SymbolRegular.Folder24 : Wpf.Ui.Controls.SymbolRegular.Document24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>0..1 の強度を Heatmap セルの背景色に変換する。</summary>
[ValueConversion(typeof(double), typeof(MediaBrush))]
public class HeatmapIntensityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var intensity = value is double v ? Math.Clamp(v, 0d, 1d) : 0d;

        var surface = Application.Current.TryFindResource("AppSurface1") as SolidColorBrush;
        if (intensity <= 0.001 || surface == null)
            return surface?.CloneCurrentValue() ?? new SolidColorBrush(MediaColor.FromRgb(48, 54, 61));

        var accent = Application.Current.TryFindResource("AppBlue") as SolidColorBrush;
        var accentColor = accent?.Color ?? MediaColor.FromRgb(88, 166, 255);

        // Keep low intensity visible while preserving contrast for dark themes.
        var alpha = (byte)(45 + (int)(intensity * 185));
        return new SolidColorBrush(MediaColor.FromArgb(alpha, accentColor.R, accentColor.G, accentColor.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>int が 0 より大きい場合に Visible、それ以外の場合に Collapsed を返す。</summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
