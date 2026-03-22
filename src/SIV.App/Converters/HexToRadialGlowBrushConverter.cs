using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using Windows.UI;

namespace SIV.App.Converters;

public sealed class HexToRadialGlowBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return null;

        hex = hex.TrimStart('#');

        if (!TryParseHexRgb(hex, out var r, out var g, out var b))
            return null;

        return new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(90, r, g, b), Offset = 0.0 },
                new GradientStop { Color = Color.FromArgb(40, r, g, b), Offset = 0.6 },
                new GradientStop { Color = Color.FromArgb(0, r, g, b), Offset = 1.0 },
            }
        };
    }

    private static bool TryParseHexRgb(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;

        if (hex.Length == 6)
            return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out r)
                && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out g)
                && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out b);

        if (hex.Length == 8)
            return byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out r)
                && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out g)
                && byte.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, null, out b);

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
