using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace SIV.App;

internal static class NativeWindowMethods
{
    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    internal static PointInt32 GetClientOrigin(nint hwnd)
    {
        var point = new POINT();
        ClientToScreen(hwnd, ref point);
        return new PointInt32(point.X, point.Y);
    }

    internal static void SetWindowSize(Window window, int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(width, height));
    }

    internal static void ConfigurePresenter(Window window, bool minimizable = false, bool maximizable = false, bool hasBorder = true, bool hasTitleBar = false)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder, hasTitleBar);
            presenter.IsMinimizable = minimizable;
            presenter.IsMaximizable = maximizable;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }
}
