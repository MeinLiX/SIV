using Microsoft.AspNetCore.DataProtection;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using System.Text;
using System.Text.Json;

namespace SIV.Infrastructure.Security;

public sealed class AccountSessionStorage : IAccountSessionStorage
{
    private const int MaxAccounts = 5;

    private readonly string _manifestPath;
    private readonly string _tokensPath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AccountSessionStorage(IDataProtectionProvider dpProvider)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIV");
        _manifestPath = Path.Combine(basePath, "accounts.json");
        _tokensPath = Path.Combine(basePath, "tokens");
        Directory.CreateDirectory(_tokensPath);
        _protector = dpProvider.CreateProtector("SIV.AccountTokens");
    }

    public async Task<IReadOnlyList<SavedAccount>> GetSavedAccountsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var accounts = await LoadManifestAsync();
            return accounts.OrderByDescending(a => a.LastLogin).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAccountAsync(SavedAccount account, string refreshToken)
    {
        await _lock.WaitAsync();
        try
        {
            var accounts = await LoadManifestAsync();

            accounts.RemoveAll(a => a.AccountName.Equals(account.AccountName, StringComparison.OrdinalIgnoreCase));

            accounts.Add(account);

            if (accounts.Count > MaxAccounts)
            {
                var oldest = accounts.OrderBy(a => a.LastLogin).First();
                accounts.Remove(oldest);
                DeleteTokenFile(oldest.AccountName);
            }

            await SaveManifestAsync(accounts);
            await SaveTokenAsync(account.AccountName, refreshToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetRefreshTokenAsync(string accountName)
    {
        await _lock.WaitAsync();
        try
        {
            var path = GetTokenPath(accountName);
            if (!File.Exists(path)) return null;

            var encrypted = await File.ReadAllBytesAsync(path);
            var decrypted = _protector.Unprotect(encrypted);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAccountAsync(string accountName)
    {
        await _lock.WaitAsync();
        try
        {
            var accounts = await LoadManifestAsync();
            accounts.RemoveAll(a => a.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
            await SaveManifestAsync(accounts);
            DeleteTokenFile(accountName);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<SavedAccount>> LoadManifestAsync()
    {
        if (!File.Exists(_manifestPath))
            return [];

        var json = await File.ReadAllTextAsync(_manifestPath);
        return JsonSerializer.Deserialize<List<SavedAccount>>(json, JsonOptions) ?? [];
    }

    private async Task SaveManifestAsync(List<SavedAccount> accounts)
    {
        var json = JsonSerializer.Serialize(accounts, JsonOptions);
        await File.WriteAllTextAsync(_manifestPath, json);
    }

    private async Task SaveTokenAsync(string accountName, string refreshToken)
    {
        var encrypted = _protector.Protect(Encoding.UTF8.GetBytes(refreshToken));
        await File.WriteAllBytesAsync(GetTokenPath(accountName), encrypted);
    }

    private void DeleteTokenFile(string accountName)
    {
        var path = GetTokenPath(accountName);
        if (File.Exists(path)) File.Delete(path);
    }

    private string GetTokenPath(string accountName) =>
        Path.Combine(_tokensPath, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(accountName.ToLowerInvariant())))[..16]);
}
