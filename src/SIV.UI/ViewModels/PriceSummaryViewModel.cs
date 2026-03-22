using CommunityToolkit.Mvvm.ComponentModel;

namespace SIV.UI.ViewModels;

public partial class PriceSummaryViewModel : ObservableObject
{
    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private int _pricedItems;

    [ObservableProperty]
    private decimal _totalValue;

    [ObservableProperty]
    private string _currency = "USD";

    [ObservableProperty]
    private int _storageItems;

    [ObservableProperty]
    private decimal _casketTotalValue;

    [ObservableProperty]
    private int _groupedCount;

    public string TotalValueFormatted => CasketTotalValue > 0
        ? $"${TotalValue:N2} (${TotalValue + CasketTotalValue:N2})"
        : $"${TotalValue:N2}";

    public string ItemCountDisplay
    {
        get
        {
            var parts = new List<string>();
            if (GroupedCount > 0)
                parts.Add($"grouped: {GroupedCount}");
            if (StorageItems > 0)
                parts.Add($"+{StorageItems} in storage");

            return parts.Count > 0
                ? $"{TotalItems} ({string.Join(", ", parts)})"
                : $"{TotalItems}";
        }
    }

    public void Update(int totalItems, int pricedItems, decimal totalValue, string currency, int storageItems = 0, decimal casketTotalValue = 0, int groupedCount = 0)
    {
        TotalItems = totalItems;
        PricedItems = pricedItems;
        TotalValue = totalValue;
        Currency = currency;
        StorageItems = storageItems;
        CasketTotalValue = casketTotalValue;
        GroupedCount = groupedCount;
        OnPropertyChanged(nameof(TotalValueFormatted));
        OnPropertyChanged(nameof(ItemCountDisplay));
    }
}
