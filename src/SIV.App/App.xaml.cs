using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using SIV.UI.ViewModels;

namespace SIV.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ServiceCollectionExtensions.ConfigureServices();
        UnhandledException += OnUnhandledException;

        MainWindow = new MainWindow
        {
            Title = "SIV — Steam Inventory Viewer"
        };

        var mainVm = Services.GetRequiredService<MainViewModel>();
        ((MainWindow)MainWindow).Initialize(mainVm);

        MainWindow.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "Unhandled UI exception");
        }
        else
        {
            Log.Error("Unhandled UI exception without exception payload. Message={Message}", e.Message);
        }
    }
}
