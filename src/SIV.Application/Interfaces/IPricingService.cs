using SIV.Application.DTOs;

namespace SIV.Application.Interfaces;

public interface IPricingService
{
    Task StartPriceFetchAsync(uint appId, IReadOnlyList<string> marketHashNames, CancellationToken ct = default);
    Task<PriceResult?> FetchSinglePriceAsync(string marketHashName, bool forceRefresh = false, CancellationToken ct = default);
    Task<PriceFetchProgress> GetProgressAsync(CancellationToken ct = default);
    Task<PriceSummaryDto> GetSummaryAsync(IReadOnlyList<InventoryGroupDto> groups, CancellationToken ct = default);
    Task<IReadOnlyList<PriceResult>> LoadCachedPricesAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default);
    event Action<PriceFetchProgress>? OnProgressChanged;
}
