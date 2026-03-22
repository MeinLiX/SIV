namespace SIV.Domain.Games;

public interface IGameDefinition
{
    uint AppId { get; }
    string Name { get; }
    string IconPath { get; }
}
