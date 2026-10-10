#if DEBUG
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;

namespace Griff.Editor.Diagnostics.Demo;

using Color = System.Windows.Media.Color;

/// <summary>
/// A click-through, never-focused window over the whole screen that draws the demo's dressing: a spotlight on what's
/// being talked about, captions, the keys being pressed, click ripples, chapter cards and a code card.
/// </summary>
internal sealed class DemoOverlay : Window
{
    private static readonly Color Accent = Color.FromRgb(0xFF, 0xB5, 0x47);
    private static readonly FontFamily UiFont = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)), Opacity = 0 };
    private readonly Border _ring;
    private readonly Border _callout;
    private readonly TextBlock _calloutText;
    private readonly Border _caption;
    private readonly TextBlock _captionText;
    private readonly Border _keys;
    private readonly TextBlock _keysText;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly Grid _card;
    private readonly TextBlock _cardKicker, _cardTitle, _cardSubtitle;
    private readonly Border _code;
    private readonly TextBlock _codeTitle;
    private readonly TextBlock _codeText;
    private int _keysVersion;

    public DemoOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Content = _canvas;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _canvas.Children.Add(_dim);

        _ring = new Border
        {
            BorderBrush = new SolidColorBrush(Accent),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            Opacity = 0,
            Effect = new DropShadowEffect { Color = Accent, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.9 },
        };
        _canvas.Children.Add(_ring);

        _calloutText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x22)), FontFamily = UiFont, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        _callout = new Border
        {
            Background = new SolidColorBrush(Accent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 9),
            Child = _calloutText,
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45 },
        };
        _canvas.Children.Add(_callout);

        _captionText = new TextBlock { Foreground = Brushes.White, FontFamily = UiFont, FontSize = 23, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 31 };
        _caption = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x12, 0x16)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22, 12, 22, 13),
            Child = _captionText,
            Opacity = 0,
        };
        _canvas.Children.Add(_caption);

        _keysText = new TextBlock { Foreground = Brushes.White, FontFamily = UiFont, FontSize = 26, FontWeight = FontWeights.SemiBold };
        _keys = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x25, 0x2C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 8, 18, 10),
            Child = _keysText,
            Opacity = 0,
        };
        _canvas.Children.Add(_keys);

        _badgeText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x22)), FontFamily = UiFont, FontSize = 18, FontWeight = FontWeights.Bold };
        _badge = new Border
        {
            Background = new SolidColorBrush(Accent),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 5, 16, 6),
            Child = _badgeText,
            Opacity = 0,
        };
        _canvas.Children.Add(_badge);

        _codeTitle = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAC)), FontFamily = UiFont, FontSize = 15, Margin = new Thickness(0, 0, 0, 10) };
        _codeText = new TextBlock { FontFamily = MonoFont, FontSize = 19, LineHeight = 27, Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)) };
        var codeStack = new StackPanel();
        codeStack.Children.Add(_codeTitle);
        codeStack.Children.Add(_codeText);
        _code = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x17, 0x19, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(26, 18, 30, 22),
            Child = codeStack,
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.6 },
        };
        _canvas.Children.Add(_code);

        _cardKicker = new TextBlock { Foreground = new SolidColorBrush(Accent), FontFamily = UiFont, FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        _cardTitle = new TextBlock { Foreground = Brushes.White, FontFamily = UiFont, FontSize = 64, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8) };
        _cardSubtitle = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xBE, 0xC9)), FontFamily = UiFont, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 1100 };
        var cardStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        cardStack.Children.Add(_cardKicker);
        cardStack.Children.Add(_cardTitle);
        cardStack.Children.Add(_cardSubtitle);
        _card = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xEC, 0x0E, 0x10, 0x14)), Opacity = 0 };
        _card.Children.Add(cardStack);
        _canvas.Children.Add(_card);

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, ExStyle, GetWindowLong(handle, ExStyle) | Transparent | Layered | NoActivate | ToolWindow);
        };
    }

    /// <summary>Covers <paramref name="screen"/> (device pixels).</summary>
    public void Cover(Rect screen)
    {
        if (!IsVisible)
            Show();
        var dips = ToDips(screen);
        Left = dips.Left;
        Top = dips.Top;
        Width = dips.Width;
        Height = dips.Height;
        _card.Width = dips.Width;
        _card.Height = dips.Height;
        UpdateLayout();
        BringToFront();
    }

    public void BringToFront()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }

    // ----- Spotlight -----

    /// <summary>Dims everything but <paramref name="screen"/>, rings it and (optionally) labels it.</summary>
    public void Spotlight(Rect screen, string? label = null, bool dim = true)
    {
        BringToFront();
        var r = ToLocal(screen);
        r.Inflate(6, 6);
        var all = new RectangleGeometry(new Rect(0, 0, Width, Height));
        var hole = new RectangleGeometry(r, 8, 8);
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, hole);
        Fade(_dim, dim ? 1 : 0, 260);

        Canvas.SetLeft(_ring, r.Left);
        Canvas.SetTop(_ring, r.Top);
        _ring.Width = r.Width;
        _ring.Height = r.Height;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = new ScaleTransform(1.06, 1.06);
        ((ScaleTransform)_ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Ms(260)) { EasingFunction = new CubicEase() });
        ((ScaleTransform)_ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Ms(260)) { EasingFunction = new CubicEase() });
        Fade(_ring, 1, 200);

        if (label is null)
        {
            Fade(_callout, 0, 150);
            return;
        }

        _calloutText.Text = label;
        _callout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _callout.DesiredSize;
        // Below the spot if there's room, else above, else beside it on whichever side has more space.
        Point at;
        if (r.Bottom + 14 + size.Height < Height - 150)
            at = new Point(r.Left + r.Width / 2 - size.Width / 2, r.Bottom + 14);
        else if (r.Top - 14 - size.Height > 0)
            at = new Point(r.Left + r.Width / 2 - size.Width / 2, r.Top - 14 - size.Height);
        else if (r.Left > Width - r.Right)
            at = new Point(r.Left - 14 - size.Width, r.Top + r.Height / 2 - size.Height / 2);
        else
            at = new Point(r.Right + 14, r.Top + r.Height / 2 - size.Height / 2);
        Canvas.SetLeft(_callout, Math.Clamp(at.X, 12, Math.Max(12, Width - size.Width - 12)));
        Canvas.SetTop(_callout, Math.Clamp(at.Y, 12, Math.Max(12, Height - size.Height - 12)));
        Fade(_callout, 1, 220);
    }

    public void ClearSpotlight()
    {
        Fade(_dim, 0, 220);
        Fade(_ring, 0, 180);
        Fade(_callout, 0, 180);
    }

    // ----- Captions, keys, badge -----

    public void Caption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Fade(_caption, 0, 200);
            return;
        }

        _captionText.Text = text;
        _captionText.MaxWidth = Math.Min(1300, Width - 120);
        _caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_caption, (Width - _caption.DesiredSize.Width) / 2);
        Canvas.SetTop(_caption, Height - _caption.DesiredSize.Height - 70);
        Fade(_caption, 1, 160);
    }

    public async void ShowKeys(string keys)
    {
        var version = ++_keysVersion;
        _keysText.Text = keys;
        _keys.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_keys, Width - _keys.DesiredSize.Width - 48);
        Canvas.SetTop(_keys, Height - _keys.DesiredSize.Height - 150);
        Fade(_keys, 1, 90);
        await Task.Delay(1300);
        if (version == _keysVersion)
            Fade(_keys, 0, 300);
    }

    /// <summary>A pill in the top-right corner, e.g. "Sped up 4×"; null hides it.</summary>
    public void Badge(string? text)
    {
        if (text is null)
        {
            Fade(_badge, 0, 200);
            return;
        }

        _badgeText.Text = text;
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_badge, Width - _badge.DesiredSize.Width - 40);
        Canvas.SetTop(_badge, 70);
        Fade(_badge, 1, 200);
    }

    public void Ripple(Point screen)
    {
        var at = ToLocal(new Rect(screen, new Size(1, 1))).TopLeft;
        var ring = new Ellipse { Width = 60, Height = 60, Stroke = new SolidColorBrush(Accent), StrokeThickness = 4, RenderTransformOrigin = new Point(0.5, 0.5) };
        var scale = new ScaleTransform(0.2, 0.2);
        ring.RenderTransform = scale;
        Canvas.SetLeft(ring, at.X - 30);
        Canvas.SetTop(ring, at.Y - 30);
        _canvas.Children.Add(ring);
        var grow = new DoubleAnimation(0.2, 1, Ms(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimation(1, 0, Ms(420));
        fade.Completed += (_, _) => _canvas.Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    // ----- Chapter card -----

    public async Task Card(string kicker, string title, string subtitle, double holdMs)
    {
        BringToFront();
        _cardKicker.Text = kicker;
        _cardTitle.Text = title;
        _cardSubtitle.Text = subtitle;
        Fade(_card, 1, 380);
        await Task.Delay(380 + (int)holdMs);
        Fade(_card, 0, 420);
        await Task.Delay(420);
    }

    public void ShowCard(string kicker, string title, string subtitle)
    {
        BringToFront();
        _cardKicker.Text = kicker;
        _cardTitle.Text = title;
        _cardSubtitle.Text = subtitle;
        Fade(_card, 1, 380);
    }

    public void HideCard() => Fade(_card, 0, 420);

    // ----- Code card -----

    /// <summary>Shows a code editor card at <paramref name="screen"/> and types <paramref name="code"/> into it.</summary>
    public async Task TypeCode(Rect screen, string title, string code, double msPerChar, Func<bool>? skip = null)
    {
        var r = ToLocal(screen);
        Canvas.SetLeft(_code, r.Left);
        Canvas.SetTop(_code, r.Top);
        _code.Width = r.Width;
        _codeTitle.Text = title;
        _codeText.Inlines.Clear();
        Fade(_code, 1, 250);
        await Task.Delay(250);

        for (var i = 1; i <= code.Length; i++)
        {
            if (skip?.Invoke() == true)
                i = code.Length;
            SetCode(code[..i] + "▌");
            var c = code[i - 1];
            if (c is ' ' or '\n' && i < code.Length && code[i] is ' ')
                continue; // indentation goes in at once, like an editor's auto-indent
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, msPerChar * (c == '\n' ? 3 : 1))));
        }

        SetCode(code);
    }

    /// <summary>Replaces <paramref name="from"/> with <paramref name="to"/> in the code card, a character at a time.</summary>
    public async Task EditCode(string code, string from, string to, double msPerChar)
    {
        var at = code.IndexOf(from, StringComparison.Ordinal);
        var head = code[..at];
        var tail = code[(at + from.Length)..];
        for (var i = from.Length; i >= 0; i--)
        {
            SetCode(head + from[..i] + "▌" + tail, highlight: (head.Length, i + 1));
            await Task.Delay(TimeSpan.FromMilliseconds(msPerChar));
        }

        for (var i = 1; i <= to.Length; i++)
        {
            SetCode(head + to[..i] + "▌" + tail, highlight: (head.Length, i + 1));
            await Task.Delay(TimeSpan.FromMilliseconds(msPerChar * 1.4));
        }

        SetCode(head + to + tail, highlight: (head.Length, to.Length));
    }

    public void CodeTitle(string title) => _codeTitle.Text = title;

    public void HideCode() => Fade(_code, 0, 250);

    private static readonly HashSet<string> Keywords = ["public", "class", "protected", "override", "void", "float", "private", "new", "var", "return", "if", "get", "set", "using"];

    // Light syntax coloring: keywords, types, numbers, comments.
    private void SetCode(string code, (int Start, int Length)? highlight = null)
    {
        _codeText.Inlines.Clear();
        var keyword = new SolidColorBrush(Color.FromRgb(0x7A, 0xA8, 0xFF));
        var type = new SolidColorBrush(Color.FromRgb(0x5E, 0xD3, 0xB5));
        var number = new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x7A));
        var comment = new SolidColorBrush(Color.FromRgb(0x6F, 0x78, 0x86));
        var mark = new SolidColorBrush(Color.FromArgb(0x55, Accent.R, Accent.G, Accent.B));

        var i = 0;
        while (i < code.Length)
        {
            int start = i;
            Brush? brush = null;
            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
                brush = comment;
            }
            else if (char.IsLetter(code[i]) || code[i] == '_')
            {
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                var word = code[start..i];
                brush = Keywords.Contains(word) ? keyword : IsTypeName(word) ? type : null;
            }
            else if (char.IsDigit(code[i]))
            {
                while (i < code.Length && (char.IsDigit(code[i]) || code[i] is '.' or 'f')) i++;
                brush = number;
            }
            else
            {
                i++;
            }

            AddRun(start, i, brush);
        }

        void AddRun(int start, int end, Brush? brush)
        {
            // Split around the highlighted range so it can get a background.
            if (highlight is { } h && start < h.Start + h.Length && end > h.Start)
            {
                var a = Math.Max(start, h.Start);
                var b = Math.Min(end, h.Start + h.Length);
                if (a > start) Add(code[start..a], brush, null);
                Add(code[a..b], brush, mark);
                if (end > b) Add(code[b..end], brush, null);
            }
            else
            {
                Add(code[start..end], brush, null);
            }
        }

        void Add(string text, Brush? brush, Brush? background)
        {
            var run = new Run(text);
            if (brush is not null) run.Foreground = brush;
            if (background is not null) run.Background = background;
            _codeText.Inlines.Add(run);
        }
    }

    private static bool IsTypeName(string word) => word is "Behavior" or "Vector3" or "Vector2" or "MathF" or "Transform" or "Time";

    // ----- Helpers -----

    private static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(ms));

    private static void Fade(UIElement element, double to, double ms) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Ms(ms)) { EasingFunction = new CubicEase() });

    private Rect ToDips(Rect device)
    {
        var source = PresentationSource.FromVisual(this) ?? PresentationSource.FromVisual(Application.Current.MainWindow);
        var m = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(m.Transform(device.TopLeft), m.Transform(device.BottomRight));
    }

    private Rect ToLocal(Rect device)
    {
        var dips = ToDips(device);
        dips.Offset(-Left, -Top);
        return dips;
    }

    private const int ExStyle = -20, Transparent = 0x20, Layered = 0x80000, NoActivate = 0x08000000, ToolWindow = 0x80;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
#endif
