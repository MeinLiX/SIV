using Microsoft.EntityFrameworkCore;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.Domain.Enums;

namespace SIV.Infrastructure.Persistence;

public sealed class PriceRepository : IPriceRepository
{
    private readonly IDbContextFactory<SivDbContext> _dbFactory;

    public PriceRepository(IDbContextFactory<SivDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Price?> GetPriceAsync(string marketHashName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Prices.FirstOrDefaultAsync(p => p.MarketHashName == marketHashName, ct);
    }

    public async Task<IReadOnlyList<Price>> GetPricesByNamesAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Prices
            .Where(p => marketHashNames.Contains(p.MarketHashName))
            .ToListAsync(ct);
    }

    public async Task UpsertPriceAsync(Price price, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Prices.FirstOrDefaultAsync(p => p.MarketHashName == price.MarketHashName, ct);
        if (existing is not null)
        {
            existing.PriceUSD = price.PriceUSD;
            existing.Source = price.Source;
            existing.UpdatedAt = price.UpdatedAt;
        }
        else
        {
            db.Prices.Add(price);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<PriceFetchSession?> GetActiveSessionAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PriceFetchSessions
            .Where(s => s.Status == SessionStatus.InProgress || s.Status == SessionStatus.Paused)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PriceFetchSession> CreateSessionAsync(PriceFetchSession session, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PriceFetchSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task UpdateSessionAsync(PriceFetchSession session, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PriceFetchSessions.Update(session);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddQueueItemsAsync(IEnumerable<PriceFetchQueueItem> items, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PriceFetchQueue.AddRange(items);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PriceFetchQueueItem>> GetPendingQueueItemsAsync(int sessionId, int batchSize, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PriceFetchQueue
            .Where(q => q.SessionId == sessionId && q.Status == FetchItemStatus.Pending)
            .OrderBy(q => q.Id)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task UpdateQueueItemAsync(PriceFetchQueueItem item, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.PriceFetchQueue.Update(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> GetQueueCountByStatusAsync(int sessionId, FetchItemStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PriceFetchQueue.CountAsync(q => q.SessionId == sessionId && q.Status == status, ct);
    }
}
