namespace SIV.Application.DTOs;

public record PriceSummaryDto(
    decimal TotalValue,
    int TotalItemCount,
    int ItemsWithPrice,
    int ItemsWithoutPrice,
    DateTime? OldestPriceDate,
    IReadOnlyList<InventoryGroupDto> Groups);
