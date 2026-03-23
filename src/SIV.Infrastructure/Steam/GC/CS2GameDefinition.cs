using SIV.Domain.Games;

namespace SIV.Infrastructure.Steam.GC;

public sealed class CS2GameDefinition : IGameDefinition
{
    public const uint CS2AppId = 730;

    public uint AppId => CS2AppId;
    public string Name => "Counter-Strike 2";
    public string IconPath => "ms-appx:///Assets/games/cs2/cs2_main_icon.jpg";
}
