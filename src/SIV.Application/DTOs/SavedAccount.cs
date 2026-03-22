namespace SIV.Application.DTOs;

public sealed record SavedAccount(
    string AccountName,
    string SteamId,
    string DisplayName,
    string AvatarUrl,
    DateTime LastLogin
);
