using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Fonts;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// The font picker's previews: a font's name drawn in the font itself, by Griff.Fonts as the game draws it (white, with
/// its coverage as alpha, so the list tints it with the theme's text color). A free font not in the project yet is drawn
/// from Griff's own copy; an installed font from this PC, which is only ever shown here, never kept.
/// </summary>
public sealed class FontPreviews
{
    /// <summary>How tall a line of preview text is shown, in pixels.</summary>
    public const double LineHeight = 20;

    private GlyphAtlas? _atlas;
    private string? _root;

    /// <summary>
    /// <paramref name="text"/> drawn in <paramref name="font"/> (as a Font names it) for the project whose Assets are
    /// <paramref name="assetRoot"/>, or null when nothing draws.
    /// </summary>
    public BitmapSource? Preview(string? assetRoot, string? font, string text)
    {
        if (_atlas is null || _root != assetRoot)
            (_atlas, _root) = (new GlyphAtlas(assetRoot ?? FreeFonts.Folder), assetRoot);
        var (rgba, width, height, _) = _atlas.Rasterize(text, FontChoices.PreviewFont(font, assetRoot), TextAlign.Left);
        _atlas.EndFrame();
        if (width <= 0 || height <= 0)
            return null;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rgba, width * 4);
        image.Freeze();
        return image;
    }
}
