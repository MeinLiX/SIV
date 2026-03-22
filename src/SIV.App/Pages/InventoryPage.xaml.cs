using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SIV.UI.ViewModels;
using Windows.Foundation;

namespace SIV.App.Pages;

public sealed partial class InventoryPage : Page
{
    private const int PopupMargin = 12;
    private const int PopupGap = 10;
    private const int AnchorGap = 6;
    private readonly DispatcherQueueTimer _hoverCloseTimer;
    private FrameworkElement? _hoverAnchor;
    private bool _isPointerOverAnchor;
    private bool _isPointerOverHoverWindow;
    private InventoryHoverWindow? _previewWindow;
    private InventoryHoverWindow? _appliedWindow;
    private InventoryHoverWindow? _originsWindow;
    private InventoryHoverWindow? _containerDropsWindow;
    private CancellationTokenSource? _iconResolutionCts;

    public InventoryViewModel ViewModel { get; private set; } = null!;

    public InventoryPage()
    {
        this.InitializeComponent();
        _hoverCloseTimer = DispatcherQueue.CreateTimer();
        _hoverCloseTimer.Interval = TimeSpan.FromMilliseconds(120);
        _hoverCloseTimer.Tick += HoverCloseTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is InventoryViewModel vm)
        {
            ViewModel = vm;
            ViewModel.ShowCasketDialog = OpenCasketWindow;
            Bindings.Update();
            ViewModel.EnsureInitialLoadStarted();
        }
    }

    private void OpenCasketWindow(CasketDetailViewModel casketVm)
    {
        var window = new CasketDetailWindow(casketVm);
        window.Activate();
    }

    private void InventoryItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not InventoryItemViewModel item)
            return;

        _hoverCloseTimer.Stop();
        _hoverAnchor = element;
        _isPointerOverAnchor = true;
        ShowHoverWindows(item, element);
    }

    private void InventoryItem_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not InventoryItemViewModel item)
            return;

        if (!ReferenceEquals(element, _hoverAnchor))
        {
            _hoverAnchor = element;
            ShowHoverWindows(item, element);
        }
    }

    private void InventoryItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !ReferenceEquals(element, _hoverAnchor))
            return;

        _isPointerOverAnchor = false;
        QueueHoverPopupClose();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DisposeHoverWindows();
    }

    private void HoverWindow_HoverStateChanged(object? sender, bool isInside)
    {
        _isPointerOverHoverWindow = isInside;
        if (isInside)
        {
            _hoverCloseTimer.Stop();
        }
        else
        {
            QueueHoverPopupClose();
        }
    }

    private void HoverCloseTimer_Tick(object? sender, object e)
    {
        _hoverCloseTimer.Stop();
        if (_isPointerOverAnchor || _isPointerOverHoverWindow)
            return;

        CloseHoverWindows();
    }

    private void QueueHoverPopupClose()
    {
        if (_isPointerOverAnchor || _isPointerOverHoverWindow)
            return;

        _hoverCloseTimer.Stop();
        _hoverCloseTimer.Start();
    }

    private void EnsureHoverWindows()
    {
        if (_previewWindow is not null)
            return;

        _previewWindow = new InventoryHoverWindow(InventoryHoverWindowMode.Preview);
        _appliedWindow = new InventoryHoverWindow(InventoryHoverWindowMode.Applied);
        _originsWindow = new InventoryHoverWindow(InventoryHoverWindowMode.Origins);
        _containerDropsWindow = new InventoryHoverWindow(InventoryHoverWindowMode.ContainerDrops);

        _previewWindow.HoverStateChanged += HoverWindow_HoverStateChanged;
        _appliedWindow.HoverStateChanged += HoverWindow_HoverStateChanged;
        _originsWindow.HoverStateChanged += HoverWindow_HoverStateChanged;
        _containerDropsWindow.HoverStateChanged += HoverWindow_HoverStateChanged;
    }

    private void ShowHoverWindows(InventoryItemViewModel item, FrameworkElement anchor)
    {
        _iconResolutionCts?.Cancel();

        EnsureHoverWindows();

        _previewWindow!.SetItem(item);
        if (item.HasStickerCards)
            _appliedWindow!.SetItem(item);
        if (item.HasOrigins)
            _originsWindow!.SetItem(item);
        if (item.HasContainerDrops)
            _containerDropsWindow!.SetItem(item);

        UpdateHoverWindows(anchor);

        var hasUnresolvedDrops = item.ContainerDrops.Any(d => string.IsNullOrEmpty(d.IconUrl));
        var hasUnresolvedOrigins = item.Origins.Any(o => string.IsNullOrEmpty(o.IconUrl));

        if (hasUnresolvedDrops || hasUnresolvedOrigins)
        {
            var cts = new CancellationTokenSource();
            _iconResolutionCts = cts;
            _ = ResolveAndRefreshIconsAsync(item, anchor, cts.Token);
        }
    }

    private async Task ResolveAndRefreshIconsAsync(InventoryItemViewModel item, FrameworkElement anchor, CancellationToken ct)
    {
        try
        {
            await ViewModel.ResolveHoverIconsAsync(item, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_hoverAnchor, anchor))
                return;

            if (item.HasContainerDrops)
                _containerDropsWindow?.SetItem(item);
            if (item.HasOrigins)
                _originsWindow?.SetItem(item);

            UpdateHoverWindows(anchor);
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void UpdateHoverWindows(FrameworkElement anchor)
    {
        if (_previewWindow is null || RootLayout.XamlRoot is null)
            return;

        var item = anchor.DataContext as InventoryItemViewModel;
        if (item is null)
            return;

        var transform = anchor.TransformToVisual(RootLayout);
        var topLeft = transform.TransformPoint(new Point(0, 0));
        var scale = RootLayout.XamlRoot.RasterizationScale;
        var mainHwnd = global::WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var clientOrigin = NativeWindowMethods.GetClientOrigin(mainHwnd);
        var anchorScreenX = clientOrigin.X + (int)Math.Round(topLeft.X * scale);
        var anchorScreenY = clientOrigin.Y + (int)Math.Round(topLeft.Y * scale);
        var anchorWidth = (int)Math.Round(anchor.ActualWidth * scale);

        var mainAppWindow = App.MainWindow.AppWindow;
        var workArea = DisplayArea.GetFromWindowId(mainAppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var availableHeight = workArea.Height - (PopupMargin * 2);
        _previewWindow.SetAvailableHeight(availableHeight);
        _appliedWindow?.SetAvailableHeight(availableHeight);
        _originsWindow?.SetAvailableHeight(availableHeight);
        _containerDropsWindow?.SetAvailableHeight(availableHeight);

        var previewSize = _previewWindow.MeasureWindowSize();
        var appliedSize = item.HasStickerCards ? _appliedWindow!.MeasureWindowSize() : new Windows.Graphics.SizeInt32(0, 0);
        var originsSize = item.HasOrigins ? _originsWindow!.MeasureWindowSize() : new Windows.Graphics.SizeInt32(0, 0);
        var containerDropsSize = item.HasContainerDrops ? _containerDropsWindow!.MeasureWindowSize() : new Windows.Graphics.SizeInt32(0, 0);

        var rightColumnWidth = Math.Max(Math.Max(appliedSize.Width, originsSize.Width), containerDropsSize.Width);
        var hasRightColumn = item.HasStickerCards || item.HasOrigins || item.HasContainerDrops;
        var groupWidth = previewSize.Width + (hasRightColumn ? PopupGap + rightColumnWidth : 0);

        var stackHeight = 0;
        if (item.HasStickerCards)
            stackHeight += appliedSize.Height;
        if (item.HasStickerCards && item.HasOrigins)
            stackHeight += (int)PopupGap;
        if (item.HasOrigins)
            stackHeight += originsSize.Height;
        if ((item.HasStickerCards || item.HasOrigins) && item.HasContainerDrops)
            stackHeight += (int)PopupGap;
        if (item.HasContainerDrops)
            stackHeight += containerDropsSize.Height;

        var groupHeight = Math.Max(previewSize.Height, stackHeight);

        var preferredPreviewX = anchorScreenX + anchorWidth + AnchorGap;
        var fallbackPreviewX = anchorScreenX - groupWidth - AnchorGap;
        var previewX = preferredPreviewX + groupWidth <= workArea.X + workArea.Width - PopupMargin
            ? preferredPreviewX
            : fallbackPreviewX;

        var minPreviewX = workArea.X + PopupMargin;
        var maxPreviewX = Math.Max(minPreviewX, workArea.X + workArea.Width - groupWidth - PopupMargin);
        previewX = Math.Clamp(previewX, minPreviewX, maxPreviewX);

        var preferredPreviewY = anchorScreenY;
        var minPreviewY = workArea.Y + PopupMargin;
        var maxPreviewY = Math.Max(minPreviewY, workArea.Y + workArea.Height - groupHeight - PopupMargin);
        var previewY = Math.Clamp(preferredPreviewY, minPreviewY, maxPreviewY);

        _previewWindow.ShowAt(previewX, previewY);

        var sideX = previewX + previewSize.Width + PopupGap;
        var sideY = previewY;

        if (item.HasStickerCards)
        {
            _appliedWindow!.ShowAt(sideX, sideY);
            sideY += appliedSize.Height + PopupGap;
        }
        else
        {
            _appliedWindow!.HideWindow();
        }

        if (item.HasOrigins)
        {
            _originsWindow!.ShowAt(sideX, sideY);
            sideY += originsSize.Height + PopupGap;
        }
        else
        {
            _originsWindow!.HideWindow();
        }

        if (item.HasContainerDrops)
        {
            _containerDropsWindow!.ShowAt(sideX, sideY);
        }
        else
        {
            _containerDropsWindow!.HideWindow();
        }
    }

    private void CloseHoverWindows()
    {
        _iconResolutionCts?.Cancel();
        _previewWindow?.HideWindow();
        _appliedWindow?.HideWindow();
        _originsWindow?.HideWindow();
        _containerDropsWindow?.HideWindow();
        _hoverAnchor = null;
        _isPointerOverHoverWindow = false;
    }

    private void DisposeHoverWindows()
    {
        CloseHoverWindows();

        if (_previewWindow is not null)
        {
            _previewWindow.HoverStateChanged -= HoverWindow_HoverStateChanged;
            _previewWindow.Close();
            _previewWindow = null;
        }

        if (_appliedWindow is not null)
        {
            _appliedWindow.HoverStateChanged -= HoverWindow_HoverStateChanged;
            _appliedWindow.Close();
            _appliedWindow = null;
        }

        if (_originsWindow is not null)
        {
            _originsWindow.HoverStateChanged -= HoverWindow_HoverStateChanged;
            _originsWindow.Close();
            _originsWindow = null;
        }

        if (_containerDropsWindow is not null)
        {
            _containerDropsWindow.HoverStateChanged -= HoverWindow_HoverStateChanged;
            _containerDropsWindow.Close();
            _containerDropsWindow = null;
        }
    }
}
