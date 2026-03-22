namespace SIV.Application.Interfaces;

public interface ISettingsService
{
    int PriceCacheDurationHours { get; set; }
    int PriceRequestDelayMs { get; set; }
    int PriceMaxConsecutiveFailures { get; set; }
    int PriceRetryDelaySeconds { get; set; }
    string Currency { get; set; }
    bool UseDarkTheme { get; set; }
    bool EnableIconCache { get; set; }
    string Cs2GamePath { get; set; }
    string UpdateRepoUrl { get; set; }
    Task SaveAsync();
    Task LoadAsync();
}
