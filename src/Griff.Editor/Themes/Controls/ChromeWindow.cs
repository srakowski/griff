using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// Window with Griff's custom title bar. <see cref="TitleBarContent"/> sits next to the app mark
/// (the main window puts its menu there). All editor windows should derive from this.
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
        NativeMethods.ApplyDarkFrame(new WindowInteropHelper(this).Handle);
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

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
