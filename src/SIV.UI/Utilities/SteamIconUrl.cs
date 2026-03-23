using System.Text.RegularExpressions;

namespace SIV.UI.Utilities;

public static partial class SteamIconUrl
{
    private const string EconomyImageBase = "https://community.fastly.steamstatic.com/economy/image/";

    public static string Normalize(string? value, string size)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();

        if (trimmed.StartsWith("econ/", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            return NormalizeAbsolute(absoluteUri, size);

        return $"{EconomyImageBase}{trimmed}/{size}";
    }

    private static string NormalizeAbsolute(Uri uri, string size)
    {
        var absolute = uri.AbsoluteUri.TrimEnd('/');

        if (!absolute.Contains("/economy/image/", StringComparison.OrdinalIgnoreCase))
            return absolute;

        if (SteamImageSizeRegex().IsMatch(uri.AbsolutePath))
            return absolute;

        return $"{absolute}/{size}";
    }

    [GeneratedRegex(@"/\d+fx?\d*x\d+f?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex SteamImageSizeRegex();
}
