using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; private set; } = null!;

    public LoginPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LoginViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
        }
    }
}
