using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class CasketDetailPage : Page
{
    public CasketDetailViewModel ViewModel { get; private set; } = null!;

    public CasketDetailPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is CasketDetailViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
        }
    }
}
