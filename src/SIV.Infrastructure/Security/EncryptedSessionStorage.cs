using Microsoft.AspNetCore.DataProtection;
using SIV.Application.Interfaces;

namespace SIV.Infrastructure.Security;

public sealed class EncryptedSessionStorage : ISessionStorage
{
    private readonly string _basePath;
    private readonly IDataProtector _protector;

    public EncryptedSessionStorage(IDataProtectionProvider dpProvider)
    {
        _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIV", "session");
        Directory.CreateDirectory(_basePath);
        _protector = dpProvider.CreateProtector("SIV.Session");
    }

    public async Task SaveAsync(string key, byte[] data)
    {
        var encrypted = _protector.Protect(data);
        await File.WriteAllBytesAsync(GetPath(key), encrypted);
    }

    public async Task<byte[]?> LoadAsync(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return null;
        var encrypted = await File.ReadAllBytesAsync(path);
        return _protector.Unprotect(encrypted);
    }

    public Task DeleteAsync(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string key) =>
        Path.Combine(_basePath, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key)))[..16]);
}
