using System.Diagnostics;

namespace SIV.UI.Utilities;

public static class SteamMarketUrl
{
    private const string MarketListingsBase = "https://steamcommunity.com/market/listings";

    public static string Build(uint appId, string marketHashName)
    {
        return $"{MarketListingsBase}/{appId}/{Uri.EscapeDataString(marketHashName)}";
    }

    public static void OpenInBrowser(uint appId, string marketHashName)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
            return;

        var url = Build(appId, marketHashName);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
