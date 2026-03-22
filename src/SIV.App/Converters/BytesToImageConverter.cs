using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SIV.App.Converters;

public sealed class BytesToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
            return null;

        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        bitmap.SetSource(stream.AsRandomAccessStream());
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
