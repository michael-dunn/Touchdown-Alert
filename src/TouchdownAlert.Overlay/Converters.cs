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

/// <summary>
/// Turns a team color hex string ("#22c55e") into a brush with the alpha given by ConverterParameter (0.0-1.0),
/// so tile tints and accents use the team color without being fully saturated.
/// </summary>
public sealed class ColorWithAlphaConverter : IValueConverter
{
    private static readonly System.Windows.Media.Color Fallback = System.Windows.Media.Color.FromRgb(0x22, 0xc5, 0x5e);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = Fallback;
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim());
            }
            catch (FormatException)
            {
                color = Fallback;
            }
        }

        var alpha = 1.0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            alpha = parsed;
        }
        else if (parameter is double d)
        {
            alpha = d;
        }

        alpha = Math.Clamp(alpha, 0.0, 1.0);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>One column for a single watched team, two columns otherwise (mirrors the dashboard's 2x2 tile grid).</summary>
public sealed class RowCountToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count <= 1 ? 1 : 2;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
