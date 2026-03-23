using SIV.Domain.Entities;

namespace SIV.Application.Interfaces;

public interface IItemDefinitionProvider
{
    uint AppId { get; }
    string? GetMarketHashName(uint defIndex, int paintIndex, float paintWear, int stickerKitId = 0, int keychainId = 0, int graffitiTintId = 0);
    string? GetItemName(uint defIndex);
    string? GetItemIconPath(uint defIndex, int paintIndex, int stickerKitId = 0, int keychainId = 0, int graffitiTintId = 0);
    string? GetPaintKitName(int paintIndex);
    string? GetStickerKitName(int stickerKitId);
    string? GetKeychainName(int keychainId);
    string? GetGraffitiTintName(int tintId);
    string? GetRarityColor(int rarity);
    string? GetQualityColor(int quality);
    string? GetStickerIconUrl(int stickerKitId);
    string? GetKeychainIconUrl(int keychainId);
    IReadOnlyList<ItemOriginInfo> GetItemOrigins(int paintIndex, uint defIndex, int stickerKitId = 0);
    IReadOnlyList<ContainerDropInfo> GetContainerDrops(uint defIndex);
    void PopulateIconCache(IReadOnlyDictionary<string, string> iconsByMarketHashName);
    string? ResolveIconHash(string marketHashName);
}
