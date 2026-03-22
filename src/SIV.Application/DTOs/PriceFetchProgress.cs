using SIV.Domain.Enums;

namespace SIV.Application.DTOs;

public record PriceFetchProgress(
    int TotalItems,
    int FetchedItems,
    int FailedItems,
    SessionStatus Status,
    string? StatusMessage);
