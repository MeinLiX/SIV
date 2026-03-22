using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class GameSelectorPage : Page
{
    public GameSelectorViewModel ViewModel { get; private set; } = null!;

    public GameSelectorPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is GameSelectorViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
        }
    }
}
