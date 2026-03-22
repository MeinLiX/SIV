using CommunityToolkit.Mvvm.ComponentModel;
using SIV.Domain.Games;

namespace SIV.UI.ViewModels;

public partial class GameViewModel : ObservableObject
{
    [ObservableProperty]
    private uint _appId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int? _itemCount;

    [ObservableProperty]
    private bool _isLoadingCount;

    public GameViewModel(IGameDefinition definition)
    {
        AppId = definition.AppId;
        Name = definition.Name;
        IconPath = definition.IconPath;
    }
}
