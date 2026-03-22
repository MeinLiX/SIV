using Microsoft.Extensions.Logging;
using SIV.Application.Interfaces;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace SIV.Infrastructure.Cache;

public sealed class IconCacheService : IIconCacheService
{
    private readonly string _cacheDir;
    private readonly ISettingsService _settings;
    private readonly ILogger<IconCacheService> _logger;
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    public bool IsEnabled => _settings.EnableIconCache;

    public IconCacheService(string appDataPath, ISettingsService settings, ILogger<IconCacheService> logger)
    {
        _cacheDir = Path.Combine(appDataPath, "cache_assets");
        _settings = settings;
        _logger = logger;
        _ = ProcessDownloadsAsync();
    }

    public string? GetCachedPath(string httpUrl)
    {
        if (!IsEnabled
            || string.IsNullOrEmpty(httpUrl)
            || !httpUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        var filePath = GetFilePath(httpUrl);
        return File.Exists(filePath) ? filePath : null;
    }

    public void QueueForCaching(string httpUrl)
    {
        if (!IsEnabled
            || string.IsNullOrEmpty(httpUrl)
            || !httpUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_queued.TryAdd(httpUrl, 0))
            return;

        _channel.Writer.TryWrite(httpUrl);
    }

    private async Task ProcessDownloadsAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        await foreach (var url in _channel.Reader.ReadAllAsync())
        {
            var filePath = GetFilePath(url);
            if (File.Exists(filePath))
                continue;

            try
            {
                var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                if (bytes.Length == 0)
                    continue;

                Directory.CreateDirectory(_cacheDir);
                await File.WriteAllBytesAsync(filePath, bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _queued.TryRemove(url, out _);
                _logger.LogDebug(ex, "Failed to cache icon: {Url}", url);
            }
        }
    }

    private string GetFilePath(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Path.Combine(_cacheDir, $"{Convert.ToHexStringLower(hash)}.png");
    }
}
