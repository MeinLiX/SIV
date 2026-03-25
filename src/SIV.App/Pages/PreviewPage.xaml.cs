using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class PreviewPage : Page
{
    public PreviewViewModel ViewModel { get; private set; } = null!;

    public PreviewPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is PreviewViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
        }
    }
}
