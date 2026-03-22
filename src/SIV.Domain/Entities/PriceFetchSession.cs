using SIV.Domain.Enums;

namespace SIV.Domain.Entities;

public class PriceFetchSession
{
    public int Id { get; set; }
    public int GameAppId { get; set; }
    public string SteamId { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int FetchedItems { get; set; }
    public SessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
