using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using SIV.Application.Interfaces;
using SIV.Domain.Enums;
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

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app", "siv_icon.ico");
        if (File.Exists(iconPath))
            MainWindow.AppWindow.SetIcon(iconPath);

        ApplyTheme(Services.GetRequiredService<ISettingsService>().Theme);

        var mainVm = Services.GetRequiredService<MainViewModel>();
        ((MainWindow)MainWindow).Initialize(mainVm);

        MainWindow.Activate();
    }

    public static void Shutdown()
    {
        try
        {
            Services?.Dispose();
            Log.CloseAndFlush();
        }
        finally
        {
            Environment.Exit(0);
        }
    }

    public static ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;
    public static event Action<ElementTheme>? ThemeChanged;

    public static void ApplyTheme(AppTheme theme)
    {
        var elementTheme = theme switch
        {
            AppTheme.Dark => ElementTheme.Dark,
            AppTheme.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        CurrentTheme = elementTheme;

        if (MainWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = elementTheme;
        }

        ThemeChanged?.Invoke(elementTheme);
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
