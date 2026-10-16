using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Contraptions;

// The Contraption template's props, 32 pixels: a crate (shared with other templates), a ball, a wheel, a balloon, a
// thruster, a button, a lamp's bulb and a logic chip.
internal static class ContraptionsArt
{
    public const string BallArt = "Textures/ball.png";
    public const string WheelArt = "Textures/wheel.png";
    public const string BalloonArt = "Textures/balloon.png";
    public const string ThrusterArt = "Textures/thruster.png";
    public const string ButtonArt = "Textures/button.png";
    public const string LampArt2 = "Textures/lamp-bulb.png";
    public const string ChipArt = "Textures/chip.png";

    internal static Dictionary<string, byte[]> Files() => new()
    {
        [CrateArt] = Crate().Png(),
        [BallArt] = Ball().Png(),
        [WheelArt] = WheelPicture().Png(),
        [BalloonArt] = Balloon().Png(),
        [ThrusterArt] = ThrusterPicture().Png(),
        [ButtonArt] = ButtonPicture().Png(),
        [LampArt2] = Bulb().Png(),
        [ChipArt] = Chip().Png(),
    };

    // A red ball with a white band round it and a spot on each side, so it's plain to see turning as it rolls. It's lit
    // evenly all round (darker towards its edge), not from one side, so it looks right however far it has turned.
    private static Canvas Ball()
    {
        var c = new Canvas(32, 32, 41);
        c.Ellipse(15, 15, 15, 15, Rgb(0xE5, 0x53, 0x4B));
        c.Ellipse(15, 15, 15, 4, Rgb(0xF2, 0xF2, 0xF2));
        c.Ellipse(15, 6, 2, 2, Rgb(0xF2, 0xF2, 0xF2));
        c.Ellipse(15, 24, 2, 2, Rgb(0x8E, 0x2A, 0x25));
        for (var y = 0; y < c.Height; y++)
        {
            for (var x = 0; x < c.Width; x++)
            {
                var (dx, dy) = ((x - 15) / 15.5f, (y - 15) / 15.5f);
                if (c.Get(x, y) is { A: > 0 } p)
                    c.Set(x, y, p.Times(1.08f - 0.4f * (dx * dx + dy * dy)));
            }
        }

        return c;
    }

    // A tyre round a grey rim with four spokes and a gold hub, so it's plain to see turning.
    private static Canvas WheelPicture()
    {
        var c = new Canvas(32, 32, 47);
        var rim = Rgb(0x9A, 0x9F, 0xAA);
        c.Ellipse(15, 15, 15, 15, Rgb(0x2B, 0x2E, 0x35));
        c.Ellipse(15, 15, 10, 10, rim);
        c.Ellipse(15, 15, 8, 8, Rgb(0x4A, 0x4F, 0x5A));
        c.Rect(14, 7, 3, 17, rim);
        c.Rect(7, 14, 17, 3, rim);
        c.Ellipse(15, 15, 3, 3, Rgb(0xE3, 0xB3, 0x41));
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
