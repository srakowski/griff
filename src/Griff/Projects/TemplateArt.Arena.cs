namespace Griff.Projects;

// The Multiplayer template's pictures: a fighter in pale grey (the game paints each player's their own color), a slime,
// a heart, and the arena's floor and walls.
internal static partial class TemplateArt
{
    public const string Fighter = "Textures/fighter.png";
    public const string Slime = "Textures/slime.png";
    public const string Heart = "Textures/heart.png";
    public const string ArenaTiles = "Textures/arena.png";

    /// <summary>The tiles across <see cref="ArenaTiles"/>: floor, the floor's other shade, and wall.</summary>
    public const int ArenaColumns = 3;

    public const int FloorTile = 0, FloorTileAlt = 1, ArenaWallTile = 2;

    private static Dictionary<string, byte[]> ArenaFiles() => new()
    {
        [Fighter] = FighterArt().Png(),
        [Slime] = SlimeArt().Png(),
        [Heart] = HeartArt().Png(),
        [ArenaTiles] = ArenaSheet().Png(),
        [CrateArt] = Crate().Png(),
    };

    // A round little fighter seen from above, facing right: pale, so a tint gives it a player's color.
    private static Canvas FighterArt()
    {
        var c = new Canvas(32, 32, 60);
        var body = Rgb(0xE8, 0xEA, 0xEE);
        c.Ellipse(15, 16, 12, 12, body);
        c.Ellipse(11, 11, 5, 4, Rgb(0xFF, 0xFF, 0xFF));
        c.Shade(0.22f);

        // Eyes looking ahead, and a darker band for a belt.
        foreach (var y in new[] { 12, 19 })
        {
            c.Ellipse(21, y, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
            c.Ellipse(22, y, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        }

        c.Rect(5, 15, 6, 2, body.Times(0.7f));
        c.Outline(Rgb(0x1A, 0x1C, 0x22));
        return c;
    }

    private static Canvas SlimeArt()
    {
        var c = new Canvas(32, 32, 61);
        var green = Rgb(0x6C, 0xC9, 0x4E);
        c.Ellipse(15, 18, 13, 10, green);
        c.Ellipse(15, 12, 9, 7, green);
        c.Ellipse(10, 10, 3, 2, Rgb(0xB9, 0xF0, 0x9A));
        c.Shade(0.25f);
        c.Ellipse(11, 17, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
        c.Ellipse(20, 17, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
        c.Ellipse(12, 18, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        c.Ellipse(21, 18, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        c.Rect(13, 23, 6, 1, green.Times(0.5f));
        c.Outline(Rgb(0x1E, 0x3A, 0x18));
        return c;
    }

    private static Canvas HeartArt()
    {
        var c = new Canvas(16, 16, 62);
        var red = Rgb(0xE5, 0x45, 0x55);
        c.Ellipse(5, 5, 3, 3, red);
        c.Ellipse(10, 5, 3, 3, red);
        for (var y = 6; y < 14; y++)
        {
            var half = Math.Max(0, 13 - y) * 6 / 7 + 1;
            c.Rect(8 - half, y, half * 2, 1, red);
        }

        c.Ellipse(5, 4, 1, 1, Rgb(0xFF, 0xB4, 0xBC));
        c.Shade(0.2f);
        c.Outline(Rgb(0x4A, 0x10, 0x18));
        return c;
    }

    // Three 32-pixel tiles: slate floor in two shades (laid as a checkerboard), and a raised wall block.
    private static Canvas ArenaSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * ArenaColumns, size);
        sheet.Paste(FloorArt(Rgb(0x2E, 0x33, 0x40), 63), FloorTile * size, 0);
        sheet.Paste(FloorArt(Rgb(0x34, 0x3A, 0x48), 64), FloorTileAlt * size, 0);
        sheet.Paste(ArenaWall(), ArenaWallTile * size, 0);
        return sheet;
    }

    private static Canvas FloorArt(Pixel color, int seed)
    {
        var c = new Canvas(32, 32, seed);
        c.Fill(color);
        c.Bevel(0, 0, 32, 32, color.Times(1.15f), color.Times(0.8f));
        c.Noise(0.04f);
        return c;
    }

    private static Canvas ArenaWall()
    {
        var c = new Canvas(32, 32, 65);
        var stone = Rgb(0x5A, 0x60, 0x70);
        c.Fill(stone);
        c.Bevel(0, 0, 32, 32, stone.Times(1.35f), stone.Times(0.55f));
        c.Bevel(1, 1, 30, 30, stone.Times(1.15f), stone.Times(0.75f));
        c.Rect(4, 4, 24, 24, stone.Times(0.95f));
        c.Noise(0.06f);
        return c;
    }
}
