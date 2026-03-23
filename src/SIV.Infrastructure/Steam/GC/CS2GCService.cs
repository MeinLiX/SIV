using Microsoft.Extensions.Logging;
using ProtoBuf;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.Domain.Enums;
using SIV.Infrastructure.Protos;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.Internal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SIV.Infrastructure.Steam.GC;

public sealed class CS2GCService : IGCService
{
    private const uint CS2GcHelloVersion = 2000202;
    private const uint EMsgGCClientHello = 4006;
    private const uint EMsgGCClientWelcome = 4004;
    private const uint EMsgGCClientConnectionStatus = 4009;
    private const uint EMsgMatchmakingClientHello = 9109;
    private const uint EMsgMatchmakingClientHelloResponse = 9110;
    private const uint EMsgClientLogonFatalError = 9187;
    private const int CSOTypeEconItem = 1;
    private const uint EMsgGCCasketItemLoadContents = 1094;
    private const uint EMsgGCItemCustomizationNotification = 1090;
    private const uint EMsgSOSingleObject = 21;
    private const uint CasketContentsNotification = 1012;
    private const int MaxHelloRetries = 5;
    private static readonly TimeSpan HelloRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GcRequestTimeout = TimeSpan.FromSeconds(90);

    private readonly SteamConnectionService _connection;
    private readonly ILogger<CS2GCService> _logger;
    private readonly IItemDefinitionProvider? _itemDefs;
    private TaskCompletionSource<List<CSOEconItem>>? _inventoryTcs;
    private TaskCompletionSource<List<CSOEconItem>>? _casketTcs;
    private readonly List<CSOEconItem> _pendingCasketItems = [];
    private IDisposable? _gcSubscription;
    private CancellationTokenSource? _helloRetryCts;

    public uint AppId => CS2GameDefinition.CS2AppId;
    public bool IsConnected { get; private set; }

    public CS2GCService(SteamConnectionService connection, ILogger<CS2GCService> logger, IItemDefinitionProvider? itemDefs = null)
    {
        _connection = connection;
        _logger = logger;
        _itemDefs = itemDefs;
    }

    private List<CSOEconItem>? _cachedItems;
    private List<CSOEconItem>? _allRawItems;
    private Dictionary<string, WebApiItemData>? _webApiByMarketHashName;
    private Dictionary<string, WebApiItemData>? _webApiByBaseName;

    private record WebApiItemData(string IconUrl, bool Tradable, bool Marketable, bool CanFetchMarketPrice, bool IsTemporaryTradeLock);

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            _logger.LogInformation("CS2 Game Coordinator is already connected");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Connecting to CS2 Game Coordinator");
        _cachedItems = null;
        _allRawItems = null;
        _gcSubscription?.Dispose();
        _gcSubscription = _connection.CallbackManager.Subscribe<SteamGameCoordinator.MessageCallback>(OnGCMessage);

        if (_connection.IsPlayingBlocked)
        {
            _gcSubscription.Dispose();
            _gcSubscription = null;

            var message = _connection.BlockingAppId is uint appId
                ? $"Steam reports that game playing is currently blocked by another client session (AppId={appId})."
                : "Steam reports that game playing is currently blocked by another client session.";

            _logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
        playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = CS2GameDefinition.CS2AppId });
        _connection.Client.Send(playGame);

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_connection.IsConnected)
        {
            var stopGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
            _connection.Client.Send(stopGame);
        }
        _gcSubscription?.Dispose();
        _gcSubscription = null;
        _allRawItems = null;
        _cachedItems = null;
        _webApiByMarketHashName = null;
        _webApiByBaseName = null;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<InventoryItem>> RequestInventoryAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting CS2 inventory via GC");

        if (_cachedItems != null)
        {
            var cached = _cachedItems;
            _cachedItems = null;
            var inventoryItems = cached.Where(i => !IsContainedInCasket(i)).Select(MapToInventoryItem).ToList();
            ApplyAcquiredOrder(inventoryItems);
            _logger.LogInformation("Using cached items: {Total} total, {Filtered} in inventory ({Excluded} casket-contained excluded)",
                cached.Count, inventoryItems.Count, cached.Count - inventoryItems.Count);
            await EnrichFromWebApiAsync(inventoryItems);
            return inventoryItems;
        }

        _inventoryTcs = new TaskCompletionSource<List<CSOEconItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _helloRetryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = SendGCHelloWithRetriesAsync(_helloRetryCts.Token);

        try
        {
            var items = await _inventoryTcs.Task.WaitAsync(GcRequestTimeout, ct);
            var inventoryItems = items.Where(i => !IsContainedInCasket(i)).Select(MapToInventoryItem).ToList();
            ApplyAcquiredOrder(inventoryItems);
            _logger.LogInformation("Received {Total} items from GC, {Filtered} in inventory ({Excluded} casket-contained excluded)",
                items.Count, inventoryItems.Count, items.Count - inventoryItems.Count);
            await EnrichFromWebApiAsync(inventoryItems);
            return inventoryItems;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timed out waiting for CS2 inventory welcome after {TimeoutSeconds}s", GcRequestTimeout.TotalSeconds);
            throw new TimeoutException("Timed out waiting for the CS2 Game Coordinator inventory response.", ex);
        }
        finally
        {
            _helloRetryCts?.Cancel();
            _helloRetryCts?.Dispose();
            _helloRetryCts = null;
        }
    }

    public async Task<IReadOnlyList<InventoryItem>> RequestCasketContentsAsync(ulong casketId, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _allRawItems is { Count: > 0 })
        {
            var cachedCasketItems = _allRawItems
                .Where(i => IsContainedInCasket(i) && GetCasketIdFromAttributes(i) == casketId)
                .ToList();

            if (cachedCasketItems.Count > 0)
            {
                _logger.LogInformation(
                    "Found {Count} items for CasketId={CasketId} already in SO cache, skipping GC request",
                    cachedCasketItems.Count, casketId);
                var cachedMapped = cachedCasketItems.Select(i =>
                {
                    var mapped = MapToInventoryItem(i);
                    mapped.ContainedInCasketId = casketId;
                    return mapped;
                }).ToList();
                ApplyAcquiredOrder(cachedMapped);
                await EnrichItemsFromCachedWebApiAsync(cachedMapped);
                return cachedMapped;
            }
        }

        _casketTcs = new TaskCompletionSource<List<CSOEconItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCasketItems.Clear();

        _logger.LogInformation("Requesting CS2 casket contents for CasketId={CasketId} via GC", casketId);
        var msg = new ClientGCMsgProtobuf<CMsgCasketItem>(EMsgGCCasketItemLoadContents);
        msg.Body.CasketItemId = casketId;
        _connection.GameCoordinator.Send(msg, CS2GameDefinition.CS2AppId);

        try
        {
            var items = await _casketTcs.Task.WaitAsync(GcRequestTimeout, ct);
            if (_allRawItems is not null)
            {
                _allRawItems.RemoveAll(i => IsContainedInCasket(i) && GetCasketIdFromAttributes(i) == casketId);
                _allRawItems.AddRange(items);
            }
            var casketMapped = items.Select(i =>
            {
                var mapped = MapToInventoryItem(i);
                mapped.ContainedInCasketId = casketId;
                return mapped;
            }).ToList();
            ApplyAcquiredOrder(casketMapped);
            await EnrichItemsFromCachedWebApiAsync(casketMapped);
            return casketMapped;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timed out waiting for casket contents after {TimeoutSeconds}s", GcRequestTimeout.TotalSeconds);
            throw new TimeoutException("Timed out waiting for casket contents from the Game Coordinator.", ex);
        }
    }

    private async Task SendGCHelloWithRetriesAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxHelloRetries; attempt++)
        {
            if (ct.IsCancellationRequested || _inventoryTcs?.Task.IsCompleted == true)
                return;

            if (attempt > 0)
            {
                _logger.LogInformation("Retrying GC hello (attempt {Attempt}/{Max})", attempt + 1, MaxHelloRetries);
                await Task.Delay(HelloRetryInterval, ct).ConfigureAwait(false);
                if (_inventoryTcs?.Task.IsCompleted == true)
                    return;
            }

            SendGCHello();
        }
    }

    private void SendGCHello()
    {
        _logger.LogInformation("Sending CS2 GC hello");
        var hello = new ClientGCMsgProtobuf<GCMsgClientHello>(EMsgGCClientHello);
        hello.Body.Version = CS2GcHelloVersion;
        _connection.GameCoordinator.Send(hello, CS2GameDefinition.CS2AppId);

        _logger.LogInformation("Sending CS2 matchmaking hello");
        var matchmakingHello = new ClientGCMsgProtobuf<CMsgGCCStrike15_v2_MatchmakingClient2GCHello>(EMsgMatchmakingClientHello);
        _connection.GameCoordinator.Send(matchmakingHello, CS2GameDefinition.CS2AppId);
    }

    private void OnGCMessage(SteamGameCoordinator.MessageCallback cb)
    {
        if (cb.AppID != CS2GameDefinition.CS2AppId) return;

        var rawEmsg = cb.EMsg;
        var emsg = rawEmsg & ~0x80000000u;
        _logger.LogInformation(
            "GC message received: RawEMsg={RawEMsg}, EMsg={EMsg}, IsProto={IsProto}, AppId={AppId}",
            rawEmsg,
            emsg,
            cb.IsProto,
            cb.AppID);

        switch (emsg)
        {
            case EMsgGCClientWelcome:
                HandleClientWelcome(cb.Message);
                break;
            case EMsgGCClientConnectionStatus:
                HandleClientConnectionStatus(cb.Message);
                break;
            case EMsgMatchmakingClientHelloResponse:
                HandleMatchmakingHello(cb.Message);
                break;
            case EMsgClientLogonFatalError:
                HandleClientLogonFatalError(cb.Message);
                break;
            case EMsgGCItemCustomizationNotification:
                HandleItemCustomizationNotification(cb.Message);
                break;
            case EMsgSOSingleObject:
                HandleSOSingleObject(cb.Message);
                break;
            default:
                if (_casketTcs is not null && TryHandleCasketCache(cb.Message, emsg))
                    break;
                if (_inventoryTcs is not null && TryHandleSubscribedCache(cb.Message, emsg))
                    break;

                _logger.LogInformation("Ignoring unsupported CS2 GC message {EMsg}", emsg);
                break;
        }
    }

    private void HandleClientWelcome(IPacketGCMsg packetMsg)
    {
        var welcome = new ClientGCMsgProtobuf<CMsgLegacySource1ClientWelcome>(packetMsg);
        _logger.LogInformation("GC Welcome received, cache count: {Count}", welcome.Body.Outofdate_Subscribed_Caches.Count);

        if (welcome.Body.GameData is { Length: > 0 })
        {
            using var ms = new MemoryStream(welcome.Body.GameData);
            var gameData = Serializer.Deserialize<CMsgGCCStrike15Welcome>(ms);
            _logger.LogInformation(
                "CS2 welcome game data received: StoreItemHash={StoreItemHash}, GsCookieId={GsCookieId}, UniqueId={UniqueId}",
                gameData.StoreItemHash,
                gameData.GsCookieId,
                gameData.UniqueId);
        }

        CompleteInventoryRequest(welcome.Body.Outofdate_Subscribed_Caches, "GC welcome");
    }

    private void HandleClientConnectionStatus(IPacketGCMsg packetMsg)
    {
        try
        {
            var msg = new ClientGCMsgProtobuf<CMsgConnectionStatus>(packetMsg);
            var status = msg.Body.Status;
            _logger.LogInformation(
                "GC connection status received: Status={Status}, QueuePosition={QueuePos}, WaitSeconds={Wait}",
                status, msg.Body.QueuePosition, msg.Body.WaitSeconds);

            // Status 0=HAVE_SESSION, 2=NO_SESSION, 3=IN_QUEUE — retries handle all cases.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse GC connection status message");
        }
    }

    private void HandleItemCustomizationNotification(IPacketGCMsg packetMsg)
    {
        try
        {
            var msg = new ClientGCMsgProtobuf<CMsgGCItemCustomizationNotification>(packetMsg);
            _logger.LogInformation(
                "GC item customization notification: Request={Request}, ItemIds=[{ItemIds}]",
                msg.Body.Request,
                string.Join(", ", msg.Body.ItemId));

            if (msg.Body.Request == CasketContentsNotification)
            {
                _logger.LogInformation("Casket contents notification received. Completing with {Count} pending items.", _pendingCasketItems.Count);
                _casketTcs?.TrySetResult(new List<CSOEconItem>(_pendingCasketItems));
                _pendingCasketItems.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse item customization notification");
        }
    }

    private void HandleSOSingleObject(IPacketGCMsg packetMsg)
    {
        try
        {
            var msg = new ClientGCMsgProtobuf<CMsgSOSingleObject>(packetMsg);

            if (msg.Body.TypeId == CSOTypeEconItem && msg.Body.ObjectData is { Length: > 0 })
            {
                var item = ParseEconItem(msg.Body.ObjectData);

                if (_casketTcs is { Task.IsCompleted: false })
                {
                    _pendingCasketItems.Add(item);
                    _logger.LogInformation(
                        "Casket item received via SO update: ItemId={Id}, DefIndex={DefIndex}",
                        item.Id, item.DefIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse SO single object message");
        }
    }

    private bool TryHandleCasketCache(IPacketGCMsg packetMsg, uint emsg)
    {
        try
        {
            var cache = new ClientGCMsgProtobuf<CMsgSOCacheSubscribed>(packetMsg);
            if (cache.Body.Objects.Count == 0)
                return false;

            _logger.LogInformation(
                "Treating GC message {EMsg} as casket SO cache with {Count} object groups",
                emsg, cache.Body.Objects.Count);

            var items = new List<CSOEconItem>();
            foreach (var obj in cache.Body.Objects)
            {
                if (obj.TypeId != CSOTypeEconItem) continue;
                foreach (var data in obj.ObjectData)
                    items.Add(ParseEconItem(data));
            }

            if (items.Count > 0)
            {
                _logger.LogInformation("Parsed {Count} casket items from GC message {EMsg}", items.Count, emsg);
                _casketTcs?.TrySetResult(items);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GC message {EMsg} could not be parsed as casket SO cache", emsg);
            return false;
        }
    }

    private void HandleMatchmakingHello(IPacketGCMsg packetMsg)
    {
        var hello = new ClientGCMsgProtobuf<CMsgGCCStrike15_v2_MatchmakingGC2ClientHello>(packetMsg);
        _logger.LogInformation("CS2 matchmaking hello received for AccountId={AccountId}", hello.Body.AccountId);
    }

    private void HandleClientLogonFatalError(IPacketGCMsg packetMsg)
    {
        var fatal = new ClientGCMsgProtobuf<CMsgGCCStrike15_v2_ClientLogonFatalError>(packetMsg);
        var message = string.IsNullOrWhiteSpace(fatal.Body.Message)
            ? $"CS2 GC login failed with error code {fatal.Body.ErrorCode}."
            : $"CS2 GC login failed: {fatal.Body.Message} (code {fatal.Body.ErrorCode}).";

        if (!string.IsNullOrWhiteSpace(fatal.Body.Country))
            message += $" Country: {fatal.Body.Country}.";

        _logger.LogError(
            "CS2 GC fatal logon error received. ErrorCode={ErrorCode}, Message={Message}, Country={Country}",
            fatal.Body.ErrorCode,
            fatal.Body.Message,
            fatal.Body.Country);

        var ex = new InvalidOperationException(message);
        _inventoryTcs?.TrySetException(ex);
        _casketTcs?.TrySetException(ex);
    }

    private bool TryHandleSubscribedCache(IPacketGCMsg packetMsg, uint emsg)
    {
        try
        {
            var cache = new ClientGCMsgProtobuf<CMsgSOCacheSubscribed>(packetMsg);
            if (cache.Body.Objects.Count == 0)
            {
                return false;
            }

            _logger.LogInformation(
                "Treating GC message {EMsg} as subscribed cache fallback with {Count} object groups",
                emsg,
                cache.Body.Objects.Count);

            CompleteInventoryRequest([cache.Body], $"GC fallback message {emsg}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GC message {EMsg} could not be parsed as subscribed cache", emsg);
            return false;
        }
    }

    private void CompleteInventoryRequest(IEnumerable<CMsgSOCacheSubscribed> caches, string source)
    {
        try
        {
            var allItems = new List<CSOEconItem>();

            foreach (var cache in caches)
            {
                foreach (var obj in cache.Objects)
                {
                    _logger.LogInformation("SO cache group from {Source}: TypeId={TypeId}, ObjectCount={Count}", source, obj.TypeId, obj.ObjectData.Count);
                    if (obj.TypeId != CSOTypeEconItem) continue;

                    foreach (var data in obj.ObjectData)
                    {
                        allItems.Add(ParseEconItem(data));
                    }
                }
            }

            _logger.LogInformation("Parsed {Count} items from {Source}", allItems.Count, source);
            _logger.LogDebug("Distinct econ attribute ids from {Source}: {Ids}",
                source, string.Join(", ", allItems.SelectMany(i => i.Attribute).Select(a => a.DefIndex).Distinct().OrderBy(i => i)));

            _allRawItems = allItems;

            if (_inventoryTcs != null)
                _inventoryTcs.TrySetResult(allItems);
            else
                _cachedItems = allItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse inventory items from {Source}", source);
            _inventoryTcs?.TrySetException(ex);
        }
    }

    private InventoryItem MapToInventoryItem(CSOEconItem proto)
    {
        float paintWear = 0;
        int paintSeed = 0;
        int paintIndex = 0;
        int casketItemCount = 0;
        bool isCasket = false;
        bool isTemporaryTradeLock = false;
        bool hadTradeLockAttribute = false;
        DateTimeOffset? tradeLockExpiresAt = null;
        int? graffitiUsesRemaining = null;
        int graffitiTintId = 0;
        var stickerSlots = CreateStickerSlots();
        var keychain = new KeychainBuilder();

        foreach (var attr in proto.Attribute)
        {
            if (TryMapStickerAttribute(attr, stickerSlots))
                continue;

            switch (attr.DefIndex)
            {
                case 6:
                    paintIndex = (int)ReadAttributeFloat(attr);
                    break;
                case 8:
                    paintWear = ReadAttributeFloat(attr);
                    break;
                case 7:
                    paintSeed = (int)ReadAttributeFloat(attr);
                    break;
                case 270:
                    casketItemCount = ReadAttributeInt32(attr);
                    isCasket = true;
                    break;
                case 299:
                    keychain.KeychainId = ReadAttributeInt32(attr);
                    break;
                case 300:
                    keychain.OffsetX = ReadAttributeFloat(attr);
                    break;
                case 301:
                    keychain.OffsetY = ReadAttributeFloat(attr);
                    break;
                case 302:
                    keychain.OffsetZ = ReadAttributeFloat(attr);
                    break;
                case 306:
                    keychain.Seed = ReadAttributeInt32(attr);
                    break;
                case 314:
                    keychain.HighlightId = ReadAttributeInt32(attr);
                    break;
                case 321:
                    keychain.StickerId = ReadAttributeInt32(attr);
                    break;
                case 75:
                    var tradableAfterSec = (long)(uint)ReadAttributeInt32(attr);
                    hadTradeLockAttribute = true;
                    if (tradableAfterSec > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        isTemporaryTradeLock = true;
                        tradeLockExpiresAt = DateTimeOffset.FromUnixTimeSeconds(tradableAfterSec);
                    }
                    break;
                case 232:
                    graffitiUsesRemaining = ReadAttributeInt32(attr);
                    break;
                case 233:
                    graffitiTintId = ReadAttributeInt32(attr);
                    break;
            }
        }

        var defIndex = (uint)proto.DefIndex;
        var baseName = _itemDefs?.GetItemName(defIndex) ?? $"Item #{defIndex}";
        string skinName;

        var stickerKitId = stickerSlots[0].StickerId;
        var itemStickerKitId = IsStickerTypeItem(defIndex) ? stickerKitId : 0;
        if (defIndex == 1355 && keychain.KeychainId > 0)
            skinName = _itemDefs?.GetKeychainName(keychain.KeychainId) ?? $"#{keychain.KeychainId}";
        else if (paintIndex == 0 && itemStickerKitId > 0)
            skinName = _itemDefs?.GetStickerKitName(itemStickerKitId) ?? $"#{itemStickerKitId}";
        else
            skinName = _itemDefs?.GetPaintKitName(paintIndex) ?? string.Empty;

        _logger.LogDebug(
            "Item {Id}: DefIndex={DefIndex}, PaintIndex={PaintIndex}, StickerKit={StickerKit}, KeychainId={KeychainId}, SkinName='{SkinName}', BaseName='{BaseName}'",
            proto.Id, defIndex, paintIndex, stickerKitId, keychain.KeychainId, skinName, baseName);

        var marketHashName = _itemDefs?.GetMarketHashName(defIndex, paintIndex, paintWear, itemStickerKitId, keychain.KeychainId, graffitiTintId) ?? string.Empty;
        var itemName = BuildDisplayName(baseName, skinName);
        var resolvedStickers = CanHaveStickers(defIndex, paintIndex) ? ResolveStickers(stickerSlots) : [];
        var resolvedKeychain = ResolveKeychain(keychain);

        return new InventoryItem
        {
            Id = proto.Id,
            OriginalId = proto.OriginalId,
            InventoryPosition = proto.Inventory,
            DefIndex = defIndex,
            BaseName = baseName,
            CustomName = proto.CustomName ?? string.Empty,
            CustomDescription = proto.CustomDesc ?? string.Empty,
            Quality = proto.Quality,
            Rarity = (ItemRarity)proto.Rarity,
            Exterior = GetExterior(paintWear),
            PaintIndex = paintIndex,
            SkinName = skinName,
            PaintWear = paintWear,
            PaintSeed = paintSeed,
            Stickers = resolvedStickers,
            Keychain = resolvedKeychain,
            IsCasket = isCasket,
            CasketItemCount = casketItemCount,
            IconUrl = _itemDefs?.GetItemIconPath(defIndex, paintIndex, itemStickerKitId, keychain.KeychainId, graffitiTintId) ?? string.Empty,
            Name = itemName,
            GroupKey = BuildGroupKey(defIndex, paintIndex, paintWear, isCasket, stickerKitId, keychain.KeychainId),
            MarketHashName = string.IsNullOrWhiteSpace(marketHashName) ? BuildDisplayName(baseName, skinName) : marketHashName,
            Tradable = false,
            Marketable = false,
            IsTemporaryTradeLock = isTemporaryTradeLock,
            TradeLockExpiresAt = tradeLockExpiresAt,
            CanFetchMarketPrice = hadTradeLockAttribute,
            IsGraffiti = defIndex is 1348 or 1349,
            GraffitiUsesRemaining = graffitiUsesRemaining,
            GraffitiTintId = graffitiTintId,
            RarityColor = _itemDefs?.GetRarityColor(proto.Rarity) ?? string.Empty,
            QualityColor = _itemDefs?.GetQualityColor(proto.Quality) ?? string.Empty,
            Origin = (int)proto.Origin
        };
    }

    private static void ApplyAcquiredOrder(List<InventoryItem> items)
    {
        var rankedItems = items
            .Select((item, index) => new { item, index })
            .OrderByDescending(x => GetNewestRank(x.item))
            .ThenBy(x => x.index)
            .ToList();

        for (var i = 0; i < rankedItems.Count; i++)
            rankedItems[i].item.AcquiredOrder = rankedItems.Count - i;
    }

    private static ulong GetNewestRank(InventoryItem item)
    {
        var assetRank = Math.Max(item.Id, item.OriginalId);
        if (assetRank > 0)
            return assetRank;

        return item.InventoryPosition;
    }

    private async Task EnrichFromWebApiAsync(List<InventoryItem> items)
    {
        var steamId64 = _connection.SteamId?.ConvertToUInt64();
        if (steamId64 is null)
            return;

        try
        {
            var (byAssetId, byMarketHashName, byBaseName) = await FetchWebApiInventoryDataAsync(steamId64.Value);
            _webApiByMarketHashName = byMarketHashName;
            _webApiByBaseName = byBaseName;

            if (_itemDefs is not null)
            {
                var iconCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (mhn, data) in byMarketHashName)
                {
                    if (!string.IsNullOrEmpty(data.IconUrl))
                        iconCache.TryAdd(mhn, data.IconUrl);
                }
                foreach (var (baseName, data) in byBaseName)
                {
                    if (!string.IsNullOrEmpty(data.IconUrl))
                        iconCache.TryAdd(baseName, data.IconUrl);
                }
                _itemDefs.PopulateIconCache(iconCache);
            }

            var enrichedIcons = 0;
            var enrichedFlags = 0;
            var unmatchedCount = 0;
            foreach (var item in items)
            {
                var data = ResolveWebApiData(item, byAssetId);
                if (data is null)
                {
                    unmatchedCount++;
                    continue;
                }

                if (!string.IsNullOrEmpty(data.IconUrl))
                {
                    item.IconUrl = data.IconUrl;
                    enrichedIcons++;
                }
                item.Tradable = data.Tradable;
                item.Marketable = data.Marketable;
                item.CanFetchMarketPrice = item.CanFetchMarketPrice || data.CanFetchMarketPrice;
                item.IsTemporaryTradeLock = item.IsTemporaryTradeLock || data.IsTemporaryTradeLock;
                enrichedFlags++;
            }

            _logger.LogInformation(
                "Enriched {Icons} icons and {Flags}/{Total} tradable/marketable flags from Steam web API ({Unmatched} unmatched)",
                enrichedIcons, enrichedFlags, items.Count, unmatchedCount);

            var unmatched = items.Where(i => string.IsNullOrEmpty(i.IconUrl) || IsLikelyVpkPath(i.IconUrl)).ToList();
            if (unmatched.Count > 0)
            {
                _logger.LogDebug("Items with no icon after web API enrichment: {Items}",
                    string.Join(", ", unmatched.Select(i => $"{i.Name} (MHN={i.MarketHashName})")));
                await FetchMissingIconsFromMarketAsync(unmatched);
            }

            ShareIconsByDefIndex(items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich from Steam web API — icons and flags may be inaccurate");
        }
    }

    private async Task EnrichItemsFromCachedWebApiAsync(List<InventoryItem> items)
    {
        var enriched = 0;
        var defaulted = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.MarketHashName))
                continue;

            var data = ResolveWebApiData(item);
            if (data is not null)
            {
                if (!string.IsNullOrEmpty(data.IconUrl))
                    item.IconUrl = data.IconUrl;
                item.Tradable = data.Tradable;
                item.Marketable = data.Marketable;
                item.CanFetchMarketPrice = item.CanFetchMarketPrice || data.CanFetchMarketPrice;
                item.IsTemporaryTradeLock = item.IsTemporaryTradeLock || data.IsTemporaryTradeLock;
                enriched++;
            }
            else
            {
                item.Marketable = false;
                item.Tradable = false;
                item.CanFetchMarketPrice = !string.IsNullOrWhiteSpace(item.MarketHashName);
                item.IsTemporaryTradeLock = false;
                defaulted++;
            }
        }

        if (enriched > 0 || defaulted > 0)
            _logger.LogInformation("Enriched {Enriched} casket items from cached web API data, {Defaulted} defaulted to price-fetchable by market hash", enriched, defaulted);

        var unmatched = items.Where(i => string.IsNullOrEmpty(i.IconUrl) || IsLikelyVpkPath(i.IconUrl)).ToList();
        if (unmatched.Count > 0)
            await FetchMissingIconsFromMarketAsync(unmatched);
    }

    private WebApiItemData? ResolveWebApiData(InventoryItem item, Dictionary<ulong, WebApiItemData>? byAssetId = null)
    {
        if (byAssetId is not null && byAssetId.TryGetValue(item.Id, out var assetData))
            return assetData;

        if (byAssetId is not null && item.OriginalId != item.Id && byAssetId.TryGetValue(item.OriginalId, out var origData))
            return origData;

        if (!string.IsNullOrEmpty(item.MarketHashName))
        {
            if (_webApiByMarketHashName?.TryGetValue(item.MarketHashName, out var nameData) == true)
                return nameData;

            var baseName = StripExterior(item.MarketHashName);
            if (_webApiByBaseName?.TryGetValue(baseName, out var baseData) == true)
                return baseData;

            var genericBase = StripTrailingParenthetical(item.MarketHashName);
            if (genericBase != item.MarketHashName && _webApiByBaseName?.TryGetValue(genericBase, out var genericData) == true)
                return genericData;
        }

        return null;
    }

    private static string StripExterior(string marketHashName)
    {
        ReadOnlySpan<string> exteriors =
        [
            " (Factory New)", " (Minimal Wear)", " (Field-Tested)",
            " (Well-Worn)", " (Battle-Scarred)"
        ];
        foreach (var ext in exteriors)
        {
            if (marketHashName.EndsWith(ext, StringComparison.Ordinal))
                return marketHashName[..^ext.Length];
        }
        return marketHashName;
    }

    private static string StripTrailingParenthetical(string value)
    {
        var idx = value.LastIndexOf(" (", StringComparison.Ordinal);
        if (idx > 0 && value.EndsWith(')'))
            return value[..idx];
        return value;
    }

    private async Task<(Dictionary<ulong, WebApiItemData> ByAssetId, Dictionary<string, WebApiItemData> ByMarketHashName, Dictionary<string, WebApiItemData> ByBaseName)> FetchWebApiInventoryDataAsync(ulong steamId64)
    {
        var byAssetId = new Dictionary<ulong, WebApiItemData>();
        var byMarketHashName = new Dictionary<string, WebApiItemData>(StringComparer.OrdinalIgnoreCase);
        var byBaseName = new Dictionary<string, WebApiItemData>(StringComparer.OrdinalIgnoreCase);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        string? lastAssetId = null;
        var pageCount = 0;
        const int maxPages = 20;

        do
        {
            var url = $"https://steamcommunity.com/inventory/{steamId64}/{CS2GameDefinition.CS2AppId}/2?l=english&count=2000";
            if (lastAssetId is not null)
                url += $"&start_assetid={lastAssetId}";

            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("success", out var success) || success.GetInt32() != 1)
                break;

            var descMap = new Dictionary<string, WebApiItemData>(StringComparer.Ordinal);
            if (root.TryGetProperty("descriptions", out var descriptions))
            {
                ExtractStickerIconsFromDescriptions(descriptions);

                foreach (var desc in descriptions.EnumerateArray())
                {
                    var classid = desc.TryGetProperty("classid", out var c) ? c.GetString() : null;
                    var instanceid = desc.TryGetProperty("instanceid", out var i) ? i.GetString() ?? "0" : "0";
                    string? iconUrl = null;
                    if (desc.TryGetProperty("icon_url_large", out var large))
                        iconUrl = large.GetString();
                    if (string.IsNullOrEmpty(iconUrl) && desc.TryGetProperty("icon_url", out var small))
                        iconUrl = small.GetString();

                    var tradable = desc.TryGetProperty("tradable", out var t) && t.GetInt32() == 1;
                    var marketable = desc.TryGetProperty("marketable", out var m) && m.GetInt32() == 1;
                    var marketHashName = desc.TryGetProperty("market_hash_name", out var mhn) ? mhn.GetString() : null;
                    var hasMarketListing = HasMarketListingCapability(desc);
                    var hasTemporaryAvailability = HasTemporaryMarketAvailabilityHint(desc);
                    var hasTemporaryHint = hasMarketListing || hasTemporaryAvailability;
                    var canFetchMarketPrice = marketable || hasTemporaryHint;
                    var isTemporaryTradeLock = !tradable && !marketable && hasTemporaryHint;

                    if (classid is not null)
                    {
                        var data = new WebApiItemData(iconUrl ?? string.Empty, tradable, marketable, canFetchMarketPrice, isTemporaryTradeLock);
                        descMap[$"{classid}_{instanceid}"] = data;
                        if (!string.IsNullOrEmpty(marketHashName))
                        {
                            if (byMarketHashName.TryGetValue(marketHashName, out var existingByName))
                                byMarketHashName[marketHashName] = MergeWebApiItemData(existingByName, data);
                            else
                                byMarketHashName[marketHashName] = data;

                            var baseName = StripExterior(marketHashName);
                            if (baseName != marketHashName)
                            {
                                if (byBaseName.TryGetValue(baseName, out var existingByBaseName))
                                    byBaseName[baseName] = MergeWebApiItemData(existingByBaseName, data);
                                else
                                    byBaseName[baseName] = data;
                            }
                            else
                            {
                                var genericBase = StripTrailingParenthetical(marketHashName);
                                if (genericBase != marketHashName)
                                    byBaseName.TryAdd(genericBase, data);
                            }
                        }
                    }
                }
            }

            lastAssetId = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetIdStr = asset.TryGetProperty("assetid", out var a) ? a.GetString() : null;
                    var classid = asset.TryGetProperty("classid", out var c2) ? c2.GetString() : null;
                    var instanceid = asset.TryGetProperty("instanceid", out var i2) ? i2.GetString() ?? "0" : "0";

                    if (assetIdStr is not null && ulong.TryParse(assetIdStr, out var assetId)
                        && classid is not null
                        && descMap.TryGetValue($"{classid}_{instanceid}", out var data))
                    {
                        byAssetId[assetId] = data;
                    }

                    lastAssetId = assetIdStr;
                }
            }

            if (!root.TryGetProperty("more_items", out var more) || more.GetInt32() != 1)
                break;

            pageCount++;
        }
        while (pageCount < maxPages);

        _logger.LogInformation("Fetched {AssetCount} asset entries and {NameCount} unique descriptions from Steam inventory web API",
            byAssetId.Count, byMarketHashName.Count);
        return (byAssetId, byMarketHashName, byBaseName);
    }

    private static bool HasMarketListingCapability(JsonElement desc)
    {
        return HasMarketListingAction(desc, "market_actions")
            || HasMarketListingAction(desc, "actions");
    }

    private static bool HasMarketListingAction(JsonElement desc, string propertyName)
    {
        if (!desc.TryGetProperty(propertyName, out var actions) || actions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var action in actions.EnumerateArray())
        {
            if (!action.TryGetProperty("link", out var link) || link.ValueKind != JsonValueKind.String)
                continue;

            var url = link.GetString();
            if (!string.IsNullOrWhiteSpace(url)
                && url.Contains("/market/listings/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTemporaryMarketAvailabilityHint(JsonElement desc)
    {
        if (HasDescriptionContaining(desc, "owner_descriptions", "trade-protected")
            || HasDescriptionContaining(desc, "descriptions", "trade-protected"))
            return true;

        if (!HasNonZeroMarketRestriction(desc))
            return false;

        return HasAvailabilityText(desc, "owner_descriptions")
            || HasAvailabilityText(desc, "descriptions");
    }

    private static bool HasNonZeroMarketRestriction(JsonElement desc)
    {
        if (!desc.TryGetProperty("market_tradable_restriction", out var restriction))
            return false;

        if (restriction.ValueKind == JsonValueKind.Number)
            return restriction.TryGetInt32(out var value) && value > 0;

        return restriction.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(restriction.GetString());
    }

    private static bool HasAvailabilityText(JsonElement desc, string propertyName)
    {
        if (!desc.TryGetProperty(propertyName, out var descriptions) || descriptions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var entry in descriptions.EnumerateArray())
        {
            if (!entry.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Contains("Tradable After", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Marketable After", StringComparison.OrdinalIgnoreCase)
                || text.Contains("can be traded after", StringComparison.OrdinalIgnoreCase)
                || text.Contains("can be marketed after", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDescriptionContaining(JsonElement desc, string propertyName, string keyword)
    {
        if (!desc.TryGetProperty(propertyName, out var descriptions) || descriptions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var entry in descriptions.EnumerateArray())
        {
            if (!entry.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)
                && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task FetchMissingIconsFromMarketAsync(List<InventoryItem> items)
    {
        var mhns = items
            .Where(i => !string.IsNullOrWhiteSpace(i.MarketHashName))
            .Select(i => i.MarketHashName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mhns.Count == 0)
            return;

        await FetchIconsFromMarketAsync(mhns);

        foreach (var item in items)
        {
            if ((!string.IsNullOrEmpty(item.IconUrl) && !IsLikelyVpkPath(item.IconUrl))
                || string.IsNullOrWhiteSpace(item.MarketHashName))
                continue;

            var resolved = _itemDefs?.ResolveIconHash(item.MarketHashName)
                        ?? (!string.IsNullOrEmpty(item.BaseName) && item.BaseName != item.MarketHashName
                            ? _itemDefs?.ResolveIconHash(item.BaseName)
                            : null);
            if (!string.IsNullOrEmpty(resolved))
                item.IconUrl = resolved;
        }

        var stillMissing = items.Where(i => string.IsNullOrEmpty(i.IconUrl) || IsLikelyVpkPath(i.IconUrl)).ToList();
        if (stillMissing.Count > 0)
            _logger.LogWarning("Items still without icon after market fallback: {Items}",
                string.Join(", ", stillMissing.Select(i => $"{i.Name} (MHN={i.MarketHashName})")));
    }

    private static void ShareIconsByDefIndex(List<InventoryItem> items)
    {
        var iconByDefIndex = new Dictionary<uint, string>();
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.IconUrl) && !IsLikelyVpkPath(item.IconUrl))
                iconByDefIndex.TryAdd(item.DefIndex, item.IconUrl);
        }

        foreach (var item in items)
        {
            if ((string.IsNullOrEmpty(item.IconUrl) || IsLikelyVpkPath(item.IconUrl))
                && iconByDefIndex.TryGetValue(item.DefIndex, out var sharedIcon))
            {
                item.IconUrl = sharedIcon;
            }
        }
    }

    private static WebApiItemData MergeWebApiItemData(WebApiItemData existing, WebApiItemData candidate)
    {
        var iconUrl = !string.IsNullOrWhiteSpace(existing.IconUrl) ? existing.IconUrl : candidate.IconUrl;
        return new WebApiItemData(
            iconUrl,
            existing.Tradable || candidate.Tradable,
            existing.Marketable || candidate.Marketable,
            existing.CanFetchMarketPrice || candidate.CanFetchMarketPrice,
            existing.IsTemporaryTradeLock || candidate.IsTemporaryTradeLock);
    }

    public async Task<int?> GetInventoryCountAsync(CancellationToken ct = default)
    {
        var steamId64 = _connection.SteamId?.ConvertToUInt64();
        if (steamId64 is null)
            return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var url = $"https://steamcommunity.com/inventory/{steamId64}/{CS2GameDefinition.CS2AppId}/2?l=english&count=1";
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("total_inventory_count", out var count))
                return count.GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch inventory count from Steam web API");
        }

        return null;
    }

    public async Task FetchIconsFromMarketAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default)
    {
        if (_itemDefs is null || marketHashNames.Count == 0)
            return;

        _logger.LogInformation("Fetching icons from Steam Market for {Count} items", marketHashNames.Count);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        foreach (var mhn in marketHashNames)
        {
            ct.ThrowIfCancellationRequested();

            if (_itemDefs.ResolveIconHash(mhn) is not null)
                continue;

            try
            {
                var encodedMhn = Uri.EscapeDataString(mhn);
                var url = $"https://steamcommunity.com/market/listings/{CS2GameDefinition.CS2AppId}/{encodedMhn}/render/?start=0&count=1&currency=1&language=english";

                var json = await http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
                    continue;

                if (!root.TryGetProperty("assets", out var assets)
                    || assets.ValueKind != JsonValueKind.Object)
                    continue;

                string? iconUrl = null;

                if (assets.TryGetProperty(CS2GameDefinition.CS2AppId.ToString(), out var app)
                    && app.TryGetProperty("2", out var ctx))
                {
                    foreach (var prop in ctx.EnumerateObject())
                    {
                        iconUrl = prop.Value.TryGetProperty("icon_url_large", out var lg) ? lg.GetString() : null;
                        iconUrl ??= prop.Value.TryGetProperty("icon_url", out var sm) ? sm.GetString() : null;
                        if (!string.IsNullOrEmpty(iconUrl))
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(iconUrl))
                {
                    _itemDefs.PopulateIconCache(
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [mhn] = iconUrl });
                    _logger.LogDebug("Resolved icon from market for '{MHN}'", mhn);
                }

                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch market icon for '{MHN}'", mhn);
            }
        }
    }

    private static ItemExterior GetExterior(float wear) => wear switch
    {
        0 => ItemExterior.None,
        < 0.07f => ItemExterior.FactoryNew,
        < 0.15f => ItemExterior.MinimalWear,
        < 0.38f => ItemExterior.FieldTested,
        < 0.45f => ItemExterior.WellWorn,
        _ => ItemExterior.BattleScarred
    };

    private static string BuildDisplayName(string baseName, string skinName)
    {
        return string.IsNullOrWhiteSpace(skinName)
            ? baseName
            : $"{baseName} | {skinName}";
    }

    private static string BuildGroupKey(uint defIndex, int paintIndex, float paintWear, bool isCasket, int stickerKitId = 0, int keychainId = 0)
    {
        if (isCasket)
            return $"casket:{defIndex}";

        return $"{defIndex}:{paintIndex}:{(int)GetExterior(paintWear)}:{stickerKitId}:{keychainId}";
    }

    private List<InventorySticker> ResolveStickers(StickerBuilder[] stickerSlots)
    {
        var stickers = new List<InventorySticker>();

        foreach (var slot in stickerSlots.Where(s => s.StickerId > 0).OrderBy(s => s.Slot))
        {
            var name = _itemDefs?.GetStickerKitName(slot.StickerId) ?? $"Sticker #{slot.StickerId}";
            stickers.Add(new InventorySticker
            {
                Slot = slot.Slot,
                StickerId = slot.StickerId,
                Name = name,
                Wear = slot.Wear,
                Scale = slot.Scale,
                Rotation = slot.Rotation,
                OffsetX = slot.OffsetX,
                OffsetY = slot.OffsetY,
                Schema = slot.Schema
            });
        }

        return stickers;
    }

    private InventoryKeychain? ResolveKeychain(KeychainBuilder source)
    {
        if (source.KeychainId <= 0)
            return null;

        return new InventoryKeychain
        {
            KeychainId = source.KeychainId,
            Name = _itemDefs?.GetKeychainName(source.KeychainId) ?? $"Keychain #{source.KeychainId}",
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            OffsetZ = source.OffsetZ,
            Seed = source.Seed,
            HighlightId = source.HighlightId,
            StickerId = source.StickerId,
            StickerName = source.StickerId > 0
                ? _itemDefs?.GetStickerKitName(source.StickerId) ?? $"Sticker #{source.StickerId}"
                : string.Empty
        };
    }

    private static StickerBuilder[] CreateStickerSlots()
    {
        return
        [
            new StickerBuilder(0),
            new StickerBuilder(1),
            new StickerBuilder(2),
            new StickerBuilder(3)
        ];
    }

    private static bool TryMapStickerAttribute(CSOEconItemAttribute attr, StickerBuilder[] stickerSlots)
    {
        for (var slot = 0; slot < stickerSlots.Length; slot++)
        {
            var idBase = 113 + (slot * 4);
            var offsetBase = 278 + (slot * 2);
            var schemaId = 290 + slot;
            var target = stickerSlots[slot];

            switch (attr.DefIndex)
            {
                case uint defIndex when defIndex == idBase:
                    target.StickerId = ReadAttributeInt32(attr);
                    return true;
                case uint defIndex when defIndex == idBase + 1:
                    target.Wear = ReadAttributeFloat(attr);
                    return true;
                case uint defIndex when defIndex == idBase + 2:
                    target.Scale = ReadAttributeFloat(attr);
                    return true;
                case uint defIndex when defIndex == idBase + 3:
                    target.Rotation = ReadAttributeFloat(attr);
                    return true;
                case uint defIndex when defIndex == offsetBase:
                    target.OffsetX = ReadAttributeFloat(attr);
                    return true;
                case uint defIndex when defIndex == offsetBase + 1:
                    target.OffsetY = ReadAttributeFloat(attr);
                    return true;
                case uint defIndex when defIndex == schemaId:
                    target.Schema = ReadAttributeInt32(attr);
                    return true;
            }
        }

        return false;
    }

    private static float ReadAttributeFloat(CSOEconItemAttribute attr)
    {
        if (attr.ValueBytes.Length >= 4)
            return BitConverter.ToSingle(attr.ValueBytes, 0);

        if (attr.Value != 0)
            return BitConverter.Int32BitsToSingle(unchecked((int)attr.Value));

        return 0;
    }

    private static int ReadAttributeInt32(CSOEconItemAttribute attr)
    {
        if (attr.ValueBytes.Length >= 4)
            return (int)BitConverter.ToUInt32(attr.ValueBytes, 0);

        return (int)attr.Value;
    }

    private static bool IsContainedInCasket(CSOEconItem item)
        => item.Attribute.Any(a => a.DefIndex == 272);

    private static ulong GetCasketIdFromAttributes(CSOEconItem item)
    {
        uint low = 0, high = 0;
        foreach (var attr in item.Attribute)
        {
            if (attr.DefIndex == 272)
                low = (uint)ReadAttributeInt32(attr);
            else if (attr.DefIndex == 273)
                high = (uint)ReadAttributeInt32(attr);
        }
        return ((ulong)high << 32) | low;
    }

    private static CMsgCasketContents ParseCasketContents(byte[] payload)
    {
        var result = new CMsgCasketContents();
        var span = payload.AsSpan();
        var offset = 0;

        while (offset < span.Length)
        {
            var tag = ReadVarint(span, ref offset);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 0x7);

            switch (field)
            {
                case 1 when wireType == 0:
                    result.CasketId = ReadVarint(span, ref offset);
                    break;
                case 2 when wireType == 2:
                    result.Items.Add(ParseEconItem(ReadLengthDelimited(span, ref offset).ToArray()));
                    break;
                default:
                    SkipField(span, ref offset, wireType);
                    break;
            }
        }

        return result;
    }

    private static CSOEconItem ParseEconItem(byte[] payload)
    {
        var item = new CSOEconItem();
        var span = payload.AsSpan();
        var offset = 0;

        while (offset < span.Length)
        {
            var tag = ReadVarint(span, ref offset);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 0x7);

            switch (field)
            {
                case 1 when wireType == 0:
                    item.Id = ReadVarint(span, ref offset);
                    break;
                case 2 when wireType == 0:
                    item.AccountId = (uint)ReadVarint(span, ref offset);
                    break;
                case 3 when wireType == 0:
                    item.Inventory = (uint)ReadVarint(span, ref offset);
                    break;
                case 4 when wireType == 0:
                    item.DefIndex = (int)ReadVarint(span, ref offset);
                    break;
                case 5 when wireType == 0:
                    item.Quantity = (uint)ReadVarint(span, ref offset);
                    break;
                case 6 when wireType == 0:
                    item.Level = (uint)ReadVarint(span, ref offset);
                    break;
                case 7 when wireType == 0:
                    item.Quality = (int)ReadVarint(span, ref offset);
                    break;
                case 8 when wireType == 0:
                    item.Flags = (uint)ReadVarint(span, ref offset);
                    break;
                case 9 when wireType == 0:
                    item.Origin = (uint)ReadVarint(span, ref offset);
                    break;
                case 10 when wireType == 2:
                    item.CustomName = Encoding.UTF8.GetString(ReadLengthDelimited(span, ref offset));
                    break;
                case 11 when wireType == 2:
                    item.CustomDesc = Encoding.UTF8.GetString(ReadLengthDelimited(span, ref offset));
                    break;
                case 12 when wireType == 2:
                    item.Attribute.Add(ParseEconItemAttribute(ReadLengthDelimited(span, ref offset)));
                    break;
                case 14 when wireType == 0:
                    item.InUse = ReadVarint(span, ref offset) != 0;
                    break;
                case 15 when wireType == 0:
                    item.Style = (uint)ReadVarint(span, ref offset);
                    break;
                case 16 when wireType == 0:
                    item.OriginalId = ReadVarint(span, ref offset);
                    break;
                case 18 when wireType == 2:
                    item.EquippedState.Add(ParseEquippedState(ReadLengthDelimited(span, ref offset)));
                    break;
                case 19 when wireType == 0:
                    item.Rarity = (byte)ReadVarint(span, ref offset);
                    break;
                default:
                    SkipField(span, ref offset, wireType);
                    break;
            }
        }

        return item;
    }

    private static CSOEconItemAttribute ParseEconItemAttribute(ReadOnlySpan<byte> payload)
    {
        var attr = new CSOEconItemAttribute();
        var offset = 0;

        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 0x7);

            switch (field)
            {
                case 1 when wireType == 0:
                    attr.DefIndex = (uint)ReadVarint(payload, ref offset);
                    break;
                case 2 when wireType == 0:
                    attr.Value = (uint)ReadVarint(payload, ref offset);
                    break;
                case 3 when wireType == 2:
                    attr.ValueBytes = ReadLengthDelimited(payload, ref offset).ToArray();
                    break;
                default:
                    SkipField(payload, ref offset, wireType);
                    break;
            }
        }

        return attr;
    }

    private static CSOEconItemEquipped ParseEquippedState(ReadOnlySpan<byte> payload)
    {
        var equipped = new CSOEconItemEquipped();
        var offset = 0;

        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 0x7);

            switch (field)
            {
                case 1 when wireType == 0:
                    equipped.NewClass = (uint)ReadVarint(payload, ref offset);
                    break;
                case 2 when wireType == 0:
                    equipped.NewSlot = (uint)ReadVarint(payload, ref offset);
                    break;
                default:
                    SkipField(payload, ref offset, wireType);
                    break;
            }
        }

        return equipped;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> span, ref int offset)
    {
        ulong value = 0;
        var shift = 0;

        while (offset < span.Length)
        {
            var b = span[offset++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;

            shift += 7;
            if (shift >= 64)
                throw new InvalidOperationException("Invalid varint in protobuf payload.");
        }

        throw new InvalidOperationException("Unexpected end of protobuf payload while reading varint.");
    }

    private static ReadOnlySpan<byte> ReadLengthDelimited(ReadOnlySpan<byte> span, ref int offset)
    {
        var length = checked((int)ReadVarint(span, ref offset));
        if (length < 0 || offset + length > span.Length)
            throw new InvalidOperationException("Invalid length-delimited field in protobuf payload.");

        var slice = span.Slice(offset, length);
        offset += length;
        return slice;
    }

    private static void SkipField(ReadOnlySpan<byte> span, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(span, ref offset);
                break;
            case 1:
                offset += 8;
                break;
            case 2:
                _ = ReadLengthDelimited(span, ref offset);
                break;
            case 5:
                offset += 4;
                break;
            default:
                throw new InvalidOperationException($"Unsupported protobuf wire type: {wireType}.");
        }

        if (offset > span.Length)
            throw new InvalidOperationException("Unexpected end of protobuf payload while skipping field.");
    }

    private sealed class StickerBuilder(int slot)
    {
        public int Slot { get; } = slot;
        public int StickerId { get; set; }
        public float Wear { get; set; }
        public float Scale { get; set; }
        public float Rotation { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public int Schema { get; set; }
    }

    private sealed class KeychainBuilder
    {
        public int KeychainId { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; }
        public int Seed { get; set; }
        public int HighlightId { get; set; }
        public int StickerId { get; set; }
    }

    private static bool IsStickerTypeItem(uint defIndex) =>
        defIndex is 1209 or 1348 or 1349 or 4609;

    private static bool CanHaveStickers(uint defIndex, int paintIndex) =>
        IsStickerTypeItem(defIndex)
        || paintIndex > 0
        || defIndex is (>= 1 and <= 64) or (>= 500 and <= 526);

    private static bool IsLikelyVpkPath(string url) =>
        url.StartsWith("econ/", StringComparison.OrdinalIgnoreCase)
        || (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && url.Contains('/')
            && (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)));

    private static readonly Regex StickerImgRegex = new(
        """src="(?<url>https?://[^"]+)"\s*title="(?:Souvenir\s+)?(?<type>Sticker|Charm|Patch):\s*(?<name>[^"]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void ExtractStickerIconsFromDescriptions(JsonElement descriptions)
    {
        if (_itemDefs is null)
            return;

        var iconCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var desc in descriptions.EnumerateArray())
        {
            if (!desc.TryGetProperty("descriptions", out var innerDescs))
                continue;

            foreach (var inner in innerDescs.EnumerateArray())
            {
                if (!inner.TryGetProperty("value", out var valProp))
                    continue;

                var html = valProp.GetString();
                if (string.IsNullOrEmpty(html)
                    || !(html.Contains("sticker_info", StringComparison.OrdinalIgnoreCase)
                         || html.Contains("keychain_info", StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (Match match in StickerImgRegex.Matches(html))
                {
                    var iconUrl = match.Groups["url"].Value;
                    var type = match.Groups["type"].Value;
                    var name = match.Groups["name"].Value.Trim();

                    if (string.IsNullOrEmpty(iconUrl) || string.IsNullOrEmpty(name))
                        continue;

                    var mhn = $"{type} | {name}";
                    iconCache.TryAdd(mhn, iconUrl);
                }
            }
        }

        if (iconCache.Count > 0)
        {
            _itemDefs.PopulateIconCache(iconCache);
            _logger.LogDebug("Extracted {Count} sticker/charm icon URLs from web API descriptions", iconCache.Count);
        }
    }
}
