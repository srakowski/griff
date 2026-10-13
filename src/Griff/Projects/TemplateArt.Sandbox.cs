namespace Griff.Projects;

// The Sandbox template's blocks (a sheet of 16-pixel tiles) and the Contraption template's props.
internal static partial class TemplateArt
{
    public const string BlockSheet = "Textures/blocks.png";
    public const string CrateArt = "Textures/crate.png";
    public const string BallArt = "Textures/ball.png";
    public const string BalloonArt = "Textures/balloon.png";
    public const string ThrusterArt = "Textures/thruster.png";
    public const string ButtonArt = "Textures/button.png";
    public const string LampArt2 = "Textures/lamp-bulb.png";
    public const string ChipArt = "Textures/chip.png";

    /// <summary>The tiles across and down <see cref="BlockSheet"/>.</summary>
    public const int BlockColumns = 8, BlockRows = 2;

    // The tiles of the block sheet, in order.
    public const int GrassBlock = 0, DirtBlock = 1, StoneBlock = 2, CoalBlock = 3, IronBlock = 4, LogBlock = 5, LeavesBlock = 6, SandBlock = 7,
        PlanksBlock = 8, TorchBlock = 9, BedrockBlock = 10, WorkbenchBlock = 11, BricksBlock = 12, GlassBlock = 13, FurnaceBlock = 14, GoldBlock = 15;

    internal static Dictionary<string, byte[]> SandboxFiles() => new() { [BlockSheet] = Blocks().Png() };

    internal static Dictionary<string, byte[]> ContraptionFiles() => new()
    {
        [CrateArt] = Crate().Png(),
        [BallArt] = Ball().Png(),
        [BalloonArt] = Balloon().Png(),
        [ThrusterArt] = ThrusterPicture().Png(),
        [ButtonArt] = ButtonPicture().Png(),
        [LampArt2] = Bulb().Png(),
        [ChipArt] = Chip().Png(),
    };

    // ----- Blocks: an 8 by 2 sheet of 16-pixel tiles -----

    private static Canvas Blocks()
    {
        const int size = 16;
        var sheet = new Canvas(size * BlockColumns, size * BlockRows);
        Canvas[] tiles =
        [
            Grass(), Dirt(), StoneBlockArt(), Ore(Rgb(0x26, 0x26, 0x2A), 31), Ore(Rgb(0xD8, 0xA8, 0x86), 32), Log(), Leaves(), Sand(),
            Planks(), Torch(), Bedrock(), Workbench(), Bricks(), Glass(), Furnace(), Ore(Rgb(0xF2, 0xC9, 0x3A), 33),
        ];
        for (var i = 0; i < tiles.Length; i++)
            sheet.Paste(tiles[i], i % BlockColumns * size, i / BlockColumns * size);
        return sheet;
    }

    private static Canvas Dirt(int seed = 20)
    {
        var c = new Canvas(16, 16, seed);
        c.Fill(Rgb(0x8B, 0x5A, 0x2B));
        Speckle(c, seed, Rgb(0x6E, 0x44, 0x1F), 0.18f);
        Speckle(c, seed + 1, Rgb(0xA6, 0x72, 0x3C), 0.1f);
        return c;
    }

    // Dirt with a ragged band of grass along the top.
    private static Canvas Grass()
    {
        var c = Dirt(21);
        var random = new Random(21);
        for (var x = 0; x < 16; x++)
        {
            var depth = 3 + random.Next(3);
            for (var y = 0; y < depth; y++)
                c.Set(x, y, (y == 0 ? Rgb(0x8F, 0xD0, 0x5A) : Rgb(0x5C, 0xA8, 0x3C)).Times(0.9f + random.NextSingle() * 0.2f));
        }

        return c;
    }

    private static Canvas StoneBlockArt()
    {
        var c = new Canvas(16, 16, 22);
        c.Fill(Rgb(0x7D, 0x7D, 0x85));
        Speckle(c, 22, Rgb(0x62, 0x62, 0x6A), 0.22f);
        Speckle(c, 23, Rgb(0x96, 0x96, 0x9E), 0.12f);
        c.Line(2, 5, 6, 4, Rgb(0x5A, 0x5A, 0x62));
        c.Line(9, 11, 13, 12, Rgb(0x5A, 0x5A, 0x62));
        return c;
    }

    // Stone with clumps of colored ore in it.
    private static Canvas Ore(Pixel color, int seed)
    {
        var c = StoneBlockArt();
        var random = new Random(seed);
        for (var clump = 0; clump < 5; clump++)
        {
            var (x, y) = (2 + random.Next(11), 2 + random.Next(11));
            c.Rect(x, y, 2, 2, color);
            c.Set(x, y, color.Times(1.25f));
            c.Set(x + 1, y + 1, color.Times(0.7f));
        }

        return c;
    }

    // A trunk seen from the side: bark down both edges, rings of grain between.
    private static Canvas Log()
    {
        var c = new Canvas(16, 16, 24);
        c.Rect(2, 0, 12, 16, Rgb(0x8A, 0x5E, 0x35));
        c.Rect(2, 0, 2, 16, Rgb(0x5E, 0x3E, 0x22));
        c.Rect(12, 0, 2, 16, Rgb(0x5E, 0x3E, 0x22));
        var random = new Random(24);
        for (var y = 0; y < 16; y++)
        {
            if (random.NextSingle() < 0.4f)
                c.Set(5 + random.Next(6), y, Rgb(0x70, 0x4A, 0x28));
        }

        return c;
    }

    private static Canvas Leaves()
    {
        var c = new Canvas(16, 16, 25);
        var random = new Random(25);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                if (random.NextSingle() < 0.85f)
                    c.Set(x, y, Rgb(0x3E, 0x8C, 0x3A).Times(0.75f + random.NextSingle() * 0.45f));
            }
        }

        return c;
    }

    private static Canvas Sand()
    {
        var c = new Canvas(16, 16, 26);
        c.Fill(Rgb(0xE3, 0xCF, 0x8C));
        Speckle(c, 26, Rgb(0xC9, 0xB2, 0x6E), 0.2f);
        Speckle(c, 27, Rgb(0xF2, 0xE3, 0xAE), 0.1f);
        return c;
    }

    private static Canvas Planks()
    {
        var c = new Canvas(16, 16, 27);
        for (var row = 0; row < 4; row++)
        {
            var color = Rgb(0xB0, 0x7F, 0x4A).Times(0.9f + row % 2 * 0.12f);
            c.Rect(0, row * 4, 16, 4, color);
            c.Rect(0, row * 4 + 3, 16, 1, color.Times(0.6f));
            c.Set(row % 2 == 0 ? 3 : 11, row * 4 + 1, color.Times(0.7f));
        }

        return c;
    }

    // A stick with a flame on top, on a clear background so the wall behind shows.
    private static Canvas Torch()
    {
        var c = new Canvas(16, 16, 28);
        c.Rect(7, 7, 2, 9, Rgb(0x7A, 0x52, 0x32));
        c.Glow(8, 5, 4, Rgb(0xFF, 0x8A, 0x2A), Rgb(0xFF, 0xF4, 0xB0));
        return c;
    }

    private static Canvas Bedrock()
    {
        var c = new Canvas(16, 16, 29);
        c.Fill(Rgb(0x2A, 0x2A, 0x30));
        Speckle(c, 29, Rgb(0x48, 0x48, 0x50), 0.3f);
        Speckle(c, 30, Rgb(0x15, 0x15, 0x18), 0.2f);
        return c;
    }

    // A little table with tools on it, on a clear background.
    private static Canvas Workbench()
    {
        var c = new Canvas(16, 16, 34);
        var wood = Rgb(0xB0, 0x7F, 0x4A);
        c.Rect(1, 5, 14, 3, wood);
        c.Rect(1, 5, 14, 1, wood.Times(1.2f));
        c.Rect(2, 8, 2, 8, wood.Times(0.7f));
        c.Rect(12, 8, 2, 8, wood.Times(0.7f));
        c.Rect(4, 3, 4, 2, Rgb(0x9A, 0x9A, 0xA4));
        c.Rect(10, 2, 1, 3, Rgb(0x6E, 0x44, 0x1F));
        c.Rect(9, 2, 3, 1, Rgb(0x9A, 0x9A, 0xA4));
        return c;
    }

    private static Canvas Bricks()
    {
        var c = new Canvas(16, 16, 35);
        c.Fill(Rgb(0x50, 0x50, 0x58));
        Courses(c, 8, 4, Rgb(0x9A, 0x9A, 0xA6), 0.12f, 1);
        return c;
    }

    private static Canvas Glass()
    {
        var c = new Canvas(16, 16, 36);
        c.Fill(new Pixel(0xBF, 0xE6, 0xF2, 0x60));
        c.Bevel(0, 0, 16, 16, new Pixel(0xE8, 0xF8, 0xFF, 0xE0), new Pixel(0x8A, 0xB8, 0xC8, 0xE0));
        c.Line(3, 9, 8, 4, new Pixel(0xFF, 0xFF, 0xFF, 0xB0));
        return c;
    }

    private static Canvas Furnace()
    {
        var c = new Canvas(16, 16, 37);
        c.Fill(Rgb(0x6A, 0x6A, 0x72));
        Courses(c, 8, 4, Rgb(0x86, 0x86, 0x90), 0.1f, 1);
        c.Rect(4, 8, 8, 6, Rgb(0x1E, 0x1A, 0x1A));
        c.Rect(5, 11, 6, 3, Rgb(0xFF, 0x8A, 0x2A));
        c.Rect(6, 12, 4, 2, Rgb(0xFF, 0xE0, 0x6A));
        return c;
    }

    // Pixels of a color scattered over what's painted.
    private static void Speckle(Canvas c, int seed, Pixel color, float amount)
    {
        var random = new Random(seed * 7 + 1);
        for (var y = 0; y < c.Height; y++)
        {
            for (var x = 0; x < c.Width; x++)
            {
                if (random.NextSingle() < amount)
                    c.Set(x, y, color);
            }
        }
    }

    // ----- Contraption props, 32 pixels -----

    private static Canvas Crate()
    {
        var c = new Canvas(32, 32, 40);
        var wood = Rgb(0xA8, 0x74, 0x3E);
        c.Fill(wood);
        c.Bevel(0, 0, 32, 32, wood.Times(1.25f), wood.Times(0.55f));
        c.Rect(3, 3, 26, 3, wood.Times(0.75f));
        c.Rect(3, 26, 26, 3, wood.Times(0.75f));
        c.Line(5, 25, 26, 6, wood.Times(0.7f));
        c.Noise(0.06f);
        return c;
    }

    private static Canvas Ball()
    {
        var c = new Canvas(32, 32, 41);
        c.Ellipse(15, 15, 15, 15, Rgb(0xE5, 0x53, 0x4B));
        c.Ellipse(15, 15, 15, 4, Rgb(0xF2, 0xF2, 0xF2));
        c.Ellipse(10, 9, 4, 3, Rgb(0xFF, 0x9A, 0x90));
        c.Shade(0.25f);
        return c;
    }

    private static Canvas Balloon()
    {
        var c = new Canvas(32, 32, 42);
        c.Ellipse(15, 14, 13, 14, Rgb(0xE8, 0x4A, 0x8A));
        c.Ellipse(10, 8, 4, 5, Rgb(0xF6, 0x9A, 0xC0));
        c.Rect(14, 28, 3, 3, Rgb(0xB8, 0x30, 0x68));
        c.Shade(0.2f);
        return c;
    }

    // A metal can with a nozzle along the bottom.
    private static Canvas ThrusterPicture()
    {
        var c = new Canvas(32, 32, 43);
        var steel = Rgb(0x8C, 0x93, 0xA0);
        c.Rect(6, 2, 20, 20, steel);
        c.Bevel(6, 2, 20, 20, steel.Times(1.3f), steel.Times(0.6f));
        c.Rect(6, 9, 20, 3, Rgb(0xE3, 0xB3, 0x41));
        c.Rect(10, 22, 12, 4, steel.Times(0.7f));
        c.Rect(8, 26, 16, 5, steel.Times(0.5f));
        return c;
    }

    private static Canvas ButtonPicture()
    {
        var c = new Canvas(32, 32, 44);
        c.Rect(2, 18, 28, 12, Rgb(0x4A, 0x4D, 0x55));
        c.Bevel(2, 18, 28, 12, Rgb(0x6E, 0x72, 0x7D), Rgb(0x2A, 0x2D, 0x33));
        c.Ellipse(15, 15, 10, 7, Rgb(0xF2, 0xF2, 0xF2));
        c.Shade(0.25f);
        return c;
    }

    private static Canvas Bulb()
    {
        var c = new Canvas(32, 32, 45);
        c.Ellipse(15, 12, 11, 11, Rgb(0xF2, 0xF2, 0xF2));
        c.Rect(10, 22, 12, 8, Rgb(0x9A, 0x9A, 0xA4));
        c.Rect(10, 24, 12, 1, Rgb(0x6E, 0x72, 0x7D));
        c.Rect(10, 27, 12, 1, Rgb(0x6E, 0x72, 0x7D));
        c.Ellipse(11, 8, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
        c.Shade(0.15f);
        return c;
    }

    // A green circuit board with gold legs.
    private static Canvas Chip()
    {
        var c = new Canvas(32, 24, 46);
        c.Rect(2, 2, 28, 20, Rgb(0x2F, 0x6B, 0x45));
        c.Bevel(2, 2, 28, 20, Rgb(0x48, 0x8F, 0x62), Rgb(0x1E, 0x45, 0x2C));
        for (var x = 5; x < 28; x += 4)
        {
            c.Rect(x, 0, 2, 2, Rgb(0xE3, 0xB3, 0x41));
            c.Rect(x, 22, 2, 2, Rgb(0xE3, 0xB3, 0x41));
        }

        return c;
    }
}
