using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>
/// A game's window sized in pixels of the game inside it (<see cref="Game.WindowSize"/>), and the room its screen has
/// for one (<see cref="Game.DisplayRoom"/>), whatever the screen's scaling: Windows works the window's frame out for
/// its screen, so a 1600x900 game is 1600x900 pixels of the screen at 100% and at 150% alike.
/// </summary>
public static class GameWindowSizing
{
    private const int StyleIndex = -16, ExStyleIndex = -20;
    // A window with a title bar and a frame that resizes (WPF's usual), for the room a windowed game has whatever the
    // window is now (fullscreen has no frame).
    private const uint WindowedStyle = 0x00CF0000, WindowedExStyle = 0x00000100;
    private const uint NearestMonitor = 2, NoZOrder = 0x0004, NoActivate = 0x0010;

    /// <summary>
    /// The most room for a game in a window on the screen <paramref name="window"/> is on: the screen's work area
    /// (without the taskbar) less a window's frame, in pixels. Null before the window has a handle.
    /// </summary>
    public static WindowSize? Room(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || WorkArea(handle) is not { } work)
            return null;
        var (frameWidth, frameHeight) = Frame(handle, WindowedStyle, WindowedExStyle);
        var room = new WindowSize(work.Right - work.Left - frameWidth, work.Bottom - work.Top - frameHeight);
        return room.IsValid ? room : null;
    }

    /// <summary>
    /// Sizes <paramref name="window"/> so the game inside it is <paramref name="size"/> pixels (no bigger than its
    /// screen's work area), centered on the screen it's on; a maximized window is restored first. Do it once the window
    /// has a handle: in its SourceInitialized to open at the size, before it's shown.
    /// </summary>
    public static void Resize(Window window, WindowSize size)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !size.IsValid || WorkArea(handle) is not { } work)
            return;
        if (window.WindowState != WindowState.Normal)
            window.WindowState = WindowState.Normal;

        var (frameWidth, frameHeight) = Frame(handle, (uint)GetWindowLongPtrW(handle, StyleIndex), (uint)GetWindowLongPtrW(handle, ExStyleIndex));
        var (workWidth, workHeight) = (work.Right - work.Left, work.Bottom - work.Top);
        var width = Math.Min(size.Width + frameWidth, workWidth);
        var height = Math.Min(size.Height + frameHeight, workHeight);
        var left = work.Left + (workWidth - width) / 2;
        var top = work.Top + (workHeight - height) / 2;
        SetWindowPos(handle, IntPtr.Zero, left, top, width, height, NoZOrder | NoActivate);
    }

    // The screen's work area the window is (mostly) on, in pixels.
    private static NativeRect? WorkArea(IntPtr handle)
    {
        var monitor = MonitorFromWindow(handle, NearestMonitor);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info) ? info.Work : null;
    }

    // How much wider and taller than what's inside it a window with these styles is, on the window's screen.
    private static (int Width, int Height) Frame(IntPtr handle, uint style, uint exStyle)
    {
        var rect = default(NativeRect);
        var dpi = GetDpiForWindow(handle);
        if (!(dpi > 0 ? AdjustWindowRectExForDpi(ref rect, style, false, exStyle, dpi) : AdjustWindowRectEx(ref rect, style, false, exStyle)))
            return (0, 0);
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectExForDpi(ref NativeRect rect, uint style, bool menu, uint exStyle, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref NativeRect rect, uint style, bool menu, uint exStyle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
