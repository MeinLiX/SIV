namespace SIV.Domain.Entities;

public sealed class InventorySticker
{
    public int Slot { get; set; }
    public int StickerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Wear { get; set; }
    public float Scale { get; set; }
    public float Rotation { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public int Schema { get; set; }
}
