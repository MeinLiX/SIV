using Microsoft.Extensions.Logging;
using SIV.Application.Interfaces;
using SIV.Domain.Games;

namespace SIV.Infrastructure.Steam.GC;

public sealed class GCServiceFactory : IGCServiceFactory
{
    private readonly SteamConnectionService _connection;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEnumerable<IItemDefinitionProvider> _itemDefs;
    private readonly List<IGameDefinition> _games;
    private readonly Dictionary<uint, IGCService> _cache = new();

    public IReadOnlyList<IGameDefinition> SupportedGames => _games;

    public GCServiceFactory(
        SteamConnectionService connection,
        IEnumerable<IGameDefinition> games,
        ILoggerFactory loggerFactory,
        IEnumerable<IItemDefinitionProvider> itemDefs)
    {
        _connection = connection;
        _loggerFactory = loggerFactory;
        _itemDefs = itemDefs;
        _games = games.ToList();

        _connection.Disconnected += _ => ResetAll();
    }

    private void ResetAll()
    {
        foreach (var gc in _cache.Values)
        {
            try { gc.DisconnectAsync(); } catch { }
        }
        _cache.Clear();
    }

    public IGCService Create(IGameDefinition game)
    {
        if (_cache.TryGetValue(game.AppId, out var existing))
            return existing;

        var itemDef = _itemDefs.FirstOrDefault(d => d.AppId == game.AppId);

        IGCService service = game.AppId switch
        {
            CS2GameDefinition.CS2AppId => new CS2GCService(_connection, _loggerFactory.CreateLogger<CS2GCService>(), itemDef),
            _ => throw new NotSupportedException($"Game {game.AppId} is not supported")
        };

        _cache[game.AppId] = service;
        return service;
    }

    public IItemDefinitionProvider? GetItemDefinitionProvider(uint appId)
        => _itemDefs.FirstOrDefault(d => d.AppId == appId);
}
