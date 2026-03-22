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
    private const string SupportedRuntimeIdentifier = "win-x64";

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
            if (segments.Length >= 2 && segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                segments[1] = segments[1][..^4];
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
            var preferredDeploymentType = isSelfContained ? "self-contained" : "framework-dependent";

            if (!root.TryGetProperty("assets", out var assets))
            {
                _logger.LogWarning("Release {Version} does not contain any assets", tagName);
                return null;
            }

            var selectedAsset = SelectReleaseAsset(assets, preferredDeploymentType);
            if (selectedAsset is null)
            {
                _logger.LogWarning(
                    "No matching asset found for runtime {RuntimeIdentifier} and preferred deployment {DeploymentType}",
                    SupportedRuntimeIdentifier,
                    preferredDeploymentType);
                return null;
            }

            var downloadUrl = selectedAsset.Value.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                _logger.LogWarning("Selected release asset is missing browser_download_url");
                return null;
            }

            var assetName = selectedAsset.Value.GetProperty("name").GetString() ?? "<unknown>";
            _logger.LogInformation("Selected update asset: {AssetName}", assetName);

            var expectedHash = GetExpectedHash(selectedAsset.Value);

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

        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appDir = Path.GetFileName(baseDir).Equals("runtime", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(baseDir)!
            : baseDir;
        var pid = Environment.ProcessId;

        _logger.LogInformation("Launching updater for version {Version}", update.NewVersion);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--app-pid");
        startInfo.ArgumentList.Add(pid.ToString());
        startInfo.ArgumentList.Add("--app-dir");
        startInfo.ArgumentList.Add(appDir);
        startInfo.ArgumentList.Add("--download-url");
        startInfo.ArgumentList.Add(update.DownloadUrl);

        if (!string.IsNullOrWhiteSpace(update.ExpectedHash))
        {
            startInfo.ArgumentList.Add("--expected-hash");
            startInfo.ArgumentList.Add(update.ExpectedHash);
        }

        Process.Start(startInfo);
    }

    private JsonElement? SelectReleaseAsset(JsonElement assets, string preferredDeploymentType)
    {
        var candidates = assets
            .EnumerateArray()
            .Where(asset =>
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains(SupportedRuntimeIdentifier, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (candidates.Count == 0)
            return null;

        var preferred = candidates.FirstOrDefault(asset =>
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            return name.Contains(preferredDeploymentType, StringComparison.OrdinalIgnoreCase);
        });

        if (preferred.ValueKind != JsonValueKind.Undefined)
            return preferred;

        var selfContainedFallback = candidates.FirstOrDefault(asset =>
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            return name.Contains("self-contained", StringComparison.OrdinalIgnoreCase);
        });

        if (selfContainedFallback.ValueKind != JsonValueKind.Undefined)
        {
            _logger.LogInformation(
                "Preferred deployment type {DeploymentType} is not available. Falling back to self-contained x64 asset.",
                preferredDeploymentType);
            return selfContainedFallback;
        }

        return candidates[0];
    }

    private string GetExpectedHash(JsonElement asset)
    {
        if (asset.TryGetProperty("digest", out var digest))
        {
            var hash = digest.GetString() ?? "";
            const string prefix = "sha256:";
            return hash.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? hash[prefix.Length..]
                : hash;
        }

        var assetName = asset.GetProperty("name").GetString() ?? "<unknown>";
        _logger.LogWarning("No digest found in release asset {AssetName}", assetName);
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
