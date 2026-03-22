using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.UI.Services;
using SIV.UI.Utilities;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class InventoryViewModel : ViewModelBase
{
    private readonly IGCServiceFactory _gcFactory;
    private readonly IPricingService _pricingService;
    private readonly INavigationService _navigation;
    private readonly uint _appId;
    private bool _shouldAutoLoadOnActivate;
    private IReadOnlyList<InventoryItem> _allItems = [];

    public Action<CasketDetailViewModel>? ShowCasketDialog { get; set; }

    [ObservableProperty]
    private ObservableCollection<InventoryGroupViewModel> _groups = [];

    [ObservableProperty]
    private ObservableCollection<InventoryGroupViewModel> _filteredGroups = [];

    [ObservableProperty]
    private ObservableCollection<InventoryItemViewModel> _items = [];

    [ObservableProperty]
    private ObservableCollection<InventoryItemViewModel> _filteredItems = [];

    [ObservableProperty]
    private bool _isGrouped;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hideNonMarketable;

    [ObservableProperty]
    private SortState _sort = new();

    [ObservableProperty]
    private string _selectedTypeFilter = "All";

    [ObservableProperty]
    private PriceSummaryViewModel _priceSummary = new();

    [ObservableProperty]
    private bool _isPricingRunning;

    [ObservableProperty]
    private int _pricingProgress;

    [ObservableProperty]
    private int _pricingTotal;

    [ObservableProperty]
    private bool _hasLoadedOnce;

    [ObservableProperty]
    private ObservableCollection<string> _availableTypes = ["All"];

    public override string Title => "Inventory";

    public bool ShowInitialLoadCta => !HasLoadedOnce && !IsBusy;
    public bool ShowInitialLoadingIndicator => !HasLoadedOnce && IsBusy;
    public bool ShowToolbarLoadingState => HasLoadedOnce && IsBusy;
    public bool ShowToolbarStatusText => ShowToolbarLoadingState && !string.IsNullOrWhiteSpace(StatusText);

    public InventoryViewModel(
        IGCServiceFactory gcFactory,
        IPricingService pricingService,
        INavigationService navigation,
        uint appId,
        bool autoLoadOnActivate = false)
    {
        _gcFactory = gcFactory;
        _pricingService = pricingService;
        _navigation = navigation;
        _appId = appId;
        _shouldAutoLoadOnActivate = autoLoadOnActivate;

        _pricingService.OnProgressChanged += progress => OnPricingProgress(progress);
        Sort.PropertyChanged += (_, _) => ApplyFilter();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HasLoadedOnce) or nameof(IsBusy))
            {
                OnPropertyChanged(nameof(ShowInitialLoadCta));
                OnPropertyChanged(nameof(ShowInitialLoadingIndicator));
                OnPropertyChanged(nameof(ShowToolbarLoadingState));
                OnPropertyChanged(nameof(ShowToolbarStatusText));
            }
            else if (e.PropertyName == nameof(StatusText))
            {
                OnPropertyChanged(nameof(ShowToolbarStatusText));
            }
        };
    }

    public void EnsureInitialLoadStarted()
    {
        if (!_shouldAutoLoadOnActivate || HasLoadedOnce || IsBusy)
            return;

        _shouldAutoLoadOnActivate = false;
        LoadInventoryCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LoadInventoryAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Connecting to Game Coordinator...";
        ErrorMessage = null;

        try
        {
            var gameDef = _gcFactory.SupportedGames.First(g => g.AppId == _appId);
            var gcService = _gcFactory.Create(gameDef);

            if (!gcService.IsConnected)
                await gcService.DisconnectAsync();

            await gcService.ConnectAsync(ct);

            StatusText = "Requesting inventory...";
            _allItems = await gcService.RequestInventoryAsync(ct);

            BuildGroups();
            HasLoadedOnce = true;

            StatusText = "Loading cached prices...";
            await LoadCachedPricesAsync(ct);

            StatusText = $"Loaded {_allItems.Count} items";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (TimeoutException)
        {
            var logPath = GetLogDirectory();
            StatusText = "Inventory request timed out";
            ErrorMessage = $"CS2 inventory loading timed out. Logs: {logPath}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FetchPricesAsync(CancellationToken ct)
    {
        if (IsPricingRunning) return;

        IsPricingRunning = true;
        ErrorMessage = null;

        try
        {
            var marketNames = _allItems
                .Where(i => i.CanFetchMarketPrice && !string.IsNullOrEmpty(i.MarketHashName))
                .Select(i => i.MarketHashName)
                .Distinct()
                .ToList();

            await _pricingService.StartPriceFetchAsync(_appId, marketNames, ct);

            await LoadCachedPricesAsync(ct);
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
    private async Task RefreshItemPriceAsync(InventoryItemViewModel item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.MarketHashName) || !item.CanFetchPrice)
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
    private async Task RefreshGroupPriceAsync(InventoryGroupViewModel group, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(group.MarketHashName) || !group.CanFetchPrice)
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

    private void ApplyPriceToAll(string marketHashName, decimal price, DateTime? updatedAt = null)
    {
        PropagatePrice(marketHashName, price, updatedAt);
        UpdateSummary();
    }

    private void PropagatePrice(string marketHashName, decimal price, DateTime? updatedAt)
    {
        foreach (var item in Items.Where(i => i.MarketHashName == marketHashName))
        {
            item.PriceUsd = price;
            item.PriceUpdatedAt = updatedAt;
        }
        foreach (var group in Groups.Where(g => g.MarketHashName == marketHashName))
        {
            group.UpdatePrice(price);
            foreach (var gi in group.Items)
            {
                gi.PriceUsd = price;
                gi.PriceUpdatedAt = updatedAt;
            }
        }
    }

    [RelayCommand]
    private async Task OpenCasketAsync(InventoryGroupViewModel group, CancellationToken ct)
    {
        var casketItem = group.Items.FirstOrDefault(i => i.IsCasket);
        if (casketItem is null) return;

        await OpenCasketItemAsync(casketItem, ct);
    }

    [RelayCommand]
    private async Task OpenCasketItemAsync(InventoryItemViewModel item, CancellationToken ct)
    {
        if (!item.IsCasket) return;

        ErrorMessage = null;

        try
        {
            var gameDef = _gcFactory.SupportedGames.First(g => g.AppId == _appId);
            var gcService = _gcFactory.Create(gameDef);
            async Task<IReadOnlyList<InventoryItem>> LoadContentsAsync(bool forceRefresh, CancellationToken token)
                => await gcService.RequestCasketContentsAsync(item.Id, forceRefresh, token);

            var casketLabel = !string.IsNullOrWhiteSpace(item.CustomName)
                ? item.CustomName
                : item.Name;
            var vm = new CasketDetailViewModel(casketLabel, item.CustomDescription, [], _navigation, _pricingService, LoadContentsAsync,
                _gcFactory.GetItemDefinitionProvider(_appId), _appId);

            vm.OnTotalPriceChanged = total =>
            {
                item.CasketTotalPriceUsd = total > 0 ? total : null;
                foreach (var g in Groups.Where(g => g.IsCasketGroup && g.Items.Contains(item)))
                    g.CasketTotalPriceUsd = g.Items.Where(i => i.CasketTotalPriceUsd.HasValue).Sum(i => i.CasketTotalPriceUsd!.Value);
                UpdateSummary();
            };

            if (ShowCasketDialog is not null)
                ShowCasketDialog(vm);
            else
                _navigation.NavigateTo(vm);

            await vm.LoadContentsAsync(false, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenMarketListing(string? marketHashName)
    {
        SteamMarketUrl.OpenInBrowser(_appId, marketHashName ?? string.Empty);
    }

    [RelayCommand]
    private void OpenGroupDetails(InventoryGroupViewModel group)
    {
        if (group.Items.Count == 0)
            return;

        var vm = new InventoryGroupDetailViewModel(
            group.Name,
            group.Items.Select(i => i.Source).ToList(),
            _navigation,
            _gcFactory.GetItemDefinitionProvider(_appId));
        _navigation.NavigateTo(vm);
    }

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
    partial void OnHideNonMarketableChanged(bool value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    public async Task ResolveHoverIconsAsync(InventoryItemViewModel item, CancellationToken ct)
    {
        var provider = _gcFactory.GetItemDefinitionProvider(_appId);
        if (provider is null)
            return;

        provider.GetContainerDrops(item.Source.DefIndex);
        var iconByDefIndex = BuildIconMap(_allItems);
        ResolveOriginIcons(item.Origins, iconByDefIndex, provider);

        var unresolvedMhns = new List<string>();

        foreach (var drop in item.ContainerDrops)
        {
            if (string.IsNullOrEmpty(drop.IconUrl) && !string.IsNullOrEmpty(drop.MarketHashName))
                unresolvedMhns.Add(drop.MarketHashName);
        }

        foreach (var origin in item.Origins)
        {
            if (string.IsNullOrEmpty(origin.IconUrl) && !string.IsNullOrEmpty(origin.Name))
                unresolvedMhns.Add(origin.Name);
        }

        if (unresolvedMhns.Count == 0)
            return;

        var gameDef = _gcFactory.SupportedGames.FirstOrDefault(g => g.AppId == _appId);
        if (gameDef is null)
            return;

        var gcService = _gcFactory.Create(gameDef);
        await gcService.FetchIconsFromMarketAsync(unresolvedMhns, ct);

        provider.GetContainerDrops(item.Source.DefIndex);
        ResolveOriginIcons(item.Origins, iconByDefIndex, provider);
    }

    private void BuildGroups()
    {
        var provider = _gcFactory.GetItemDefinitionProvider(_appId);
        var iconByDefIndex = BuildIconMap(_allItems);

        InventoryItemViewModel CreateVm(InventoryItem item)
            => CreateViewModel(item, provider, iconByDefIndex);

        var itemVms = _allItems
            .Where(i => i.ContainedInCasketId == 0)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.PaintWear)
            .ThenBy(i => i.Id)
            .Select(CreateVm)
            .ToList();

        Items = new ObservableCollection<InventoryItemViewModel>(itemVms);

        var grouped = _allItems
            .Where(i => i.ContainedInCasketId == 0)
            .GroupBy(i => i.GroupKey)
            .Select(g =>
            {
                var vms = g.Select(CreateVm);
                return new InventoryGroupViewModel(vms);
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        Groups = new ObservableCollection<InventoryGroupViewModel>(grouped);

        var types = Items
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

    private void ApplyFilter()
    {
        IEnumerable<InventoryGroupViewModel> filteredGroups = Groups;
        IEnumerable<InventoryItemViewModel> filteredItems = Items;

        if (HideNonMarketable)
        {
            filteredItems = filteredItems.Where(i => i.IsMarketable);
            filteredGroups = filteredGroups.Where(g => g.Items.Any(i => i.IsMarketable));
        }

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
            filteredGroups = filteredGroups.Where(g =>
                g.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || g.MarketHashName.Contains(term, StringComparison.OrdinalIgnoreCase));
            filteredItems = filteredItems.Where(i =>
                i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.MarketHashName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.TechnicalSummary.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.StickersSummary.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.KeychainSummary.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        filteredItems = SortHelper.SortItems(filteredItems, Sort);
        filteredGroups = SortHelper.SortGroups(filteredGroups, Sort);

        FilteredGroups = new ObservableCollection<InventoryGroupViewModel>(filteredGroups);
        FilteredItems = new ObservableCollection<InventoryItemViewModel>(filteredItems);

        UpdateSummary();
    }



    private void ApplyPrices(IReadOnlyList<PriceResult> prices)
    {
        foreach (var result in prices)
        {
            if (result.PriceUSD is { } price)
                PropagatePrice(result.MarketHashName, price, result.UpdatedAt);
        }
        UpdateSummary();
    }

    private async Task LoadCachedPricesAsync(CancellationToken ct)
    {
        var names = Items
            .Where(i => i.CanFetchPrice && !string.IsNullOrEmpty(i.MarketHashName))
            .Select(i => i.MarketHashName)
            .Distinct()
            .ToList();

        if (names.Count == 0) return;

        var cached = await _pricingService.LoadCachedPricesAsync(names, ct);
        if (cached.Count > 0)
            ApplyPrices(cached);
    }

    private void UpdateSummary()
    {
        int totalItems;
        int pricedItems;
        decimal totalValue;
        int storageItems;
        decimal casketTotalValue;
        int groupedCount;

        if (IsGrouped)
        {
            totalItems = FilteredGroups.Sum(g => g.Count);
            pricedItems = FilteredGroups.Where(g => g.UnitPriceUsd.HasValue).Sum(g => g.Count);
            totalValue = FilteredGroups.Where(g => g.TotalPriceUsd.HasValue).Sum(g => g.TotalPriceUsd!.Value);
            storageItems = FilteredGroups.Where(g => g.IsCasketGroup).Sum(g => g.Items.Sum(i => i.CasketItemCount));
            casketTotalValue = FilteredGroups.Where(g => g.CasketTotalPriceUsd.HasValue).Sum(g => g.CasketTotalPriceUsd!.Value);
            groupedCount = FilteredGroups.Count;
        }
        else
        {
            totalItems = FilteredItems.Count;
            pricedItems = FilteredItems.Count(i => i.PriceUsd.HasValue);
            totalValue = FilteredItems.Where(i => i.PriceUsd.HasValue).Sum(i => i.PriceUsd!.Value);
            storageItems = FilteredItems.Where(i => i.IsCasket).Sum(i => i.CasketItemCount);
            casketTotalValue = FilteredItems.Where(i => i.CasketTotalPriceUsd.HasValue).Sum(i => i.CasketTotalPriceUsd!.Value);
            groupedCount = 0;
        }

        PriceSummary.Update(totalItems, pricedItems, totalValue, "USD", storageItems, casketTotalValue, groupedCount);
    }

    private void OnPricingProgress(PriceFetchProgress progress)
    {
        PricingProgress = progress.FetchedItems;
        PricingTotal = progress.TotalItems;
        StatusText = $"Fetching prices: {progress.FetchedItems}/{progress.TotalItems}";
    }

    private static string GetLogDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIV", "logs");

    internal static Dictionary<uint, string> BuildIconMap(IEnumerable<InventoryItem> items)
    {
        var map = new Dictionary<uint, string>();
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.IconUrl) && !map.ContainsKey(item.DefIndex))
                map[item.DefIndex] = item.IconUrl;
        }
        return map;
    }

    internal static InventoryItemViewModel CreateViewModel(
        InventoryItem item, IItemDefinitionProvider? provider, Dictionary<uint, string> iconByDefIndex)
    {
        var vm = new InventoryItemViewModel(item);
        if (provider is not null)
            PopulateItemMetadata(vm, item, provider, iconByDefIndex);
        return vm;
    }

    internal static void ResolveOriginIcons(IReadOnlyList<ItemOriginInfo> origins, Dictionary<uint, string> iconByDefIndex, IItemDefinitionProvider? provider = null)
    {
        foreach (var origin in origins)
        {
            if (!string.IsNullOrEmpty(origin.IconUrl))
                continue;

            if (origin.DefIndex > 0 && iconByDefIndex.TryGetValue((uint)origin.DefIndex, out var url))
            {
                origin.IconUrl = url;
                continue;
            }

            if (provider is not null && !string.IsNullOrEmpty(origin.Name))
            {
                var hash = provider.ResolveIconHash(origin.Name);
                if (!string.IsNullOrEmpty(hash))
                {
                    origin.IconUrl = hash;
                    continue;
                }
            }

            if (origin.DefIndex > 0 && provider is not null)
                origin.IconUrl = provider.GetItemIconPath((uint)origin.DefIndex, 0) ?? string.Empty;
        }
    }

    internal static void PopulateStickerCards(InventoryItemViewModel vm, InventoryItem item, IItemDefinitionProvider provider)
    {
        var cards = new List<StickerCardInfo>();
        foreach (var s in item.Stickers)
        {
            cards.Add(new StickerCardInfo
            {
                Name = s.Name,
                IconUrl = provider.GetStickerIconUrl(s.StickerId),
                Wear = s.Wear
            });
        }
        if (item.Keychain is not null)
        {
            cards.Add(new StickerCardInfo
            {
                Name = item.Keychain.Name,
                IconUrl = provider.GetKeychainIconUrl(item.Keychain.KeychainId),
                IsKeychain = true
            });
        }
        if (cards.Count > 0)
            vm.StickerCards = cards;
    }

    internal static void PopulateItemMetadata(
        InventoryItemViewModel vm,
        InventoryItem item,
        IItemDefinitionProvider provider,
        Dictionary<uint, string> iconByDefIndex)
    {
        vm.Origins = provider.GetItemOrigins(item.PaintIndex, item.DefIndex, item.Stickers.Count > 0 ? item.Stickers[0].StickerId : 0);
        ResolveOriginIcons(vm.Origins, iconByDefIndex, provider);
        PopulateStickerCards(vm, item, provider);
        vm.ContainerDrops = provider.GetContainerDrops(item.DefIndex);
    }
}
