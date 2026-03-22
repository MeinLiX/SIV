using SIV.Domain.Entities;
using SIV.Domain.Enums;

namespace SIV.Application.Interfaces;

public interface IPriceRepository
{
    Task<Price?> GetPriceAsync(string marketHashName, CancellationToken ct = default);
    Task<IReadOnlyList<Price>> GetPricesByNamesAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default);
    Task UpsertPriceAsync(Price price, CancellationToken ct = default);
    Task<PriceFetchSession?> GetActiveSessionAsync(CancellationToken ct = default);
    Task<PriceFetchSession> CreateSessionAsync(PriceFetchSession session, CancellationToken ct = default);
    Task UpdateSessionAsync(PriceFetchSession session, CancellationToken ct = default);
    Task AddQueueItemsAsync(IEnumerable<PriceFetchQueueItem> items, CancellationToken ct = default);
    Task<IReadOnlyList<PriceFetchQueueItem>> GetPendingQueueItemsAsync(int sessionId, int batchSize, CancellationToken ct = default);
    Task UpdateQueueItemAsync(PriceFetchQueueItem item, CancellationToken ct = default);
    Task<int> GetQueueCountByStatusAsync(int sessionId, FetchItemStatus status, CancellationToken ct = default);
}
