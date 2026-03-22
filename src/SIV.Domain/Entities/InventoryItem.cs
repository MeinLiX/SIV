using SIV.Domain.Enums;

namespace SIV.Domain.Entities;

public class InventoryItem
{
    public ulong Id { get; set; }
    public ulong OriginalId { get; set; }
    public uint InventoryPosition { get; set; }
    public uint DefIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseName { get; set; } = string.Empty;
    public string CustomName { get; set; } = string.Empty;
    public string CustomDescription { get; set; } = string.Empty;
    public string MarketHashName { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public ItemRarity Rarity { get; set; }
    public ItemExterior Exterior { get; set; }
    public int Quality { get; set; }
    public int PaintIndex { get; set; }
    public string SkinName { get; set; } = string.Empty;
    public float PaintWear { get; set; }
    public int PaintSeed { get; set; }
    public List<InventorySticker> Stickers { get; set; } = [];
    public InventoryKeychain? Keychain { get; set; }
    public bool IsCasket { get; set; }
    public ulong CasketId { get; set; }
    public int CasketItemCount { get; set; }
    public ulong ContainedInCasketId { get; set; }
    public bool Tradable { get; set; }
    public bool Marketable { get; set; }
    public bool CanFetchMarketPrice { get; set; }
    public bool IsTemporaryTradeLock { get; set; }
    public DateTimeOffset? TradeLockExpiresAt { get; set; }
    public bool IsGraffiti { get; set; }
    public int? GraffitiUsesRemaining { get; set; }
    public string RarityColor { get; set; } = string.Empty;
    public string QualityColor { get; set; } = string.Empty;
    public int Origin { get; set; } = -1;
    public int AcquiredOrder { get; set; }
}
