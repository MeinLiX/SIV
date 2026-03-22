namespace SIV.Domain.Entities;

public class Price
{
    public int Id { get; set; }
    public string MarketHashName { get; set; } = string.Empty;
    public decimal PriceUSD { get; set; }
    public string Source { get; set; } = string.Empty;
    public string RequestUrl { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
