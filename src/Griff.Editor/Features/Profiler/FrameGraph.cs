using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Griff.Runtime;

namespace Griff.Editor.Features.Profiler;

/// <summary>
/// Draws a <see cref="FrameProfiler"/>'s frames as a bar each, newest on the right: how long the frame was, and from the
/// bottom, what of it the game's steps, building the picture and drawing it took, with lines where frames are as long as
/// 60 and 30 a second allow. Redraws when <see cref="Version"/> changes.
/// </summary>
public sealed class FrameGraph : FrameworkElement
{
    public static readonly DependencyProperty FramesProperty = Register<FrameProfiler?>(nameof(Frames), null);
    public static readonly DependencyProperty VersionProperty = Register<long>(nameof(Version), 0L);
    public static readonly DependencyProperty FrameBrushProperty = Register<Brush?>(nameof(FrameBrush), null);
    public static readonly DependencyProperty StepBrushProperty = Register<Brush?>(nameof(StepBrush), null);
    public static readonly DependencyProperty BuildBrushProperty = Register<Brush?>(nameof(BuildBrush), null);
    public static readonly DependencyProperty DrawBrushProperty = Register<Brush?>(nameof(DrawBrush), null);
    public static readonly DependencyProperty LineBrushProperty = Register<Brush?>(nameof(LineBrush), null);

    // Frames up to this long fit at first; a longer one doubles the scale until it fits.
    private const double SmallestScaleMs = 40;

    // Bars narrower than a pixel side by side: smoothing their edges would leave seams between them.
    public FrameGraph() => RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

    private FrameSample[] _samples = [];

    // Each frame's five heights on the graph: its bottom, the tops of its steps, building and drawing, and its own top.
    private double[] _edges = [];

    public FrameProfiler? Frames { get => (FrameProfiler?)GetValue(FramesProperty); set => SetValue(FramesProperty, value); }

    public long Version { get => (long)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>The whole frame, behind its parts: the time it had to spare.</summary>
    public Brush? FrameBrush { get => (Brush?)GetValue(FrameBrushProperty); set => SetValue(FrameBrushProperty, value); }

    public Brush? StepBrush { get => (Brush?)GetValue(StepBrushProperty); set => SetValue(StepBrushProperty, value); }

    public Brush? BuildBrush { get => (Brush?)GetValue(BuildBrushProperty); set => SetValue(BuildBrushProperty, value); }

    public Brush? DrawBrush { get => (Brush?)GetValue(DrawBrushProperty); set => SetValue(DrawBrushProperty, value); }

    /// <summary>The 60 and 30 frames a second lines, and their labels.</summary>
    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    /// <summary>The longest frame the graph's height shows, in milliseconds, as it was last drawn.</summary>
    public double ScaleMs { get; private set; } = SmallestScaleMs;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var drawing = drawingContext;
        var (width, height) = (ActualWidth, ActualHeight);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (Frames is not { } frames || width <= 0 || height <= 0)
            return;

        if (_samples.Length != frames.Capacity)
            _samples = new FrameSample[frames.Capacity];
        var count = frames.CopyTo(_samples);
        var worst = 0f;
        for (var i = 0; i < count; i++)
            worst = Math.Max(worst, _samples[i].FrameMs);
        ScaleMs = SmallestScaleMs;
        while (ScaleMs < worst && ScaleMs < 10_000)
            ScaleMs *= 2;

        // One bar a frame, the newest at the right edge, the graph filling from there as frames come in. Each part is drawn
        // as one shape over every frame (the tops of its bars, then back along their bottoms), rather than a box a bar.
        var bar = width / frames.Capacity;
        var perMs = height / ScaleMs;
        if (_edges.Length != frames.Capacity * 5)
            _edges = new double[frames.Capacity * 5];
        for (var i = 0; i < count; i++)
        {
            var sample = _samples[i];
            var at = i * 5;
            _edges[at] = height;
            _edges[at + 1] = Above(height, sample.StepMs);
            _edges[at + 2] = Above(_edges[at + 1], sample.BuildMs);
            _edges[at + 3] = Above(_edges[at + 2], sample.DrawMs);
            _edges[at + 4] = Above(height, sample.FrameMs);
        }

        Area(FrameBrush, 0, 4);
        Area(StepBrush, 0, 1);
        Area(BuildBrush, 1, 2);
        Area(DrawBrush, 2, 3);

        double Above(double bottom, float ms) => bottom - Math.Min(Math.Max(ms, 0) * perMs, bottom);

        // The part between each frame's edges `lower` and `upper`.
        void Area(Brush? brush, int lower, int upper)
        {
            if (brush is null || count == 0)
                return;
            var start = width - count * bar;
            var geometry = new StreamGeometry();
            using (var shape = geometry.Open())
            {
                shape.BeginFigure(new Point(start, _edges[lower]), isFilled: true, isClosed: true);
                for (var i = 0; i < count; i++)
                {
                    var y = _edges[i * 5 + upper];
                    shape.LineTo(new Point(start + i * bar, y), false, false);
                    shape.LineTo(new Point(start + (i + 1) * bar, y), false, false);
                }

                for (var i = count - 1; i >= 0; i--)
                {
                    var y = _edges[i * 5 + lower];
                    shape.LineTo(new Point(start + (i + 1) * bar, y), false, false);
                    shape.LineTo(new Point(start + i * bar, y), false, false);
                }
            }

            geometry.Freeze();
            drawing.DrawGeometry(brush, null, geometry);
        }

        Line(1000 / 60.0, "60 fps");
        Line(1000 / 30.0, "30 fps");

        void Line(double ms, string label)
        {
            if (LineBrush is null || ms > ScaleMs)
                return;
            var y = Math.Round(height - ms * perMs) + 0.5;
            var pen = new Pen(LineBrush, 1) { DashStyle = DashStyles.Dash };
            drawing.DrawLine(pen, new Point(0, y), new Point(width, y));
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                LineBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new Point(4, y - text.Height - 1));
        }
    }

    private static DependencyProperty Register<T>(string name, T fallback) =>
        DependencyProperty.Register(name, typeof(T), typeof(FrameGraph), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));
}
