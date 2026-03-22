using SIV.Application.DTOs;

namespace SIV.Application.Interfaces;

public interface IAccountSessionStorage
{
    Task<IReadOnlyList<SavedAccount>> GetSavedAccountsAsync();
    Task SaveAccountAsync(SavedAccount account, string refreshToken);
    Task<string?> GetRefreshTokenAsync(string accountName);
    Task RemoveAccountAsync(string accountName);
}
