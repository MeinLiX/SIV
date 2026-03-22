namespace SIV.Domain.Entities;

public sealed class ItemOriginInfo
{
    public string Name { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public int DefIndex { get; set; }
    public ItemOriginType Type { get; set; }

    public string TypeDisplayName => Type switch
    {
        ItemOriginType.Case => "Case",
        ItemOriginType.Collection => "Collection",
        _ => string.Empty
    };
}

public enum ItemOriginType
{
    Case,
    Collection
}
