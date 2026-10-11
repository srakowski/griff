using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Griff.Editor.Shell;

/// <summary>
/// The box that shows where a dragged panel will go, with what dropping it there does. It's an overlay rather than an
/// editor window, so it isn't a ChromeWindow: see-through, on top, never focused and never in the mouse's way.
/// </summary>
internal sealed class DockPreviewWindow : Window
{
    private readonly TextBlock _label;

    public DockPreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var fill = new Border { Opacity = 0.22 };
        fill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        var outline = new Border { BorderThickness = new Thickness(2) };
        outline.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");

        _label = new TextBlock { Margin = new Thickness(10, 4, 10, 4), FontSize = 12 };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.OnAccent");
        _label.SetResourceReference(TextBlock.FontFamilyProperty, "Font.UI");
        var chip = new Border
        {
            Child = _label,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");

        var root = new Grid();
        root.Children.Add(fill);
        root.Children.Add(outline);
        root.Children.Add(chip);
        Content = root;
    }

    /// <summary>The text on the box, e.g. "Tab with Inspector".</summary>
    public string Label => _label.Text;

    public void ShowAt(Rect bounds, string label)
    {
        _label.Text = label;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = Math.Max(bounds.Width, 40);
        Height = Math.Max(bounds.Height, 40);
        if (!IsVisible)
            Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Clicks and the mouse pass through to whatever's under it.
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, ExStyle, GetWindowLong(handle, ExStyle) | Transparent | NoActivate | ToolWindow);
    }

    private const int ExStyle = -20;
    private const int Transparent = 0x20;
    private const int ToolWindow = 0x80;
    private const int NoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
