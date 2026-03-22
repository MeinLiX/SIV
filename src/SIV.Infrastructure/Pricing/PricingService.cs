using Microsoft.Extensions.Logging;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.Domain.Enums;

namespace SIV.Infrastructure.Pricing;

public sealed class PricingService : IPricingService
{
    private readonly IPriceRepository _repo;
    private readonly IPriceProvider _priceProvider;
    private readonly IAuthService _auth;
    private readonly ISettingsService _settings;
    private readonly ILogger<PricingService> _logger;

    private PriceFetchSession? _currentSession;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private uint _currentAppId;

    public event Action<PriceFetchProgress>? OnProgressChanged;

    public PricingService(
        IPriceRepository repo,
        IPriceProvider priceProvider,
        IAuthService auth,
        ISettingsService settings,
        ILogger<PricingService> logger)
    {
        _repo = repo;
        _priceProvider = priceProvider;
        _auth = auth;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartPriceFetchAsync(uint appId, IReadOnlyList<string> marketHashNames, CancellationToken ct = default)
    {
        _currentAppId = appId;
        _logger.LogInformation("Starting price fetch for {Count} items, AppId={AppId}", marketHashNames.Count, appId);
        var cacheDuration = TimeSpan.FromHours(_settings.PriceCacheDurationHours);

        var session = await _repo.GetActiveSessionAsync(ct);

        if (session is not null)
        {
            var staleQueue = await _repo.GetPendingQueueItemsAsync(session.Id, int.MaxValue, ct);
            var staleNames = new HashSet<string>(staleQueue.Select(q => q.MarketHashName));
            var newNames = new HashSet<string>(marketHashNames);

            if (staleNames.SetEquals(newNames))
            {
                _currentSession = session;
                session.Status = SessionStatus.InProgress;
                session.LastUpdatedAt = DateTime.UtcNow;
                await _repo.UpdateSessionAsync(session, ct);
            }
            else
            {
                session.Status = SessionStatus.Completed;
                session.LastUpdatedAt = DateTime.UtcNow;
                await _repo.UpdateSessionAsync(session, ct);
                session = null;
            }
        }

        if (session is null)
        {
            session = new PriceFetchSession
            {
                GameAppId = (int)appId,
                SteamId = _auth.CurrentSteamId ?? string.Empty,
                TotalItems = marketHashNames.Count,
                FetchedItems = 0,
                Status = SessionStatus.InProgress,
                StartedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            };
            session = await _repo.CreateSessionAsync(session, ct);
            _currentSession = session;

            var queueItems = new List<PriceFetchQueueItem>();
            foreach (var name in marketHashNames)
            {
                var cached = await _repo.GetPriceAsync(name, ct);
                var status = cached is not null && (DateTime.UtcNow - cached.UpdatedAt) < cacheDuration
                    ? FetchItemStatus.Fetched
                    : FetchItemStatus.Pending;

                queueItems.Add(new PriceFetchQueueItem
                {
                    SessionId = session.Id,
                    MarketHashName = name,
                    Status = status
                });
            }
            await _repo.AddQueueItemsAsync(queueItems, ct);

            session.FetchedItems = queueItems.Count(q => q.Status == FetchItemStatus.Fetched);
            await _repo.UpdateSessionAsync(session, ct);
        }

        await ProcessQueueAsync(session, ct);
    }

    private async Task ProcessQueueAsync(PriceFetchSession session, CancellationToken ct)
    {
        int consecutiveFailures = 0;
        int maxFailures = _settings.PriceMaxConsecutiveFailures;
        var retryDelay = TimeSpan.FromSeconds(_settings.PriceRetryDelaySeconds);

        while (!ct.IsCancellationRequested)
        {
            var batch = await _repo.GetPendingQueueItemsAsync(session.Id, 1, ct);
            if (batch.Count == 0) break;

            var item = batch[0];
            var requestUrl = BuildRequestUrl(_currentAppId, item.MarketHashName);

            try
            {
                await EnforceRequestDelayAsync(ct);

                var results = await _priceProvider.GetPricesAsync([item.MarketHashName], ct);
                _lastRequestTime = DateTime.UtcNow;
                var result = results.FirstOrDefault();

                if (result?.PriceUSD.HasValue == true)
                {
                    await _repo.UpsertPriceAsync(new Price
                    {
                        MarketHashName = item.MarketHashName,
                        PriceUSD = result.PriceUSD.Value,
                        Source = result.Source ?? _priceProvider.SourceName,
                        RequestUrl = requestUrl,
                        UpdatedAt = DateTime.UtcNow
                    }, ct);
                    item.Status = FetchItemStatus.Fetched;
                    consecutiveFailures = 0;
                }
                else
                {
                    item.RetryCount++;
                    item.LastError = "No price returned";
                    item.Status = item.RetryCount >= _settings.PriceMaxConsecutiveFailures
                        ? FetchItemStatus.Failed : FetchItemStatus.Pending;
                    consecutiveFailures++;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rate limited during price fetch");
                item.RetryCount++;
                item.LastError = "Rate limited";
                item.Status = FetchItemStatus.Pending;
                consecutiveFailures++;
                await Task.Delay(retryDelay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching price for {Name}", item.MarketHashName);
                item.RetryCount++;
                item.LastError = ex.Message;
                item.Status = FetchItemStatus.Pending;
                consecutiveFailures++;
            }

            await _repo.UpdateQueueItemAsync(item, ct);

            session.FetchedItems = await _repo.GetQueueCountByStatusAsync(session.Id, FetchItemStatus.Fetched, ct);
            session.LastUpdatedAt = DateTime.UtcNow;
            await _repo.UpdateSessionAsync(session, ct);
            NotifyProgress(session);

            if (consecutiveFailures >= maxFailures)
            {
                _logger.LogWarning("Stopping price fetch: {Count} consecutive failures", consecutiveFailures);
                session.Status = SessionStatus.Failed;
                session.LastUpdatedAt = DateTime.UtcNow;
                await _repo.UpdateSessionAsync(session, ct);
                NotifyProgress(session, $"Stopped: {consecutiveFailures} consecutive failures");
                return;
            }

            if (consecutiveFailures > 0)
                await Task.Delay(retryDelay, ct);
        }

        var failedCount = await _repo.GetQueueCountByStatusAsync(session.Id, FetchItemStatus.Failed, ct);
        session.Status = SessionStatus.Completed;
        session.LastUpdatedAt = DateTime.UtcNow;
        await _repo.UpdateSessionAsync(session, ct);
        NotifyProgress(session, failedCount > 0 ? $"{failedCount} items failed" : null);
    }

    public async Task<PriceFetchProgress> GetProgressAsync(CancellationToken ct = default)
    {
        var session = _currentSession ?? await _repo.GetActiveSessionAsync(ct);
        if (session is null)
            return new PriceFetchProgress(0, 0, 0, SessionStatus.Completed, null);

        var failed = await _repo.GetQueueCountByStatusAsync(session.Id, FetchItemStatus.Failed, ct);
        return new PriceFetchProgress(session.TotalItems, session.FetchedItems, failed, session.Status, null);
    }

    public async Task<PriceSummaryDto> GetSummaryAsync(IReadOnlyList<InventoryGroupDto> groups, CancellationToken ct = default)
    {
        var resultGroups = new List<InventoryGroupDto>();
        decimal totalValue = 0;
        int withPrice = 0, withoutPrice = 0;
        DateTime? oldestDate = null;
        int totalItems = 0;

        foreach (var group in groups)
        {
            var price = await _repo.GetPriceAsync(group.MarketHashName, ct);
            decimal? pricePerItem = price?.PriceUSD;
            decimal? groupTotal = pricePerItem.HasValue ? pricePerItem.Value * group.Count : null;

            resultGroups.Add(group with { PricePerItem = pricePerItem, TotalPrice = groupTotal });

            if (pricePerItem.HasValue)
            {
                totalValue += groupTotal!.Value;
                withPrice += group.Count;
                if (price!.UpdatedAt < (oldestDate ?? DateTime.MaxValue))
                    oldestDate = price.UpdatedAt;
            }
            else
            {
                withoutPrice += group.Count;
            }
            totalItems += group.Count;
        }

        return new PriceSummaryDto(totalValue, totalItems, withPrice, withoutPrice, oldestDate, resultGroups);
    }

    public async Task<PriceResult?> FetchSinglePriceAsync(string marketHashName, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh)
        {
            var cacheDuration = TimeSpan.FromHours(_settings.PriceCacheDurationHours);
            var cached = await _repo.GetPriceAsync(marketHashName, ct);
            if (cached is not null && (DateTime.UtcNow - cached.UpdatedAt) < cacheDuration)
                return new PriceResult(cached.MarketHashName, cached.PriceUSD, cached.Source, cached.UpdatedAt, FromCache: true);
        }

        try
        {
            await EnforceRequestDelayAsync(ct);

            var results = await _priceProvider.GetPricesAsync([marketHashName], ct);
            _lastRequestTime = DateTime.UtcNow;
            var result = results.FirstOrDefault();

            if (result is not null && result.PriceUSD.HasValue)
            {
                var requestUrl = BuildRequestUrl(_currentAppId, marketHashName);
                var now = DateTime.UtcNow;
                await _repo.UpsertPriceAsync(new Price
                {
                    MarketHashName = marketHashName,
                    PriceUSD = result.PriceUSD.Value,
                    Source = result.Source ?? _priceProvider.SourceName,
                    RequestUrl = requestUrl,
                    UpdatedAt = now
                }, ct);
                return new PriceResult(marketHashName, result.PriceUSD, result.Source, now);
            }
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch single price for {Name}", marketHashName);
            return null;
        }
    }

    public async Task<IReadOnlyList<PriceResult>> LoadCachedPricesAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default)
    {
        var cacheDuration = TimeSpan.FromHours(_settings.PriceCacheDurationHours);
        var prices = await _repo.GetPricesByNamesAsync(marketHashNames, ct);

        return prices
            .Where(p => (DateTime.UtcNow - p.UpdatedAt) < cacheDuration)
            .Select(p => new PriceResult(p.MarketHashName, p.PriceUSD, p.Source, p.UpdatedAt, FromCache: true))
            .ToList();
    }

    private async Task EnforceRequestDelayAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(_settings.PriceRequestDelayMs);
        var elapsed = DateTime.UtcNow - _lastRequestTime;
        if (elapsed < delay)
            await Task.Delay(delay - elapsed, ct);
    }

    private static string BuildRequestUrl(uint appId, string marketHashName)
    {
        var encoded = Uri.EscapeDataString(marketHashName);
        return $"https://steamcommunity.com/market/priceoverview/?appid={appId}&currency=1&market_hash_name={encoded}";
    }

    private void NotifyProgress(PriceFetchSession session, string? message = null)
    {
        OnProgressChanged?.Invoke(new PriceFetchProgress(
            session.TotalItems, session.FetchedItems, 0, session.Status, message));
    }
}
