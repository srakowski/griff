using System.Numerics;
using Griff.Scenes;

namespace Griff.Fonts;

/// <summary>The sizes game text is rasterized at.</summary>
public static class TextRaster
{
    /// <summary>Pixels in one line of text: big enough to stay crisp when it's drawn bigger.</summary>
    public const int LinePixels = 64;

    /// <summary>The font's size (its em) in pixels: 0.8 of a line, so a line has room above and below its letters.</summary>
    public const float EmPixels = LinePixels * 0.8f;
}

/// <summary>
/// One glyph of laid-out text, as a quad: where it goes (in lines, so a <see cref="SpriteDraw"/> of text, one line high,
/// takes it to the world; Y up, the origin where the text is anchored) and the part of an atlas
/// <see cref="Page"/> it shows (u, v, width, height, from the page's top left, 0 to 1).
/// </summary>
public readonly record struct GlyphQuad(float Left, float Top, float Right, float Bottom, Vector4 Source, int Page);

/// <summary>
/// Lays text out as quads, one a glyph, from glyphs rasterized into atlas pages the first time they're drawn: the same
/// on the desktop and in the browser, with nothing from the operating system. Each renderer has its own and draws the
/// quads with its textures of the <see cref="Pages"/>. Laying out text whose glyphs are all on a page makes no garbage,
/// so a score or a timer changing every frame costs nothing more than drawing it.
/// </summary>
/// <remarks>
/// Lines are <see cref="TextRaster.LinePixels"/> high and the font's em 0.8 of that, as Griff's text always was; a text
/// block is centered on its anchor up and down, and each line lines up with it by the <see cref="TextAlign"/>: starting
/// at it, ending at it or centered on it. Fonts come from <see cref="Fonts"/> (see <see cref="FontCatalog"/>); a
/// character a font hasn't got comes from the built-in font, then from the fallback (arrows, shapes, symbols such as ♥,
/// Greek and Cyrillic; see <see cref="BuiltInFonts"/>), then, with <see cref="InstalledFallback"/> (on the desktop), from
/// Windows' own fonts on the player's computer (Chinese, Japanese, Korean, emoji...; see
/// <see cref="InstalledFonts.Fallback"/>), and is drawn as the font's missing-glyph box if none has it.
/// </remarks>
public sealed class GlyphAtlas
{
    /// <summary>Pages the atlas grows to before it starts again from nothing (between frames).</summary>
    public const int MaxPages = 8;

    private const int MaxKerningPairs = 1 << 16;

    private readonly List<GlyphPage> _pages = [];
    private readonly Dictionary<(int Face, int Codepoint), Glyph> _chars = [];
    private readonly Dictionary<(int Face, int Index), Glyph> _glyphs = [];
    private readonly Dictionary<(int Face, int Left, int Right), float> _kerning = [];
    private GlyphQuad[] _quads = new GlyphQuad[64];

    /// <param name="assetRoot">The project's Assets folder, which font files are found in; null for none.</param>
    /// <param name="installedFallback">See <see cref="InstalledFallback"/>.</param>
    public GlyphAtlas(string? assetRoot, bool installedFallback = true)
    {
        Fonts = new FontCatalog(assetRoot);
        InstalledFallback = installedFallback;
    }

    /// <summary>Where the faces text names come from.</summary>
    public FontCatalog Fonts { get; }

    /// <summary>
    /// Whether a character no built-in font has is looked for in the fonts installed on this computer, as WPF did: drawn
    /// there and never shipped or kept. A browser has none, so there it stays the missing-glyph box.
    /// </summary>
    public bool InstalledFallback { get; }

    /// <summary>The face <paramref name="codepoint"/> is drawn from in <paramref name="font"/>: its own, a fallback's, or null for the missing-glyph box.</summary>
    public FontFace? FaceOf(int codepoint, string? font) =>
        Resolve(Fonts.FaceFor(font), codepoint) is { Index: > 0 } glyph ? glyph.Face : null;

    /// <summary>The pages glyphs are on, which <see cref="GlyphQuad.Page"/> indexes.</summary>
    public IReadOnlyList<GlyphPage> Pages => _pages;

    /// <summary>
    /// Lays <paramref name="text"/> out as quads in <paramref name="font"/>, rasterizing glyphs not on a page yet. The
    /// span is the atlas's own and lasts until it's next called.
    /// </summary>
    public ReadOnlySpan<GlyphQuad> Lay(string text, string? font, TextAlign align)
    {
        var count = 0;
        if (text.Length == 0)
            return [];
        var face = Fonts.FaceFor(font);
        var lines = text.AsSpan().Count('\n') + 1;
        var line = 0;
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;
            var width = Measure(face, text, start, end);
            var pen = align switch { TextAlign.Left => 0f, TextAlign.Right => -width, _ => -width / 2 };
            var baseline = (lines / 2f - line - face.BaselineShare) * TextRaster.LinePixels;
            Glyph? previous = null;
            for (var i = start; i < end;)
            {
                var codepoint = Next(text, ref i);
                if (Resolve(face, codepoint) is not { } glyph)
                    continue;
                if (previous is not null && previous.Face == glyph.Face)
                    pen += Kerning(previous, glyph);
                if (glyph.Width > 0 && Place(glyph))
                {
                    if (count == _quads.Length)
                        Array.Resize(ref _quads, count * 2);
                    var left = pen + glyph.Left - 1;
                    var top = baseline - glyph.Top + 1;
                    _quads[count++] = new GlyphQuad(left / TextRaster.LinePixels, top / TextRaster.LinePixels,
                        (left + glyph.Width + 2) / TextRaster.LinePixels, (top - glyph.Height - 2) / TextRaster.LinePixels,
                        new Vector4(glyph.X, glyph.Y, glyph.Width + 2, glyph.Height + 2) / GlyphPage.Size, glyph.Page);
                }

                pen += Advance(face, glyph, codepoint);
                previous = glyph;
            }

            start = end + 1;
            line++;
        }

        return _quads.AsSpan(0, count);
    }

    /// <summary>
    /// <paramref name="text"/> drawn into an image of its own (straight-alpha RGBA, white with coverage as alpha, rows from
    /// the top), and where it goes: its top left corner, in lines from the anchor (Y up), as <see cref="Lay"/> places it.
    /// For drawing text where the atlas's pages can't be (the editor's Scene view, tests). Empty for no glyphs.
    /// </summary>
    public (byte[] Rgba, int Width, int Height, Vector2 TopLeft) Rasterize(string text, string? font, TextAlign align)
    {
        var quads = Lay(text, font, align);
        if (quads.Length == 0)
            return ([], 0, 0, Vector2.Zero);
        int Pixel(float lines) => (int)MathF.Round(lines * TextRaster.LinePixels);
        int left = int.MaxValue, top = int.MinValue, right = int.MinValue, bottom = int.MaxValue;
        foreach (var quad in quads)
        {
            left = Math.Min(left, Pixel(quad.Left));
            top = Math.Max(top, Pixel(quad.Top));
            right = Math.Max(right, Pixel(quad.Left) + (int)MathF.Round(quad.Source.Z * GlyphPage.Size));
            bottom = Math.Min(bottom, Pixel(quad.Top) - (int)MathF.Round(quad.Source.W * GlyphPage.Size));
        }

        var (width, height) = (right - left, top - bottom);
        var rgba = new byte[width * height * 4];
        foreach (var quad in quads)
        {
            var page = _pages[quad.Page].Coverage;
            var (sx, sy) = ((int)MathF.Round(quad.Source.X * GlyphPage.Size), (int)MathF.Round(quad.Source.Y * GlyphPage.Size));
            var (w, h) = ((int)MathF.Round(quad.Source.Z * GlyphPage.Size), (int)MathF.Round(quad.Source.W * GlyphPage.Size));
            var (dx, dy) = (Pixel(quad.Left) - left, top - Pixel(quad.Top));
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var at = ((dy + y) * width + dx + x) * 4 + 3;
                    rgba[at] = Math.Max(rgba[at], page[(sy + y) * GlyphPage.Size + sx + x]);
                }
            }
        }

        for (var i = 0; i < rgba.Length; i += 4)
            rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
        return (rgba, width, height, new Vector2(left, top) / TextRaster.LinePixels);
    }

    /// <summary>
    /// Reads again the font files that changed (checked at most once a second), so editing a font updates the running
    /// game. True if one did.
    /// </summary>
    public bool RefreshChanged() => Fonts.RefreshChanged();

    /// <summary>
    /// Called once a frame, after drawing it: an atlas that has grown past <see cref="MaxPages"/> starts again from nothing,
    /// to fill with the glyphs the next frames draw.
    /// </summary>
    public void EndFrame()
    {
        if (_pages.Count > MaxPages)
        {
            _chars.Clear();
            _glyphs.Clear();
            _pages.RemoveRange(1, _pages.Count - 1);
            _pages[0].Clear();
        }

        if (_kerning.Count > MaxKerningPairs)
            _kerning.Clear();
    }

    // The width of a line, in pixels, to its last glyph that isn't white space (as WPF lined text up).
    private float Measure(FontFace face, string text, int start, int end)
    {
        float pen = 0, width = 0;
        Glyph? previous = null;
        for (var i = start; i < end;)
        {
            var codepoint = Next(text, ref i);
            if (Resolve(face, codepoint) is not { } glyph)
                continue;
            if (previous is not null && previous.Face == glyph.Face)
                pen += Kerning(previous, glyph);
            pen += Advance(face, glyph, codepoint);
            if (!char.IsWhiteSpace((char)Math.Min(codepoint, char.MaxValue)) || codepoint > char.MaxValue)
                width = pen;
            previous = glyph;
        }

        return width;
    }

    // A tab is four spaces wide.
    private float Advance(FontFace face, Glyph glyph, int codepoint) =>
        codepoint == '\t' && Resolve(face, ' ') is { } space ? space.Advance * 4 : glyph.Advance;

    // The character at i (a pair of surrogates makes one), moving i past it.
    private static int Next(string text, ref int i)
    {
        var c = text[i++];
        if (char.IsHighSurrogate(c) && i < text.Length && char.IsLowSurrogate(text[i]))
            return char.ConvertToUtf32(c, text[i++]);
        return c;
    }

    // The glyph a character is drawn with in a face, looked for in the built-in font, then the fallback, then (on the
    // desktop) the installed fonts when the face hasn't got it, and the face's missing-glyph box when none has. Control characters (a carriage return) are nothing.
    private Glyph? Resolve(FontFace face, int codepoint)
    {
        if (codepoint < ' ' && codepoint != '\t')
            return null;
        if (_chars.TryGetValue((face.Id, codepoint), out var glyph))
            return glyph;

        var (from, index) = (face, codepoint == '\t' ? face.GlyphOf(' ') : face.GlyphOf(codepoint));
        var (builtIn, fallback) = (BuiltInFonts.Default.Face, BuiltInFonts.Fallback.Face);
        if (index == 0 && face != builtIn && builtIn.GlyphOf(codepoint) is var inBuiltIn and > 0)
            (from, index) = (builtIn, inBuiltIn);
        if (index == 0 && fallback.GlyphOf(codepoint) is var inFallback and > 0)
            (from, index) = (fallback, inFallback);
        if (index == 0 && InstalledFallback && codepoint > ' ' && InstalledFonts.Fallback(codepoint) is { } installed)
            (from, index) = (installed, installed.GlyphOf(codepoint));

        if (!_glyphs.TryGetValue((from.Id, index), out glyph))
        {
            var (left, top, width, height) = from.Box(index);
            glyph = new Glyph(from, index, from.Advance(index), left, top, width, height);
            _glyphs[(from.Id, index)] = glyph;
        }

        _chars[(face.Id, codepoint)] = glyph;
        return glyph;
    }

    private float Kerning(Glyph left, Glyph right)
    {
        if (!left.Face.HasKerning)
            return 0;
        var key = (left.Face.Id, left.Index, right.Index);
        if (!_kerning.TryGetValue(key, out var kerning))
            _kerning[key] = kerning = left.Face.Kerning(left.Index, right.Index);
        return kerning;
    }

    // Rasterizes a glyph onto a page the first time it's drawn: the last page if it has room, otherwise a new one.
    private bool Place(Glyph glyph)
    {
        if (glyph.Page >= 0)
            return true;
        if (_pages.Count == 0)
            _pages.Add(new GlyphPage(0));
        var page = _pages[^1];
        if (!page.TryPlace(glyph.Width, glyph.Height, out var x, out var y))
        {
            page = new GlyphPage(_pages.Count);
            if (!page.TryPlace(glyph.Width, glyph.Height, out x, out y))
                return false;
            _pages.Add(page);
        }

        glyph.Face.Rasterize(glyph.Index, page.Coverage, (y + 1) * GlyphPage.Size + x + 1, GlyphPage.Size, glyph.Width, glyph.Height);
        page.Changed(y, glyph.Height + 2);
        (glyph.Page, glyph.X, glyph.Y) = (page.Index, x, y);
        return true;
    }

    // A glyph of a face: how far it moves the pen, the box its coverage fills from the pen on the baseline (pixels, Y
    // down), and once it's drawn, where on which page it is (its box with a pixel of nothing round it).
    private sealed class Glyph(FontFace face, int index, float advance, int left, int top, int width, int height)
    {
        public FontFace Face { get; } = face;

        public int Index { get; } = index;

        public float Advance { get; } = advance;

        public int Left { get; } = left;

        public int Top { get; } = top;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public int Page { get; set; } = -1;

        public int X { get; set; }

        public int Y { get; set; }
    }
}
