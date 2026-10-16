using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Match3;

// The Match-3 template's pictures: six cut gems (each its own color and shape, so they tell apart without color too) and
// a stone block for the gem board.
internal static class Match3Art
{
    public const string GemTiles = "Textures/gems.png";

    /// <summary>The tiles across <see cref="GemTiles"/>: six gems, then the stone block.</summary>
    public const int GemColumns = 7;

    public const int GemStoneTile = 6;

    internal static Dictionary<string, byte[]> Files() => new() { [GemTiles] = GemSheet().Png() };

    private static Canvas GemSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * GemColumns, size);
        // A round ruby, a hexagonal topaz, a diamond-cut citrine, a square emerald, a sapphire drop and an amethyst star.
        (Pixel Color, (float X, float Y)[] Shape)[] gems =
        [
            (Rgb(0xE0, 0x3C, 0x48), Ring(16, 15.5f, 15.5f, 12.5f, 0)),
            (Rgb(0xF0, 0x8C, 0x2E), Ring(6, 15.5f, 15.5f, 13, 0)),
            (Rgb(0xF2, 0xD3, 0x4B), Ring(4, 15.5f, 15.5f, 13.5f, MathF.PI / 2)),
            (Rgb(0x4C, 0xC0, 0x5C), [(9, 4), (23, 4), (28, 9), (28, 23), (23, 28), (9, 28), (4, 23), (4, 9)]),
            (Rgb(0x4C, 0x8C, 0xF0), [(15.5f, 3), (25, 15), (26, 20), (23, 26), (15.5f, 29), (8, 26), (5, 20), (6, 15)]),
            (Rgb(0xB0, 0x5C, 0xE0), Star(15.5f, 16.5f, 14, 7)),
        ];
        for (var i = 0; i < gems.Length; i++)
            sheet.Paste(Gem(gems[i].Color, gems[i].Shape, 80 + i), i * size, 0);
        sheet.Paste(GemStone(), GemStoneTile * size, 0);
        return sheet;
    }

    // A gem: its outline filled, a lighter facet in the middle, a glint at the top left, lit from above and outlined dark.
    private static Canvas Gem(Pixel color, (float X, float Y)[] shape, int seed)
    {
        var c = new Canvas(32, 32, seed);
        Polygon(c, shape, color);
        var (cx, cy) = (shape.Average(p => p.X), shape.Average(p => p.Y));
        Polygon(c, shape.Select(p => (cx + (p.X - cx) * 0.55f, cy + (p.Y - cy) * 0.55f - 1)).ToArray(), color.Times(1.25f));
        c.Shade(0.25f);
        c.Ellipse(11, 10, 2, 1, Rgb(0xFF, 0xFF, 0xFF));
        c.Set(15, 8, Rgb(0xFF, 0xFF, 0xFF));
        c.Outline(color.Times(0.35f));
        return c;
    }

    // A grey block of stone: it can't be swapped and gems stop on it.
    private static Canvas GemStone()
    {
        var c = new Canvas(32, 32, 86);
        var stone = Rgb(0x6A, 0x66, 0x70);
        c.Rect(1, 1, 30, 30, stone);
        c.Bevel(1, 1, 30, 30, stone.Times(1.35f), stone.Times(0.55f));
        c.Line(8, 6, 13, 14, stone.Times(0.6f));
        c.Line(13, 14, 11, 22, stone.Times(0.6f));
        c.Line(20, 18, 26, 24, stone.Times(0.6f));
        c.Noise(0.08f);
        return c;
    }
}
