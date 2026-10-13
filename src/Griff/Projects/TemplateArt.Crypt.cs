namespace Griff.Projects;

// The Roguelike template's pictures: a crypt's stone walls and flagstone floor, its hero, the slimes (the Multiplayer
// template's) and bats that haunt it, potions, gold and the stairs down.
internal static partial class TemplateArt
{
    public const string CryptTiles = "Textures/crypt.png";
    public const string Hero = "Textures/hero.png";
    public const string Bat = "Textures/bat.png";
    public const string Potion = "Textures/potion.png";
    public const string Gold = "Textures/gold.png";
    public const string Stairs = "Textures/stairs.png";

    /// <summary>The tiles across <see cref="CryptTiles"/>: wall and floor.</summary>
    public const int CryptColumns = 2;

    public const int CryptWallTile = 0, CryptFloorTile = 1;

    internal static Dictionary<string, byte[]> CryptFiles() => new()
    {
        [CryptTiles] = CryptSheet().Png(),
        [Hero] = HeroArt().Png(),
        [Slime] = SlimeArt().Png(),
        [Bat] = BatArt().Png(),
        [Potion] = PotionArt().Png(),
        [Gold] = GoldArt().Png(),
        [Stairs] = StairsArt().Png(),
    };

    // Two 32-pixel tiles: the stone wall, and worn flagstones.
    private static Canvas CryptSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * CryptColumns, size);
        sheet.Paste(Stone(), CryptWallTile * size, 0);
        sheet.Paste(Flagstones(), CryptFloorTile * size, 0);
        return sheet;
    }

    // Four dark slabs, each its own shade, with cracks between.
    private static Canvas Flagstones()
    {
        var c = new Canvas(32, 32, 80);
        var slab = Rgb(0x2E, 0x2A, 0x30);
        var random = new Random(80);
        foreach (var (x, y) in new[] { (0, 0), (16, 0), (0, 16), (16, 16) })
        {
            c.Rect(x, y, 16, 16, slab.Times(0.85f + random.NextSingle() * 0.3f));
            c.Rect(x, y + 15, 16, 1, slab.Times(0.55f));
            c.Rect(x + 15, y, 1, 16, slab.Times(0.55f));
        }

        c.Line(4, 6, 9, 9, slab.Times(0.6f));
        c.Noise(0.06f);
        return c;
    }

    // A knight in a blue tunic with a sword, from the side, facing right.
    private static Canvas HeroArt()
    {
        var c = new Canvas(32, 32, 81);
        var tunic = Rgb(0x4C, 0x7C, 0xE8);
        var steel = Rgb(0xC4, 0xCC, 0xDC);
        var skin = Rgb(0xF2, 0xC2, 0x96);
        c.Rect(11, 16, 10, 9, tunic);
        c.Rect(12, 25, 3, 4, Rgb(0x4A, 0x3A, 0x2E));
        c.Rect(17, 25, 3, 4, Rgb(0x4A, 0x3A, 0x2E));
        c.Ellipse(16, 11, 6, 6, steel);
        c.Rect(13, 10, 8, 3, skin);
        c.Rect(18, 11, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        c.Rect(10, 6, 12, 2, steel.Times(0.8f));
        c.Rect(22, 12, 2, 10, steel);
        c.Rect(21, 20, 4, 2, Rgb(0x8A, 0x6A, 0x3A));
        c.Rect(8, 16, 4, 8, Rgb(0xB0, 0x40, 0x40));
        c.Shade(0.2f);
        c.Outline(Rgb(0x14, 0x16, 0x22));
        return c;
    }

    // A purple bat, wings spread.
    private static Canvas BatArt()
    {
        var c = new Canvas(32, 32, 82);
        var wing = Rgb(0x7A, 0x4C, 0xB8);
        c.Ellipse(16, 16, 5, 6, wing.Times(1.15f));
        for (var x = 2; x < 12; x++)
        {
            var drop = (12 - x) / 3;
            c.Rect(x, 12 + drop, 1, 6 - drop, wing);
            c.Rect(31 - x, 12 + drop, 1, 6 - drop, wing);
        }

        c.Rect(13, 9, 2, 3, wing);
        c.Rect(18, 9, 2, 3, wing);
        c.Set(14, 15, Rgb(0xFF, 0xE0, 0x4A));
        c.Set(18, 15, Rgb(0xFF, 0xE0, 0x4A));
        c.Shade(0.25f);
        c.Outline(Rgb(0x1E, 0x10, 0x2E));
        return c;
    }

    // A round red flask with a cork.
    private static Canvas PotionArt()
    {
        var c = new Canvas(16, 16, 83);
        var red = Rgb(0xE0, 0x40, 0x60);
        c.Ellipse(7, 10, 5, 4, red);
        c.Rect(6, 3, 3, 4, Rgb(0xC8, 0xD8, 0xE8));
        c.Rect(6, 2, 3, 1, Rgb(0x9A, 0x6A, 0x3A));
        c.Set(5, 9, Rgb(0xFF, 0xB8, 0xC8));
        c.Shade(0.2f);
        c.Outline(Rgb(0x3A, 0x10, 0x18));
        return c;
    }

    // A little heap of gold coins.
    private static Canvas GoldArt()
    {
        var c = new Canvas(16, 16, 84);
        var gold = Rgb(0xF2, 0xC9, 0x4C);
        c.Ellipse(5, 11, 4, 2, gold.Times(0.85f));
        c.Ellipse(10, 11, 4, 2, gold);
        c.Ellipse(8, 8, 4, 2, gold.Times(1.1f));
        c.Set(7, 7, Rgb(0xFF, 0xF4, 0xC0));
        c.Shade(0.2f);
        c.Outline(Rgb(0x4A, 0x36, 0x10));
        return c;
    }

    // Stone steps going down into the dark.
    private static Canvas StairsArt()
    {
        var c = new Canvas(32, 32, 85);
        c.Fill(Rgb(0x0C, 0x0A, 0x10));
        var step = Rgb(0x8A, 0x86, 0x94);
        for (var i = 0; i < 5; i++)
        {
            var shade = 1 - i * 0.16f;
            c.Rect(2 + i * 2, 4 + i * 5, 28 - i * 4, 4, step.Times(shade));
            c.Rect(2 + i * 2, 8 + i * 5, 28 - i * 4, 1, step.Times(shade * 0.5f));
        }

        c.Noise(0.05f);
        c.Outline(Rgb(0x1A, 0x18, 0x20));
        return c;
    }
}
