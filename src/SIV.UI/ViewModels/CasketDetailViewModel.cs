using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.UI.Services;
using SIV.UI.Utilities;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class CasketDetailViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IPricingService? _pricingService;
    private readonly IItemDefinitionProvider? _itemDefs;
    private readonly ISettingsService? _settingsService;
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<InventoryItem>>>? _reloadContentsAsync;
    private readonly uint _appId;
    private List<InventoryItemViewModel> _allItems = [];
    private List<InventoryGroupViewModel> _allGroups = [];

    [ObservableProperty]
    private string _casketName = string.Empty;

    [ObservableProperty]
    private string _casketDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<InventoryItemViewModel> _items = [];

    [ObservableProperty]
    private ObservableCollection<InventoryGroupViewModel> _groups = [];

    [ObservableProperty]
    private bool _isGrouped = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SortState _sort = new();

    [ObservableProperty]
    private string _selectedTypeFilter = "All";

    [ObservableProperty]
    private int _totalItemCount;

    [ObservableProperty]
    private PriceSummaryViewModel _priceSummary = new();

    public Action<decimal>? OnTotalPriceChanged { get; set; }

    [ObservableProperty]
    private bool _isPricingRunning;

    [ObservableProperty]
    private int _pricingProgress;

    [ObservableProperty]
    private int _pricingTotal;

    [ObservableProperty]
    private bool _isRefreshingStorage;

    [ObservableProperty]
    private ObservableCollection<string> _availableTypes = ["All"];

    public bool ShowLoadingOverlay => IsBusy;
    public bool HasCasketDescription => !string.IsNullOrWhiteSpace(CasketDescription);

    public override string Title => $"Casket: {CasketName}";

    public CasketDetailViewModel(
        string casketName,
        string? casketDescription,
        IReadOnlyList<InventoryItem>? contents,
        INavigationService navigation,
        IPricingService? pricingService = null,
        Func<bool, CancellationToken, Task<IReadOnlyList<InventoryItem>>>? reloadContentsAsync = null,
        IItemDefinitionProvider? itemDefs = null,
        ISettingsService? settingsService = null,
        uint appId = 0)
    {
        _navigation = navigation;
        _pricingService = pricingService;
        _itemDefs = itemDefs;
        _settingsService = settingsService;
        _reloadContentsAsync = reloadContentsAsync;
        _appId = appId;
        CasketName = casketName;
        CasketDescription = casketDescription ?? string.Empty;

        Sort.PropertyChanged += (_, _) => ApplyFilter();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                OnPropertyChanged(nameof(ShowLoadingOverlay));
            else if (e.PropertyName == nameof(CasketDescription))
                OnPropertyChanged(nameof(HasCasketDescription));
        };
        SetContents(contents ?? []);

        if (_pricingService is not null && _allItems.Count > 0)
            _ = LoadCachedPricesAsync().ContinueWith(t =>
            {
                if (t.Exception?.InnerException is { } ex)
                    ErrorMessage = ex.Message;
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task LoadCachedPricesAsync()
    {
        var names = _allItems
            .Where(i => i.CanFetchPrice && !string.IsNullOrEmpty(i.MarketHashName))
            .Select(i => i.MarketHashName)
            .Distinct()
            .ToList();

        if (names.Count == 0) return;

        var cached = await _pricingService!.LoadCachedPricesAsync(names);
        foreach (var result in cached)
        {
            if (result.PriceUSD is { } price)
                ApplyPriceToAll(result.MarketHashName, price, result.UpdatedAt);
        }
    }

    [RelayCommand]
    private void OpenMarketListing(string? marketHashName)
    {
        SteamMarketUrl.Open(_appId, marketHashName ?? string.Empty, _settingsService?.OpenLinkIn ?? Domain.Enums.OpenLinkIn.Browser);
    }

    public string MarketLinkTooltip => SteamMarketUrl.GetTooltip(_settingsService?.OpenLinkIn ?? Domain.Enums.OpenLinkIn.Browser);

    [RelayCommand]
    private void ToggleSort(string fieldName)
    {
        if (Enum.TryParse<SortField>(fieldName, out var field))
            Sort.Toggle(field);
    }

    [RelayCommand]
    private void ToggleSortDirection() => Sort.ToggleDirection();

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnIsGroupedChanged(bool value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<InventoryItemViewModel> filteredItems = _allItems;
        IEnumerable<InventoryGroupViewModel> filteredGroups = _allGroups;

        if (SelectedTypeFilter != "All" && !string.IsNullOrEmpty(SelectedTypeFilter))
        {
            filteredItems = filteredItems.Where(i =>
                i.BaseName.Equals(SelectedTypeFilter, StringComparison.OrdinalIgnoreCase));
            filteredGroups = filteredGroups.Where(g =>
                g.Items.Any(i => i.BaseName.Equals(SelectedTypeFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            filteredItems = filteredItems.Where(i =>
                i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.MarketHashName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.BaseName.Contains(term, StringComparison.OrdinalIgnoreCase));
            filteredGroups = filteredGroups.Where(g =>
                g.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || g.MarketHashName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        filteredItems = SortHelper.SortItems(filteredItems, Sort);
        filteredGroups = SortHelper.SortGroups(filteredGroups, Sort);

        Items = new ObservableCollection<InventoryItemViewModel>(filteredItems);
        Groups = new ObservableCollection<InventoryGroupViewModel>(filteredGroups);

        UpdateSummary();
    }

    [RelayCommand]
    private async Task FetchPricesAsync(CancellationToken ct)
    {
        if (_pricingService is null || IsPricingRunning) return;

        IsPricingRunning = true;
        ErrorMessage = null;

        try
        {
            var marketNames = _allItems
                .Where(i => i.CanFetchPrice && !string.IsNullOrEmpty(i.MarketHashName))
                .Select(i => i.MarketHashName)
                .Distinct()
                .ToList();

            PricingTotal = marketNames.Count;
            PricingProgress = 0;

            foreach (var name in marketNames)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _pricingService.FetchSinglePriceAsync(name, false, ct);
                PricingProgress++;

                if (result?.PriceUSD is { } price)
                    ApplyPriceToAll(name, price, result.UpdatedAt);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Price fetch cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsPricingRunning = false;
        }
    }

    [RelayCommand]
    private async Task RefreshGroupPriceAsync(InventoryGroupViewModel group, CancellationToken ct)
    {
        if (_pricingService is null || string.IsNullOrEmpty(group.MarketHashName) || !group.CanFetchPrice)
            return;

        group.IsRefreshingPrice = true;
        try
        {
            var result = await _pricingService.FetchSinglePriceAsync(group.MarketHashName, forceRefresh: true, ct);
            if (result?.PriceUSD is { } price)
                ApplyPriceToAll(group.MarketHashName, price, result.UpdatedAt);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            group.IsRefreshingPrice = false;
        }
    }

    [RelayCommand]
    private async Task RefreshItemPriceAsync(InventoryItemViewModel item, CancellationToken ct)
    {
        if (_pricingService is null || string.IsNullOrEmpty(item.MarketHashName) || !item.CanFetchPrice)
            return;

        item.IsRefreshingPrice = true;
        try
        {
            var result = await _pricingService.FetchSinglePriceAsync(item.MarketHashName, forceRefresh: true, ct);
            if (result?.PriceUSD is { } price)
                ApplyPriceToAll(item.MarketHashName, price, result.UpdatedAt);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            item.IsRefreshingPrice = false;
        }
    }

    [RelayCommand]
    private async Task RefreshStorageAsync(CancellationToken ct)
    {
        if (_reloadContentsAsync is null || IsRefreshingStorage)
            return;

        IsRefreshingStorage = true;

        try
        {
            await LoadContentsAsync(true, ct);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Storage refresh cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRefreshingStorage = false;
        }
    }

    public async Task LoadContentsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (_reloadContentsAsync is null)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var contents = await _reloadContentsAsync(forceRefresh, ct);
            SetContents(contents);

            if (_pricingService is not null)
                await LoadCachedPricesAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = forceRefresh ? "Storage refresh cancelled" : "Storage loading cancelled";
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetContents(IReadOnlyList<InventoryItem> contents)
    {
        TotalItemCount = contents.Count;

        var iconByDefIndex = InventoryViewModel.BuildIconMap(contents);

        InventoryItemViewModel CreateVm(InventoryItem item)
            => InventoryViewModel.CreateViewModel(item, _itemDefs, iconByDefIndex);

        _allItems = contents
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id)
            .Select(CreateVm)
            .ToList();

        _allGroups = contents
            .GroupBy(i => i.GroupKey)
            .Select(g => new InventoryGroupViewModel(g.Select(CreateVm)))
            .OrderByDescending(g => g.Count)
            .ToList();

        var types = _allItems
            .Select(i => i.BaseName)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AvailableTypes = new ObservableCollection<string>(["All", .. types]);
        if (!AvailableTypes.Contains(SelectedTypeFilter))
            SelectedTypeFilter = "All";

        ApplyFilter();
    }

    private void ApplyPriceToAll(string marketHashName, decimal price, DateTime? updatedAt = null)
    {
        foreach (var i in _allItems.Where(i => i.MarketHashName == marketHashName))
        {
            i.PriceUsd = price;
            i.PriceUpdatedAt = updatedAt;
        }
        foreach (var g in _allGroups.Where(g => g.MarketHashName == marketHashName))
        {
            g.UpdatePrice(price);
            foreach (var gi in g.Items)
            {
                gi.PriceUsd = price;
                gi.PriceUpdatedAt = updatedAt;
            }
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int totalItems, pricedItems;
        decimal totalValue;
        int groupedCount;

        if (IsGrouped)
        {
            totalItems = Groups.Sum(g => g.Count);
            pricedItems = Groups.Where(g => g.UnitPriceUsd.HasValue).Sum(g => g.Count);
            totalValue = Groups.Where(g => g.TotalPriceUsd.HasValue).Sum(g => g.TotalPriceUsd!.Value);
            groupedCount = Groups.Count;
        }
        else
        {
            totalItems = Items.Count;
            pricedItems = Items.Count(i => i.PriceUsd.HasValue);
            totalValue = Items.Where(i => i.PriceUsd.HasValue).Sum(i => i.PriceUsd!.Value);
            groupedCount = 0;
        }

        PriceSummary.Update(totalItems, pricedItems, totalValue, "USD", groupedCount: groupedCount);
        OnTotalPriceChanged?.Invoke(totalValue);
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigation.GoBack();
    }
}
