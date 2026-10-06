using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Graphics;
using Griff.Scenes;

namespace Griff.Platform.Windows;

/// <summary>Rasterizes game text with WPF: white glyphs, lined up by alignment, straight-alpha RGBA.</summary>
public static class WpfText
{
    /// <summary>Pixel height of one line in the texture. Big enough to stay crisp when scaled up.</summary>
    public const double LineHeight = 64;

    private static readonly string[] FontFileExtensions = [".ttf", ".otf", ".ttc"];

    /// <summary>The built-in font.</summary>
    public static readonly Typeface DefaultTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly ConcurrentDictionary<(string Font, string AssetRoot), (Typeface Typeface, DateTime Stamp)> Typefaces = new();

    /// <summary>A rasterizer for the renderer that finds font files in <paramref name="assetRoot"/>.</summary>
    public static TextRasterizer For(string assetRoot) => (text, font, align) => Rasterize(text, font, align, assetRoot);

    /// <summary>
    /// The typeface for a <see cref="Text.Font"/>: a font file relative to <paramref name="assetRoot"/>, or an installed
    /// font's name. Falls back to the built-in font when the file is missing or unreadable. Font files are re-read
    /// when they change.
    /// </summary>
    public static Typeface TypefaceFor(string? font, string? assetRoot)
    {
        if (string.IsNullOrWhiteSpace(font))
            return DefaultTypeface;
        font = font.Trim();

        if (!FontFileExtensions.Any(e => font.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            return Typefaces.GetOrAdd((font, ""), key => (new Typeface(new FontFamily(key.Font), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), default)).Typeface;

        if (assetRoot is null)
            return DefaultTypeface;
        var path = Path.GetFullPath(Path.Combine(assetRoot, font.Replace('/', Path.DirectorySeparatorChar)));
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (Typefaces.TryGetValue((font, assetRoot), out var cached) && cached.Stamp == stamp)
            return cached.Typeface;

        var typeface = stamp == DateTime.MinValue ? DefaultTypeface : LoadFontFile(path) ?? DefaultTypeface;
        Typefaces[(font, assetRoot)] = (typeface, stamp);
        return typeface;
    }

    public static (byte[] Rgba, int Width, int Height) Rasterize(string text, string? font = null, TextAlign align = TextAlign.Center,
        string? assetRoot = null)
    {
        var lines = text.Split('\n');
        var formatted = Format(text, TypefaceFor(font, assetRoot), align, Brushes.White, 1.0);

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

    /// <summary>Text laid out the way the game draws it: one <see cref="LineHeight"/> per line, lined up by <paramref name="align"/>.</summary>
    public static FormattedText Format(string text, Typeface typeface, TextAlign align, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, LineHeight * 0.8, brush, pixelsPerDip)
        {
            TextAlignment = align switch { TextAlign.Left => TextAlignment.Left, TextAlign.Right => TextAlignment.Right, _ => TextAlignment.Center },
            LineHeight = LineHeight,
        };

    // The first typeface in the file (a .ttc may hold several).
    private static Typeface? LoadFontFile(string path)
    {
        try
        {
            return Fonts.GetTypefaces(new Uri(path)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }
}
