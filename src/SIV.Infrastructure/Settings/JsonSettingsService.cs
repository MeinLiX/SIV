using SIV.Application.Interfaces;
using SIV.Domain.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIV.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;

    public int PriceCacheDurationHours { get; set; } = 12;
    public int PriceRequestDelayMs { get; set; } = 3500;
    public int PriceMaxConsecutiveFailures { get; set; } = 3;
    public int PriceRetryDelaySeconds { get; set; } = 10;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool EnableIconCache { get; set; } = true;
    public string Cs2GamePath { get; set; } = string.Empty;
    public string UpdateRepoUrl { get; set; } = "https://github.com/MeinLiX/SIV";
    public OpenLinkIn OpenLinkIn { get; set; } = OpenLinkIn.Browser;
    public bool ShowUpdateButton { get; set; } = true;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
            Theme = Theme,
            EnableIconCache = EnableIconCache,
            Cs2GamePath = Cs2GamePath,
            UpdateRepoUrl = UpdateRepoUrl,
            OpenLinkIn = OpenLinkIn,
            ShowUpdateButton = ShowUpdateButton
        };
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath)) return;

        var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<SettingsData>(json, _jsonOptions);
        if (data is null) return;

        PriceCacheDurationHours = data.PriceCacheDurationHours;
        PriceRequestDelayMs = data.PriceRequestDelayMs;
        PriceMaxConsecutiveFailures = data.PriceMaxConsecutiveFailures;
        PriceRetryDelaySeconds = data.PriceRetryDelaySeconds;
        Theme = data.Theme;
        EnableIconCache = data.EnableIconCache;
        Cs2GamePath = data.Cs2GamePath;
        UpdateRepoUrl = data.UpdateRepoUrl;
        OpenLinkIn = data.OpenLinkIn;
        ShowUpdateButton = data.ShowUpdateButton;
    }

    private sealed class SettingsData
    {
        public int PriceCacheDurationHours { get; set; } = 12;
        public int PriceRequestDelayMs { get; set; } = 3500;
        public int PriceMaxConsecutiveFailures { get; set; } = 3;
        public int PriceRetryDelaySeconds { get; set; } = 10;
        public AppTheme Theme { get; set; } = AppTheme.Dark;
        public bool EnableIconCache { get; set; } = true;
        public string Cs2GamePath { get; set; } = string.Empty;
        public string UpdateRepoUrl { get; set; } = "https://github.com/MeinLiX/SIV";
        public OpenLinkIn OpenLinkIn { get; set; } = OpenLinkIn.Browser;
        public bool ShowUpdateButton { get; set; } = true;
    }
}
