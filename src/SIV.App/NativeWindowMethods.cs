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

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int GWLP_WNDPROC = -4;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(nint hWnd);

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    // Must be kept alive to prevent GC of the delegate
    private static readonly Dictionary<nint, (WndProcDelegate Proc, nint Prev, int MinWidth, int MinHeight)> SubclassedWindows = [];

    internal static void SetMinWindowSize(Window window, int minWidth, int minHeight)
    {
        var hwnd = WindowNative.GetWindowHandle(window);

        WndProcDelegate newProc = (hWnd, msg, wParam, lParam) =>
        {
            if (msg == WM_GETMINMAXINFO && SubclassedWindows.TryGetValue(hWnd, out var entry))
            {
                var dpi = GetDpiForWindow(hWnd);
                var scaleFactor = dpi / 96.0;
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.X = (int)(entry.MinWidth * scaleFactor);
                info.ptMinTrackSize.Y = (int)(entry.MinHeight * scaleFactor);
                Marshal.StructureToPtr(info, lParam, false);
                return 0;
            }

            return SubclassedWindows.TryGetValue(hWnd, out var e)
                ? CallWindowProc(e.Prev, hWnd, msg, wParam, lParam)
                : 0;
        };

        var prev = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(newProc));
        SubclassedWindows[hwnd] = (newProc, prev, minWidth, minHeight);
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
