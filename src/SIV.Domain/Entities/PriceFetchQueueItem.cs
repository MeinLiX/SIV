using SIV.Domain.Enums;

namespace SIV.Domain.Entities;

public class PriceFetchQueueItem
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string MarketHashName { get; set; } = string.Empty;
    public FetchItemStatus Status { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
