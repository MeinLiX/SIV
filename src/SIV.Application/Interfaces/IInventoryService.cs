using SIV.Domain.Entities;

namespace SIV.Application.Interfaces;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryItem>> GetInventoryAsync(uint appId, CancellationToken ct = default);
    Task<IReadOnlyList<InventoryItem>> GetCasketContentsAsync(uint appId, ulong casketId, CancellationToken ct = default);
}
