namespace SIV.Application.DTOs;

public record PriceResult(string MarketHashName, decimal? PriceUSD, string? Source, DateTime? UpdatedAt = null, bool FromCache = false);
