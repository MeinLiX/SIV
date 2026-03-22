using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.Domain.Enums;
using SIV.UI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SIV.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; private set; } = null!;

    public SettingsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is SettingsViewModel vm)
        {
            ViewModel = vm;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Bindings.Update();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedThemeIndex))
        {
            App.ApplyTheme((AppTheme)ViewModel.SelectedThemeIndex);
        }
    }

    private async void BrowseCs2Path_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        var window = App.MainWindow;
        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.Cs2GamePath = folder.Path;
        }
    }
}
