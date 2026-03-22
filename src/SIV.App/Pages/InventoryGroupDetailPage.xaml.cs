using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class InventoryGroupDetailPage : Page
{
    public InventoryGroupDetailViewModel ViewModel { get; private set; } = null!;

    public InventoryGroupDetailPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is InventoryGroupDetailViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
        }
    }
}
