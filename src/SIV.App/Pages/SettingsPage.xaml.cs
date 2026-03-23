using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.Domain.Enums;
using SIV.UI.ViewModels;
using System.Diagnostics;

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
            ViewModel.OnSaved = ShowPostSaveDialog;
            Bindings.Update();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.OnSaved = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedThemeIndex))
        {
            App.ApplyTheme((AppTheme)ViewModel.SelectedThemeIndex);
        }
        else if (e.PropertyName == nameof(SettingsViewModel.UpdateInfoSeverity))
        {
            UpdateInfoBar.Severity = ViewModel.UpdateInfoSeverity == 1
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Informational;
        }
    }

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            GeneralSection.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
            PricingSection.Visibility = tag == "Pricing" ? Visibility.Visible : Visibility.Collapsed;
            AboutSection.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
            ActionBar.Visibility = tag == "About" ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void ShowPostSaveDialog()
    {
        if (App.MainWindow is MainWindow mw)
            mw.UpdateToolbar();

        var dialog = new ContentDialog
        {
            Title = "Settings saved",
            Content = "Would you like to go back or continue editing?",
            PrimaryButtonText = "Go Back",
            CloseButtonText = "Stay",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.GoBackCommand.Execute(null);
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        var url = ViewModel?.GitHubUrl;
        if (!string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIV");
        if (Directory.Exists(appDataPath))
            Process.Start(new ProcessStartInfo(appDataPath) { UseShellExecute = true });
    }
}
