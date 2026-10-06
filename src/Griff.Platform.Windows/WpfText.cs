using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Griff.Platform.Windows;

/// <summary>Rasterizes game text with WPF: white, semibold, centered lines, straight-alpha RGBA.</summary>
public static class WpfText
{
    /// <summary>Pixel height of one line in the texture. Big enough to stay crisp when scaled up.</summary>
    public const double LineHeight = 64;

    private static readonly Typeface Typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static (byte[] Rgba, int Width, int Height) Rasterize(string text)
    {
        var lines = text.Split('\n');
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface,
            LineHeight * 0.8, Brushes.White, 1.0)
        {
            TextAlignment = TextAlignment.Center,
            LineHeight = LineHeight,
        };

        var width = Math.Max(1, (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 8);
        var height = (int)(LineHeight * lines.Length);
        formatted.MaxTextWidth = width;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawText(formatted, new Point(0, 0));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        // Glyphs are white and colored by the tint, so keep only coverage: white RGB everywhere, alpha as drawn.
        // White under transparent pixels also avoids dark fringes when the texture is smoothed.
        for (var i = 0; i < pixels.Length; i += 4)
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;

        return (pixels, width, height);
    }
}
