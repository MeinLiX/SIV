using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.Interfaces;
using SIV.Domain.Entities;
using SIV.UI.Services;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class InventoryGroupDetailViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string _groupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<InventoryItemViewModel> _items = [];

    public override string Title => GroupName;

    public InventoryGroupDetailViewModel(
        string groupName,
        IReadOnlyList<InventoryItem> items,
        INavigationService navigation,
        IItemDefinitionProvider? itemDefs = null)
    {
        _navigation = navigation;
        GroupName = groupName;

        var iconByDefIndex = InventoryViewModel.BuildIconMap(items);

        Items = new ObservableCollection<InventoryItemViewModel>(items.Select(i =>
            InventoryViewModel.CreateViewModel(i, itemDefs, iconByDefIndex)));
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigation.GoBack();
    }
}
