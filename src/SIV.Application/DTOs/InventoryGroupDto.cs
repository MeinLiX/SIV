namespace SIV.Application.DTOs;

public record InventoryGroupDto(string MarketHashName, int Count, decimal? PricePerItem, decimal? TotalPrice);
