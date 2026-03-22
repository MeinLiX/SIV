using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using SIV.Application.Interfaces;
using SIV.UI.Utilities;

namespace SIV.App.Converters;

public sealed class StringToImageSourceConverter : IValueConverter
{
    internal static IIconCacheService? IconCache { get; set; }

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = SteamIconUrl.Normalize(path, "256x256");
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return null;

        try
        {
            var cachedPath = IconCache?.GetCachedPath(normalized);
            if (cachedPath is not null)
                return new BitmapImage(new Uri(cachedPath));

            IconCache?.QueueForCaching(normalized);
            return new BitmapImage(uri);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
