using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.Interfaces;
using SIV.Domain.Enums;
using SIV.UI.Services;
using System.Reflection;

namespace SIV.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private int _priceCacheDurationHours;

    [ObservableProperty]
    private int _priceRequestDelayMs;

    [ObservableProperty]
    private int _priceMaxConsecutiveFailures;

    [ObservableProperty]
    private int _priceRetryDelaySeconds;

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private bool _enableIconCache;

    [ObservableProperty]
    private int _selectedOpenLinkInIndex;

    [ObservableProperty]
    private string _cs2GamePath = string.Empty;

    [ObservableProperty]
    private string _cs2PathStatus = string.Empty;

    [ObservableProperty]
    private bool _hasChanges;

    public string CurrentVersion { get; } = Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public override string Title => "Settings";

    public SettingsViewModel(ISettingsService settings, INavigationService navigation)
    {
        _settings = settings;
        _navigation = navigation;
        LoadSettings();
    }

    private void LoadSettings()
    {
        PriceCacheDurationHours = _settings.PriceCacheDurationHours;
        PriceRequestDelayMs = _settings.PriceRequestDelayMs;
        PriceMaxConsecutiveFailures = _settings.PriceMaxConsecutiveFailures;
        PriceRetryDelaySeconds = _settings.PriceRetryDelaySeconds;
        SelectedThemeIndex = (int)_settings.Theme;
        EnableIconCache = _settings.EnableIconCache;
        SelectedOpenLinkInIndex = (int)_settings.OpenLinkIn;
        Cs2GamePath = _settings.Cs2GamePath;
        ValidateCs2Path();
        HasChanges = false;
    }

    partial void OnPriceCacheDurationHoursChanged(int value) => HasChanges = true;
    partial void OnPriceRequestDelayMsChanged(int value) => HasChanges = true;
    partial void OnPriceMaxConsecutiveFailuresChanged(int value) => HasChanges = true;
    partial void OnPriceRetryDelaySecondsChanged(int value) => HasChanges = true;
    partial void OnSelectedThemeIndexChanged(int value) => HasChanges = true;
    partial void OnEnableIconCacheChanged(bool value) => HasChanges = true;
    partial void OnSelectedOpenLinkInIndexChanged(int value) => HasChanges = true;
    partial void OnCs2GamePathChanged(string value)
    {
        HasChanges = true;
        ValidateCs2Path();
    }

    private void ValidateCs2Path()
    {
        if (string.IsNullOrWhiteSpace(Cs2GamePath))
        {
            Cs2PathStatus = string.Empty;
            return;
        }

        var csgoDir = Path.Combine(Cs2GamePath, "game", "csgo");
        if (Directory.Exists(csgoDir))
        {
            var locFile = Path.Combine(csgoDir, "resource", "csgo_english.txt");
            Cs2PathStatus = File.Exists(locFile)
                ? "Valid CS2 path (localization found)"
                : "Valid CS2 path (localization file not found)";
        }
        else
        {
            Cs2PathStatus = "Invalid: 'game/csgo' directory not found";
        }
    }

    public Action? OnSaved { get; set; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.PriceCacheDurationHours = PriceCacheDurationHours;
        _settings.PriceRequestDelayMs = PriceRequestDelayMs;
        _settings.PriceMaxConsecutiveFailures = PriceMaxConsecutiveFailures;
        _settings.PriceRetryDelaySeconds = PriceRetryDelaySeconds;
        _settings.Theme = (AppTheme)SelectedThemeIndex;
        _settings.EnableIconCache = EnableIconCache;
        _settings.OpenLinkIn = (OpenLinkIn)SelectedOpenLinkInIndex;
        _settings.Cs2GamePath = Cs2GamePath;
        await _settings.SaveAsync();
        HasChanges = false;
        StatusText = "Settings saved";
        OnSaved?.Invoke();
    }

    [RelayCommand]
    private void Reset()
    {
        LoadSettings();
        StatusText = "Settings reset";
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigation.GoBack();
    }
}
