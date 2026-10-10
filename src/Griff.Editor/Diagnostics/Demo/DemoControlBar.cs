#if DEBUG
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Griff.Editor.Diagnostics.Demo;

using Color = System.Windows.Media.Color;

/// <summary>
/// The demo's transport bar, pinned to the top of the screen: back, pause, next, speed, a dot per chapter and Exit.
/// It never takes focus (so the demo's typing keeps going to the editor), and the same controls work from anywhere with
/// hotkeys: Ctrl+Alt+Space pause, Ctrl+Alt+Left/Right chapter, Ctrl+Alt+Up/Down speed, Ctrl+Alt+X exit.
/// </summary>
internal sealed class DemoControlBar : Window
{
    private static readonly Color Accent = Color.FromRgb(0xFF, 0xB5, 0x47);
    private static readonly FontFamily UiFont = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily Symbols = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly DemoControl _control;
    private readonly Button _pause;
    private readonly StackPanel _speeds = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0) };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontFamily = UiFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), MinWidth = 220 };
    private HwndSource? _source;

    public DemoControlBar(DemoControl control)
    {
        _control = control;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph("", "Back a chapter (Ctrl+Alt+Left)", control.Back));
        _pause = Glyph("", "Pause (Ctrl+Alt+Space)", control.TogglePause);
        row.Children.Add(_pause);
        row.Children.Add(Glyph("", "Next chapter (Ctrl+Alt+Right)", control.Next));
        row.Children.Add(_speeds);
        row.Children.Add(_dots);
        row.Children.Add(_title);
        row.Children.Add(Text("Exit demo", "Stop the demo and remove its overlays (Ctrl+Alt+X)", control.Exit, danger: true));

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x15, 0x17, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(12),
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.5 },
        };

        control.Changed += (_, _) => Dispatcher.BeginInvoke(Refresh);
        SourceInitialized += (_, _) => Hook();
        Closed += (_, _) => Unhook();
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Centers the bar along the top of <paramref name="screen"/> (device pixels), below the title bar.</summary>
    public void PlaceAt(Rect screen)
    {
        if (!IsVisible)
            Show();
        UpdateLayout();
        var source = PresentationSource.FromVisual(this);
        var m = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = m.Transform(screen.TopLeft);
        var size = m.Transform(new Point(screen.Width, screen.Height));
        Left = topLeft.X + (size.X - ActualWidth) / 2;
        Top = topLeft.Y + 28;
        BringToFront();
    }

    public void BringToFront()
    {
        if (_source is not null)
            SetWindowPos(_source.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }

    private void Refresh()
    {
        _pause.Content = GlyphText(_control.Paused ? "" : "");
        _pause.ToolTip = _control.Paused ? "Play (Ctrl+Alt+Space)" : "Pause (Ctrl+Alt+Space)";

        _speeds.Children.Clear();
        foreach (var speed in DemoControl.Speeds)
        {
            var s = speed;
            var chip = Text($"{speed:0}×", $"Play at {speed:0}× (Ctrl+Alt+Up/Down)", () => _control.SetSpeed(s));
            if (Math.Abs(_control.Speed - speed) < 0.01)
            {
                chip.Background = new SolidColorBrush(Accent);
                chip.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x22));
            }

            _speeds.Children.Add(chip);
        }

        _dots.Children.Clear();
        for (var i = 0; i < _control.Chapters.Count; i++)
        {
            var chapter = i;
            var current = i == _control.Current;
            var reached = i < _control.Current || _control.Snapshots.Contains(i);
            var dot = new Button
            {
                Width = current ? 22 : 12,
                Height = 12,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand,
                Focusable = false,
                ToolTip = $"{i}. {_control.Chapters[i]}",
                Template = DotTemplate(current ? Accent : reached ? Color.FromRgb(0xC8, 0xCC, 0xD6) : Color.FromRgb(0x4A, 0x4F, 0x5C)),
            };
            dot.Click += (_, _) => _control.GoTo(chapter);
            _dots.Children.Add(dot);
        }

        var name = _control.Current < _control.Chapters.Count ? _control.Chapters[_control.Current] : "";
        _title.Text = _control.Skipping ? $"Skipping ahead… {name}" : _control.Paused ? $"Paused · {name}" : name;
    }

    private static ControlTemplate DotTemplate(Color color)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(color));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        template.VisualTree = border;
        return template;
    }

    private Button Glyph(string glyph, string tip, Action click)
    {
        var button = Plain(tip, click);
        button.Content = GlyphText(glyph);
        button.Width = 36;
        return button;
    }

    private Button Text(string text, string tip, Action click, bool danger = false)
    {
        var button = Plain(tip, click);
        button.Content = text;
        button.Padding = new Thickness(10, 3, 10, 4);
        if (danger)
        {
            button.Foreground = Brushes.White;
            button.Background = new SolidColorBrush(Color.FromRgb(0xB8, 0x3A, 0x3A));
        }

        return button;
    }

    private static TextBlock GlyphText(string glyph) => new() { Text = glyph, FontFamily = Symbols, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };

    private static Button Plain(string tip, Action click)
    {
        var button = new Button
        {
            ToolTip = tip,
            Height = 30,
            Margin = new Thickness(2, 0, 2, 0),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = UiFont,
            FontSize = 13,
            Cursor = Cursors.Hand,
            Focusable = false,
        };

        // A rounded, flat look with a hover highlight, without the editor's theme.
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(TextElement.ForegroundProperty, Brushes.White);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, 0.8));
        template.Triggers.Add(hover);
        button.Template = template;

        button.Click += (_, _) => click();
        return button;
    }

    // ----- Never take focus; hotkeys -----

    private const int ExStyle = -20, NoActivate = 0x08000000, ToolWindow = 0x80, WmHotkey = 0x0312, WmMouseActivate = 0x21, MaNoActivate = 3;
    private const uint ModAlt = 1, ModControl = 2, ModNoRepeat = 0x4000;

    private static readonly (int Id, uint Key, Action<DemoControl> Run)[] Hotkeys =
    [
        (1, 0x20, c => c.TogglePause()), // Space
        (2, 0x25, c => c.Back()),        // Left
        (3, 0x27, c => c.Next()),        // Right
        (4, 0x26, c => c.Faster()),      // Up
        (5, 0x28, c => c.Slower()),      // Down
        (6, 0x58, c => c.Exit()),        // X
    ];

    private void Hook()
    {
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        SetWindowLong(_source.Handle, ExStyle, GetWindowLong(_source.Handle, ExStyle) | NoActivate | ToolWindow);
        _source.AddHook(WndProc);
        foreach (var (id, key, _) in Hotkeys)
            RegisterHotKey(_source.Handle, id, ModControl | ModAlt | ModNoRepeat, key);
    }

    private void Unhook()
    {
        if (_source is null)
            return;
        foreach (var (id, _, _) in Hotkeys)
            UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        if (msg == WmHotkey && Hotkeys.FirstOrDefault(h => h.Id == wParam.ToInt32()) is { Run: not null } hotkey)
        {
            hotkey.Run(_control);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
#endif
