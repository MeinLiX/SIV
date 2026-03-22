using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using Windows.UI;

namespace SIV.App.Converters;

public sealed class HexStringToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return null;

        hex = hex.TrimStart('#');

        if (hex.Length == 6
            && byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out var b))
        {
            return new SolidColorBrush(Color.FromArgb(255, r, g, b));
        }

        if (hex.Length == 8
            && byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out var a2)
            && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out var r2)
            && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out var g2)
            && byte.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, null, out var b2))
        {
            return new SolidColorBrush(Color.FromArgb(a2, r2, g2, b2));
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
