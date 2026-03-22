using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.Application.DTOs;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class AccountPickerPage : Page
{
    public AccountPickerViewModel ViewModel { get; private set; } = null!;

    public AccountPickerPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AccountPickerViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
        }
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedAccount account })
            ViewModel.SelectAccountCommand.Execute(account);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedAccount account })
            ViewModel.RemoveAccountCommand.Execute(account);
    }
}
