using System.Text.RegularExpressions;

namespace SIV.Infrastructure.Steam.GC;

internal sealed record CaseItemInfo(string LocKey, string IconPath, int SupplyCrateSeries);
internal sealed record ItemSetEntry(string LocKey, List<string> SkinKeys);

internal sealed class CS2ItemsGameSchema
{
    public Dictionary<int, string> PaintKits { get; } = [];
    public Dictionary<int, string> StickerKits { get; } = [];
    public Dictionary<int, string> Keychains { get; } = [];
    public Dictionary<int, string> Items { get; } = [];
    public Dictionary<int, string> ItemImagePaths { get; } = [];
    public Dictionary<string, string> RarityColors { get; } = [];
    public Dictionary<int, string> QualityColors { get; } = [];

    public Dictionary<int, string> PaintKitInternalNames { get; } = [];
    public Dictionary<int, string> StickerKitInternalNames { get; } = [];
    public Dictionary<int, string> StickerKitMaterials { get; } = [];
    public Dictionary<int, string> KeychainImagePaths { get; } = [];
    public Dictionary<string, List<string>> ClientLootLists { get; } = [];
    public Dictionary<int, string> RevolvingLootLists { get; } = [];
    public Dictionary<int, CaseItemInfo> CaseItems { get; } = [];
    public Dictionary<string, ItemSetEntry> ItemSets { get; } = [];

    private Dictionary<int, string> PaintKitLocKeys { get; } = [];
    private Dictionary<int, string> StickerKitLocKeys { get; } = [];
    private Dictionary<int, string> KeychainLocKeys { get; } = [];
    private Dictionary<int, string> ItemLocKeys { get; } = [];

    private static readonly Regex QuotedTokenRegex = new("\"([^\"]*)\"", RegexOptions.Compiled);

    public static CS2ItemsGameSchema Load(string path, string? cs2GamePath = null)
    {
        var schema = new CS2ItemsGameSchema();
        using var reader = File.OpenText(path);

        var stack = new List<string>();
        string? pendingKey = null;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed == "{")
            {
                if (!string.IsNullOrEmpty(pendingKey))
                {
                    stack.Add(pendingKey);
                    pendingKey = null;
                }

                continue;
            }

            if (trimmed == "}")
            {
                pendingKey = null;
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            var matches = QuotedTokenRegex.Matches(trimmed);
            if (matches.Count == 1)
            {
                pendingKey = matches[0].Groups[1].Value;
                continue;
            }

            if (matches.Count < 2)
            {
                continue;
            }

            var key = matches[0].Groups[1].Value;
            var value = matches[1].Groups[1].Value;

            if (stack.Count < 2 || !string.Equals(stack[0], "items_game", StringComparison.Ordinal))
            {
                continue;
            }

            if (stack.Count == 3 && stack[1] == "colors" && key == "hex_color")
            {
                schema.RarityColors[stack[2]] = value;
                continue;
            }

            if (stack.Count == 3 && stack[1] == "qualities")
            {
                if (key == "value" && int.TryParse(value, out var qualVal))
                {
                }
                else if (key == "hexColor")
                {
                }
            }

            if (stack.Count == 3 && stack[1] == "items" && int.TryParse(stack[2], out var itemDefIndex))
            {
                if (key == "item_name")
                {
                    schema.ItemLocKeys[itemDefIndex] = value;
                    schema.Items[itemDefIndex] = HumanizeSchemaName(value);
                }
                else if (key == "image_inventory")
                {
                    schema.ItemImagePaths[itemDefIndex] = value;
                }

            }

            if (stack.Count != 3)
            {
                continue;
            }

            if (!int.TryParse(stack[2], out var id))
            {
                continue;
            }

            switch (stack[1])
            {
                case "paint_kits":
                    if (key is "description_tag")
                    {
                        schema.PaintKitLocKeys[id] = value.TrimStart('#');
                        schema.PaintKits[id] = SelectBetterName(schema.PaintKits.GetValueOrDefault(id), value);
                    }
                    else if (key is "name")
                    {
                        schema.PaintKits.TryAdd(id, HumanizeSchemaName(value));
                        schema.PaintKitInternalNames.TryAdd(id, value);
                    }
                    else if (key is "description_string")
                    {
                        schema.PaintKits.TryAdd(id, HumanizeSchemaName(value));
                    }
                    break;
                case "sticker_kits":
                    if (key is "item_name")
                    {
                        schema.StickerKitLocKeys[id] = value.TrimStart('#');
                        schema.StickerKits[id] = SelectBetterName(schema.StickerKits.GetValueOrDefault(id), value);
                    }
                    else if (key is "name")
                    {
                        schema.StickerKits.TryAdd(id, HumanizeSchemaName(value));
                        schema.StickerKitInternalNames.TryAdd(id, value);
                    }
                    else if (key is "sticker_material")
                    {
                        schema.StickerKitMaterials.TryAdd(id, value);
                    }
                    break;
                case "keychain_definitions":
                    if (key is "loc_name")
                    {
                        schema.KeychainLocKeys[id] = value.TrimStart('#');
                        schema.Keychains[id] = SelectBetterName(schema.Keychains.GetValueOrDefault(id), value);
                    }
                    else if (key is "name")
                    {
                        schema.Keychains.TryAdd(id, HumanizeSchemaName(value));
                    }
                    else if (key is "image_inventory")
                    {
                        schema.KeychainImagePaths.TryAdd(id, value);
                    }
                    break;
            }
        }

        ParseQualityColors(path, schema);
        ParseOriginData(path, schema);

        var localization = LoadLocalization(cs2GamePath);
        if (localization.Count > 0)
        {
            ApplyLocalization(schema, localization);
            ApplyOriginLocalization(schema, localization);
        }

        return schema;
    }

    private static void ParseQualityColors(string path, CS2ItemsGameSchema schema)
    {
        using var reader = File.OpenText(path);
        var stack = new List<string>();
        string? pendingKey = null;
        int currentQualityValue = -1;
        string? currentQualityHex = null;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (trimmed == "{")
            {
                if (!string.IsNullOrEmpty(pendingKey))
                {
                    stack.Add(pendingKey);
                    pendingKey = null;
                }
                continue;
            }

            if (trimmed == "}")
            {
                if (stack.Count == 3 && stack[1] == "qualities" && currentQualityValue >= 0 && currentQualityHex is not null)
                {
                    schema.QualityColors[currentQualityValue] = currentQualityHex;
                }
                currentQualityValue = -1;
                currentQualityHex = null;

                pendingKey = null;
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }

            var matches = QuotedTokenRegex.Matches(trimmed);
            if (matches.Count == 1)
            {
                pendingKey = matches[0].Groups[1].Value;
                continue;
            }

            if (matches.Count < 2)
                continue;

            var key = matches[0].Groups[1].Value;
            var value = matches[1].Groups[1].Value;

            if (stack.Count == 3 && stack[0] == "items_game" && stack[1] == "qualities")
            {
                if (key == "value" && int.TryParse(value, out var v))
                    currentQualityValue = v;
                else if (key == "hexColor")
                    currentQualityHex = value;
            }
        }
    }

    private static void ParseOriginData(string path, CS2ItemsGameSchema schema)
    {
        using var reader = File.OpenText(path);
        var stack = new List<string>();
        string? pendingKey = null;

        var currentItemDefIndex = -1;
        string? currentItemLocKey = null;
        string? currentItemName = null;
        string? currentItemIcon = null;
        string? currentItemPrefab = null;
        int currentItemSeries = -1;
        string? currentItemSetTag = null;

        var currentSetName = "";
        string? currentSetLocKey = null;
        var currentSetItems = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (trimmed == "{")
            {
                if (!string.IsNullOrEmpty(pendingKey))
                {
                    stack.Add(pendingKey);
                    pendingKey = null;
                }
                continue;
            }

            if (trimmed == "}")
            {
                if (stack.Count == 3 && stack[1] == "items" && currentItemDefIndex >= 0)
                {
                    if (currentItemSeries >= 0 &&
                        (currentItemPrefab is "weapon_case" or "weapon_case_base" or "weapon_case_souvenirpkg"
                            or "weapon_case_selfopening_collection" || currentItemName?.StartsWith("crate_", StringComparison.Ordinal) == true))
                    {
                        var locKey = currentItemLocKey ?? currentItemName ?? $"Case #{currentItemDefIndex}";
                        var icon = currentItemIcon ?? "econ/weapon_cases/weapon_case_generic";
                        schema.CaseItems.TryAdd(currentItemDefIndex, new CaseItemInfo(locKey, icon, currentItemSeries));
                    }
                    currentItemDefIndex = -1;
                    currentItemLocKey = null;
                    currentItemName = null;
                    currentItemIcon = null;
                    currentItemPrefab = null;
                    currentItemSeries = -1;
                    currentItemSetTag = null;
                }

                if (stack.Count == 3 && stack[1] == "item_sets" && !string.IsNullOrEmpty(currentSetName))
                {
                    if (currentSetLocKey is not null || currentSetItems.Count > 0)
                    {
                        schema.ItemSets.TryAdd(currentSetName, new ItemSetEntry(
                            currentSetLocKey ?? currentSetName,
                            [.. currentSetItems]));
                    }
                    currentSetName = "";
                    currentSetLocKey = null;
                    currentSetItems.Clear();
                }

                pendingKey = null;
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }

            var matches = QuotedTokenRegex.Matches(trimmed);
            if (matches.Count == 1)
            {
                pendingKey = matches[0].Groups[1].Value;

                if (stack.Count == 2 && stack[1] == "items" && int.TryParse(pendingKey, out var di))
                    currentItemDefIndex = di;
                else if (stack.Count == 2 && stack[1] == "item_sets")
                    currentSetName = pendingKey;
                continue;
            }

            if (matches.Count < 2)
                continue;

            var key = matches[0].Groups[1].Value;
            var value = matches[1].Groups[1].Value;

            if (stack.Count < 2 || stack[0] != "items_game")
                continue;

            if (stack.Count == 2 && stack[1] == "revolving_loot_lists" && int.TryParse(key, out var series))
            {
                schema.RevolvingLootLists.TryAdd(series, value);
                continue;
            }

            if (stack.Count == 3 && stack[1] == "client_loot_lists")
            {
                var listName = stack[2];
                if (!schema.ClientLootLists.TryGetValue(listName, out var entries))
                {
                    entries = [];
                    schema.ClientLootLists[listName] = entries;
                }
                entries.Add(key);
                continue;
            }

            if (stack.Count == 3 && stack[1] == "items" && currentItemDefIndex >= 0)
            {
                if (key == "item_name") currentItemLocKey = value;
                else if (key == "name") currentItemName = value;
                else if (key == "image_inventory") currentItemIcon = value;
                else if (key == "prefab") currentItemPrefab = value;
                continue;
            }

            if (stack.Count >= 4 && stack[1] == "items" && currentItemDefIndex >= 0)
            {
                if (key == "value" && stack[^1] == "set supply crate series" && int.TryParse(value, out var sv))
                {
                    currentItemSeries = sv;
                }
                if (key == "tag_value" && stack[^1] == "ItemSet")
                {
                    currentItemSetTag = value;
                }
                continue;
            }

            if (stack.Count == 3 && stack[1] == "item_sets" && !string.IsNullOrEmpty(currentSetName))
            {
                if (key == "name") currentSetLocKey = value;
                continue;
            }

            if (stack.Count == 4 && stack[1] == "item_sets" && stack[3] == "items")
            {
                currentSetItems.Add(key);
                continue;
            }
        }
    }

    private static void ApplyOriginLocalization(CS2ItemsGameSchema schema, Dictionary<string, string> localization)
    {
        foreach (var (defIndex, caseInfo) in schema.CaseItems)
        {
            var locKey = caseInfo.LocKey.TrimStart('#');
            if (localization.TryGetValue(locKey, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                schema.CaseItems[defIndex] = caseInfo with { LocKey = name };
            }
        }

        foreach (var (setName, setEntry) in schema.ItemSets)
        {
            var locKey = setEntry.LocKey.TrimStart('#');
            if (localization.TryGetValue(locKey, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                schema.ItemSets[setName] = setEntry with { LocKey = name };
            }
        }
    }

    private static Dictionary<string, string> LoadLocalization(string? cs2GamePath)
    {
        var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var locPath = ResolveLocalFile(cs2GamePath, "csgo_english.txt",
            ["game/csgo/resource", "csgo/resource", "resource"]);

        locPath ??= DownloadAndCache("csgo_english.txt",
            "https://raw.githubusercontent.com/SteamDatabase/GameTracking-CS2/refs/heads/master/game/csgo/pak01_dir/resource/csgo_english.txt");

        if (locPath is null)
            return localization;

        using var reader = File.OpenText(locPath);
        var inTokens = false;
        var depth = 0;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (trimmed == "{")
            {
                depth++;
                continue;
            }

            if (trimmed == "}")
            {
                if (inTokens) inTokens = false;
                depth--;
                continue;
            }

            var matches = QuotedTokenRegex.Matches(trimmed);
            if (matches.Count == 1)
            {
                var val = matches[0].Groups[1].Value;
                if (string.Equals(val, "Tokens", StringComparison.OrdinalIgnoreCase))
                    inTokens = true;
                continue;
            }

            if (matches.Count >= 2 && inTokens)
            {
                var key = matches[0].Groups[1].Value;
                var value = matches[1].Groups[1].Value;
                localization[key] = value;
            }
        }

        return localization;
    }

    private static void ApplyLocalization(CS2ItemsGameSchema schema, Dictionary<string, string> localization)
    {
        foreach (var (id, locKey) in schema.PaintKitLocKeys)
        {
            if (localization.TryGetValue(locKey, out var name) && !string.IsNullOrWhiteSpace(name))
                schema.PaintKits[id] = name;
        }

        foreach (var (id, locKey) in schema.StickerKitLocKeys)
        {
            if (localization.TryGetValue(locKey, out var name) && !string.IsNullOrWhiteSpace(name))
                schema.StickerKits[id] = name;
        }

        foreach (var (id, locKey) in schema.KeychainLocKeys)
        {
            if (localization.TryGetValue(locKey, out var name) && !string.IsNullOrWhiteSpace(name))
                schema.Keychains[id] = name;
        }

        foreach (var (id, locKey) in schema.ItemLocKeys)
        {
            var key = locKey.TrimStart('#');
            if (localization.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name))
                schema.Items[id] = name;
        }
    }

    private static string SelectBetterName(string? existing, string candidate)
    {
        var normalized = HumanizeSchemaName(candidate);
        if (string.IsNullOrWhiteSpace(existing))
        {
            return normalized;
        }

        if (candidate.StartsWith('#') && !existing.Contains(' ', StringComparison.Ordinal))
        {
            return normalized;
        }

        return existing;
    }

    private static string HumanizeSchemaName(string value)
    {
        var normalized = value.Trim().TrimStart('#');
        normalized = normalized
            .Replace("PaintKit_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("StickerKit_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("keychain_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_Tag", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_desc", string.Empty, StringComparison.OrdinalIgnoreCase);

        var segments = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 1 && segments[0].Length <= 3 && segments[0].All(char.IsLetter))
        {
            segments = segments[1..];
        }

        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = HumanizeWord(segments[i]);
        }

        return string.Join(' ', segments);
    }

    private static string HumanizeWord(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "ak47" => "AK-47",
            "m4a1s" => "M4A1-S",
            "m4a4" => "M4A4",
            "m4a1" => "M4A4",
            "sg553" => "SG 553",
            "g3sg1" => "G3SG1",
            "ssg08" => "SSG 08",
            "cz75a" => "CZ75-Auto",
            "mp5sd" => "MP5-SD",
            "p250" => "P250",
            "p2000" => "P2000",
            "usp" => "USP",
            "usps" => "USP-S",
            "xm1014" => "XM1014",
            "awp" => "AWP",
            "aug" => "AUG",
            "nova" => "Nova",
            "bizon" => "PP-Bizon",
            "mac10" => "MAC-10",
            "ump45" => "UMP-45",
            "mp7" => "MP7",
            "mp9" => "MP9",
            "tec9" => "Tec-9",
            "fiveseven" => "Five-SeveN",
            "deagle" => "Desert Eagle",
            "hkp2000" => "P2000",
            "galilar" => "Galil AR",
            "famas" => "FAMAS",
            "glock18" => "Glock-18",
            "dualberretta" => "Dual Berettas",
            "elite" => "Dual Berettas",
            _ => char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant()
        };
    }

    private static string? ResolveLocalFile(string? basePath, string fileName, string[] subPaths)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return null;

        foreach (var sub in subPaths)
        {
            var candidate = Path.Combine(basePath, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIV", "cache");

    private static string? DownloadAndCache(string fileName, string url)
    {
        var cachedPath = Path.Combine(CacheDir, fileName);
        var maxAge = TimeSpan.FromHours(24);

        if (File.Exists(cachedPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath)) < maxAge)
        {
            var firstLine = File.ReadLines(cachedPath).FirstOrDefault() ?? "";
            if (!firstLine.TrimStart().StartsWith('<'))
                return cachedPath;
        }

        try
        {
            Directory.CreateDirectory(CacheDir);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var data = http.GetStringAsync(url).GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(data) && !data.TrimStart().StartsWith('<'))
            {
                File.WriteAllText(cachedPath, data);
                return cachedPath;
            }
        }
        catch
        {
        }

        if (File.Exists(cachedPath))
        {
            var firstLine = File.ReadLines(cachedPath).FirstOrDefault() ?? "";
            if (!firstLine.TrimStart().StartsWith('<'))
                return cachedPath;
        }

        return null;
    }
}
