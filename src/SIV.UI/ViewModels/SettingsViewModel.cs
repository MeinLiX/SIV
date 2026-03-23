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
    private bool _showUpdateButton;

    [ObservableProperty]
    private string _updateStatusText = "Checking...";

    [ObservableProperty]
    private bool _hasChanges;

    public string CurrentVersion { get; } = Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public string VersionDescription => $"Version {CurrentVersion}";

    public string GitHubUrl => _settings.UpdateRepoUrl;

    /// <summary>
    /// Called by the Page after creation to inject live update state from MainViewModel.
    /// </summary>
    public void SetUpdateInfo(bool updateAvailable, string? newVersion)
    {
        if (updateAvailable && !string.IsNullOrEmpty(newVersion))
            UpdateStatusText = $"Update available: v{newVersion}";
        else
            UpdateStatusText = "You are up to date";
        OnPropertyChanged(nameof(UpdateInfoSeverity));
    }

    public int UpdateInfoSeverity =>
        UpdateStatusText.StartsWith("Update available", StringComparison.Ordinal) ? 0 : 1;

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
        ShowUpdateButton = _settings.ShowUpdateButton;
        SelectedOpenLinkInIndex = (int)_settings.OpenLinkIn;
        HasChanges = false;
    }

    partial void OnPriceCacheDurationHoursChanged(int value) => HasChanges = true;
    partial void OnPriceRequestDelayMsChanged(int value) => HasChanges = true;
    partial void OnPriceMaxConsecutiveFailuresChanged(int value) => HasChanges = true;
    partial void OnPriceRetryDelaySecondsChanged(int value) => HasChanges = true;
    partial void OnSelectedThemeIndexChanged(int value) => HasChanges = true;
    partial void OnEnableIconCacheChanged(bool value) => HasChanges = true;
    partial void OnShowUpdateButtonChanged(bool value) => HasChanges = true;
    partial void OnSelectedOpenLinkInIndexChanged(int value) => HasChanges = true;

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
        _settings.ShowUpdateButton = ShowUpdateButton;
        _settings.OpenLinkIn = (OpenLinkIn)SelectedOpenLinkInIndex;
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
