using static Griff.Projects.TemplateArt;

namespace Griff.Modules.PhysicsGames;

// The Physics template's pictures: planks, blocks and wedges of wood, glass and stone (a plank 8 times as long as it's
// thick, a square block, a triangle), the bird and the boulder to launch, the green grub to knock out, and a sunny
// meadow: rolling hills and clouds behind, grass and earth underfoot, and grey rock to build on.
internal static class PhysicsArt
{
    public const string WoodPlank = "Textures/wood-plank.png";
    public const string WoodBlock = "Textures/wood-block.png";
    public const string WoodWedge = "Textures/wood-wedge.png";
    public const string GlassPlank = "Textures/glass-plank.png";
    public const string GlassBlock = "Textures/glass-block.png";
    public const string StonePlank = "Textures/stone-plank.png";
    public const string StoneBlock = "Textures/stone-block.png";
    public const string StoneWedge = "Textures/stone-wedge.png";
    public const string Bird = "Textures/bird.png";
    public const string Boulder = "Textures/boulder.png";
    public const string Grub = "Textures/grub.png";
    public const string Hills = "Textures/hills.png";
    public const string Clouds = "Textures/clouds.png";
    public const string Ground = "Textures/ground.png";
    public const string Rock = "Textures/rock.png";

    internal static Dictionary<string, byte[]> Files() => new()
    {
        [WoodPlank] = Plank(Material.Wood).Png(),
        [WoodBlock] = Block(Material.Wood).Png(),
        [WoodWedge] = Wedge(Material.Wood).Png(),
        [GlassPlank] = Plank(Material.Glass).Png(),
        [GlassBlock] = Block(Material.Glass).Png(),
        [StonePlank] = Plank(Material.Stone).Png(),
        [StoneBlock] = Block(Material.Stone).Png(),
        [StoneWedge] = Wedge(Material.Stone).Png(),
        [Bird] = BirdArt().Png(),
        [Boulder] = BoulderArt().Png(),
        [Grub] = GrubArt().Png(),
        [Hills] = HillsArt().Png(),
        [Clouds] = CloudsArt().Png(),
        [Ground] = GroundArt().Png(),
        [Rock] = RockArt().Png(),
    };

    internal enum Material
    {
        Wood,
        Glass,
        Stone,
    }

    private static readonly Pixel WoodColor = Rgb(0xC0, 0x84, 0x4A);
    private static readonly Pixel GlassColor = new(0xA8, 0xDC, 0xF4, 0xB4);
    private static readonly Pixel StoneColor = Rgb(0x8E, 0x92, 0x9C);
    private static readonly Pixel Eye = Rgb(0x1A, 0x1C, 0x22);
    private static readonly Pixel White = Rgb(0xFA, 0xFA, 0xF4);

    // A plank, 64 by 8: wood with its grain along it and a knot or two, glass with a gleam across it, stone in long courses.
    private static Canvas Plank(Material material)
    {
        var c = new Canvas(64, 8, 400 + (int)material);
        Surface(c, material);
        return c;
    }

    // A square block, 16 by 16.
    private static Canvas Block(Material material)
    {
        var c = new Canvas(16, 16, 410 + (int)material);
        Surface(c, material);
        return c;
    }

    // A triangle 32 across and 16 high, its point at the top in the middle: a roof.
    private static Canvas Wedge(Material material)
    {
        var block = new Canvas(32, 16, 420 + (int)material);
        Surface(block, material);
        var c = new Canvas(32, 16, 421 + (int)material);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var inside = MathF.Abs(x + 0.5f - 16) <= (y + 0.5f);
                if (inside)
                    c.Set(x, y, block.Get(x, y));
            }
        }

        var edge = Edge(material);
        for (var y = 0; y < 16; y++)
        {
            c.Set(15 - y, y, edge.Times(1.25f));
            c.Set(16 + y, y, edge);
        }

        return c;
    }

    private static Pixel Edge(Material material) => material switch
    {
        Material.Glass => new Pixel(0xE8, 0xF8, 0xFF, 0xE0),
        Material.Stone => StoneColor.Times(0.6f),
        _ => WoodColor.Times(0.55f),
    };

    // The material across the whole canvas, edged so a piece stands out from the one beside it.
    private static void Surface(Canvas c, Material material)
    {
        var (w, h) = (c.Width, c.Height);
        var random = new Random(c.Seed);
        switch (material)
        {
            case Material.Wood:
                c.Fill(WoodColor);
                for (var y = 1; y < h - 1; y++)
                {
                    // Grain: streaks along it, a little darker or lighter.
                    for (var x = 0; x < w; x++)
                    {
                        var wave = MathF.Sin(x * 0.35f + y * 2.1f + c.Seed) * 0.06f;
                        c.Set(x, y, WoodColor.Times(0.92f + wave + (y % 3 == 0 ? -0.08f : 0.04f)));
                    }
                }

                for (var i = 0; i < Math.Max(1, w / 24); i++)
                    c.Ellipse(random.Next(4, w - 4), random.Next(2, h - 2), 1, Math.Min(1, h / 8), WoodColor.Times(0.6f));
                c.Noise(0.05f);
                c.Bevel(0, 0, w, h, WoodColor.Times(1.25f), WoodColor.Times(0.55f));
                break;

            case Material.Glass:
                c.Fill(GlassColor);
                for (var x = -h; x < w; x += 9)
                {
                    for (var i = 0; i < h; i++)
                    {
                        c.Set(x + i, h - 1 - i, new Pixel(0xF0, 0xFA, 0xFF, 0xD8));
                        c.Set(x + i + 1, h - 1 - i, new Pixel(0xF0, 0xFA, 0xFF, 0x90));
                    }
                }

                c.Bevel(0, 0, w, h, new Pixel(0xF4, 0xFC, 0xFF, 0xF0), new Pixel(0x60, 0x9C, 0xC0, 0xE8));
                break;

            default:
                c.Fill(StoneColor.Times(0.55f));
                Courses(c, Math.Min(16, w), Math.Max(4, Math.Min(8, h / 2)), StoneColor, 0.12f, 1);
                c.Noise(0.07f);
                c.Bevel(0, 0, w, h, StoneColor.Times(1.3f), StoneColor.Times(0.45f));
                break;
        }
    }

    // A round red bird with a pale belly, big eyes under cross brows, a yellow beak, and a tuft on top, facing right.
    private static Canvas BirdArt()
    {
        var c = new Canvas(32, 32, 430);
        var red = Rgb(0xD8, 0x34, 0x30);
        c.Ellipse(16, 17, 13, 13, red);
        c.Ellipse(15, 24, 8, 5, Rgb(0xF0, 0xD8, 0xB8));
        c.Rect(14, 2, 3, 3, red.Times(0.85f));
        c.Rect(17, 1, 2, 3, red.Times(0.85f));
        c.Ellipse(16, 13, 3, 3, White);
        c.Ellipse(23, 13, 3, 3, White);
        c.Rect(17, 12, 2, 3, Eye);
        c.Rect(24, 12, 2, 3, Eye);
        c.Line(12, 8, 18, 10, Eye);
        c.Line(21, 10, 27, 8, Eye);
        Polygon(c, [(24, 17), (31, 19), (24, 22)], Rgb(0xF4, 0xB8, 0x30));
        c.Shade(0.12f);
        c.Outline(red.Times(0.4f));
        return c;
    }

    // A round grey boulder, cracked and heavy-looking.
    private static Canvas BoulderArt()
    {
        var c = new Canvas(32, 32, 431);
        var grey = Rgb(0x7C, 0x80, 0x88);
        c.Ellipse(16, 16, 14, 14, grey);
        var random = new Random(431);
        for (var i = 0; i < 6; i++)
            c.Ellipse(random.Next(8, 24), random.Next(8, 24), random.Next(1, 3), random.Next(1, 3), grey.Times(0.75f));
        c.Line(9, 12, 14, 16, grey.Times(0.5f));
        c.Line(14, 16, 13, 21, grey.Times(0.5f));
        c.Noise(0.08f);
        c.Shade(0.2f);
        c.Outline(grey.Times(0.35f));
        return c;
    }

    // A round green grub with a snout, two beady eyes and a smirk: the target.
    private static Canvas GrubArt()
    {
        var c = new Canvas(32, 32, 432);
        var green = Rgb(0x6C, 0xC2, 0x4A);
        c.Ellipse(16, 17, 13, 12, green);
        c.Ellipse(9, 6, 2, 2, green.Times(0.9f));
        c.Ellipse(23, 6, 2, 2, green.Times(0.9f));
        c.Ellipse(16, 19, 5, 4, green.Times(1.2f));
        c.Rect(14, 18, 1, 2, green.Times(0.5f));
        c.Rect(18, 18, 1, 2, green.Times(0.5f));
        c.Ellipse(10, 13, 2, 2, White);
        c.Ellipse(22, 13, 2, 2, White);
        c.Rect(10, 13, 1, 2, Eye);
        c.Rect(22, 13, 1, 2, Eye);
        c.Line(12, 25, 19, 26, green.Times(0.45f));
        c.Shade(0.15f);
        c.Outline(green.Times(0.35f));
        return c;
    }

    // Rolling green hills along the bottom, 128 by 32, the rest clear: repeated across behind the level.
    private static Canvas HillsArt()
    {
        var c = new Canvas(128, 32, 433);
        var far = Rgb(0x8C, 0xC8, 0x8A);
        var near = Rgb(0x6A, 0xB0, 0x6A);
        for (var x = 0; x < 128; x++)
        {
            var top = 14 + MathF.Sin(x / 128f * MathF.Tau * 2) * 6 + MathF.Sin(x / 128f * MathF.Tau * 5 + 1) * 2;
            for (var y = (int)top; y < 32; y++)
                c.Set(x, y, far.Times(1 - (y - top) * 0.004f));
            var low = 24 + MathF.Sin(x / 128f * MathF.Tau * 3 + 2) * 4;
            for (var y = (int)low; y < 32; y++)
                c.Set(x, y, near);
        }

        var random = new Random(433);
        for (var i = 0; i < 6; i++)
        {
            // A few round trees on the far hills.
            var x = random.Next(4, 124);
            var top = (int)(14 + MathF.Sin(x / 128f * MathF.Tau * 2) * 6 + MathF.Sin(x / 128f * MathF.Tau * 5 + 1) * 2);
            c.Rect(x, top - 3, 1, 3, Rgb(0x6A, 0x50, 0x3A));
            c.Ellipse(x, top - 5, 3, 3, Rgb(0x4E, 0x96, 0x52));
        }

        return c;
    }

    // White puffs of cloud across a clear sky, 128 by 32.
    private static Canvas CloudsArt()
    {
        var c = new Canvas(128, 32, 434);
        var white = new Pixel(0xFF, 0xFF, 0xFF, 0xD8);
        foreach (var (x, y, r) in new[] { (16, 14, 6), (24, 12, 8), (32, 15, 5), (74, 20, 5), (82, 18, 7), (90, 20, 5), (112, 10, 4), (118, 9, 5) })
            c.Ellipse(x, y, r + 2, r, white);
        foreach (var (x, y, w) in new[] { (8, 17, 30), (66, 22, 30), (106, 12, 18) })
            c.Rect(x, y, w, 3, new Pixel(0xE4, 0xEE, 0xF8, 0xD8));
        return c;
    }

    // Grass along the top of brown earth with pebbles in it, 160 by 16 (40 by 4 in the world).
    private static Canvas GroundArt()
    {
        var c = new Canvas(160, 16, 435);
        var earth = Rgb(0x8A, 0x5E, 0x3A);
        c.Fill(earth);
        var random = new Random(435);
        for (var i = 0; i < 90; i++)
            c.Set(random.Next(160), random.Next(4, 16), earth.Times(random.Next(2) == 0 ? 0.75f : 1.2f));
        var grass = Rgb(0x5C, 0xB0, 0x44);
        for (var x = 0; x < 160; x++)
        {
            var depth = 3 + (int)(MathF.Sin(x * 0.9f) * 1.2f + 0.5f);
            c.Rect(x, 0, 1, depth, grass);
            c.Set(x, 0, grass.Times(1.25f));
            c.Set(x, depth, grass.Times(0.7f));
        }

        c.Noise(0.05f);
        return c;
    }

    // Grey rock to build on, 16 by 16: rough, with mossy grass along its top.
    private static Canvas RockArt()
    {
        var c = new Canvas(16, 16, 436);
        var rock = Rgb(0x6E, 0x6A, 0x70);
        c.Fill(rock);
        var random = new Random(436);
        for (var i = 0; i < 10; i++)
            c.Ellipse(random.Next(16), random.Next(3, 16), random.Next(1, 3), 1, rock.Times(random.Next(2) == 0 ? 0.8f : 1.15f));
        c.Rect(0, 0, 16, 2, Rgb(0x5C, 0xB0, 0x44));
        c.Noise(0.06f);
        c.Bevel(0, 0, 16, 16, rock.Times(1.2f), rock.Times(0.6f));
        c.Rect(0, 0, 16, 2, Rgb(0x5C, 0xB0, 0x44));
        return c;
    }
}
