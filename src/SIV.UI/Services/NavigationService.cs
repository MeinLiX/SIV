using SIV.UI.ViewModels;

namespace SIV.UI.Services;

public sealed class NavigationService : INavigationService
{
    private readonly Stack<ViewModelBase> _history = new();

    public ViewModelBase? CurrentView { get; private set; }
    public bool CanGoBack => _history.Count > 0;

    public event Action? NavigationChanged;

    public void NavigateTo(ViewModelBase viewModel)
    {
        if (CurrentView is not null)
            _history.Push(CurrentView);

        CurrentView = viewModel;
        NavigationChanged?.Invoke();
    }

    public void GoBack()
    {
        if (_history.Count == 0) return;

        CurrentView = _history.Pop();
        NavigationChanged?.Invoke();
    }

    public void ClearHistory()
    {
        _history.Clear();
        CurrentView = null;
        NavigationChanged?.Invoke();
    }
}
