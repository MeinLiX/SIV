using CommunityToolkit.Mvvm.ComponentModel;

namespace SIV.UI.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusText;

    public virtual string Title => GetType().Name.Replace("ViewModel", "");
}
