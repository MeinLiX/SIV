using System.Text.Json.Serialization;

namespace SIV.Updater;

internal sealed class ReleaseManifest
{
    [JsonPropertyName("format")]
    public int Format { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("runtimeIdentifier")]
    public string RuntimeIdentifier { get; init; } = string.Empty;

    [JsonPropertyName("deploymentType")]
    public string DeploymentType { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public string GeneratedAtUtc { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public List<ReleaseManifestFile> Files { get; init; } = [];
}

internal sealed class ReleaseManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
}
