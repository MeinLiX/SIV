using SIV.Application.DTOs;

namespace SIV.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, string? twoFactorCode, CancellationToken ct = default);
    Task<AuthResult> LoginViaQrAsync(Action<string> onChallengeUrlChanged, CancellationToken ct = default);
    Task<AuthResult> RestoreAccountSessionAsync(string accountName, CancellationToken ct = default);
    Task LogoutAsync();
    Task<IReadOnlyList<SavedAccount>> GetSavedAccountsAsync();
    Task RemoveAccountAsync(string accountName);
    Task<SteamPlayerProfile?> FetchProfileAsync(CancellationToken ct = default);
    bool IsLoggedIn { get; }
    string? CurrentSteamId { get; }
}
