using SIV.Domain.Games;

namespace SIV.Application.Interfaces;

public interface IGCServiceFactory
{
    IGCService Create(IGameDefinition game);
    IReadOnlyList<IGameDefinition> SupportedGames { get; }
    IItemDefinitionProvider? GetItemDefinitionProvider(uint appId);
}
