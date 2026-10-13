using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Griff.Editor.Features.SceneView;

/// <summary>The theme's brushes and caption text, as the Scene view draws them, looked up from where it sits.</summary>
internal sealed class SceneViewStyle(FrameworkElement view)
{
    public double PixelsPerDip => VisualTreeHelper.GetDpi(view).PixelsPerDip;

    public Brush Brush(string key) => (Brush)view.FindResource(key);

    public Brush Faded(string key, double opacity)
    {
        var brush = Brush(key).Clone();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }

    /// <summary>A line of caption text in the UI font.</summary>
    public FormattedText Text(string text, string brushKey) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface((FontFamily)view.FindResource("Font.UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        (double)view.FindResource("FontSize.Caption"),
        Brush(brushKey),
        PixelsPerDip);

    /// <summary>How big a line of caption text is.</summary>
    public Size Measure(string text)
    {
        var formatted = Text(text, "Brush.Text.Primary");
        return new Size(formatted.Width, formatted.Height);
    }
}
