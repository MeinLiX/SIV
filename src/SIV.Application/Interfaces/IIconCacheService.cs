namespace SIV.Application.Interfaces;

public interface IIconCacheService
{
    bool IsEnabled { get; }
    string? GetCachedPath(string httpUrl);
    void QueueForCaching(string httpUrl);
}
