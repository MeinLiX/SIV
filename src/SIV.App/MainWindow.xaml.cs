using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SIV.App.Pages;
using SIV.UI.ViewModels;

namespace SIV.App;

public sealed partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        this.InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        NativeWindowMethods.ConfigurePresenter(this);
        NativeWindowMethods.SetWindowSize(this, 1100, 750);
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        App.Shutdown();
    }

    public void Initialize(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        _viewModel.RequestAppExit += () =>
        {
            DispatcherQueue.TryEnqueue(() => Close());
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentView))
                NavigateToView(_viewModel.CurrentView);
            else if (e.PropertyName is nameof(MainViewModel.IsLoggedIn)
                     or nameof(MainViewModel.PlayerName)
                     or nameof(MainViewModel.PlayerAvatarUrl)
                     or nameof(MainViewModel.UpdateAvailable)
                     or nameof(MainViewModel.UpdateVersion)
                     or nameof(MainViewModel.CurrentVersion))
                UpdateToolbar();
        };

        NavigateToView(_viewModel.CurrentView);
        UpdateToolbar();
    }

    private void NavigateToView(ViewModelBase? vm)
    {
        if (vm is null) return;

        var pageType = vm switch
        {
            AccountPickerViewModel => typeof(AccountPickerPage),
            LoginViewModel => typeof(LoginPage),
            GameSelectorViewModel => typeof(GameSelectorPage),
            InventoryViewModel => typeof(InventoryPage),
            CasketDetailViewModel => typeof(CasketDetailPage),
            InventoryGroupDetailViewModel => typeof(InventoryGroupDetailPage),
            SettingsViewModel => typeof(SettingsPage),
            _ => null
        };

        if (pageType is not null)
            ContentFrame.Navigate(pageType, vm);
    }

    private void UpdateToolbar()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var loggedIn = _viewModel?.IsLoggedIn ?? false;

            ProfileButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;

            var current = _viewModel?.CurrentVersion ?? "";
            VersionText.Text = string.IsNullOrEmpty(current) ? "" : $"v{current}";

            var updateAvailable = _viewModel?.UpdateAvailable ?? false;
            UpdateButton.Visibility = updateAvailable ? Visibility.Visible : Visibility.Collapsed;
            if (updateAvailable)
                UpdateButtonText.Text = $"Update to v{_viewModel?.UpdateVersion}";

            if (loggedIn && _viewModel is not null)
            {
                PlayerNameText.Text = _viewModel.PlayerName;

                if (!string.IsNullOrEmpty(_viewModel.PlayerAvatarUrl))
                {
                    try
                    {
                        AvatarImage.Source = new BitmapImage(new System.Uri(_viewModel.PlayerAvatarUrl));
                    }
                    catch
                    {
                        AvatarImage.Source = null;
                    }
                }
                else
                {
                    AvatarImage.Source = null;
                }
            }
        });
    }

    private void ChangeGameButton_Click(object sender, RoutedEventArgs e)
        => _viewModel?.NavigateToGameSelectorCommand.Execute(null);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _viewModel?.NavigateToSettingsCommand.Execute(null);

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
        => _viewModel?.InstallUpdateCommand.Execute(null);

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
        => _viewModel?.LogoutCommand.Execute(null);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
