using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using SIV.UI.Services;
using System.Reflection;

namespace SIV.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly IGCServiceFactory _gcFactory;
    private readonly IPricingService _pricingService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigation;
    private readonly IUpdateService _updateService;
    private UpdateInfo? _pendingUpdate;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string? _steamId;

    [ObservableProperty]
    private string _playerName = string.Empty;

    [ObservableProperty]
    private string _playerAvatarUrl = string.Empty;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    public string CurrentVersion { get; } = System.Reflection.Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public override string Title => "SIV — Steam Inventory Viewer";

    public MainViewModel(
        IAuthService auth,
        IGCServiceFactory gcFactory,
        IPricingService pricingService,
        ISettingsService settingsService,
        INavigationService navigation,
        IUpdateService updateService)
    {
        _auth = auth;
        _gcFactory = gcFactory;
        _pricingService = pricingService;
        _settingsService = settingsService;
        _navigation = navigation;
        _updateService = updateService;

        _navigation.NavigationChanged += () => OnNavigationChanged();

        _ = ShowStartScreenAsync();
        _ = CheckForUpdateAsync();
    }

    private async Task ShowStartScreenAsync()
    {
        var accounts = await _auth.GetSavedAccountsAsync();
        if (accounts.Count > 0)
            ShowAccountPicker();
        else
            ShowLogin();
    }

    private void ShowAccountPicker()
    {
        var pickerVm = new AccountPickerViewModel(
            _auth,
            onAddAccount: () => ShowLogin(canCancel: true),
            onAccountExpired: (accountName) => ShowLogin(canCancel: true, prefillUsername: accountName),
            onLoginSuccess: OnLoginSuccess);
        _navigation.ClearHistory();
        _navigation.NavigateTo(pickerVm);
    }

    private void ShowLogin(bool canCancel = false, string? prefillUsername = null)
    {
        Action? onCancel = canCancel ? ShowAccountPicker : null;
        var loginVm = new LoginViewModel(_auth, OnLoginSuccess, onCancel, prefillUsername);
        _navigation.ClearHistory();
        _navigation.NavigateTo(loginVm);
    }

    private async void OnLoginSuccess()
    {
        IsLoggedIn = true;
        SteamId = _auth.CurrentSteamId;
        await LoadProfileAsync();
        ShowGameSelector();
    }

    private async Task LoadProfileAsync()
    {
        var profile = await _auth.FetchProfileAsync();
        if (profile is not null)
        {
            PlayerName = profile.DisplayName;
            PlayerAvatarUrl = profile.AvatarUrl;
        }
    }

    private void ShowGameSelector()
    {
        var vm = new GameSelectorViewModel(_gcFactory, OnGameSelected);
        _navigation.ClearHistory();
        _navigation.NavigateTo(vm);
    }

    private void OnGameSelected(uint appId)
    {
        var vm = new InventoryViewModel(_gcFactory, _pricingService, _navigation, _settingsService, appId, autoLoadOnActivate: true);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private void NavigateToGameSelector()
    {
        ShowGameSelector();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        var vm = new SettingsViewModel(_settingsService, _navigation);
        vm.SetUpdateInfo(UpdateAvailable, UpdateVersion);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigation.GoBack();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _auth.LogoutAsync();
        IsLoggedIn = false;
        SteamId = null;
        PlayerName = string.Empty;
        PlayerAvatarUrl = string.Empty;
        ShowAccountPicker();
    }

    private void OnNavigationChanged()
    {
        CurrentView = _navigation.CurrentView;
        CanGoBack = _navigation.CanGoBack;
    }

    private async Task CheckForUpdateAsync()
    {
        var update = await _updateService.CheckForUpdateAsync();
        if (update is not null)
        {
            _pendingUpdate = update;
            UpdateVersion = update.NewVersion;
            UpdateAvailable = true;
        }
    }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (_pendingUpdate is null) return;
        _updateService.LaunchUpdater(_pendingUpdate);
        RequestAppExit?.Invoke();
    }

    /// <summary>
    /// The App layer subscribes to this to handle process exit after launching the updater.
    /// </summary>
    public event Action? RequestAppExit;
}
