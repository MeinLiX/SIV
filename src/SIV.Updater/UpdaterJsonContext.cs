using System.Text.Json.Serialization;

namespace SIV.Updater;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(List<ReleaseManifestFile>))]
internal sealed partial class UpdaterJsonContext : JsonSerializerContext
{
}
