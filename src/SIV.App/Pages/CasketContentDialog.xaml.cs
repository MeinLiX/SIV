using Microsoft.UI.Xaml.Controls;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class CasketContentDialog : ContentDialog
{
    public CasketDetailViewModel ViewModel { get; }

    public CasketContentDialog(CasketDetailViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        Title = viewModel.CasketName;
    }
}
