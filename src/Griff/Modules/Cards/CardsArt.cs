using static Griff.Projects.TemplateArt;

namespace Griff.Modules.Cards;

// The Cards template's pictures, all painted here: a card table of green felt in a wooden rim, a card back, the two heroes'
// portraits (the player's mage and the rival lich), and a picture for every card in the Cards table, the creatures standing
// in a dusky glade and each spell on a glow of its own color. Card pictures are 56 by 35, the shape of the space a card
// keeps for its picture.
internal static class CardsArt
{
    public const string Table = "Textures/card-table.png";
    public const string Back = "Textures/card-back.png";
    public const string Mage = "Textures/hero-mage.png";
    public const string Lich = "Textures/hero-lich.png";

    // Each card's picture, by its row in the Cards table.
    public static string Picture(string card) => $"Textures/Cards/{card.ToLowerInvariant().Replace(' ', '-')}.png";

    internal static Dictionary<string, byte[]> Files()
    {
        var files = new Dictionary<string, byte[]>
        {
            [Table] = TableArt().Png(),
            [Back] = BackArt().Png(),
            [Mage] = Portrait(MageArt(), Rgb(0x3A, 0x5C, 0xB0), flip: false).Png(),
            [Lich] = Portrait(LichArt(), Rgb(0x8A, 0x2E, 0x4A), flip: true).Png(),
        };
        foreach (var (name, art) in Pictures())
            files[Picture(name)] = art.Png();
        return files;
    }

    private static IEnumerable<(string Name, Canvas Art)> Pictures()
    {
        // Creatures, in a glade.
        yield return ("Slime", Creature(SlimeArt(), 201));
        yield return ("Goblin", Creature(GoblinArt(), 202));
        yield return ("Rogue", Creature(RogueArt(), 203));
        yield return ("Skeleton", Creature(SkeletonArt(), 204));
        yield return ("Cleric", Creature(ClericArt(), 205));
        yield return ("Knight", Creature(HeroArt(), 206));
        yield return ("Orc", Creature(OrcArt(), 207));
        yield return ("Wyvern", Creature(WyvernArt(), 208));

        // Spells, each on a glow of its own.
        yield return ("Spark", SparkArt());
        yield return ("Fireball", FireballArt());
        yield return ("Chain Lightning", LightningArt());
        yield return ("Healing Potion", PotionArt());
        yield return ("Insight", BookArt());
        yield return ("Mana Coin", CoinArt());
        yield return ("Meteor", MeteorArt());
        yield return ("Quake", QuakeArt());
    }

    private const int W = 56, H = 35;

    // ----- The table and the back -----

    // Green felt, a little lighter in the middle, in a rim of dark wood, 128 by 72, stretched across the view.
    private static Canvas TableArt()
    {
        const int width = 128, height = 72;
        var c = new Canvas(width, height, 200);
        var felt = Rgb(0x1E, 0x5A, 0x3E);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (dx, dy) = ((x - width / 2f) / (width / 2f), (y - height / 2f) / (height / 2f));
                var light = 1.12f - 0.35f * (dx * dx * 0.6f + dy * dy);
                c.Set(x, y, felt.Times(light));
            }
        }

        c.Noise(0.05f);
        var wood = Rgb(0x4A, 0x2E, 0x1E);
        c.Rect(0, 0, width, 3, wood);
        c.Rect(0, height - 3, width, 3, wood);
        c.Rect(0, 0, 3, height, wood);
        c.Rect(width - 3, 0, 3, height, wood);
        c.Bevel(0, 0, width, height, wood.Times(1.5f), wood.Times(0.6f));
        c.Bevel(3, 3, width - 6, height - 6, felt.Times(0.55f), felt.Times(1.25f));
        return c;
    }

    // Deep blue with a gold frame and a lattice of diamonds about a star.
    private static Canvas BackArt()
    {
        const int width = 40, height = 56;
        var c = new Canvas(width, height, 209);
        var blue = Rgb(0x1E, 0x2C, 0x5C);
        var gold = Rgb(0xD8, 0xB0, 0x4C);
        c.Fill(blue);
        for (var y = 4; y < height - 4; y++)
        {
            for (var x = 4; x < width - 4; x++)
            {
                if ((x + y) % 8 == 0 || (x - y + 64) % 8 == 0)
                    c.Set(x, y, blue.Times(1.45f));
            }
        }

        c.Bevel(1, 1, width - 2, height - 2, gold, gold.Times(0.7f));
        c.Bevel(3, 3, width - 6, height - 6, gold.Times(0.8f), gold.Times(0.55f));
        c.Glow(width / 2, height / 2, 11, blue.Times(1.6f), Rgb(0x6A, 0x8C, 0xE8));
        Polygon(c, Star(width / 2f, height / 2f, 9, 4), gold);
        Polygon(c, Star(width / 2f, height / 2f, 5, 2.2f), gold.Times(1.25f));
        c.Noise(0.03f);
        return c;
    }

    // A hero in a frame of its color, 40 by 40.
    private static Canvas Portrait(Canvas figure, Pixel color, bool flip)
    {
        const int size = 40;
        var c = new Canvas(size, size, 210);
        for (var y = 0; y < size; y++)
        {
            var t = y / (size - 1f);
            c.Rect(0, y, size, 1, color.Times(0.35f + 0.45f * (1 - t)));
        }

        c.Glow(size / 2, size / 2 - 2, 16, color.Times(0.7f), color.Times(1.3f));
        Over(c, flip ? Flipped(figure) : figure, 4, 6);
        c.Bevel(0, 0, size, size, color.Times(1.7f), color.Times(0.5f));
        c.Bevel(1, 1, size - 2, size - 2, color.Times(1.2f), color.Times(0.7f));
        return c;
    }

    // The skeleton in a purple robe with a crown and a glow in its eyes.
    private static Canvas LichArt()
    {
        var c = SkeletonArt();
        var robe = Rgb(0x5A, 0x2A, 0x7A);
        for (var y = 16; y < 30; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                if (c.Get(x, y).A > 0)
                    c.Set(x, y, robe.Times(1 - (y - 16) * 0.02f));
            }
        }

        var gold = Rgb(0xE8, 0xC0, 0x40);
        c.Rect(11, 3, 10, 2, gold);
        c.Set(11, 2, gold);
        c.Set(15, 1, gold);
        c.Set(16, 1, gold);
        c.Set(20, 2, gold);
        return c;
    }

    // ----- Creatures -----

    // A creature standing in a dusky glade.
    private static Canvas Creature(Canvas figure, int seed)
    {
        var c = new Canvas(W, H, seed);
        var top = Rgb(0x2C, 0x34, 0x5E);
        var low = Rgb(0x6A, 0x58, 0x7A);
        for (var y = 0; y < H; y++)
        {
            var t = y / (H - 1f);
            c.Rect(0, y, W, 1, new Pixel(Pixel.Clamp(top.R + (low.R - top.R) * t), Pixel.Clamp(top.G + (low.G - top.G) * t),
                Pixel.Clamp(top.B + (low.B - top.B) * t), 255));
        }

        var random = new Random(seed);
        var tree = Rgb(0x1E, 0x2A, 0x3A);
        foreach (var x in new[] { 4, 14, 44, 52 })
            c.Ellipse(x + random.Next(-2, 3), 14 + random.Next(0, 4), 5, 9, tree);
        var grass = Rgb(0x2E, 0x5A, 0x34);
        c.Rect(0, H - 7, W, 7, grass);
        for (var i = 0; i < 30; i++)
            c.Set(random.Next(W), H - 7 - random.Next(0, 2), grass.Times(1.2f));
        Over(c, figure, (W - 32) / 2, H - 33);
        c.Noise(0.03f);
        return c;
    }

    // ----- Spells -----

    // A glow of the spell's color over a dark ground, the picture's middle lightest.
    private static Canvas Spell(Pixel color, int seed)
    {
        var c = new Canvas(W, H, seed);
        var dark = color.Times(0.22f);
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var (dx, dy) = ((x - W / 2f) / (W / 2f), (y - H / 2f) / (H / 2f));
                var t = Math.Clamp(1 - MathF.Sqrt(dx * dx + dy * dy), 0, 1);
                c.Set(x, y, new Pixel(Pixel.Clamp(dark.R + (color.R * 0.6f - dark.R) * t), Pixel.Clamp(dark.G + (color.G * 0.6f - dark.G) * t),
                    Pixel.Clamp(dark.B + (color.B * 0.6f - dark.B) * t), 255));
            }
        }

        var random = new Random(seed);
        for (var i = 0; i < 18; i++)
            c.Blend(random.Next(W), random.Next(H), color.Times(1.4f), 0.5f);
        return c;
    }

    private static Canvas SparkArt()
    {
        var c = Spell(Rgb(0xE8, 0xC8, 0x40), 211);
        c.Glow(W / 2, H / 2, 13, Rgb(0xC8, 0x90, 0x20), Rgb(0xFF, 0xF4, 0xC0));
        Polygon(c, Star(W / 2f, H / 2f, 11, 3.5f), Rgb(0xFF, 0xE8, 0x70));
        Polygon(c, Star(W / 2f, H / 2f, 6, 2), Rgb(0xFF, 0xFF, 0xE0));
        return c;
    }

    private static Canvas FireballArt()
    {
        var c = Spell(Rgb(0xE0, 0x60, 0x20), 212);
        // A tail of flame behind it, from the top right.
        for (var i = 0; i < 6; i++)
            c.Glow(34 + i * 3, 13 - i * 2, 7 - i, Rgb(0xC0, 0x30, 0x10), Rgb(0xF0, 0x80, 0x20));
        c.Glow(25, 21, 11, Rgb(0xD0, 0x40, 0x10), Rgb(0xFF, 0xE0, 0x80));
        c.Ellipse(25, 21, 5, 5, Rgb(0xFF, 0xD0, 0x60));
        c.Ellipse(24, 20, 2, 2, Rgb(0xFF, 0xFA, 0xE0));
        return c;
    }

    private static Canvas LightningArt()
    {
        var c = Spell(Rgb(0x50, 0x90, 0xF0), 213);
        var bolt = Rgb(0xD8, 0xF0, 0xFF);
        var glow = Rgb(0x70, 0xB0, 0xFF);
        foreach (var (x0, shift) in new[] { (12, 0), (28, 3), (44, -2) })
        {
            var points = new[] { (x0, 2), (x0 - 4, 11 + shift), (x0 + 2, 13 + shift), (x0 - 3, 24 + shift), (x0 + 3, 22 + shift), (x0 - 1, 33) };
            for (var i = 0; i < points.Length - 1; i++)
            {
                c.Line(points[i].Item1 - 1, points[i].Item2, points[i + 1].Item1 - 1, points[i + 1].Item2, glow);
                c.Line(points[i].Item1, points[i].Item2, points[i + 1].Item1, points[i + 1].Item2, bolt);
            }
        }

        return c;
    }

    private static Canvas PotionArt()
    {
        var c = Spell(Rgb(0x50, 0xC0, 0x70), 214);
        var glass = Rgb(0xC8, 0xE8, 0xE0);
        c.Glow(W / 2, 21, 14, Rgb(0x30, 0x90, 0x50), Rgb(0x90, 0xF0, 0xA0));
        c.Ellipse(W / 2, 22, 9, 9, glass);
        c.Ellipse(W / 2, 23, 8, 7, Rgb(0xE0, 0x40, 0x60));
        c.Rect(W / 2 - 8, 17, 17, 4, glass);
        c.Ellipse(W / 2, 23, 8, 7, Rgb(0xE0, 0x40, 0x60));
        c.Rect(W / 2 - 3, 6, 7, 8, glass);
        c.Rect(W / 2 - 4, 4, 9, 3, Rgb(0x8A, 0x5A, 0x30));
        c.Ellipse(W / 2 - 4, 20, 2, 2, Rgb(0xFF, 0xC0, 0xD0));
        return c;
    }

    private static Canvas BookArt()
    {
        var c = Spell(Rgb(0x9A, 0x60, 0xE0), 215);
        var page = Rgb(0xF0, 0xE8, 0xD4);
        var cover = Rgb(0x6A, 0x3A, 0x9A);
        c.Glow(W / 2, 12, 12, Rgb(0x7A, 0x50, 0xC0), Rgb(0xE0, 0xD0, 0xFF));
        c.Rect(W / 2 - 18, 15, 36, 15, cover);
        c.Rect(W / 2 - 17, 14, 16, 14, page);
        c.Rect(W / 2 + 1, 14, 16, 14, page);
        for (var y = 17; y < 27; y += 3)
        {
            c.Rect(W / 2 - 15, y, 12, 1, page.Times(0.6f));
            c.Rect(W / 2 + 3, y, 12, 1, page.Times(0.6f));
        }

        c.Rect(W / 2 - 1, 14, 2, 16, cover.Times(0.7f));
        return c;
    }

    private static Canvas CoinArt()
    {
        var c = Spell(Rgb(0x40, 0x80, 0xE0), 216);
        var gold = Rgb(0xE8, 0xC0, 0x40);
        c.Glow(W / 2, H / 2, 15, Rgb(0x30, 0x60, 0xC0), Rgb(0xA0, 0xD0, 0xFF));
        c.Ellipse(W / 2, H / 2, 11, 11, gold.Times(0.75f));
        c.Ellipse(W / 2, H / 2, 9, 9, gold);
        Polygon(c, Ring(4, W / 2f, H / 2f, 6, MathF.PI / 2), Rgb(0x3A, 0x7A, 0xE8));
        Polygon(c, Ring(4, W / 2f - 1, H / 2f - 1, 3, MathF.PI / 2), Rgb(0xB0, 0xD8, 0xFF));
        return c;
    }

    private static Canvas MeteorArt()
    {
        var c = Spell(Rgb(0xC8, 0x48, 0x28), 217);
        for (var i = 0; i < 8; i++)
            c.Glow(40 - i * 2 + 6, 6 + i * 2 - 6, 8, Rgb(0xA0, 0x30, 0x10), Rgb(0xF8, 0x90, 0x30));
        c.Glow(24, 22, 13, Rgb(0xC0, 0x40, 0x10), Rgb(0xFF, 0xC0, 0x60));
        c.Ellipse(24, 22, 8, 7, Rgb(0x5A, 0x40, 0x38));
        c.Ellipse(22, 20, 3, 2, Rgb(0x7A, 0x5A, 0x50));
        c.Set(27, 24, Rgb(0x3A, 0x2A, 0x24));
        c.Set(20, 25, Rgb(0x3A, 0x2A, 0x24));
        return c;
    }

    private static Canvas QuakeArt()
    {
        var c = Spell(Rgb(0xB0, 0x80, 0x40), 218);
        var earth = Rgb(0x6A, 0x4A, 0x2E);
        c.Rect(0, 20, W, 15, earth);
        Polygon(c, [(0, 20), (14, 16), (24, 19), (34, 14), (46, 18), (56, 15), (56, 21), (0, 21)], earth.Times(1.2f));
        var crack = Rgb(0x1E, 0x14, 0x0E);
        c.Line(8, 34, 14, 26, crack);
        c.Line(14, 26, 22, 30, crack);
        c.Line(22, 30, 28, 20, crack);
        c.Line(28, 20, 36, 28, crack);
        c.Line(36, 28, 44, 22, crack);
        c.Line(44, 22, 50, 33, crack);
        var rock = Rgb(0x9A, 0x80, 0x6A);
        c.Ellipse(16, 9, 3, 2, rock);
        c.Ellipse(38, 6, 2, 2, rock);
        c.Ellipse(29, 11, 2, 1, rock);
        c.Noise(0.04f);
        return c;
    }

    // ----- Helpers -----

    // Paints `figure` onto `c` at (left, top), skipping its see-through pixels.
    private static void Over(Canvas c, Canvas figure, int left, int top)
    {
        for (var y = 0; y < figure.Height; y++)
        {
            for (var x = 0; x < figure.Width; x++)
            {
                var p = figure.Get(x, y);
                if (p.A > 128)
                    c.Set(left + x, top + y, p);
            }
        }
    }

    private static Canvas Flipped(Canvas figure)
    {
        var c = new Canvas(figure.Width, figure.Height, figure.Seed);
        for (var y = 0; y < figure.Height; y++)
        {
            for (var x = 0; x < figure.Width; x++)
                c.Set(figure.Width - 1 - x, y, figure.Get(x, y));
        }

        return c;
    }
}
