using Microsoft.UI.Xaml;
using SIV.UI.ViewModels;

namespace SIV.App.Pages;

public sealed partial class CasketDetailWindow : Window
{
    private static readonly HashSet<CasketDetailWindow> OpenWindows = [];

    public CasketDetailViewModel ViewModel { get; }

    public CasketDetailWindow(CasketDetailViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        Bindings.Update();
        CasketDescriptionText.Visibility = ViewModel.HasCasketDescription
            ? Visibility.Visible
            : Visibility.Collapsed;

        Title = viewModel.CasketName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        NativeWindowMethods.ConfigurePresenter(this);
        NativeWindowMethods.SetWindowSize(this, 960, 720);
        NativeWindowMethods.SetMinWindowSize(this, 700, 500);
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.CurrentTheme;
            App.ThemeChanged += theme => root.RequestedTheme = theme;
        }

        OpenWindows.Add(this);
        Closed += CasketDetailWindow_Closed;
        ContentFrame.Navigate(typeof(CasketContentPage), viewModel);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void CasketDetailWindow_Closed(object sender, WindowEventArgs args)
    {
        OpenWindows.Remove(this);
        Closed -= CasketDetailWindow_Closed;
    }
}
