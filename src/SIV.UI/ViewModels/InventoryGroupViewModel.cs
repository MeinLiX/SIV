using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class InventoryGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _marketHashName = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _iconUrl = string.Empty;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private decimal? _unitPriceUsd;

    [ObservableProperty]
    private decimal? _totalPriceUsd;

    [ObservableProperty]
    private bool _isCasketGroup;

    [ObservableProperty]
    private int _casketItemCount;

    [ObservableProperty]
    private decimal? _casketTotalPriceUsd;

    [ObservableProperty]
    private string _rarityColor = string.Empty;

    [ObservableProperty]
    private bool _isRefreshingPrice;

    [ObservableProperty]
    private bool _isMarketable;

    [ObservableProperty]
    private bool _canFetchPrice;

    [ObservableProperty]
    private bool _hasTemporaryTradeLock;

    [ObservableProperty]
    private string _tradeLockExpiresText = string.Empty;

    [ObservableProperty]
    private bool _isGraffiti;

    [ObservableProperty]
    private string _graffitiUsesText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<InventoryItemViewModel> _items = [];

    public string CountDisplay => IsCasketGroup && CasketItemCount > 0
        ? $"×{Count} ({CasketItemCount} items inside)"
        : $"×{Count}";

    public bool HasCasketTotalPrice => CasketTotalPriceUsd.HasValue;

    public string CasketTotalPriceDisplay => CasketTotalPriceUsd.HasValue
        ? $"${CasketTotalPriceUsd.Value:N2}"
        : string.Empty;

    partial void OnCasketTotalPriceUsdChanged(decimal? value)
    {
        OnPropertyChanged(nameof(HasCasketTotalPrice));
        OnPropertyChanged(nameof(CasketTotalPriceDisplay));
    }

    public InventoryGroupViewModel(IEnumerable<InventoryItemViewModel> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        var first = list[0];
        MarketHashName = first.MarketHashName;
        Name = first.GroupDisplayName;
        IconUrl = first.IconUrl;
        Count = list.Count;
        IsCasketGroup = first.IsCasket;
        IsMarketable = list.Any(i => i.IsMarketable);
        CanFetchPrice = list.Any(i => i.CanFetchPrice);
        HasTemporaryTradeLock = list.Any(i => i.IsTemporaryTradeLock);
        TradeLockExpiresText = list.Where(i => i.IsTemporaryTradeLock).Select(i => i.TradeLockExpiresText).FirstOrDefault() ?? string.Empty;
        IsGraffiti = first.IsGraffiti;
        var totalSprays = list.Where(i => i.IsGraffiti && !string.IsNullOrEmpty(i.GraffitiUsesText)).Sum(i => i.Source.GraffitiUsesRemaining ?? 0);
        GraffitiUsesText = IsGraffiti && totalSprays > 0 ? $"{totalSprays} sprays" : string.Empty;
        CasketItemCount = list.Where(i => i.IsCasket).Sum(i => i.CasketItemCount);
        RarityColor = first.RarityColor;

        Items = new ObservableCollection<InventoryItemViewModel>(list);
    }

    public void UpdatePrice(decimal? unitPrice)
    {
        UnitPriceUsd = unitPrice;
        TotalPriceUsd = unitPrice.HasValue ? unitPrice.Value * Count : null;
    }
}
