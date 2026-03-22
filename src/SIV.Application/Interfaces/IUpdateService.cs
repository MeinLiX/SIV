using SIV.Application.DTOs;

namespace SIV.Application.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    void LaunchUpdater(UpdateInfo update);
}
