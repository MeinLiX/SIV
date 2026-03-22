using CommunityToolkit.Mvvm.ComponentModel;
using SIV.Domain.Entities;
using SIV.Domain.Enums;
using SIV.UI.Utilities;
using System.Globalization;

namespace SIV.UI.ViewModels;

public partial class InventoryItemViewModel : ObservableObject
{
    public InventoryItem Source { get; }

    [ObservableProperty]
    private ulong _id;

    [ObservableProperty]
    private uint _inventoryPosition;

    [ObservableProperty]
    private int _acquiredOrder;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _baseName = string.Empty;

    [ObservableProperty]
    private string _customName = string.Empty;

    [ObservableProperty]
    private string _customDescription = string.Empty;

    [ObservableProperty]
    private string _marketHashName = string.Empty;

    [ObservableProperty]
    private string _iconUrl = string.Empty;

    [ObservableProperty]
    private string _largeIconUrl = string.Empty;

    [ObservableProperty]
    private ItemRarity _rarity;

    [ObservableProperty]
    private ItemExterior _exterior;

    [ObservableProperty]
    private int _quality;

    [ObservableProperty]
    private int _paintIndex;

    [ObservableProperty]
    private string _skinName = string.Empty;

    [ObservableProperty]
    private float _paintWear;

    [ObservableProperty]
    private int _paintSeed;

    [ObservableProperty]
    private bool _isCasket;

    [ObservableProperty]
    private int _casketItemCount;

    [ObservableProperty]
    private bool _isTradable;

    [ObservableProperty]
    private bool _isMarketable;

    [ObservableProperty]
    private bool _canFetchPrice;

    [ObservableProperty]
    private bool _isTemporaryTradeLock;

    [ObservableProperty]
    private string _tradeLockExpiresText = string.Empty;

    [ObservableProperty]
    private bool _isGraffiti;

    [ObservableProperty]
    private string _graffitiUsesText = string.Empty;

    [ObservableProperty]
    private decimal? _priceUsd;

    [ObservableProperty]
    private DateTime? _priceUpdatedAt;

    public string PriceUpdatedAtText => PriceUpdatedAt.HasValue
        ? $"Updated: {PriceUpdatedAt.Value.ToLocalTime():g}"
        : string.Empty;

    partial void OnPriceUpdatedAtChanged(DateTime? value) => OnPropertyChanged(nameof(PriceUpdatedAtText));

    [ObservableProperty]
    private bool _isRefreshingPrice;

    [ObservableProperty]
    private decimal? _casketTotalPriceUsd;

    [ObservableProperty]
    private string _rarityColor = string.Empty;

    [ObservableProperty]
    private string _qualityColor = string.Empty;

    [ObservableProperty]
    private string _exteriorColor = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ItemOriginInfo> _origins = [];

    public bool HasOrigins => Origins.Count > 0;

    partial void OnOriginsChanged(IReadOnlyList<ItemOriginInfo> value) => OnPropertyChanged(nameof(HasOrigins));

    [ObservableProperty]
    private int _origin = -1;

    public string OriginDisplayName => Origin switch
    {
        0 => "Timed Drop",
        1 => "Achievement",
        2 => "Purchased",
        3 => "Traded",
        4 => "Crafted",
        5 => "Store Promotion",
        6 => "Gifted",
        7 => "Support",
        8 => "Unboxed",
        _ => string.Empty
    };

    public bool HasOriginBadge => Origin >= 0 && !string.IsNullOrEmpty(OriginDisplayName);

    public string OriginBadgeColor => Origin switch
    {
        0 => "#607D8B",
        1 => "#9C27B0",
        2 => "#2196F3",
        3 => "#FF9800",
        4 => "#4CAF50",
        5 => "#E91E63",
        6 => "#F44336",
        7 => "#795548",
        8 => "#FFD700",
        _ => "#9E9E9E"
    };

    public InventoryItemViewModel(InventoryItem item)
    {
        Source = item;
        Id = item.Id;
        InventoryPosition = item.InventoryPosition;
        AcquiredOrder = item.AcquiredOrder;
        Name = item.Name;
        BaseName = item.BaseName;
        CustomName = item.CustomName;
        CustomDescription = item.CustomDescription;
        MarketHashName = item.MarketHashName;
        if (string.IsNullOrEmpty(item.IconUrl))
        {
            IconUrl = string.Empty;
            LargeIconUrl = string.Empty;
        }
        else
        {
            IconUrl = SteamIconUrl.Normalize(item.IconUrl, "256x256");
            LargeIconUrl = SteamIconUrl.Normalize(item.IconUrl, "330x330");
        }
        Rarity = item.Rarity;
        Exterior = item.Exterior;
        Quality = item.Quality;
        PaintIndex = item.PaintIndex;
        SkinName = item.SkinName;
        PaintWear = item.PaintWear;
        PaintSeed = item.PaintSeed;
        IsCasket = item.IsCasket;
        CasketItemCount = item.CasketItemCount;
        IsTradable = item.Tradable;
        IsMarketable = item.Marketable;
        CanFetchPrice = item.CanFetchMarketPrice;
        IsTemporaryTradeLock = item.IsTemporaryTradeLock;
        TradeLockExpiresText = item.TradeLockExpiresAt?.LocalDateTime.ToString("g") ?? string.Empty;
        TradeLockRemainingText = FormatTradeLockRemaining(item.TradeLockExpiresAt);
        IsGraffiti = item.IsGraffiti;
        GraffitiUsesText = item.GraffitiUsesRemaining.HasValue ? $"{item.GraffitiUsesRemaining.Value} sprays" : string.Empty;
        RarityColor = item.RarityColor;
        QualityColor = item.QualityColor;
        ExteriorColor = GetExteriorColor(item.Exterior);
        Origin = item.Origin;
    }

    public bool HasSkin => PaintIndex > 0;

    public bool HasFloat => PaintWear > 0;

    public bool HasPattern => PaintSeed > 0;

    public bool HasCustomName => !string.IsNullOrWhiteSpace(CustomName);
    public bool HasCustomDescription => !string.IsNullOrWhiteSpace(CustomDescription);

    public string GroupDisplayName => string.IsNullOrWhiteSpace(SkinName)
        ? BaseName
        : $"{BaseName} | {SkinName}";

    public long NewestSortKey => AcquiredOrder > 0
        ? AcquiredOrder
        : (long)Math.Min(Id, (ulong)long.MaxValue);

    public string CasketItemCountDisplay => IsCasket && CasketItemCount > 0
        ? $"{CasketItemCount} items"
        : string.Empty;

    public bool HasCasketItems => IsCasket && CasketItemCount > 0;

    public bool HasCasketTotalPrice => CasketTotalPriceUsd.HasValue;

    public string CasketTotalPriceDisplay => CasketTotalPriceUsd.HasValue
        ? $"${CasketTotalPriceUsd.Value:N2}"
        : string.Empty;

    partial void OnCasketTotalPriceUsdChanged(decimal? value)
    {
        OnPropertyChanged(nameof(HasCasketTotalPrice));
        OnPropertyChanged(nameof(CasketTotalPriceDisplay));
    }

    public string SkinDisplayName => string.IsNullOrWhiteSpace(SkinName)
        ? string.Empty
        : SkinName;

    public string FloatDisplayName => PaintWear > 0
        ? PaintWear.ToString("0.##########", CultureInfo.InvariantCulture)
        : string.Empty;

    public string PatternDisplayName => PaintSeed > 0
        ? PaintSeed.ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    public string TechnicalSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (HasSkin)
                parts.Add($"Skin: {SkinDisplayName}");
            if (HasFloat)
                parts.Add($"Float: {FloatDisplayName}");
            if (PaintSeed > 0)
                parts.Add($"Pattern: {PatternDisplayName}");
            return parts.Count > 0 ? string.Join("   ", parts) : string.Empty;
        }
    }

    public bool HasStickers => Source.Stickers.Count > 0;

    public string StickersSummary => Source.Stickers.Count == 0
        ? string.Empty
        : "Stickers: " + string.Join(", ", Source.Stickers.Select(FormatSticker));

    public bool HasKeychain => Source.Keychain is not null;

    public string KeychainSummary => Source.Keychain is null
        ? string.Empty
        : "Keychain: " + FormatKeychain(Source.Keychain);

    [ObservableProperty]
    private IReadOnlyList<StickerCardInfo> _stickerCards = [];

    public bool HasStickerCards => StickerCards.Count > 0;

    partial void OnStickerCardsChanged(IReadOnlyList<StickerCardInfo> value) => OnPropertyChanged(nameof(HasStickerCards));

    [ObservableProperty]
    private IReadOnlyList<ContainerDropInfo> _containerDrops = [];

    public bool HasContainerDrops => ContainerDrops.Count > 0;

    partial void OnContainerDropsChanged(IReadOnlyList<ContainerDropInfo> value) => OnPropertyChanged(nameof(HasContainerDrops));

    public string RarityDisplayName => Rarity switch
    {
        ItemRarity.Consumer => "Consumer Grade",
        ItemRarity.Industrial => "Industrial Grade",
        ItemRarity.MilSpec => "Mil-Spec",
        ItemRarity.Restricted => "Restricted",
        ItemRarity.Classified => "Classified",
        ItemRarity.Covert => "Covert",
        ItemRarity.Contraband => "Contraband",
        ItemRarity.Special => "★ Rare Special",
        _ => "Unknown"
    };

    public string ExteriorDisplayName => Exterior switch
    {
        ItemExterior.FactoryNew => "Factory New",
        ItemExterior.MinimalWear => "Minimal Wear",
        ItemExterior.FieldTested => "Field-Tested",
        ItemExterior.WellWorn => "Well-Worn",
        ItemExterior.BattleScarred => "Battle-Scarred",
        _ => ""
    };

    public string QualityDisplayName => Quality switch
    {
        1 => "Genuine",
        2 => "Vintage",
        3 => "★ Unusual",
        5 => "Community",
        6 => "Developer",
        7 => "Self-Made",
        9 => "StatTrak™",
        12 => "Souvenir",
        _ => ""
    };

    public bool HasSpecialQuality => Quality is 1 or 2 or 3 or 5 or 6 or 7 or 9 or 12;

    public bool HasExterior => Exterior != ItemExterior.None;

    public string TradeLockRemainingText { get; }

    public bool HasTradeLockRemaining => IsTemporaryTradeLock && !string.IsNullOrEmpty(TradeLockRemainingText);

    public string TradableStatusText => IsTradable ? "Tradable" : "Not Tradable";

    public string MarketableStatusText => IsMarketable ? "Marketable" : "Not Marketable";

    public string PriceDisplayText => PriceUsd.HasValue
        ? $"${PriceUsd.Value:N2}"
        : string.Empty;

    public bool HasPrice => PriceUsd.HasValue;

    private static string FormatTradeLockRemaining(DateTimeOffset? expiresAt)
    {
        if (expiresAt is null) return string.Empty;
        var remaining = expiresAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return string.Empty;
        if (remaining.TotalDays >= 1)
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{remaining.Minutes}m";
    }

    private static string GetExteriorColor(ItemExterior exterior) => exterior switch
    {
        ItemExterior.FactoryNew => "#4CAF50",
        ItemExterior.MinimalWear => "#8BC34A",
        ItemExterior.FieldTested => "#FFC107",
        ItemExterior.WellWorn => "#FF9800",
        ItemExterior.BattleScarred => "#F44336",
        _ => string.Empty
    };

    private static string FormatSticker(InventorySticker sticker)
    {
        var name = sticker.Name;
        if (sticker.Wear > 0)
            name += $" ({sticker.Wear:P0})";
        return name;
    }

    private static string FormatKeychain(InventoryKeychain keychain)
    {
        return keychain.Name;
    }
}
