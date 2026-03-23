using Microsoft.Extensions.Logging;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;

namespace SIV.Infrastructure.Steam.GC;

public sealed class CS2ItemDefinitionProvider : IItemDefinitionProvider
{
    public uint AppId => CS2GameDefinition.CS2AppId;
    private readonly Lazy<CS2ItemsGameSchema> _schema;
    private readonly Lazy<Dictionary<int, IReadOnlyList<ContainerDropInfo>>> _containerDropMap;
    private readonly Dictionary<string, string> _iconHashCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<int, string> RarityColorKeys = new()
    {
        [0] = "desc_default",     // Default
        [1] = "desc_common",      // Consumer Grade
        [2] = "desc_uncommon",    // Industrial Grade
        [3] = "desc_rare",        // Mil-Spec
        [4] = "desc_mythical",    // Restricted
        [5] = "desc_legendary",   // Classified
        [6] = "desc_ancient",     // Covert
        [7] = "desc_immortal",    // Contraband
        [99] = "desc_unusual",    // Special (★)
    };

    private static readonly Dictionary<string, string> FallbackRarityColors = new()
    {
        ["desc_default"] = "#ded6cc",
        ["desc_common"] = "#b0c3d9",
        ["desc_uncommon"] = "#5e98d9",
        ["desc_rare"] = "#4b69ff",
        ["desc_mythical"] = "#8847ff",
        ["desc_legendary"] = "#d32ce6",
        ["desc_ancient"] = "#eb4b4b",
        ["desc_immortal"] = "#e4ae39",
        ["desc_unusual"] = "#ffd700",
        ["desc_strange"] = "#CF6A32",
    };

    private static readonly Dictionary<int, string> FallbackQualityColors = new()
    {
        [0] = "#B2B2B2",   // normal
        [1] = "#4D7455",   // genuine
        [2] = "#476291",   // vintage
        [3] = "#8650AC",   // unusual
        [4] = "#D2D2D2",   // unique
        [5] = "#70B04A",   // community
        [6] = "#A50F79",   // developer
        [7] = "#70B04A",   // selfmade
        [8] = "#FFEEEE",   // customized
        [9] = "#CF6A32",   // strange
        [10] = "#8650AC",  // completed
        [11] = "#8650AC",  // haunted
        [12] = "#FFD700",  // tournament
    };

    private static readonly Dictionary<uint, string> WeaponNames = new()
    {
        [1] = "Desert Eagle",
        [2] = "Dual Berettas",
        [3] = "Five-SeveN",
        [4] = "Glock-18",
        [7] = "AK-47",
        [8] = "AUG",
        [9] = "AWP",
        [10] = "FAMAS",
        [11] = "G3SG1",
        [13] = "Galil AR",
        [14] = "M249",
        [16] = "M4A4",
        [17] = "MAC-10",
        [19] = "P90",
        [23] = "MP5-SD",
        [24] = "UMP-45",
        [25] = "XM1014",
        [26] = "PP-Bizon",
        [27] = "MAG-7",
        [28] = "Negev",
        [29] = "Sawed-Off",
        [30] = "Tec-9",
        [31] = "Zeus x27",
        [32] = "P2000",
        [33] = "MP7",
        [34] = "MP9",
        [35] = "Nova",
        [36] = "P250",
        [38] = "SCAR-20",
        [39] = "SG 553",
        [40] = "SSG 08",
        [42] = "Knife",
        [43] = "Flashbang",
        [44] = "HE Grenade",
        [45] = "Smoke Grenade",
        [46] = "Molotov",
        [47] = "Decoy Grenade",
        [48] = "Incendiary Grenade",
        [49] = "C4 Explosive",
        [59] = "Knife",
        [60] = "M4A1-S",
        [61] = "USP-S",
        [63] = "CZ75-Auto",
        [64] = "R8 Revolver",
        [500] = "Bayonet",
        [503] = "Classic Knife",
        [505] = "Flip Knife",
        [506] = "Gut Knife",
        [507] = "Karambit",
        [508] = "M9 Bayonet",
        [509] = "Huntsman Knife",
        [512] = "Falchion Knife",
        [514] = "Bowie Knife",
        [515] = "Butterfly Knife",
        [516] = "Shadow Daggers",
        [517] = "Paracord Knife",
        [518] = "Survival Knife",
        [519] = "Ursus Knife",
        [520] = "Navaja Knife",
        [521] = "Nomad Knife",
        [522] = "Stiletto Knife",
        [523] = "Talon Knife",
        [525] = "Skeleton Knife",
        [526] = "Kukri Knife",
        [1348] = "Sealed Graffiti",
        [1349] = "Graffiti",
    };

    private static readonly Dictionary<uint, string> SpecialItems = new()
    {
        [1201] = "Storage Unit",
    };

    private static readonly Dictionary<uint, string> WeaponEntityNames = new()
    {
        [1] = "weapon_deagle",
        [2] = "weapon_elite",
        [3] = "weapon_fiveseven",
        [4] = "weapon_glock",
        [7] = "weapon_ak47",
        [8] = "weapon_aug",
        [9] = "weapon_awp",
        [10] = "weapon_famas",
        [11] = "weapon_g3sg1",
        [13] = "weapon_galilar",
        [14] = "weapon_m249",
        [16] = "weapon_m4a1",
        [17] = "weapon_mac10",
        [19] = "weapon_p90",
        [23] = "weapon_mp5sd",
        [24] = "weapon_ump45",
        [25] = "weapon_xm1014",
        [26] = "weapon_bizon",
        [27] = "weapon_mag7",
        [28] = "weapon_negev",
        [29] = "weapon_sawedoff",
        [30] = "weapon_tec9",
        [32] = "weapon_hkp2000",
        [33] = "weapon_mp7",
        [34] = "weapon_mp9",
        [35] = "weapon_nova",
        [36] = "weapon_p250",
        [38] = "weapon_scar20",
        [39] = "weapon_sg556",
        [40] = "weapon_ssg08",
        [42] = "weapon_knife",
        [43] = "weapon_flashbang",
        [59] = "weapon_knife",
        [60] = "weapon_m4a1_silencer",
        [61] = "weapon_usp_silencer",
        [63] = "weapon_cz75a",
        [64] = "weapon_revolver",
        [500] = "weapon_knife_bayonet",
        [503] = "weapon_knife_css",
        [505] = "weapon_knife_flip",
        [506] = "weapon_knife_gut",
        [507] = "weapon_knife_karambit",
        [508] = "weapon_knife_m9_bayonet",
        [509] = "weapon_knife_tactical",
        [512] = "weapon_knife_falchion",
        [514] = "weapon_knife_survival_bowie",
        [515] = "weapon_knife_butterfly",
        [516] = "weapon_knife_push",
        [517] = "weapon_knife_cord",
        [518] = "weapon_knife_canis",
        [519] = "weapon_knife_ursus",
        [520] = "weapon_knife_gypsy_jackknife",
        [521] = "weapon_knife_outdoor",
        [522] = "weapon_knife_stiletto",
        [523] = "weapon_knife_widowmaker",
        [525] = "weapon_knife_skeleton",
        [526] = "weapon_knife_kukri",
    };

    private Lazy<Dictionary<string, List<ItemOriginInfo>>> _originMap;

    public CS2ItemDefinitionProvider(ILogger<CS2ItemDefinitionProvider> logger, ISettingsService settings)
    {
        _schema = new Lazy<CS2ItemsGameSchema>(() => LoadSchema(logger, settings.Cs2GamePath), LazyThreadSafetyMode.ExecutionAndPublication);
        _originMap = new Lazy<Dictionary<string, List<ItemOriginInfo>>>(() => BuildOriginMap(_schema.Value), LazyThreadSafetyMode.ExecutionAndPublication);
        _containerDropMap = new Lazy<Dictionary<int, IReadOnlyList<ContainerDropInfo>>>(() => BuildContainerDropMap(_schema.Value), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string? GetItemName(uint defIndex)
    {
        if (WeaponNames.TryGetValue(defIndex, out var name))
            return name;

        return _schema.Value.Items.GetValueOrDefault((int)defIndex);
    }

    public string? GetItemIconPath(uint defIndex, int paintIndex, int stickerKitId = 0, int keychainId = 0, int graffitiTintId = 0)
    {
        if (defIndex == 1355 && keychainId > 0)
            return GetKeychainIconUrl(keychainId);

        if (defIndex is 1348 or 1349 && stickerKitId > 0)
            return GetGraffitiIconUrl(defIndex, stickerKitId, graffitiTintId);

        if (paintIndex == 0 && stickerKitId > 0)
            return GetStickerIconUrl(stickerKitId);

        var mhn = GetMarketHashName(defIndex, paintIndex, 0.15f, stickerKitId, keychainId, graffitiTintId);
        if (!string.IsNullOrEmpty(mhn) && _iconHashCache.TryGetValue(mhn, out var hash))
            return hash;

        var itemName = GetItemName(defIndex);
        if (!string.IsNullOrEmpty(itemName) && itemName != mhn && _iconHashCache.TryGetValue(itemName, out var nameHash))
            return nameHash;

        if (_schema.Value.ItemImagePaths.TryGetValue((int)defIndex, out var path) && !IsVpkPath(path))
            return path;

        return null;
    }

    public string? GetMarketHashName(uint defIndex, int paintIndex, float paintWear, int stickerKitId = 0, int keychainId = 0, int graffitiTintId = 0)
    {
        if (SpecialItems.TryGetValue(defIndex, out var special))
            return special;

        var weaponName = GetItemName(defIndex);
        if (string.IsNullOrWhiteSpace(weaponName))
            return null;

        var prefix = IsKnife(defIndex) ? "★ " : "";

        if (keychainId > 0)
        {
            var keychainName = GetKeychainName(keychainId);
            return string.IsNullOrWhiteSpace(keychainName)
                ? $"{prefix}{weaponName}"
                : $"{prefix}{weaponName} | {keychainName}";
        }

        if (paintIndex == 0 && stickerKitId > 0)
        {
            var kitName = GetStickerKitName(stickerKitId);
            if (string.IsNullOrWhiteSpace(kitName))
                return $"{prefix}{weaponName}";

            var baseMhn = $"{prefix}{weaponName} | {kitName}";

            if (defIndex is 1348 or 1349 && graffitiTintId > 0)
            {
                var tintName = GetGraffitiTintName(graffitiTintId);
                if (!string.IsNullOrWhiteSpace(tintName))
                    return $"{baseMhn} ({tintName})";
            }

            return baseMhn;
        }

        if (paintIndex == 0)
        {
            return $"{prefix}{weaponName}";
        }

        var paintKitName = GetPaintKitName(paintIndex);
        var exterior = GetExteriorString(paintWear);
        var baseName = string.IsNullOrWhiteSpace(paintKitName)
            ? $"{prefix}{weaponName}"
            : $"{prefix}{weaponName} | {paintKitName}";

        return string.IsNullOrWhiteSpace(exterior)
            ? baseName
            : $"{baseName} ({exterior})";
    }

    public string? GetPaintKitName(int paintIndex)
    {
        if (paintIndex <= 0)
            return null;

        return _schema.Value.PaintKits.GetValueOrDefault(paintIndex);
    }

    public string? GetStickerKitName(int stickerKitId)
    {
        if (stickerKitId <= 0)
            return null;

        return _schema.Value.StickerKits.GetValueOrDefault(stickerKitId);
    }

    public string? GetKeychainName(int keychainId)
    {
        if (keychainId <= 0)
            return null;

        return _schema.Value.Keychains.GetValueOrDefault(keychainId);
    }

    public string? GetGraffitiTintName(int tintId)
    {
        if (tintId <= 0)
            return null;

        return _schema.Value.GraffitiTints.GetValueOrDefault(tintId);
    }

    public string? GetRarityColor(int rarity)
    {
        if (RarityColorKeys.TryGetValue(rarity, out var colorKey))
        {
            if (_schema.Value.RarityColors.TryGetValue(colorKey, out var hex))
                return hex;

            if (FallbackRarityColors.TryGetValue(colorKey, out var fallback))
                return fallback;
        }

        return null;
    }

    public string? GetQualityColor(int quality)
    {
        if (_schema.Value.QualityColors.TryGetValue(quality, out var hex))
            return hex;

        return FallbackQualityColors.GetValueOrDefault(quality);
    }

    public string? GetStickerIconUrl(int stickerKitId)
    {
        if (stickerKitId <= 0)
            return null;

        var name = GetStickerKitName(stickerKitId);
        if (!string.IsNullOrEmpty(name))
        {
            var mhn = $"Sticker | {name}";
            if (_iconHashCache.TryGetValue(mhn, out var hash))
                return hash;
        }

        return null;
    }

    public string? GetGraffitiIconUrl(uint defIndex, int stickerKitId, int graffitiTintId = 0)
    {
        if (stickerKitId <= 0)
            return null;

        var kitName = GetStickerKitName(stickerKitId);
        if (string.IsNullOrEmpty(kitName))
            return null;

        var prefix = defIndex == 1348 ? "Sealed Graffiti" : "Graffiti";
        var baseMhn = $"{prefix} | {kitName}";

        // Try exact match with tint color first (e.g., "Sealed Graffiti | Sorry (Shark White)")
        if (graffitiTintId > 0)
        {
            var tintName = GetGraffitiTintName(graffitiTintId);
            if (!string.IsNullOrWhiteSpace(tintName))
            {
                var tintedMhn = $"{baseMhn} ({tintName})";
                if (_iconHashCache.TryGetValue(tintedMhn, out var tintedHash))
                    return tintedHash;
            }
        }

        // Exact match without tint (works for unique/tournament graffiti)
        if (_iconHashCache.TryGetValue(baseMhn, out var hash))
            return hash;

        // Fuzzy prefix match: any tint variant of the same graffiti
        foreach (var (key, value) in _iconHashCache)
        {
            if (key.StartsWith(baseMhn, StringComparison.OrdinalIgnoreCase)
                && key.Length > baseMhn.Length
                && key[baseMhn.Length] == ' ')
                return value;
        }

        return null;
    }

    public string? GetKeychainIconUrl(int keychainId)
    {
        if (keychainId <= 0)
            return null;

        var name = GetKeychainName(keychainId);
        if (!string.IsNullOrEmpty(name))
        {
            var mhn = $"Charm | {name}";
            if (_iconHashCache.TryGetValue(mhn, out var hash))
                return hash;
        }

        if (_schema.Value.KeychainImagePaths.TryGetValue(keychainId, out var path))
            return path;

        return null;
    }

    public IReadOnlyList<ContainerDropInfo> GetContainerDrops(uint defIndex)
    {
        if (!_containerDropMap.Value.TryGetValue((int)defIndex, out var drops))
            return [];

        foreach (var drop in drops)
        {
            if (!string.IsNullOrEmpty(drop.IconUrl) || string.IsNullOrEmpty(drop.MarketHashName))
                continue;

            var resolved = ResolveIconHash(drop.MarketHashName);
            if (!string.IsNullOrEmpty(resolved))
                drop.IconUrl = resolved;
        }

        return drops;
    }

    public void PopulateIconCache(IReadOnlyDictionary<string, string> iconsByMarketHashName)
    {
        foreach (var (mhn, hash) in iconsByMarketHashName)
        {
            if (!string.IsNullOrEmpty(hash))
                _iconHashCache.TryAdd(mhn, hash);
        }
    }

    public string? ResolveIconHash(string marketHashName)
    {
        if (string.IsNullOrEmpty(marketHashName))
            return null;

        return _iconHashCache.GetValueOrDefault(marketHashName);
    }

    private static bool IsVpkPath(string value) =>
        value.StartsWith("econ/", StringComparison.OrdinalIgnoreCase)
        || (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && value.Contains('/')
            && (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)));

    private static bool IsKnife(uint defIndex) =>
        defIndex is 42 or 59 or (>= 500 and <= 526);

    private static string GetExteriorString(float wear) => wear switch
    {
        <= 0.07f => "Factory New",
        < 0.15f => "Minimal Wear",
        < 0.38f => "Field-Tested",
        < 0.45f => "Well-Worn",
        _ => "Battle-Scarred"
    };

    public IReadOnlyList<ItemOriginInfo> GetItemOrigins(int paintIndex, uint defIndex, int stickerKitId = 0)
    {
        var schema = _schema.Value;

        if (paintIndex == 0 && stickerKitId > 0)
        {
            if (schema.StickerKitInternalNames.TryGetValue(stickerKitId, out var stickerName))
            {
                var stickerKey = $"[{stickerName}]sticker";
                if (_originMap.Value.TryGetValue(stickerKey, out var stickerOrigins))
                    return stickerOrigins;
            }
            return [];
        }

        if (!schema.PaintKitInternalNames.TryGetValue(paintIndex, out var paintKitName))
            return [];

        if (!WeaponEntityNames.TryGetValue(defIndex, out var weaponName))
            return [];

        var skinKey = $"[{paintKitName}]{weaponName}";

        if (_originMap.Value.TryGetValue(skinKey, out var origins))
            return origins;

        return [];
    }

    private static Dictionary<string, List<ItemOriginInfo>> BuildOriginMap(CS2ItemsGameSchema schema)
    {
        var map = new Dictionary<string, List<ItemOriginInfo>>(StringComparer.OrdinalIgnoreCase);

        var seriesToCase = new Dictionary<int, (CaseItemInfo Info, int DefIndex)>();
        foreach (var (defIdx, caseInfo) in schema.CaseItems)
        {
            seriesToCase.TryAdd(caseInfo.SupplyCrateSeries, (caseInfo, defIdx));
        }

        var lootListToSeries = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (series, listName) in schema.RevolvingLootLists)
        {
            if (!lootListToSeries.TryGetValue(listName, out var seriesList))
            {
                seriesList = [];
                lootListToSeries[listName] = seriesList;
            }
            seriesList.Add(series);
        }

        var childToParents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (listName, entries) in schema.ClientLootLists)
        {
            foreach (var entry in entries)
            {
                if (entry.StartsWith('['))
                    continue;

                if (!childToParents.TryGetValue(entry, out var parents))
                {
                    parents = [];
                    childToParents[entry] = parents;
                }
                parents.Add(listName);
            }
        }

        HashSet<int> ResolveToSeries(string listName, HashSet<string>? visited = null)
        {
            visited ??= [];
            var result = new HashSet<int>();

            if (!visited.Add(listName))
                return result;

            if (lootListToSeries.TryGetValue(listName, out var directSeries))
            {
                foreach (var s in directSeries)
                    result.Add(s);
            }

            if (childToParents.TryGetValue(listName, out var parents))
            {
                foreach (var parent in parents)
                {
                    foreach (var s in ResolveToSeries(parent, visited))
                        result.Add(s);
                }
            }

            return result;
        }

        foreach (var (listName, entries) in schema.ClientLootLists)
        {
            foreach (var entry in entries)
            {
                if (!entry.StartsWith('['))
                    continue;

                var seriesSet = ResolveToSeries(listName);

                foreach (var series in seriesSet)
                {
                    if (!seriesToCase.TryGetValue(series, out var caseEntry))
                        continue;

                    if (!map.TryGetValue(entry, out var origins))
                    {
                        origins = [];
                        map[entry] = origins;
                    }

                    if (!origins.Any(o => o.Name == caseEntry.Info.LocKey && o.Type == ItemOriginType.Case))
                        origins.Add(new ItemOriginInfo { Name = caseEntry.Info.LocKey, DefIndex = caseEntry.DefIndex, Type = ItemOriginType.Case });
                }
            }
        }

        foreach (var (_, setEntry) in schema.ItemSets)
        {
            foreach (var skinKey in setEntry.SkinKeys)
            {
                if (!map.TryGetValue(skinKey, out var origins))
                {
                    origins = [];
                    map[skinKey] = origins;
                }

                if (!origins.Any(o => o.Name == setEntry.LocKey && o.Type == ItemOriginType.Collection))
                    origins.Add(new ItemOriginInfo { Name = setEntry.LocKey, Type = ItemOriginType.Collection });
            }
        }

        return map;
    }

    private Dictionary<int, IReadOnlyList<ContainerDropInfo>> BuildContainerDropMap(CS2ItemsGameSchema schema)
    {
        var map = new Dictionary<int, IReadOnlyList<ContainerDropInfo>>();
        var paintIdsByInternalName = schema.PaintKitInternalNames
            .ToDictionary(static kvp => kvp.Value, static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
        var stickerIdsByInternalName = schema.StickerKitInternalNames
            .ToDictionary(static kvp => kvp.Value, static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
        var defIndexByEntityName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entityDefIndex, entityName) in WeaponEntityNames)
            defIndexByEntityName.TryAdd(entityName, entityDefIndex);

        foreach (var (defIndex, containerInfo) in schema.CaseItems)
        {
            if (!schema.RevolvingLootLists.TryGetValue(containerInfo.SupplyCrateSeries, out var topList))
                continue;

            var seenLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenDrops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var drops = new List<ContainerDropInfo>();

            VisitLootList(topList);

            if (drops.Count > 0)
                map[defIndex] = drops;

            void VisitLootList(string listName)
            {
                if (!seenLists.Add(listName))
                    return;

                if (!schema.ClientLootLists.TryGetValue(listName, out var entries))
                {
                    if (IsUnusualLootList(listName))
                        AddKnifeDrops();

                    return;
                }

                foreach (var entry in entries)
                {
                    if (TryCreateContainerDrop(entry, paintIdsByInternalName, stickerIdsByInternalName, defIndexByEntityName, out var drop))
                    {
                        if (seenDrops.Add(drop.Name))
                            drops.Add(drop);

                        continue;
                    }

                    if (IsUnusualLootList(entry))
                    {
                        AddKnifeDrops();
                        continue;
                    }

                    VisitLootList(entry);
                }
            }

            void AddKnifeDrops()
            {
                foreach (var knifeDefIndex in WeaponNames.Keys.Where(k => k >= 500).OrderBy(k => k))
                {
                    var name = GetItemName(knifeDefIndex);
                    if (string.IsNullOrWhiteSpace(name) || !seenDrops.Add(name))
                        continue;

                    var knifeMhn = $"★ {name}";
                    drops.Add(new ContainerDropInfo
                    {
                        Name = name,
                        Subtitle = "Vanilla",
                        MarketHashName = knifeMhn,
                        IconUrl = ResolveIconHash(knifeMhn) ?? string.Empty
                    });
                }
            }
        }

        return map;
    }

    private bool TryCreateContainerDrop(
        string lootEntry,
        IReadOnlyDictionary<string, int> paintIdsByInternalName,
        IReadOnlyDictionary<string, int> stickerIdsByInternalName,
        IReadOnlyDictionary<string, uint> defIndexByEntityName,
        out ContainerDropInfo drop)
    {
        drop = new ContainerDropInfo();

        if (!lootEntry.StartsWith("[", StringComparison.Ordinal))
            return false;

        var separatorIndex = lootEntry.IndexOf(']');
        if (separatorIndex <= 1 || separatorIndex >= lootEntry.Length - 1)
            return false;

        var internalName = lootEntry[1..separatorIndex];
        var entityName = lootEntry[(separatorIndex + 1)..];

        if (string.Equals(entityName, "sticker", StringComparison.OrdinalIgnoreCase))
        {
            if (!stickerIdsByInternalName.TryGetValue(internalName, out var stickerId))
                return false;

            var name = GetStickerKitName(stickerId);
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var stickerMhn = $"Sticker | {name}";
            drop = new ContainerDropInfo
            {
                Name = name,
                Subtitle = "Sticker",
                MarketHashName = stickerMhn,
                IconUrl = GetStickerIconUrl(stickerId) ?? string.Empty
            };

            return true;
        }

        if (!defIndexByEntityName.TryGetValue(entityName, out var defIndex))
            return false;

        var baseName = GetItemName(defIndex);
        if (string.IsNullOrWhiteSpace(baseName))
            return false;

        if (IsKnife(defIndex))
        {
            var knifeMhn = $"★ {baseName}";
            drop = new ContainerDropInfo
            {
                Name = baseName,
                Subtitle = "Vanilla",
                MarketHashName = knifeMhn,
                IconUrl = ResolveIconHash(knifeMhn) ?? string.Empty
            };

            return true;
        }

        var paintName = paintIdsByInternalName.TryGetValue(internalName, out var paintId)
            ? GetPaintKitName(paintId)
            : null;

        var dropName = string.IsNullOrWhiteSpace(paintName) ? baseName : $"{baseName} | {paintName}";
        var prefix = IsKnife(defIndex) ? "★ " : "";
        var dropMhn = $"{prefix}{dropName}";

        drop = new ContainerDropInfo
        {
            Name = dropName,
            MarketHashName = dropMhn,
            IconUrl = ResolveIconHash(dropMhn) ?? string.Empty
        };

        return true;
    }

    private static bool IsUnusualLootList(string listName)
        => listName.EndsWith("_unusual", StringComparison.OrdinalIgnoreCase);

    private static CS2ItemsGameSchema LoadSchema(ILogger<CS2ItemDefinitionProvider> logger, string? cs2GamePath)
    {
        var path = ResolveItemsGamePath();
        if (path is null)
        {
            logger.LogWarning("CS2 items_game.txt was not found. Paint, sticker, and keychain names will use fallbacks.");
            return new CS2ItemsGameSchema();
        }

        logger.LogInformation("Loading CS2 schema from {Path}", path);

        var resolvedCs2Path = !string.IsNullOrWhiteSpace(cs2GamePath) && Directory.Exists(cs2GamePath)
            ? cs2GamePath
            : null;

        if (resolvedCs2Path is not null)
            logger.LogInformation("Using CS2 game path for localization: {Cs2Path}", resolvedCs2Path);

        return CS2ItemsGameSchema.Load(path, resolvedCs2Path);
    }

    private static string? ResolveItemsGamePath()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "dump", "cs2", "items_game.txt");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}
