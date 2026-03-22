using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIV.Application.Interfaces;
using System.Collections.ObjectModel;

namespace SIV.UI.ViewModels;

public partial class GameSelectorViewModel : ViewModelBase
{
    private readonly IGCServiceFactory _gcFactory;
    private readonly Action<uint> _onGameSelected;

    [ObservableProperty]
    private ObservableCollection<GameViewModel> _games = [];

    [ObservableProperty]
    private GameViewModel? _selectedGame;

    public override string Title => "Select Game";

    public GameSelectorViewModel(IGCServiceFactory gcFactory, Action<uint> onGameSelected)
    {
        _gcFactory = gcFactory;
        _onGameSelected = onGameSelected;
        LoadGames();
        _ = LoadItemCountsAsync();
    }

    private void LoadGames()
    {
        Games.Clear();
        foreach (var def in _gcFactory.SupportedGames)
        {
            Games.Add(new GameViewModel(def));
        }
    }

    private async Task LoadItemCountsAsync()
    {
        foreach (var game in Games)
        {
            game.IsLoadingCount = true;
            try
            {
                var def = _gcFactory.SupportedGames.FirstOrDefault(d => d.AppId == game.AppId);
                if (def is null) continue;

                var service = _gcFactory.Create(def);
                game.ItemCount = await service.GetInventoryCountAsync();
            }
            catch
            {
            }
            finally
            {
                game.IsLoadingCount = false;
            }
        }
    }

    partial void OnSelectedGameChanged(GameViewModel? value)
    {
        if (value is not null)
            SelectGameCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSelectGame))]
    private void SelectGame()
    {
        if (SelectedGame is not null)
            _onGameSelected(SelectedGame.AppId);
    }

    private bool CanSelectGame() => SelectedGame is not null;
}
