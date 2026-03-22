namespace SIV.Application.Interfaces;

public interface ISessionStorage
{
    Task SaveAsync(string key, byte[] data);
    Task<byte[]?> LoadAsync(string key);
    Task DeleteAsync(string key);
}
