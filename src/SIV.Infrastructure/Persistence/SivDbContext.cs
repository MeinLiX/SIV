using Microsoft.EntityFrameworkCore;
using SIV.Domain.Entities;

namespace SIV.Infrastructure.Persistence;

public sealed class SivDbContext : DbContext
{
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<PriceFetchSession> PriceFetchSessions => Set<PriceFetchSession>();
    public DbSet<PriceFetchQueueItem> PriceFetchQueue => Set<PriceFetchQueueItem>();

    public SivDbContext(DbContextOptions<SivDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Price>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.MarketHashName).IsUnique();
            e.Property(p => p.PriceUSD).HasColumnType("REAL");
            e.Property(p => p.RequestUrl).HasDefaultValue(string.Empty);
        });

        modelBuilder.Entity<PriceFetchSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Status).HasConversion<string>();
        });

        modelBuilder.Entity<PriceFetchQueueItem>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.Status).HasConversion<string>();
            e.HasIndex(q => new { q.SessionId, q.Status });
        });
    }
}
