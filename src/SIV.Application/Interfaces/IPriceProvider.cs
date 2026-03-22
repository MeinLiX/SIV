using SIV.Application.DTOs;

namespace SIV.Application.Interfaces;

public interface IPriceProvider
{
    string SourceName { get; }
    Task<IReadOnlyList<PriceResult>> GetPricesAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default);
}
