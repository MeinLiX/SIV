using SIV.Application.Interfaces;
using SIV.Domain.Entities;

namespace SIV.Infrastructure.Steam;

public sealed class InventoryService : IInventoryService
{
    private readonly IGCServiceFactory _gcFactory;
    private readonly Dictionary<uint, IGCService> _activeServices = new();

    public InventoryService(IGCServiceFactory gcFactory)
    {
        _gcFactory = gcFactory;
    }

    public async Task<IReadOnlyList<InventoryItem>> GetInventoryAsync(uint appId, CancellationToken ct = default)
    {
        var gc = await GetOrConnectAsync(appId, ct);
        return await gc.RequestInventoryAsync(ct);
    }

    public async Task<IReadOnlyList<InventoryItem>> GetCasketContentsAsync(uint appId, ulong casketId, CancellationToken ct = default)
    {
        var gc = await GetOrConnectAsync(appId, ct);
        return await gc.RequestCasketContentsAsync(casketId, ct: ct);
    }

    private async Task<IGCService> GetOrConnectAsync(uint appId, CancellationToken ct)
    {
        if (_activeServices.TryGetValue(appId, out var existing) && existing.IsConnected)
            return existing;

        var game = _gcFactory.SupportedGames.First(g => g.AppId == appId);
        var gc = _gcFactory.Create(game);

        if (!gc.IsConnected)
            await gc.ConnectAsync(ct);

        _activeServices[appId] = gc;
        return gc;
    }
}
