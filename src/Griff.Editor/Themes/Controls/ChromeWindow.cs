using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// Window with Griff's custom title bar. <see cref="TitleBarContent"/> sits next to the app mark
/// (the main window puts its menu there). All editor windows should derive from this.
/// A window that opens centered (on its owner or the screen) opens no bigger than the work area of the screen it opens on,
/// and wholly on it, whatever that screen's size and DPI.
/// </summary>
public class ChromeWindow : Window
{
    public static readonly DependencyProperty TitleBarContentProperty = DependencyProperty.Register(
        nameof(TitleBarContent), typeof(object), typeof(ChromeWindow));

    private FrameworkElement? _root;

    public ChromeWindow()
    {
        // Implicit styles don't apply to subclasses, so look the style up by this base type explicitly.
        SetResourceReference(StyleProperty, typeof(ChromeWindow));

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
    }

    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("PART_Root") as FrameworkElement;
        UpdateMaximizedInset();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyDarkFrame(hwnd);

        // By now Windows has the window centered where it will open; windows placed by hand (floating panels, a kept
        // layout) are left where they were put.
        if (WindowStartupLocation != WindowStartupLocation.Manual && WindowState == WindowState.Normal)
            FitWorkArea(hwnd);
    }

    // Shrinks the window to the work area of the screen it's on (the one holding most of it) if it's bigger, keeping its
    // center, then moves it wholly onto that work area. All in the screen's pixels, so its DPI doesn't matter.
    private void FitWorkArea(IntPtr hwnd)
    {
        if (NativeMethods.WorkArea(hwnd) is not { } area || NativeMethods.Bounds(hwnd) is not { } bounds)
            return;

        var fitted = Fit(bounds, area);
        if (fitted == bounds)
            return;

        // Min sizes would hold it at its old size, so they give way where the screen is smaller than them.
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0)
            scale = 1;
        MinWidth = Math.Min(MinWidth, fitted.Width / scale);
        MinHeight = Math.Min(MinHeight, fitted.Height / scale);
        NativeMethods.Move(hwnd, fitted);
    }

    /// <summary>The work area (the screen less the taskbar) of the screen holding most of a window, in its pixels.</summary>
    public static Int32Rect? WorkArea(Window window) =>
        new WindowInteropHelper(window).Handle is var hwnd && hwnd != IntPtr.Zero ? NativeMethods.WorkArea(hwnd) : null;

    /// <summary>
    /// Moves <paramref name="window"/> (made, not yet shown) to the middle of the work area of the screen holding most of
    /// <paramref name="other"/>, so it opens, and maximizes, on that screen. In the screen's pixels, so its DPI doesn't matter.
    /// </summary>
    public static void MoveToScreenOf(Window window, Window other)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var otherHwnd = new WindowInteropHelper(other).Handle;
        if (hwnd == IntPtr.Zero || otherHwnd == IntPtr.Zero || NativeMethods.WorkArea(otherHwnd) is not { } area ||
            NativeMethods.Bounds(hwnd) is not { } bounds)
            return;
        var centered = bounds with { X = area.X + (area.Width - bounds.Width) / 2, Y = area.Y + (area.Height - bounds.Height) / 2 };
        NativeMethods.Move(hwnd, Fit(centered, area));
    }

    /// <summary>
    /// Whether a window is wholly on the work area of the screen holding most of it (a maximized one always is); null when
    /// it isn't showing.
    /// </summary>
    public static bool? IsOnItsScreen(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || NativeMethods.WorkArea(hwnd) is not { } area || NativeMethods.Bounds(hwnd) is not { } bounds)
            return null;
        return window.WindowState == WindowState.Maximized || Fit(bounds, area) == bounds;
    }

    /// <summary>
    /// <paramref name="window"/> made no bigger than <paramref name="area"/> (keeping its center) and moved wholly into
    /// it. Both are in the same units (a screen's pixels).
    /// </summary>
    public static Int32Rect Fit(Int32Rect window, Int32Rect area)
    {
        var width = Math.Min(window.Width, area.Width);
        var height = Math.Min(window.Height, area.Height);
        var x = window.X + (window.Width - width) / 2;
        var y = window.Y + (window.Height - height) / 2;
        x = Math.Clamp(x, area.X, area.X + area.Width - width);
        y = Math.Clamp(y, area.Y, area.Y + area.Height - height);
        return new Int32Rect(x, y, width, height);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizedInset();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateMaximizedInset();
    }

    // A maximized WindowChrome window extends past the screen edge by the resize frame;
    // inset the content by the same amount so nothing is clipped.
    private void UpdateMaximizedInset()
    {
        if (_root is null)
            return;

        if (WindowState != WindowState.Maximized)
        {
            _root.Margin = new Thickness(0);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var frame = NativeMethods.GetResizeFrameThickness((uint)dpi.PixelsPerInchX);
        _root.Margin = new Thickness(frame / dpi.DpiScaleX);
    }

    private static class NativeMethods
    {
        private const int SM_CXSIZEFRAME = 32;
        private const int SM_CXPADDEDBORDER = 92;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUND = 2;

        // COLORREF (0x00BBGGRR) matching Brush.Border.Window.
        private const int BorderColor = 0x003D3533;

        public static int GetResizeFrameThickness(uint dpi) =>
            GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);

        // Dark system frame and rounded corners on Windows 11; harmless no-ops on Windows 10.
        public static void ApplyDarkFrame(IntPtr hwnd)
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
            var corners = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            var border = BorderColor;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

        /// <summary>The work area (the screen less the taskbar) of the screen holding most of the window.</summary>
        public static Int32Rect? WorkArea(IntPtr hwnd)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.rcWork.ToInt32Rect() : null;
        }

        public static Int32Rect? Bounds(IntPtr hwnd) => GetWindowRect(hwnd, out var rect) ? rect.ToInt32Rect() : null;

        public static void Move(IntPtr hwnd, Int32Rect rect) =>
            SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;

            public readonly Int32Rect ToInt32Rect() => new(Left, Top, Right - Left, Bottom - Top);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
