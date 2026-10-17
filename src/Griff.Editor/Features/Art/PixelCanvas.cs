using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Griff.Editor.Features.Art;

/// <summary>
/// The Sprite Painter's drawing area: the frame being painted, as big as fits in whole-pixel steps, over a checker
/// that shows what's clear. Left-drag paints with the tool, right-drag erases, Alt-click picks a color.
/// </summary>
public sealed class PixelCanvas : FrameworkElement
{
    public static readonly DependencyProperty PainterProperty = DependencyProperty.Register(
        nameof(Painter), typeof(SpritePainterViewModel), typeof(PixelCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPainterChanged));

    private static readonly Brush Checker = MakeChecker();
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), 1));
    private static readonly Pen HoverPen = Frozen(new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)), 1));
    private static readonly Pen MirrorPen = Frozen(new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 41, 173, 255)), 1) { DashStyle = DashStyles.Dash });

    private (int X, int Y)? _hover;

    public PixelCanvas()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        // Clicking it takes the keyboard, so the painter's keys (B, E, G..., Ctrl+Z) work in its tab.
        Focusable = true;
        FocusVisualStyle = null;
    }

    public SpritePainterViewModel? Painter
    {
        get => (SpritePainterViewModel?)GetValue(PainterProperty);
        set => SetValue(PainterProperty, value);
    }

    /// <summary>Screen pixels per picture pixel.</summary>
    public double Zoom
    {
        get
        {
            if (Painter is not { } painter)
                return 1;
            var layout = painter.Layout;
            var fit = Math.Min((ActualWidth - 16) / layout.CellWidth, (ActualHeight - 16) / layout.CellHeight);
            return Math.Max(1, Math.Floor(fit));
        }
    }

    /// <summary>Where the frame's top-left corner is drawn.</summary>
    public Point Origin
    {
        get
        {
            if (Painter is not { } painter)
                return default;
            var zoom = Zoom;
            return new Point(Math.Floor((ActualWidth - painter.Layout.CellWidth * zoom) / 2), Math.Floor((ActualHeight - painter.Layout.CellHeight * zoom) / 2));
        }
    }

    /// <summary>The middle of a picture pixel, in this control's coordinates.</summary>
    public Point PointOf(int x, int y)
    {
        var (origin, zoom) = (Origin, Zoom);
        return new Point(origin.X + (x + 0.5) * zoom, origin.Y + (y + 0.5) * zoom);
    }

    private (int X, int Y) PixelAt(Point point)
    {
        var (origin, zoom) = (Origin, Zoom);
        return ((int)Math.Floor((point.X - origin.X) / zoom), (int)Math.Floor((point.Y - origin.Y) / zoom));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Painter is not { } painter)
            return;

        var layout = painter.Layout;
        var (origin, zoom) = (Origin, Zoom);
        var area = new Rect(origin.X, origin.Y, layout.CellWidth * zoom, layout.CellHeight * zoom);
        drawingContext.DrawRectangle(Checker, null, area);

        if (painter.Onion && painter.Frame > 0)
        {
            drawingContext.PushOpacity(0.3);
            drawingContext.DrawImage(painter.CellBitmap(painter.Frame - 1), area);
            drawingContext.Pop();
        }

        drawingContext.DrawImage(painter.CellBitmap(painter.Frame), area);

        if (painter.ShowGrid && zoom >= 8)
        {
            for (var x = 1; x < layout.CellWidth; x++)
                drawingContext.DrawLine(GridPen, new Point(area.Left + x * zoom + 0.5, area.Top), new Point(area.Left + x * zoom + 0.5, area.Bottom));
            for (var y = 1; y < layout.CellHeight; y++)
                drawingContext.DrawLine(GridPen, new Point(area.Left, area.Top + y * zoom + 0.5), new Point(area.Right, area.Top + y * zoom + 0.5));
        }

        if (painter.Mirror)
        {
            var middle = Math.Floor(area.Left + area.Width / 2) + 0.5;
            drawingContext.DrawLine(MirrorPen, new Point(middle, area.Top), new Point(middle, area.Bottom));
        }

        if (_hover is { } hover && hover.X >= 0 && hover.Y >= 0 && hover.X < layout.CellWidth && hover.Y < layout.CellHeight)
            drawingContext.DrawRectangle(null, HoverPen, new Rect(area.Left + hover.X * zoom + 0.5, area.Top + hover.Y * zoom + 0.5, zoom - 1, zoom - 1));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Painter is not { } painter || e.ChangedButton is not (MouseButton.Left or MouseButton.Right))
            return;
        Focus();
        var (x, y) = PixelAt(e.GetPosition(this));
        var pick = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        painter.BeginStroke(x, y, erase: e.ChangedButton == MouseButton.Right, pick: pick);
        if (painter.IsStroking)
            CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Painter is not { } painter)
            return;
        var pixel = PixelAt(e.GetPosition(this));
        if (pixel != _hover)
        {
            _hover = pixel;
            var inside = pixel.X >= 0 && pixel.Y >= 0 && pixel.X < painter.Layout.CellWidth && pixel.Y < painter.Layout.CellHeight;
            painter.Hover(inside ? pixel : null);
            InvalidateVisual();
        }

        if (painter.IsStroking && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed))
            painter.ContinueStroke(pixel.X, pixel.Y);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        Finish();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Finish();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Painter?.Hover(null);
        InvalidateVisual();
    }

    private void Finish()
    {
        Painter?.EndStroke();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private static void OnPainterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (PixelCanvas)d;
        if (e.OldValue is SpritePainterViewModel old)
            old.PixelsChanged -= canvas.OnPixelsChanged;
        if (e.NewValue is SpritePainterViewModel painter)
            painter.PixelsChanged += canvas.OnPixelsChanged;
    }

    private void OnPixelsChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static DrawingBrush MakeChecker()
    {
        var light = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x42));
        var dark = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0x2C, 0x33));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
