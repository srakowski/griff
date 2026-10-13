namespace Griff.Projects;

// The Match-3 and Falling Blocks templates' pictures: six cut gems (each its own color and shape, so they tell apart
// without color too) and a stone block for the gem board, and seven bevelled blocks, one for each falling shape.
internal static partial class TemplateArt
{
    public const string GemTiles = "Textures/gems.png";
    public const string PieceTiles = "Textures/pieces.png";

    /// <summary>The tiles across <see cref="GemTiles"/>: six gems, then the stone block.</summary>
    public const int GemColumns = 7;

    public const int GemStoneTile = 6;

    /// <summary>The tiles across <see cref="PieceTiles"/>: one for each shape, in the order of FallingBoard.AllShapes.</summary>
    public const int PieceColumns = 7;

    private static Dictionary<string, byte[]> MatchFiles() => new() { [GemTiles] = GemSheet().Png() };

    private static Dictionary<string, byte[]> FallingFiles() => new() { [PieceTiles] = PieceSheet().Png() };

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

    private static Canvas PieceSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * PieceColumns, size);
        // I J L O S T Z: cyan, blue, orange, yellow, green, purple, red.
        Pixel[] colors =
        [
            Rgb(0x4C, 0xD4, 0xE8), Rgb(0x4A, 0x6C, 0xE8), Rgb(0xF0, 0x9A, 0x3A), Rgb(0xF2, 0xD3, 0x4B),
            Rgb(0x5C, 0xC0, 0x5A), Rgb(0xA0, 0x5C, 0xD8), Rgb(0xE0, 0x4A, 0x4A),
        ];
        for (var i = 0; i < colors.Length; i++)
        {
            var c = new Canvas(size, size, 90 + i);
            var color = colors[i];
            c.Fill(color.Times(0.85f));
            c.Rect(5, 5, 22, 22, color);
            c.Rect(8, 7, 10, 3, color.Times(1.3f));
            c.Rect(0, 0, 32, 3, color.Times(1.35f));
            c.Rect(0, 0, 3, 32, color.Times(1.2f));
            c.Rect(0, 29, 32, 3, color.Times(0.55f));
            c.Rect(29, 0, 3, 32, color.Times(0.65f));
            c.Bevel(0, 0, 32, 32, color.Times(1.5f), color.Times(0.4f));
            sheet.Paste(c, i * size, 0);
        }

        return sheet;
    }

    // A regular polygon's corners around (cx, cy), the first at the given angle (0 is straight right).
    private static (float X, float Y)[] Ring(int corners, float cx, float cy, float radius, float start) =>
        Enumerable.Range(0, corners).Select(i => start + i * MathF.Tau / corners)
            .Select(a => (cx + radius * MathF.Cos(a), cy - radius * MathF.Sin(a))).ToArray();

    private static (float X, float Y)[] Star(float cx, float cy, float outer, float inner) =>
        Enumerable.Range(0, 10).Select(i => (Angle: MathF.PI / 2 + i * MathF.PI / 5, Radius: i % 2 == 0 ? outer : inner))
            .Select(p => (cx + p.Radius * MathF.Cos(p.Angle), cy - p.Radius * MathF.Sin(p.Angle))).ToArray();

    // Fills the pixels whose middles are inside the polygon.
    private static void Polygon(Canvas c, (float X, float Y)[] points, Pixel color)
    {
        for (var y = 0; y < c.Height; y++)
        {
            for (var x = 0; x < c.Width; x++)
            {
                var (px, py) = (x + 0.5f, y + 0.5f);
                var inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                {
                    var (a, b) = (points[i], points[j]);
                    if ((a.Y > py) != (b.Y > py) && px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }

                if (inside)
                    c.Set(x, y, color);
            }
        }
    }
}
