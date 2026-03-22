using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SIV.App.Converters;
using SIV.Application.Interfaces;
using SIV.Domain.Games;
using SIV.Infrastructure.Cache;
using SIV.Infrastructure.Persistence;
using SIV.Infrastructure.Pricing;
using SIV.Infrastructure.Security;
using SIV.Infrastructure.Settings;
using SIV.Infrastructure.Steam;
using SIV.Infrastructure.Steam.GC;
using SIV.Infrastructure.Update;
using SIV.UI.Services;
using SIV.UI.ViewModels;

namespace SIV.App;

public static class ServiceCollectionExtensions
{
    public static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIV");
        Directory.CreateDirectory(appDataPath);
        var dbPath = Path.Combine(appDataPath, "siv.db");
        var logPath = Path.Combine(appDataPath, "logs", "siv-.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Error()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        services.AddDbContextFactory<SivDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddDataProtection()
            .SetApplicationName("SIV")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataPath, "keys")));

        services.AddSingleton<SteamConnectionService>();
        services.AddSingleton<IAccountSessionStorage, AccountSessionStorage>();
        services.AddSingleton<IAuthService, AuthService>();

        services.AddSingleton<IGameDefinition, CS2GameDefinition>();
        services.AddSingleton<IItemDefinitionProvider, CS2ItemDefinitionProvider>();
        services.AddSingleton<IGCServiceFactory, GCServiceFactory>();
        services.AddSingleton<IInventoryService, InventoryService>();

        services.AddSingleton<IPriceRepository, PriceRepository>();
        services.AddHttpClient<IPriceProvider, SteamMarketPriceProvider>();
        services.AddSingleton<IPricingService, PricingService>();

        var settingsService = new JsonSettingsService(appDataPath);
        settingsService.LoadAsync().GetAwaiter().GetResult();
        services.AddSingleton<ISettingsService>(settingsService);

        services.AddSingleton<IIconCacheService>(sp =>
            new IconCacheService(appDataPath, sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILogger<IconCacheService>>()));

        services.AddSingleton<INavigationService, NavigationService>();

        services.AddHttpClient<IUpdateService, GitHubUpdateService>();

        services.AddTransient<MainViewModel>();

        var provider = services.BuildServiceProvider();

        StringToImageSourceConverter.IconCache = provider.GetRequiredService<IIconCacheService>();

        using (var scope = provider.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SivDbContext>>();
            using var db = dbFactory.CreateDbContext();
            db.Database.EnsureCreated();

            using var cmd = db.Database.GetDbConnection().CreateCommand();
            db.Database.OpenConnection();
            cmd.CommandText = "PRAGMA table_info(Prices)";
            using var reader = cmd.ExecuteReader();
            bool hasRequestUrl = false;
            while (reader.Read())
            {
                if (reader.GetString(1) == "RequestUrl")
                    hasRequestUrl = true;
            }
            reader.Close();
            if (!hasRequestUrl)
            {
                using var alter = db.Database.GetDbConnection().CreateCommand();
                alter.CommandText = "ALTER TABLE Prices ADD COLUMN RequestUrl TEXT NOT NULL DEFAULT ''";
                alter.ExecuteNonQuery();
            }
        }

        return provider;
    }
}
