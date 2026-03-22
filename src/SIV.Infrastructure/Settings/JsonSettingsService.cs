using SIV.Application.Interfaces;
using System.Text.Json;

namespace SIV.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;

    public int PriceCacheDurationHours { get; set; } = 12;
    public int PriceRequestDelayMs { get; set; } = 3500;
    public int PriceMaxConsecutiveFailures { get; set; } = 3;
    public int PriceRetryDelaySeconds { get; set; } = 10;
    public string Currency { get; set; } = "USD";
    public bool UseDarkTheme { get; set; } = true;
    public bool EnableIconCache { get; set; } = true;
    public string Cs2GamePath { get; set; } = string.Empty;
    public string UpdateRepoUrl { get; set; } = "https://github.com/MeinLiX/SIV";

    public JsonSettingsService(string appDataPath)
    {
        _filePath = Path.Combine(appDataPath, "settings.json");
    }

    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null) Directory.CreateDirectory(dir);

        var data = new SettingsData
        {
            PriceCacheDurationHours = PriceCacheDurationHours,
            PriceRequestDelayMs = PriceRequestDelayMs,
            PriceMaxConsecutiveFailures = PriceMaxConsecutiveFailures,
            PriceRetryDelaySeconds = PriceRetryDelaySeconds,
            Currency = Currency,
            UseDarkTheme = UseDarkTheme,
            EnableIconCache = EnableIconCache,
            Cs2GamePath = Cs2GamePath,
            UpdateRepoUrl = UpdateRepoUrl
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath)) return;

        var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<SettingsData>(json);
        if (data is null) return;

        PriceCacheDurationHours = data.PriceCacheDurationHours;
        PriceRequestDelayMs = data.PriceRequestDelayMs;
        PriceMaxConsecutiveFailures = data.PriceMaxConsecutiveFailures;
        PriceRetryDelaySeconds = data.PriceRetryDelaySeconds;
        Currency = data.Currency;
        UseDarkTheme = data.UseDarkTheme;
        EnableIconCache = data.EnableIconCache;
        Cs2GamePath = data.Cs2GamePath;
        UpdateRepoUrl = data.UpdateRepoUrl;
    }

    private sealed class SettingsData
    {
        public int PriceCacheDurationHours { get; set; } = 12;
        public int PriceRequestDelayMs { get; set; } = 3500;
        public int PriceMaxConsecutiveFailures { get; set; } = 3;
        public int PriceRetryDelaySeconds { get; set; } = 10;
        public string Currency { get; set; } = "USD";
        public bool UseDarkTheme { get; set; } = true;
        public bool EnableIconCache { get; set; } = true;
        public string Cs2GamePath { get; set; } = string.Empty;
        public string UpdateRepoUrl { get; set; } = "https://github.com/MeinLiX/SIV";
    }
}
