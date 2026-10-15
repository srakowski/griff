using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace Griff.Fonts;

/// <summary>
/// One font (a .ttf or .otf file, or the first face of a .ttc), read with StbTrueTypeSharp: which glyph draws a
/// character, how far each goes, the kerning between two, and a glyph's coverage at <see cref="TextRaster.EmPixels"/>.
/// </summary>
/// <remarks>
/// Faces are shared: the built-in ones and installed fonts by every atlas. Reading one is safe from any thread, and
/// rasterizing a glyph locks the face.
/// </remarks>
public sealed unsafe class FontFace
{
    private static int s_nextId;

    private readonly stbtt_fontinfo _info;
    private readonly float _scale;

    private FontFace(stbtt_fontinfo info, string name)
    {
        _info = info;
        Name = name;
        Id = Interlocked.Increment(ref s_nextId);
        _scale = stbtt_ScaleForMappingEmToPixels(info, TextRaster.EmPixels);
        HasKerning = info.kern != 0 || info.gpos != 0;

        // Where the baseline sits in a line: the share of the line above it, by the font's Windows ascent and descent
        // (what WPF used, so text sits where it always has), or its own ascent and descent if it has no OS/2 table.
        var os2 = stbtt__find_table(info.data, (uint)info.fontstart, "OS/2");
        int ascent, descent;
        if (os2 != 0 && ReadUShort(info.data + os2 + 74) is var winAscent and > 0 && ReadUShort(info.data + os2 + 76) is var winDescent)
        {
            (ascent, descent) = (winAscent, winDescent);
        }
        else
        {
            int a, d, gap;
            stbtt_GetFontVMetrics(info, &a, &d, &gap);
            (ascent, descent) = (a, -d);
        }

        BaselineShare = ascent + descent > 0 ? (float)ascent / (ascent + descent) : 0.8f;
    }

    /// <summary>Unique among the faces loaded, for keying glyphs by face.</summary>
    public int Id { get; }

    /// <summary>What it was loaded from: a resource's name, a file's path or an installed font's.</summary>
    public string Name { get; }

    /// <summary>How far down a line its baseline is, as a share of the line's height.</summary>
    public float BaselineShare { get; }

    /// <summary>Whether it has kerning (a kern table, or pairs in GPOS).</summary>
    public bool HasKerning { get; }

    /// <summary>A face from a font file's bytes, or null if they aren't a font StbTrueType can read.</summary>
    public static FontFace? Load(byte[] data, string name)
    {
        if (data.Length < 12)
            return null;
        try
        {
            int offset;
            fixed (byte* bytes = data)
                offset = stbtt_GetFontOffsetForIndex(bytes, 0);
            if (offset < 0)
                return null;
            var info = CreateFont(data, offset);
            return info is null || info.numGlyphs <= 0 ? null : new FontFace(info, name);
        }
#pragma warning disable CA1031 // Whatever a broken file throws, it isn't a font: the text falls back to the built-in one.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>The glyph that draws <paramref name="codepoint"/>, or 0 if the font hasn't one.</summary>
    public int GlyphOf(int codepoint) => stbtt_FindGlyphIndex(_info, codepoint);

    /// <summary>How far the pen moves after <paramref name="glyph"/>, in raster pixels.</summary>
    public float Advance(int glyph)
    {
        int advance, bearing;
        stbtt_GetGlyphHMetrics(_info, glyph, &advance, &bearing);
        return advance * _scale;
    }

    /// <summary>What to add to the pen between <paramref name="left"/> and <paramref name="right"/>, in raster pixels.</summary>
    public float Kerning(int left, int right) => HasKerning ? stbtt_GetGlyphKernAdvance(_info, left, right) * _scale : 0;

    /// <summary>
    /// The box <paramref name="glyph"/>'s coverage fills, in raster pixels from its pen position on the baseline (Y down),
    /// or an empty one for a glyph with nothing to draw (a space).
    /// </summary>
    public (int Left, int Top, int Width, int Height) Box(int glyph)
    {
        if (stbtt_IsGlyphEmpty(_info, glyph) != 0)
            return default;
        int x0, y0, x1, y1;
        stbtt_GetGlyphBitmapBox(_info, glyph, _scale, _scale, &x0, &y0, &x1, &y1);
        return (x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    /// <summary>
    /// Draws <paramref name="glyph"/>'s coverage (0 to 255) into <paramref name="into"/>, whose rows are
    /// <paramref name="stride"/> bytes apart, starting at <paramref name="at"/>, in a box as big as <see cref="Box"/> says.
    /// </summary>
    public void Rasterize(int glyph, Span<byte> into, int at, int stride, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;
        if (at < 0 || at + (height - 1) * stride + width > into.Length)
            throw new ArgumentOutOfRangeException(nameof(at), "The glyph doesn't fit where it's put.");
        lock (_info)
        {
            fixed (byte* output = into)
                stbtt_MakeGlyphBitmap(_info, output + at, width, height, stride, _scale, _scale, glyph);
        }
    }

    // OpenType numbers are big-endian.
    private static int ReadUShort(byte* at) => (at[0] << 8) | at[1];

    /// <summary>Reads a font embedded in this assembly (see Griff.Fonts.csproj).</summary>
    internal static FontFace Embedded(string fileName)
    {
        var name = "Griff.Fonts." + fileName;
        using var stream = typeof(FontFace).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"The font {name} isn't in Griff.Fonts.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return Load(data, fileName) ?? throw new InvalidDataException($"The font {name} can't be read.");
    }
}
