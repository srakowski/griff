namespace Griff.Templates;

// The Isometric template's pictures: a tileset of 32 by 32 pixel tiles, each standing on a 32 by 16 diamond at its
// bottom (flat ground, and blocks as tall again), and the courtyard's people, slime, crate and plate, drawn for the slant.
public static partial class TemplateArt
{
    public const string IsoTiles = "Textures/iso-tiles.png";
    public const string IsoHero = "Textures/iso-hero.png";
    public const string IsoSlime = "Textures/iso-slime.png";
    public const string IsoCrate = "Textures/iso-crate.png";
    public const string IsoPlate = "Textures/iso-plate.png";
    public const string IsoGardener = "Textures/iso-gardener.png";
    public const string IsoGardenerPortrait = "Textures/gardener.png";

    /// <summary>The tiles across and down <see cref="IsoTiles"/>.</summary>
    public const int IsoTileColumns = 8, IsoTileRows = 1;

    /// <summary>The Isometric template's tiles, by number: grass, flowers, path and dirt on the ground; stone, mossy stone, hedge and a low wall standing up.</summary>
    public const int IsoGrass = 0, IsoFlowers = 1, IsoPath = 2, IsoDirt = 3, IsoStone = 4, IsoMossy = 5, IsoHedge = 6, IsoLowWall = 7;

    /// <summary>The Isometric template's pictures.</summary>
    public static Dictionary<string, byte[]> IsometricFiles() => new()
    {
        [IsoTiles] = IsoTileset().Png(),
        [IsoHero] = IsoPerson(Rgb(0x4C, 0x9A, 0xFF), Rgb(0x6B, 0x4A, 0x2E), hat: null).Png(),
        [IsoGardener] = IsoPerson(Rgb(0x57, 0xAB, 0x5A), Rgb(0x8A, 0x8F, 0x9C), hat: Rgb(0xE3, 0xC5, 0x6B)).Png(),
        [IsoSlime] = IsoSlimeArt().Png(),
        [IsoCrate] = IsoCrateArt().Png(),
        [IsoPlate] = IsoPlateArt().Png(),
        [IsoGardenerPortrait] = GardenerPortrait().Png(),
    };

    private static Canvas IsoTileset()
    {
        var sheet = new Canvas(32 * IsoTileColumns, 32 * IsoTileRows, seed: 40);
        var tiles = new[]
        {
            Ground(Rgb(0x5E, 0xA8, 0x4E), Rgb(0x7C, 0xC4, 0x5E), seed: 41),
            Flowers(Ground(Rgb(0x5E, 0xA8, 0x4E), Rgb(0x7C, 0xC4, 0x5E), seed: 42)),
            Paving(),
            Ground(Rgb(0x9C, 0x7A, 0x4E), Rgb(0xB4, 0x92, 0x62), seed: 43),
            Block(Rgb(0x9A, 0x9C, 0xA6), Rgb(0x9A, 0x9C, 0xA6), top: 0, seed: 44, courses: true),
            Block(Rgb(0x6F, 0xA8, 0x55), Rgb(0x8E, 0x90, 0x98), top: 0, seed: 45, courses: true),
            Block(Rgb(0x3F, 0x8A, 0x44), Rgb(0x3F, 0x8A, 0x44), top: 0, seed: 46, courses: false),
            Block(Rgb(0xB0, 0xA0, 0x88), Rgb(0xA0, 0x90, 0x78), top: 8, seed: 47, courses: true),
        };
        for (var i = 0; i < tiles.Length; i++)
        {
            // In one row, with each tile's outermost columns left clear: the screen's pixels at a picture's very edge can
            // pick up the pixel next to it in the image, and an isometric tile's left and right corners aren't covered by
            // its neighbors' pictures, as square tiles' edges are.
            var tile = tiles[i];
            for (var y = 0; y < 32; y++)
            {
                tile.Set(0, y, Pixel.Clear);
                tile.Set(31, y, Pixel.Clear);
            }

            sheet.Paste(tile, i % IsoTileColumns * 32, i / IsoTileColumns * 32);
        }

        return sheet;
    }

    // The diamond a tile stands on, at the bottom of its 32 by 32 picture.
    private static (float X, float Y)[] Diamond(float cx, float cy, float halfWidth, float halfHeight) =>
        [(cx, cy - halfHeight), (cx + halfWidth, cy), (cx, cy + halfHeight), (cx - halfWidth, cy)];

    // Flat ground: a diamond of `color`, flecked with `light`, a shade darker along its two front edges.
    private static Canvas Ground(Pixel color, Pixel light, int seed)
    {
        var c = new Canvas(32, 32, seed);
        Polygon(c, Diamond(16, 24, 16, 8), color);
        var random = new Random(seed);
        for (var i = 0; i < 26; i++)
        {
            var (x, y) = (random.Next(4, 28), random.Next(18, 31));
            if (c.Get(x, y).A > 0)
                c.Set(x, y, random.Next(3) == 0 ? color.Times(0.8f) : light);
        }

        c.Noise(0.05f);
        FrontEdges(c, color.Times(0.75f));
        return c;
    }

    // Little red, yellow and white flowers on the grass.
    private static Canvas Flowers(Canvas c)
    {
        Pixel[] petals = [Rgb(0xE5, 0x53, 0x4B), Rgb(0xF2, 0xD0, 0x4A), Rgb(0xF4, 0xF1, 0xEA)];
        (int X, int Y)[] at = [(9, 22), (16, 19), (22, 24), (13, 27), (19, 28)];
        for (var i = 0; i < at.Length; i++)
        {
            var (x, y) = at[i];
            var petal = petals[i % petals.Length];
            c.Set(x - 1, y, petal);
            c.Set(x + 1, y, petal);
            c.Set(x, y - 1, petal);
            c.Set(x, y + 1, petal);
            c.Set(x, y, Rgb(0xF2, 0xB4, 0x3A));
        }

        return c;
    }

    // Pale flagstones with dark seams.
    private static Canvas Paving()
    {
        var stone = Rgb(0xC8, 0xBC, 0xA4);
        var c = Ground(stone, stone.Times(1.08f), seed: 48);
        var seam = stone.Times(0.7f);
        for (var i = 0; i <= 8; i++)
        {
            // Seams running the two ways the diamond's edges do, through its middle.
            c.Blend(8 + i * 2, 20 + i, seam, 0.8f);
            c.Blend(24 - i * 2, 20 + i, seam, 0.8f);
        }

        return c;
    }

    // A block as tall as its diamond is wide: a lit top, a left face in the light and a right one in shade. `top` lowers
    // it (8 is a low wall, half as tall).
    private static Canvas Block(Pixel topColor, Pixel sideColor, int top, int seed, bool courses)
    {
        var c = new Canvas(32, 32, seed);
        var (lid, rim) = (top + 8, top + 16);
        Polygon(c, [(0, lid), (16, rim), (16, 32), (0, 24)], sideColor.Times(0.85f));
        Polygon(c, [(16, rim), (32, lid), (32, 24), (16, 32)], sideColor.Times(0.6f));
        if (courses)
        {
            // Mortar between courses of stone, running along each face.
            for (var row = lid + 6; row < 32; row += 6)
            {
                for (var x = 0; x < 32; x++)
                {
                    var y = x < 16 ? row + x / 2 : row + (31 - x) / 2;
                    if (y < 32 && c.Get(x, y).A > 0)
                        c.Blend(x, y, Rgb(0x30, 0x30, 0x36), 0.45f);
                }
            }
        }

        Polygon(c, Diamond(16, lid, 16, 8), topColor.Times(1.1f));
        c.Noise(courses ? 0.06f : 0.14f);
        return c;
    }

    // Darkens the two lower edges of a flat diamond, so neighboring tiles read as separate.
    private static void FrontEdges(Canvas c, Pixel edge)
    {
        for (var x = 0; x < 32; x++)
        {
            for (var y = 31; y >= 16; y--)
            {
                if (c.Get(x, y).A == 0)
                    continue;
                c.Set(x, y, edge);
                break;
            }
        }
    }

    // Someone standing on the ground, 16 by 24, feet at the bottom: a shirt, trousers, a face and hair or a hat.
    private static Canvas IsoPerson(Pixel shirt, Pixel trousers, Pixel? hat)
    {
        var c = new Canvas(16, 24, seed: 49);
        c.Ellipse(8, 22, 5, 1, new Pixel(0, 0, 0, 90)); // shadow on the ground
        c.Rect(5, 16, 2, 6, trousers);
        c.Rect(9, 16, 2, 6, trousers);
        c.Rect(4, 9, 8, 8, shirt);
        c.Rect(3, 10, 1, 5, shirt.Times(0.8f));
        c.Rect(12, 10, 1, 5, shirt.Times(0.8f));
        var skin = Rgb(0xE8, 0xB8, 0x8E);
        c.Rect(5, 3, 6, 6, skin);
        c.Rect(9, 5, 1, 1, Rgb(0x22, 0x1C, 0x1A));
        c.Rect(6, 5, 1, 1, Rgb(0x22, 0x1C, 0x1A));
        if (hat is { } straw)
        {
            c.Rect(2, 3, 12, 1, straw);
            c.Rect(5, 0, 6, 3, straw.Times(0.9f));
        }
        else
        {
            c.Rect(5, 2, 6, 2, Rgb(0x6B, 0x4A, 0x2E));
            c.Rect(5, 4, 1, 2, Rgb(0x6B, 0x4A, 0x2E));
        }

        c.Shade(0.1f);
        c.Outline(Rgb(0x1E, 0x22, 0x2B));
        return c;
    }

    // A green blob with a shine and two eyes, 16 by 12.
    private static Canvas IsoSlimeArt()
    {
        var c = new Canvas(16, 12, seed: 50);
        var green = Rgb(0x57, 0xC0, 0x5A);
        c.Ellipse(8, 7, 6, 4, green);
        c.Rect(3, 8, 10, 3, green);
        c.Rect(5, 4, 2, 1, green.Times(1.4f));
        c.Rect(6, 7, 1, 2, Rgb(0x1E, 0x22, 0x2B));
        c.Rect(10, 7, 1, 2, Rgb(0x1E, 0x22, 0x2B));
        c.Shade(0.15f);
        c.Outline(Rgb(0x1E, 0x40, 0x22));
        return c;
    }

    // A wooden crate seen at the slant: a cube of planks, 32 by 32, its base the bottom half.
    private static Canvas IsoCrateArt()
    {
        var c = new Canvas(32, 32, seed: 51);
        var wood = Rgb(0xB0, 0x7A, 0x44);
        Polygon(c, [(0, 8), (16, 16), (16, 32), (0, 24)], wood.Times(0.85f));
        Polygon(c, [(16, 16), (32, 8), (32, 24), (16, 32)], wood.Times(0.62f));
        Polygon(c, Diamond(16, 8, 16, 8), wood.Times(1.1f));
        var dark = Rgb(0x4A, 0x30, 0x1C);
        // The frame round each face, and a brace across it.
        for (var x = 0; x < 32; x++)
        {
            var side = x < 16 ? x / 2 : (31 - x) / 2;
            c.Set(x, 8 + side, dark);
            c.Set(x, 23 + side, dark);
            c.Set(x, 8 - side, dark);
            c.Set(x, 16 + (x < 16 ? 8 - x / 2 : (x - 16) / 2), dark);
        }

        for (var y = 8; y < 32; y++)
        {
            if (y < 24)
            {
                c.Set(0, y, dark);
                c.Set(31, y, dark);
            }

            if (y >= 16)
                c.Set(16, y, dark);
        }

        c.Noise(0.06f);
        return c;
    }

    // A stone pressure plate, a flat diamond 32 by 16 with a raised rim.
    private static Canvas IsoPlateArt()
    {
        var c = new Canvas(32, 16, seed: 52);
        Polygon(c, Diamond(16, 8, 16, 8), Rgb(0x6E, 0x72, 0x7E));
        Polygon(c, Diamond(16, 8, 11, 5.5f), Rgb(0xC2, 0x4E, 0x4E));
        c.Noise(0.05f);
        return c;
    }

    // The gardener, framed for the dialogue box: a weathered face under a straw hat.
    private static Canvas GardenerPortrait()
    {
        var c = new Canvas(24, 24, seed: 53);
        var frame = Rgb(0x2A, 0x2F, 0x3A);
        c.Fill(frame);
        c.Rect(2, 2, 20, 20, Rgb(0x4E, 0x7A, 0x52));
        c.Rect(4, 18, 16, 4, Rgb(0x57, 0xAB, 0x5A)); // shoulders
        var skin = Rgb(0xD8, 0xA0, 0x78);
        c.Ellipse(12, 13, 5, 5, skin);
        c.Rect(9, 13, 2, 2, Rgb(0x22, 0x1C, 0x1A)); // eyes
        c.Rect(14, 13, 2, 2, Rgb(0x22, 0x1C, 0x1A));
        c.Rect(10, 17, 5, 1, Rgb(0x8A, 0x4A, 0x40)); // smile
        var straw = Rgb(0xE3, 0xC5, 0x6B);
        c.Rect(3, 8, 18, 2, straw); // brim
        c.Rect(7, 4, 10, 4, straw.Times(0.9f)); // crown
        c.Rect(7, 7, 10, 1, Rgb(0xC2, 0x4E, 0x4E)); // band
        c.Shade(0.08f);
        c.Bevel(0, 0, 24, 24, frame.Times(1.6f), frame.Times(0.6f));
        return c;
    }
}
