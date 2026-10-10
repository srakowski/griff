using System.Windows;
using System.Windows.Media;

namespace Griff.Editor.Features.Audio;

/// <summary>Draws a sound's shape from its peaks (0 to 1 per slice), mirrored about the middle.</summary>
public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(
        nameof(Line), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[]? Peaks
    {
        get => (float[]?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The center line.</summary>
    public Brush Line
    {
        get => (Brush)GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var (width, height) = (ActualWidth, ActualHeight);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var middle = height / 2;
        context.DrawRectangle(Line, null, new Rect(0, Math.Floor(middle), width, 1));
        if (Peaks is not { Length: > 0 } peaks)
            return;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, middle), true, true);
            for (var i = 0; i < peaks.Length; i++)
                g.LineTo(new Point(i * width / (peaks.Length - 1.0), middle - Math.Max(0.5, peaks[i] * middle * 0.95)), true, false);
            for (var i = peaks.Length - 1; i >= 0; i--)
                g.LineTo(new Point(i * width / (peaks.Length - 1.0), middle + Math.Max(0.5, peaks[i] * middle * 0.95)), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(Fill, null, geometry);
    }
}
