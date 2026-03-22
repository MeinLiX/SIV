using Microsoft.Extensions.Logging;
using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace SIV.Infrastructure.Update;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string DefaultRepoUrl = "https://github.com/MeinLiX/SIV";

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILogger<GitHubUpdateService> _logger;

    public GitHubUpdateService(HttpClient http, ISettingsService settings, ILogger<GitHubUpdateService> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SIV-UpdateChecker/1.0");
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            _logger.LogInformation("Checking for updates. Current version: {Version}", currentVersion);

            var repoUrl = string.IsNullOrWhiteSpace(_settings.UpdateRepoUrl)
                ? DefaultRepoUrl
                : _settings.UpdateRepoUrl;

            var uri = new Uri(repoUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2)
            {
                _logger.LogWarning("Invalid repo URL: {Url}", repoUrl);
                return null;
            }

            var apiUrl = $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
            var releaseNotes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? ""
                : "";

            if (!Version.TryParse(tagName, out var remoteVersion) ||
                !Version.TryParse(currentVersion, out var localVersion))
            {
                _logger.LogWarning("Could not parse versions. Remote: {Remote}, Local: {Local}", tagName, currentVersion);
                return null;
            }

            if (remoteVersion <= localVersion)
            {
                _logger.LogInformation("App is up to date (remote: {Remote})", tagName);
                return null;
            }

            _logger.LogInformation("Update available: {Remote} > {Local}", tagName, currentVersion);

            var isSelfContained = File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));
            var assetSuffix = isSelfContained ? "self-contained" : "framework-dependent";

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Contains(assetSuffix, StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (downloadUrl is null)
            {
                _logger.LogWarning("No matching asset found for deployment type: {Type}", assetSuffix);
                return null;
            }

            var expectedHash = await GetExpectedHashAsync(assets, assetSuffix, ct);

            return new UpdateInfo(tagName, currentVersion, downloadUrl, expectedHash, releaseNotes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
            return null;
        }
    }

    public void LaunchUpdater(UpdateInfo update)
    {
        var updaterPath = Path.Combine(AppContext.BaseDirectory, "updater", "SIV.Updater.exe");
        if (!File.Exists(updaterPath))
        {
            _logger.LogError("Updater not found at {Path}", updaterPath);
            return;
        }

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pid = Environment.ProcessId;

        _logger.LogInformation("Launching updater for version {Version}", update.NewVersion);

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = $"--app-pid {pid} --app-dir \"{appDir}\" --download-url \"{update.DownloadUrl}\" --expected-hash \"{update.ExpectedHash}\"",
            UseShellExecute = false
        });
    }

    private async Task<string> GetExpectedHashAsync(JsonElement assets, string assetSuffix, CancellationToken ct)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            var sumsUrl = asset.GetProperty("browser_download_url").GetString();
            if (sumsUrl is null) break;

            var sums = await _http.GetStringAsync(sumsUrl, ct);
            foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains(assetSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1) return parts[0];
                }
            }
            break;
        }

        _logger.LogWarning("SHA256SUMS.txt not found or no matching hash for {Type}", assetSuffix);
        return string.Empty;
    }

    private static string GetCurrentVersion()
    {
        return Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]
            ?? "0.0.0";
    }
}
