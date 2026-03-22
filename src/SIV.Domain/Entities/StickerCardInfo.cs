namespace SIV.Domain.Entities;

public sealed class StickerCardInfo
{
    public string Name { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public float Wear { get; set; }
    public bool IsKeychain { get; set; }
}
