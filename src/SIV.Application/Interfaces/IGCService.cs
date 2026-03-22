using SIV.Domain.Entities;

namespace SIV.Application.Interfaces;

public interface IGCService
{
    uint AppId { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<IReadOnlyList<InventoryItem>> RequestInventoryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InventoryItem>> RequestCasketContentsAsync(ulong casketId, bool forceRefresh = false, CancellationToken ct = default);
    Task<int?> GetInventoryCountAsync(CancellationToken ct = default);
    Task FetchIconsFromMarketAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default);
    bool IsConnected { get; }
}
