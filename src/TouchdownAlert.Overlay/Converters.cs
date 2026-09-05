using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TouchdownAlert.Overlay;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns an opacity (0.0-1.0) into a semi-transparent near-black brush for the overlay's card background.</summary>
public sealed class OpacityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var opacity = value is double d ? d : 0.7;
        opacity = Math.Clamp(opacity, 0.0, 1.0);
        var alpha = (byte)Math.Round(opacity * 255);
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 15, 15, 20));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
