using Microsoft.UI.Xaml.Data;

namespace SIV.App.Converters;

public sealed class DecimalFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal d)
            return $"${d:N2}";
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
