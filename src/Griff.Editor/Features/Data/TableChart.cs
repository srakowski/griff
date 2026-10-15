using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Data;

namespace Griff.Editor.Features.Data;

/// <summary>
/// Draws the Tables panel's chart (<see cref="TableChartModel"/>): the picked column's values as bars over the rows or a
/// line over the levels, dice as their range (a thin line from least to most), spread (a box either side of the average)
/// and average (a dot), the open curve editor's curve dashed over them, and a hand-drawn curve's points, which are dragged,
/// added with a click and taken away with a right-click. Resting the mouse on it says the row under it. Its brushes come
/// from the theme.
/// </summary>
public sealed class TableChart : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = Register<TableChartModel?>(nameof(Model), null);
    public static readonly DependencyProperty VersionProperty = Register(nameof(Version), 0);
    public static readonly DependencyProperty CurveProperty = Register<TableCurveModel?>(nameof(Curve), null);
    public static readonly DependencyProperty ValueBrushProperty = Register<Brush?>(nameof(ValueBrush), null);
    public static readonly DependencyProperty SpreadBrushProperty = Register<Brush?>(nameof(SpreadBrush), null);
    public static readonly DependencyProperty RangeBrushProperty = Register<Brush?>(nameof(RangeBrush), null);
    public static readonly DependencyProperty CurveBrushProperty = Register<Brush?>(nameof(CurveBrush), null);
    public static readonly DependencyProperty GridBrushProperty = Register<Brush?>(nameof(GridBrush), null);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>(nameof(TextBrush), null);
    public static readonly DependencyProperty HoverBrushProperty = Register<Brush?>(nameof(HoverBrush), null);

    private const double Left = 46;
    private const double Right = 10;
    private const double Top = 8;
    private const double Bottom = 15;
    private const double HandleRadius = 5;

    private int _hover = -1;
    private int _dragging = -1;
    private (double Low, double High)? _dragRange;
    private (double Low, double High) _range;

    public TableChart()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    public TableChartModel? Model { get => (TableChartModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    /// <summary>Changes whenever the chart's model does, so it's drawn again.</summary>
    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>The curve editor, whose hand-drawn points the chart moves.</summary>
    public TableCurveModel? Curve { get => (TableCurveModel?)GetValue(CurveProperty); set => SetValue(CurveProperty, value); }

    /// <summary>The values: bars, the line and dice averages.</summary>
    public Brush? ValueBrush { get => (Brush?)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

    /// <summary>Dice's spread, either side of their average.</summary>
    public Brush? SpreadBrush { get => (Brush?)GetValue(SpreadBrushProperty); set => SetValue(SpreadBrushProperty, value); }

    /// <summary>Dice's range, least to most.</summary>
    public Brush? RangeBrush { get => (Brush?)GetValue(RangeBrushProperty); set => SetValue(RangeBrushProperty, value); }

    /// <summary>The curve the curve editor would generate, and its points.</summary>
    public Brush? CurveBrush { get => (Brush?)GetValue(CurveBrushProperty); set => SetValue(CurveBrushProperty, value); }

    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    /// <summary>The line under the mouse, and the row it's on.</summary>
    public Brush? HoverBrush { get => (Brush?)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }

    /// <summary>What the chart says about the row under the mouse, or null; for tests and demos.</summary>
    public string? HoverText { get; private set; }

    // ----- Where things go

    private int Slots => Model is { } model ? Math.Max(1, Math.Max(model.Points.Count, model.Preview.Count)) : 1;

    private double PlotWidth => Math.Max(1, ActualWidth - Left - Right);

    private double PlotHeight => Math.Max(1, ActualHeight - Top - Bottom);

    private bool Bars => Model is { OverLevels: false };

    /// <summary>Where the row (or level, from 0) at <paramref name="index"/> is across the chart.</summary>
    public double X(double index) => Bars
        ? Left + (index + 0.5) * PlotWidth / Slots
        : Slots == 1 ? Left + PlotWidth / 2 : Left + index * PlotWidth / (Slots - 1);

    private double IndexAt(double x) => Bars
        ? (x - Left) / (PlotWidth / Slots) - 0.5
        : Slots == 1 ? 0 : (x - Left) / (PlotWidth / (Slots - 1));

    /// <summary>Where <paramref name="value"/> is up the chart.</summary>
    public double Y(double value)
    {
        var (low, high) = _dragRange ?? _range;
        return Top + PlotHeight * (1 - (value - low) / (high - low));
    }

    private double ValueAt(double y)
    {
        var (low, high) = _dragRange ?? _range;
        return low + (1 - (y - Top) / PlotHeight) * (high - low);
    }

    // From 0 (or below, for values below it) to a round number above the highest.
    private (double Low, double High) Range()
    {
        var model = Model!;
        var values = model.Points.Where(p => !p.IsEmpty).SelectMany(p => new[] { p.Low, p.High, p.Value })
            .Concat(model.Preview).Concat(model.Handles.Select(h => h.Value)).Where(double.IsFinite).ToList();
        if (values.Count == 0)
            return (0, 1);
        var low = Math.Min(0, values.Min());
        var high = Math.Max(0, values.Max());
        if (high - low < 1e-9)
            high = low + 1;
        return (low < 0 ? -Nice(-low) : 0, high > 0 ? Nice(high) : 0);
    }

    // The next round number at or above the value: 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 times a power of ten.
    private static double Nice(double value)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
        {
            if (step * power >= value * 0.9999)
                return step * power;
        }

        return 10 * power;
    }

    // ----- Drawing

    protected override void OnRender(DrawingContext drawingContext)
    {
        var drawing = drawingContext;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Model is not { HasChart: true } model || ActualWidth < Left + Right + 10 || ActualHeight < Top + Bottom + 10)
            return;
        _range = Range();
        var (low, high) = _dragRange ?? _range;
        var grid = new Pen(GridBrush, 1);
        grid.Freeze();

        // Lines across at the bottom, the middle (with room for its label) and the top, labelled.
        var across = PlotHeight >= 48 ? new[] { low, (low + high) / 2, high } : [low, high];
        foreach (var value in across.Append(low < 0 && high > 0 ? 0 : low).Distinct())
        {
            var y = Math.Round(Y(value)) + 0.5;
            drawing.DrawLine(grid, new Point(Left, y), new Point(Left + PlotWidth, y));
            var label = Text(TablesPanel.Format(value), 10, TextBrush);
            drawing.DrawText(label, new Point(Left - 4 - label.Width, y - label.Height / 2));
        }

        var points = model.Points;
        var zero = Y(Math.Max(low, Math.Min(0, high)));
        var slot = PlotWidth / Slots;

        if (model.IsDice)
        {
            var range = new Pen(RangeBrush, 1.5);
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p.IsEmpty)
                    continue;
                var x = X(i);
                var half = Math.Clamp(slot * 0.3, 2, 10);
                drawing.DrawLine(range, new Point(x, Y(p.Low)), new Point(x, Y(p.High)));
                drawing.DrawLine(range, new Point(x - half / 2, Y(p.Low)), new Point(x + half / 2, Y(p.Low)));
                drawing.DrawLine(range, new Point(x - half / 2, Y(p.High)), new Point(x + half / 2, Y(p.High)));
                var top = Y(Math.Min(p.High, p.Value + p.Spread));
                var bottom = Y(Math.Max(p.Low, p.Value - p.Spread));
                drawing.DrawRectangle(SpreadBrush, null, new Rect(x - half, top, half * 2, Math.Max(1, bottom - top)));
                drawing.DrawEllipse(ValueBrush, null, new Point(x, Y(p.Value)), 3, 3);
            }
        }
        else if (Bars)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].IsEmpty)
                    continue;
                var y = Y(points[i].Value);
                var width = Math.Max(1, slot * 0.7);
                drawing.DrawRectangle(ValueBrush, null, new Rect(X(i) - width / 2, Math.Min(y, zero), width, Math.Max(1, Math.Abs(zero - y))));
            }
        }
        else
        {
            var line = new Pen(ValueBrush, 2) { LineJoin = PenLineJoin.Round };
            Polyline(drawing, line, points.Select((p, i) => (i, p.Value)));
            if (points.Count <= 60)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    if (!points[i].IsEmpty)
                        drawing.DrawEllipse(ValueBrush, null, new Point(X(i), Y(points[i].Value)), 2.5, 2.5);
                }
            }
        }

        // The curve the editor would generate, dashed over the values, and the points it's drawn through.
        if (model.Preview.Count > 0)
        {
            var curve = new Pen(CurveBrush, 2) { DashStyle = new DashStyle([3, 2], 0), LineJoin = PenLineJoin.Round };
            Polyline(drawing, curve, model.Preview.Select((v, i) => (i, v)));
        }

        foreach (var handle in model.Handles)
            drawing.DrawEllipse(Background(), new Pen(CurveBrush, 2), new Point(X(handle.Level - 1), Y(handle.Value)), HandleRadius, HandleRadius);

        // The first and last rows' names along the bottom (and the middle one's, with room).
        var names = Math.Max(points.Count, model.Preview.Count);
        var labelled = names <= 1 ? new[] { 0 } : names * 60 < PlotWidth ? Enumerable.Range(0, names).ToArray() : new[] { 0, (names - 1) / 2, names - 1 }.Distinct().ToArray();
        foreach (var i in labelled)
        {
            var name = i < points.Count ? points[i].Label : (i + 1).ToString(CultureInfo.InvariantCulture);
            var text = Text(name, 10, TextBrush);
            var x = Math.Clamp(X(i) - text.Width / 2, Left - 8, ActualWidth - text.Width);
            drawing.DrawText(text, new Point(x, Top + PlotHeight + 1));
        }

        // The row under the mouse.
        HoverText = null;
        if (_hover >= 0 && _hover < names)
        {
            var x = Math.Round(X(_hover)) + 0.5;
            drawing.DrawLine(new Pen(HoverBrush, 1), new Point(x, Top), new Point(x, Top + PlotHeight));
            var says = _hover < points.Count ? points[_hover].Readout : "";
            if (_hover < model.Preview.Count)
                says += (says.Length > 0 ? "; " : $"{_hover + 1}: ") + $"the curve gives {TablesPanel.Format(model.Preview[_hover])}";
            HoverText = says;
            Note(drawing, Text(says, 11, HoverBrush));
        }
    }

    // A line of text over the top of the chart, on the panel's color so the lines under it don't show through.
    private void Note(DrawingContext drawing, FormattedText text)
    {
        var at = new Point(Left + 2, Top);
        drawing.DrawRectangle(Background(), null, new Rect(at.X - 2, at.Y - 1, Math.Min(text.Width + 4, ActualWidth - at.X), text.Height + 2));
        drawing.DrawText(text, at);
    }

    private Brush Background() => (Brush)(TryFindResource("Brush.Surface.Panel") ?? Brushes.Black);

    private void Polyline(DrawingContext drawing, Pen pen, IEnumerable<(int Index, double Value)> values)
    {
        var geometry = new StreamGeometry();
        using (var shape = geometry.Open())
        {
            var drawingLine = false;
            foreach (var (index, value) in values)
            {
                if (!double.IsFinite(value))
                {
                    drawingLine = false;
                    continue;
                }

                var point = new Point(X(index), Y(value));
                if (!drawingLine)
                    shape.BeginFigure(point, isFilled: false, isClosed: false);
                else
                    shape.LineTo(point, isStroked: true, isSmoothJoin: true);
                drawingLine = true;
            }
        }

        geometry.Freeze();
        drawing.DrawGeometry(null, pen, geometry);
    }

    private FormattedText Text(string text, double size, Brush? brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ----- The mouse: what's under it, and the hand-drawn curve's points

    private bool Drawing => Curve is { IsOpen: true, UsesPoints: true } && Model is { HasChart: true };

    private int HandleAt(Point at)
    {
        if (!Drawing || Model is null)
            return -1;
        var handles = Model.Handles;
        for (var i = 0; i < handles.Count; i++)
        {
            if ((new Point(X(handles[i].Level - 1), Y(handles[i].Value)) - at).Length <= HandleRadius + 3)
                return i;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var at = e.GetPosition(this);
        if (_dragging >= 0 && Curve is { } curve)
        {
            curve.MovePoint(_dragging, LevelAt(at.X), ValueAt(at.Y));
            // The points are kept in order of level, so the one dragged is found again by where it went.
            _dragging = Nearest(curve, at);
        }

        var hover = (int)Math.Round(IndexAt(at.X));
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }

        Cursor = Drawing ? (HandleAt(at) >= 0 || _dragging >= 0 ? Cursors.SizeAll : Cursors.Cross) : null;
    }

    private int Nearest(TableCurveModel curve, Point at) =>
        curve.PointList.Select((p, i) => (i, Distance: (new Point(X(p.Level - 1), Y(p.Value)) - at).Length)).MinBy(p => p.Distance).i;

    private double LevelAt(double x) => Math.Clamp(Math.Round(IndexAt(x)) + 1, 1, Math.Max(1, Curve?.Levels ?? Slots));

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Drawing || Curve is not { } curve)
            return;
        var at = e.GetPosition(this);
        var handle = HandleAt(at);
        _dragRange = _range;
        if (handle < 0)
        {
            curve.AddPoint(LevelAt(at.X), ValueAt(at.Y));
            handle = Nearest(curve, at);
        }

        _dragging = handle;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging < 0)
            return;
        _dragging = -1;
        _dragRange = null;
        ReleaseMouseCapture();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Curve is { } curve && HandleAt(e.GetPosition(this)) is >= 0 and var handle)
        {
            curve.RemovePoint(handle);
            e.Handled = true;
        }
    }

    private static DependencyProperty Register<T>(string name, T fallback) =>
        DependencyProperty.Register(name, typeof(T), typeof(TableChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));
}
