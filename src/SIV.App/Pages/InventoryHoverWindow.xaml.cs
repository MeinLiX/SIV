using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SIV.UI.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace SIV.App.Pages;

public sealed partial class InventoryHoverWindow : Window, INotifyPropertyChanged
{
    private const int WindowOuterWidthPadding = 10;
    private const int WindowOuterHeightPadding = 16;
    private const double PreviewChromeHeight = 32;
    private const double CollectionMinWidth = 156;
    private const double CollectionMaxWidth = 760;
    private const double PreviewWidth = 400;
    private const int PreviewMinHeight = 470;
    private const int CollectionMinHeight = 88;
    private const double StickerColumnWidth = 120;
    private const double OriginColumnWidth = 140;
    private const double ContainerDropColumnWidth = 172;
    private const double StickerCardHeight = 156;
    private const double OriginCardHeight = 172;
    private const double ContainerDropCardHeight = 188;
    private const double CollectionChromeWidth = 30;
    private const double CollectionHeaderHeight = 58;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly InventoryHoverWindowMode _mode;
    private double _maxAvailableHeight = 720;

    public event EventHandler<bool>? HoverStateChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public InventoryItemViewModel? Item { get; private set; }

    public InventoryHoverWindow(InventoryHoverWindowMode mode)
    {
        InitializeComponent();

        _mode = mode;
        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;
        RootHitTarget.DataContext = this;

        ConfigureWindowChrome();
        ConfigureMode();

        RootHitTarget.PointerEntered += (_, _) => HoverStateChanged?.Invoke(this, true);
        RootHitTarget.PointerExited += (_, _) => HoverStateChanged?.Invoke(this, false);

        Activate();
        _appWindow.Hide();
    }

    public void SetItem(InventoryItemViewModel item)
    {
        if (ReferenceEquals(Item, item))
        {
            UpdateCollectionContent();
            return;
        }

        Item = item;
        OnPropertyChanged(nameof(Item));
        UpdateCollectionContent();
    }

    public void ShowAt(int x, int y)
    {
        var size = MeasureWindowSize();
        _appWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
        _appWindow.Show(false);
    }

    public void HideWindow()
    {
        _appWindow.Hide();
    }

    public void SetAvailableHeight(int availableHeight)
    {
        _maxAvailableHeight = Math.Max(
            _mode == InventoryHoverWindowMode.Preview ? PreviewMinHeight : CollectionMinHeight,
            availableHeight);
    }

    public SizeInt32 MeasureWindowSize()
    {
        FrameworkElement visibleRoot = _mode == InventoryHoverWindowMode.Preview ? PreviewRoot : CollectionRoot;
        var widthConstraint = _mode == InventoryHoverWindowMode.Preview
            ? PreviewWidth
            : CollectionRoot.Width > 0
                ? CollectionRoot.Width
                : CollectionMinWidth;

        if (_mode == InventoryHoverWindowMode.Preview)
        {
            var previewViewportWidth = Math.Max(0, PreviewWidth - PreviewChromeHeight);
            PreviewContent.Measure(new Size(previewViewportWidth, double.PositiveInfinity));
            PreviewContent.UpdateLayout();

            var desiredContentHeight = Math.Ceiling(PreviewContent.DesiredSize.Height);
            var maxWindowHeight = Math.Max(PreviewMinHeight, _maxAvailableHeight);
            var maxViewportHeight = Math.Max(0, maxWindowHeight - PreviewChromeHeight);
            var viewportHeight = Math.Min(desiredContentHeight, maxViewportHeight);
            var desiredWindowHeight = Math.Clamp(viewportHeight + PreviewChromeHeight, PreviewMinHeight, maxWindowHeight);

            PreviewScrollViewer.MaxHeight = maxViewportHeight;
            PreviewScrollViewer.Height = viewportHeight;
            PreviewRoot.Height = desiredWindowHeight;
            PreviewRoot.MaxHeight = maxWindowHeight;
        }

        visibleRoot.Measure(new Size(widthConstraint, double.PositiveInfinity));
        visibleRoot.UpdateLayout();

        var measuredWidth = visibleRoot.ActualWidth > 0 ? visibleRoot.ActualWidth : visibleRoot.DesiredSize.Width;
        var measuredHeight = visibleRoot.ActualHeight > 0 ? visibleRoot.ActualHeight : visibleRoot.DesiredSize.Height;
        var width = Math.Max(
            _mode == InventoryHoverWindowMode.Preview ? (int)PreviewWidth : (int)Math.Ceiling(widthConstraint),
            (int)Math.Ceiling(measuredWidth));
        var height = Math.Max(
            _mode == InventoryHoverWindowMode.Preview ? PreviewMinHeight : (int)Math.Ceiling(CollectionRoot.Height),
            (int)Math.Ceiling(measuredHeight));
        return new SizeInt32(width + WindowOuterWidthPadding, height + WindowOuterHeightPadding);
    }

    private void ConfigureWindowChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = null;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        _appWindow.IsShownInSwitchers = false;
    }

    private void ConfigureMode()
    {
        var isPreview = _mode == InventoryHoverWindowMode.Preview;
        PreviewRoot.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
        CollectionRoot.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;

        if (!isPreview)
        {
            CollectionTitleText.Text = _mode switch
            {
                InventoryHoverWindowMode.Applied => "Applied",
                InventoryHoverWindowMode.Origins => "Drops from",
                InventoryHoverWindowMode.ContainerDrops => "Possible drops",
                _ => string.Empty
            };
            CollectionScrollViewer.MaxHeight = _mode == InventoryHoverWindowMode.Applied ? 300 : 360;
        }
    }

    private void UpdateCollectionContent()
    {
        if (Item is null || _mode == InventoryHoverWindowMode.Preview)
            return;

        var maxViewportHeight = Math.Max(80, Math.Min(_maxAvailableHeight - CollectionHeaderHeight, _mode == InventoryHoverWindowMode.Applied ? 300 : 360));

        if (_mode == InventoryHoverWindowMode.Applied)
        {
            CollectionCountText.Text = Item.StickerCards.Count.ToString();
            CollectionItemsControl.ItemsSource = Item.StickerCards;
            CollectionItemsControl.ItemTemplate = (DataTemplate)RootHitTarget.Resources["InventoryStickerCardTemplate"];
            UpdateCollectionLayout(Item.StickerCards.Count, StickerColumnWidth, StickerCardHeight, maxViewportHeight, 4);
        }
        else if (_mode == InventoryHoverWindowMode.Origins)
        {
            CollectionCountText.Text = Item.Origins.Count.ToString();
            CollectionItemsControl.ItemsSource = Item.Origins;
            CollectionItemsControl.ItemTemplate = (DataTemplate)RootHitTarget.Resources["InventoryOriginCardTemplate"];
            UpdateCollectionLayout(Item.Origins.Count, OriginColumnWidth, OriginCardHeight, maxViewportHeight, 5);
        }
        else
        {
            CollectionCountText.Text = Item.ContainerDrops.Count.ToString();
            CollectionItemsControl.ItemsSource = Item.ContainerDrops;
            CollectionItemsControl.ItemTemplate = (DataTemplate)RootHitTarget.Resources["InventoryContainerDropTemplate"];
            UpdateCollectionLayout(Item.ContainerDrops.Count, ContainerDropColumnWidth, ContainerDropCardHeight, maxViewportHeight, 4);
        }
    }

    private void UpdateCollectionLayout(
        int itemCount,
        double columnWidth,
        double fallbackItemHeight,
        double maxViewportHeight,
        int maxColumns)
    {
        if (itemCount <= 0)
        {
            CollectionItemsControl.Width = double.NaN;
            CollectionRoot.Width = CollectionMinWidth;
            CollectionScrollViewer.Height = 0;
            CollectionRoot.Height = CollectionMinHeight;
            return;
        }

        var bestColumns = 1;
        var bestItemsWidth = columnWidth;
        var bestContentHeight = fallbackItemHeight;
        var bestVisibleRatio = -1d;
        var bestRootWidth = CollectionMinWidth;

        for (var columns = 1; columns <= Math.Min(itemCount, maxColumns); columns++)
        {
            var itemsWidth = columns * columnWidth;
            CollectionItemsControl.Width = itemsWidth;
            CollectionItemsControl.Measure(new Size(itemsWidth, double.PositiveInfinity));
            CollectionItemsControl.UpdateLayout();

            var measuredHeight = Math.Max(fallbackItemHeight, Math.Ceiling(CollectionItemsControl.DesiredSize.Height));
            var visibleRatio = Math.Min(1d, maxViewportHeight / measuredHeight);
            var rootWidth = Math.Clamp(Math.Ceiling(itemsWidth + CollectionChromeWidth), CollectionMinWidth, CollectionMaxWidth);

            var isBetter = visibleRatio > bestVisibleRatio
                || (Math.Abs(visibleRatio - bestVisibleRatio) < 0.001 && rootWidth < bestRootWidth)
                || (Math.Abs(visibleRatio - bestVisibleRatio) < 0.001
                    && Math.Abs(rootWidth - bestRootWidth) < 0.001
                    && columns < bestColumns);

            if (!isBetter)
                continue;

            bestColumns = columns;
            bestItemsWidth = itemsWidth;
            bestContentHeight = measuredHeight;
            bestVisibleRatio = visibleRatio;
            bestRootWidth = rootWidth;
        }

        CollectionItemsControl.Width = bestItemsWidth;
        CollectionRoot.Width = bestRootWidth;
        CollectionScrollViewer.Height = Math.Min(maxViewportHeight, bestContentHeight);
        CollectionRoot.Height = Math.Max(CollectionMinHeight, CollectionHeaderHeight + CollectionScrollViewer.Height);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
