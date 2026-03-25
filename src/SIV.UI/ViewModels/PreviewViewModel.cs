using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.UI.Services;
using SIV.UI.Utilities;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class PreviewViewModel : ViewModelBase
{
    private readonly IItemDefinitionProvider _provider;
    private readonly IGCServiceFactory _gcFactory;
    private readonly uint _appId;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _iconFetchCts;

    [ObservableProperty]
    private ObservableCollection<WeaponInfo> _weapons = [];

    [ObservableProperty]
    private WeaponInfo? _selectedWeapon;

    [ObservableProperty]
    private ObservableCollection<PaintKitInfo> _paintKits = [];

    [ObservableProperty]
    private PaintKitInfo? _selectedPaintKit;

    [ObservableProperty]
    private double _wear = 0.15;

    public string WearText => Wear.ToString("F4");

    [ObservableProperty]
    private int _seed;

    [ObservableProperty]
    private string _previewName = string.Empty;

    [ObservableProperty]
    private string _previewIconUrl = string.Empty;

    [ObservableProperty]
    private string _exteriorText = string.Empty;

    [ObservableProperty]
    private string _exteriorColor = string.Empty;

    [ObservableProperty]
    private string _rarityColor = string.Empty;

    [ObservableProperty]
    private string _searchWeaponText = string.Empty;

    [ObservableProperty]
    private string _searchSkinText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<WeaponInfo> _filteredWeapons = [];

    [ObservableProperty]
    private ObservableCollection<PaintKitInfo> _filteredPaintKits = [];

    [ObservableProperty]
    private bool _isLoadingIcon;

    public bool ShowIconNotAvailable => string.IsNullOrEmpty(PreviewIconUrl) && !IsLoadingIcon && SelectedPaintKit is not null;

    partial void OnIsLoadingIconChanged(bool value) => OnPropertyChanged(nameof(ShowIconNotAvailable));
    partial void OnPreviewIconUrlChanged(string value) => OnPropertyChanged(nameof(ShowIconNotAvailable));

    public override string Title => "Preview";

    public PreviewViewModel(IItemDefinitionProvider provider, IGCServiceFactory gcFactory, uint appId, INavigationService navigation)
    {
        _provider = provider;
        _gcFactory = gcFactory;
        _appId = appId;
        _navigation = navigation;

        var weapons = provider.GetSkinnableWeapons();
        Weapons = new ObservableCollection<WeaponInfo>(weapons);
        FilteredWeapons = new ObservableCollection<WeaponInfo>(weapons);
    }

    partial void OnSelectedWeaponChanged(WeaponInfo? value)
    {
        if (value is null)
        {
            PaintKits.Clear();
            FilteredPaintKits.Clear();
            SelectedPaintKit = null;
            return;
        }

        var kits = _provider.GetPaintKitsForWeapon(value.DefIndex);
        PaintKits = new ObservableCollection<PaintKitInfo>(kits);
        SearchSkinText = string.Empty;
        FilteredPaintKits = new ObservableCollection<PaintKitInfo>(kits);
        SelectedPaintKit = null;
        _ = UpdatePreviewAsync();
    }

    partial void OnSelectedPaintKitChanged(PaintKitInfo? value)
    {
        OnPropertyChanged(nameof(ShowIconNotAvailable));
        _ = UpdatePreviewAsync();
    }
    partial void OnWearChanged(double value)
    {
        OnPropertyChanged(nameof(WearText));
        _ = UpdatePreviewAsync();
    }
    partial void OnSeedChanged(int value) => _ = UpdatePreviewAsync();

    partial void OnSearchWeaponTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredWeapons = new ObservableCollection<WeaponInfo>(Weapons);
            return;
        }

        FilteredWeapons = new ObservableCollection<WeaponInfo>(
            Weapons.Where(w => w.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    partial void OnSearchSkinTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredPaintKits = new ObservableCollection<PaintKitInfo>(PaintKits);
            return;
        }

        FilteredPaintKits = new ObservableCollection<PaintKitInfo>(
            PaintKits.Where(p => p.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    // Wear values for fallback market lookups, ordered by listing frequency.
    // GetItemIconPath uses 0.15f internally, so try that first.
    private static readonly float[] FallbackWears = [0.15f, 0.25f, 0.01f, 0.40f, 0.80f];

    private async Task UpdatePreviewAsync()
    {
        _iconFetchCts?.Cancel();

        if (SelectedWeapon is null)
        {
            PreviewName = string.Empty;
            PreviewIconUrl = string.Empty;
            ExteriorText = string.Empty;
            ExteriorColor = string.Empty;
            RarityColor = string.Empty;
            IsLoadingIcon = false;
            return;
        }

        var paintIndex = SelectedPaintKit?.PaintIndex ?? 0;
        var displayName = _provider.GetMarketHashName(
            SelectedWeapon.DefIndex, paintIndex, (float)Wear);

        PreviewName = displayName ?? string.Empty;
        ExteriorText = paintIndex > 0 ? GetExteriorString((float)Wear) : string.Empty;
        ExteriorColor = paintIndex > 0 ? GetExteriorColor((float)Wear) : string.Empty;

        // GetItemIconPath uses hardcoded 0.15f wear for cache lookup
        var iconHash = _provider.GetItemIconPath(
            SelectedWeapon.DefIndex, paintIndex);

        if (iconHash is not null)
        {
            PreviewIconUrl = SteamIconUrl.Normalize(iconHash, "720x420");
            IsLoadingIcon = false;
            return;
        }

        if (paintIndex == 0)
        {
            PreviewIconUrl = string.Empty;
            IsLoadingIcon = false;
            return;
        }

        IsLoadingIcon = true;
        PreviewIconUrl = string.Empty;

        var cts = new CancellationTokenSource();
        _iconFetchCts = cts;

        try
        {
            var gameDef = _gcFactory.SupportedGames.FirstOrDefault(g => g.AppId == _appId);
            if (gameDef is null)
                return;

            var gcService = _gcFactory.Create(gameDef);

            // Try each exterior until one resolves an icon
            foreach (var fallbackWear in FallbackWears)
            {
                cts.Token.ThrowIfCancellationRequested();

                var mhn = _provider.GetMarketHashName(
                    SelectedWeapon.DefIndex, paintIndex, fallbackWear);
                if (mhn is null)
                    continue;

                await gcService.FetchIconsFromMarketAsync([mhn], cts.Token);

                // Check if the icon was resolved (cache key matches GetItemIconPath's 0.15f lookup)
                iconHash = _provider.GetItemIconPath(
                    SelectedWeapon.DefIndex, paintIndex);
                if (iconHash is not null)
                {
                    PreviewIconUrl = SteamIconUrl.Normalize(iconHash, "720x420");
                    return;
                }

                // Also check via ResolveIconHash for non-0.15f exteriors
                var resolved = _provider.ResolveIconHash(mhn);
                if (resolved is not null)
                {
                    PreviewIconUrl = SteamIconUrl.Normalize(resolved, "720x420");
                    return;
                }
            }

            PreviewIconUrl = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Selection changed, ignore
        }
        finally
        {
            if (_iconFetchCts == cts)
                IsLoadingIcon = false;
        }
    }

    private static string GetExteriorString(float wear) => wear switch
    {
        <= 0.07f => "Factory New",
        < 0.15f => "Minimal Wear",
        < 0.38f => "Field-Tested",
        < 0.45f => "Well-Worn",
        _ => "Battle-Scarred"
    };

    private static string GetExteriorColor(float wear) => wear switch
    {
        <= 0.07f => "#4CAF50",
        < 0.15f => "#8BC34A",
        < 0.38f => "#FFC107",
        < 0.45f => "#FF9800",
        _ => "#F44336"
    };

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();
}
