using SIV.Domain.Enums;

namespace SIV.Application.Interfaces;

public interface ISettingsService
{
    int PriceCacheDurationHours { get; set; }
    int PriceRequestDelayMs { get; set; }
    int PriceMaxConsecutiveFailures { get; set; }
    int PriceRetryDelaySeconds { get; set; }
    AppTheme Theme { get; set; }
    bool EnableIconCache { get; set; }
    string UpdateRepoUrl { get; set; }
    OpenLinkIn OpenLinkIn { get; set; }
    bool ShowUpdateButton { get; set; }
    Task SaveAsync();
    Task LoadAsync();
}
