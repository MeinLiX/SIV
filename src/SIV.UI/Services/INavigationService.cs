using SIV.UI.ViewModels;

namespace SIV.UI.Services;

public interface INavigationService
{
    ViewModelBase? CurrentView { get; }
    bool CanGoBack { get; }
    void NavigateTo(ViewModelBase viewModel);
    void GoBack();
    void ClearHistory();
    event Action? NavigationChanged;
}
