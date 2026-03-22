namespace SIV.Domain.Entities;

public sealed class InventoryKeychain
{
    public int KeychainId { get; set; }
    public string Name { get; set; } = string.Empty;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public int Seed { get; set; }
    public int HighlightId { get; set; }
    public int StickerId { get; set; }
    public string StickerName { get; set; } = string.Empty;
}
