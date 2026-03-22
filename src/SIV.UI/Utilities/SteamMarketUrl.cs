using SIV.Domain.Enums;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SIV.UI.Utilities;

public static class SteamMarketUrl
{
    private const string MarketListingsBase = "https://steamcommunity.com/market/listings";
    private const string SteamOpenUrlPrefix = "steam://openurl/";
    private const int VK_CONTROL = 0x11;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static string Build(uint appId, string marketHashName)
    {
        return $"{MarketListingsBase}/{appId}/{Uri.EscapeDataString(marketHashName)}";
    }

    public static void Open(uint appId, string marketHashName, OpenLinkIn setting)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
            return;

        var url = Build(appId, marketHashName);
        var ctrlPressed = IsCtrlPressed();
        var useSteam = setting == OpenLinkIn.SteamApp;

        if (ctrlPressed)
            useSteam = !useSteam;

        if (useSteam)
        {
            try
            {
                Process.Start(new ProcessStartInfo($"{SteamOpenUrlPrefix}{url}") { UseShellExecute = true });
                return;
            }
            catch
            {
                // Steam not installed or not registered — fall through to browser
            }
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static string GetTooltip(OpenLinkIn setting)
    {
        return setting == OpenLinkIn.SteamApp
            ? "Open in Steam (Ctrl+Click → Browser)"
            : "Open in Browser (Ctrl+Click → Steam)";
    }

    private static bool IsCtrlPressed()
    {
        return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    }
}
