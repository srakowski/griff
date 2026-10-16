using static Griff.Templates.TemplateArt;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// The pictures Create Stage puts in a project, painted in code as pixel art: two backgrounds (a park by day, a cosy room)
/// and two characters, Ava and Ben, each with three expressions. Saved as PNGs under Stage/, so they open (and can be
/// repainted) in the Sprite Painter.
/// </summary>
internal static class StageArt
{
    public const string Park = "Stage/park.png";
    public const string Room = "Stage/room.png";

    public static string Face(string who, string expression) => $"Stage/{who.ToLowerInvariant()}-{expression.ToLowerInvariant()}.png";

    /// <summary>Every picture, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files()
    {
        var files = new Dictionary<string, byte[]>
        {
            [Park] = ParkArt().Png(),
            [Room] = RoomArt().Png(),
        };
        foreach (var expression in AvaExpressions)
            files[Face("Ava", expression)] = Person(ava: true, expression).Png();
        foreach (var expression in BenExpressions)
            files[Face("Ben", expression)] = Person(ava: false, expression).Png();
        return files;
    }

    public static readonly string[] AvaExpressions = ["Neutral", "Happy", "Sad"];
    public static readonly string[] BenExpressions = ["Neutral", "Happy", "Surprised"];

    // A park on a sunny day: sky, far hills, a tree, a path and a bench.
    private static Canvas ParkArt()
    {
        var c = new Canvas(160, 90, seed: 71);
        for (var y = 0; y < 90; y++)
        {
            var t = y / 60f;
            c.Rect(0, y, 160, 1, new Pixel(Pixel.Clamp(0x7E + 0x50 * t), Pixel.Clamp(0xB8 + 0x30 * t), Pixel.Clamp(0xF0 + 0x0A * t), 255));
        }

        foreach (var (x, y, r) in new[] { (30, 14, 7), (40, 12, 9), (52, 15, 6), (112, 20, 6), (121, 18, 8) })
            c.Ellipse(x, y, r + 3, r, Rgb(0xF6, 0xFA, 0xFF));
        c.Ellipse(36, 70, 60, 22, Rgb(0x7F, 0xB0, 0x6A));
        c.Ellipse(130, 72, 55, 20, Rgb(0x6C, 0xA0, 0x5C));
        c.Rect(0, 62, 160, 28, Rgb(0x6A, 0xB0, 0x52));
        for (var y = 62; y < 90; y++)
        {
            var half = 6 + (y - 62) * 1.4f;
            c.Rect((int)(80 - half), y, (int)(half * 2), 1, Rgb(0xD9, 0xC2, 0x8C));
        }

        c.Rect(128, 30, 6, 36, Rgb(0x6B, 0x47, 0x2C));
        c.Ellipse(131, 26, 18, 15, Rgb(0x3E, 0x8A, 0x3E));
        c.Ellipse(124, 30, 10, 9, Rgb(0x4A, 0x9A, 0x47));
        c.Ellipse(140, 22, 9, 8, Rgb(0x52, 0xA3, 0x4E));
        c.Rect(18, 66, 26, 3, Rgb(0x8A, 0x5A, 0x34));
        c.Rect(18, 60, 26, 2, Rgb(0x8A, 0x5A, 0x34));
        c.Rect(20, 69, 2, 5, Rgb(0x4A, 0x4A, 0x52));
        c.Rect(40, 69, 2, 5, Rgb(0x4A, 0x4A, 0x52));
        c.Noise(0.03f);
        return c;
    }

    // A cosy room: a warm wall, a window onto the evening, a shelf and a wooden floor.
    private static Canvas RoomArt()
    {
        var c = new Canvas(160, 90, seed: 72);
        c.Fill(Rgb(0xE8, 0xD3, 0xB0));
        for (var x = 0; x < 160; x += 12)
            c.Rect(x, 0, 6, 64, Rgb(0xE0, 0xC8, 0xA2));
        c.Rect(0, 64, 160, 26, Rgb(0x9C, 0x6B, 0x43));
        for (var y = 66; y < 90; y += 5)
            c.Rect(0, y, 160, 1, Rgb(0x83, 0x58, 0x36));
        c.Rect(0, 62, 160, 3, Rgb(0x7A, 0x52, 0x33));
        c.Rect(98, 12, 40, 32, Rgb(0xF4, 0xEC, 0xDC));
        for (var y = 0; y < 28; y++)
        {
            var t = y / 28f;
            c.Rect(100, 14 + y, 36, 1, new Pixel(Pixel.Clamp(0xF2 - 0x90 * t), Pixel.Clamp(0xA6 - 0x60 * t), Pixel.Clamp(0x5A + 0x20 * t), 255));
        }

        c.Rect(117, 14, 2, 28, Rgb(0xF4, 0xEC, 0xDC));
        c.Rect(100, 27, 36, 2, Rgb(0xF4, 0xEC, 0xDC));
        c.Rect(18, 24, 44, 3, Rgb(0x7A, 0x52, 0x33));
        foreach (var (x, h, color) in new[] { (20, 10, Rgb(0xC2, 0x4E, 0x4E)), (25, 12, Rgb(0x3E, 0x6E, 0xB0)), (30, 9, Rgb(0xE3, 0xB3, 0x41)), (34, 11, Rgb(0x57, 0xAB, 0x5A)) })
            c.Rect(x, 24 - h, 4, h, color);
        c.Ellipse(52, 18, 5, 6, Rgb(0x57, 0xAB, 0x5A));
        c.Rect(49, 20, 7, 4, Rgb(0xB0, 0x6A, 0x3A));
        c.Noise(0.03f);
        return c;
    }

    /// <summary>A new character's picture, to repaint: fair hair and a green top, looking as the expression says.</summary>
    public static Canvas Generic(string expression) =>
        Person(true, expression, Rgb(0xE8, 0xBE, 0x9A), Rgb(0xD9, 0xB2, 0x5A), Rgb(0x4E, 0x9A, 0x62), seed: 83);

    // A character from the knees up, facing us: Ava with long brown hair and a red top, Ben with short dark hair and a blue
    // jacket, looking as the expression says.
    private static Canvas Person(bool ava, string expression) => ava
        ? Person(true, expression, Rgb(0xF0, 0xC2, 0x9A), Rgb(0x7A, 0x45, 0x22), Rgb(0xD0, 0x45, 0x4C), seed: 81)
        : Person(false, expression, Rgb(0xC9, 0x8E, 0x62), Rgb(0x26, 0x22, 0x2A), Rgb(0x3B, 0x63, 0xA8), seed: 82);

    private static Canvas Person(bool ava, string expression, Pixel skin, Pixel hair, Pixel top, int seed)
    {
        var c = new Canvas(40, 72, seed);
        var ink = Rgb(0x2A, 0x20, 0x24);

        if (ava)
            c.Rect(9, 10, 22, 30, hair); // long hair behind
        c.Rect(8, 36, 24, 36, top); // body
        c.Rect(4, 40, 5, 26, top.Times(0.9f)); // arms
        c.Rect(31, 40, 5, 26, top.Times(0.9f));
        c.Rect(4, 64, 5, 4, skin);
        c.Rect(31, 64, 5, 4, skin);
        if (!ava)
        {
            c.Rect(18, 36, 4, 20, Rgb(0xE8, 0xE8, 0xF0)); // shirt under the jacket
            c.Line(17, 36, 15, 46, top.Times(0.7f));
            c.Line(22, 36, 24, 46, top.Times(0.7f));
        }

        c.Rect(17, 31, 6, 6, skin.Times(0.92f)); // neck
        c.Ellipse(20, 20, 10, 12, skin); // face
        c.Ellipse(20, 11, 11, 6, hair); // hair on top
        if (ava)
        {
            c.Rect(9, 12, 3, 16, hair);
            c.Rect(28, 12, 3, 16, hair);
        }
        else
        {
            c.Rect(10, 12, 2, 6, hair);
            c.Rect(28, 12, 2, 6, hair);
        }

        // Eyes and brows.
        var (eyeY, browY) = (20, 16);
        switch (expression)
        {
            case "Happy":
                c.Line(14, eyeY, 15, eyeY - 1, ink);
                c.Line(16, eyeY - 1, 17, eyeY, ink);
                c.Line(22, eyeY, 23, eyeY - 1, ink);
                c.Line(24, eyeY - 1, 25, eyeY, ink);
                break;
            case "Surprised":
                c.Rect(14, eyeY - 2, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
                c.Rect(23, eyeY - 2, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
                c.Set(15, eyeY - 1, ink);
                c.Set(24, eyeY - 1, ink);
                c.Rect(13, browY - 2, 5, 1, hair);
                c.Rect(22, browY - 2, 5, 1, hair);
                break;
            case "Sad":
                c.Rect(15, eyeY, 2, 2, ink);
                c.Rect(23, eyeY, 2, 2, ink);
                c.Line(13, browY + 1, 16, browY, hair);
                c.Line(23, browY, 26, browY + 1, hair);
                break;
            default:
                c.Rect(15, eyeY - 1, 2, 3, ink);
                c.Rect(23, eyeY - 1, 2, 3, ink);
                c.Rect(13, browY, 5, 1, hair);
                c.Rect(22, browY, 5, 1, hair);
                break;
        }

        // Mouth.
        var mouth = Rgb(0xA0, 0x48, 0x48);
        switch (expression)
        {
            case "Happy":
                c.Rect(17, 26, 6, 1, mouth);
                c.Set(16, 25, mouth);
                c.Set(23, 25, mouth);
                c.Rect(18, 27, 4, 1, mouth.Times(0.8f));
                break;
            case "Surprised":
                c.Ellipse(20, 27, 1, 2, mouth.Times(0.7f));
                break;
            case "Sad":
                c.Rect(18, 26, 4, 1, mouth);
                c.Set(17, 27, mouth);
                c.Set(22, 27, mouth);
                break;
            default:
                c.Rect(18, 26, 4, 1, mouth);
                break;
        }

        if (expression == "Happy")
        {
            c.Blend(13, 23, Rgb(0xF0, 0x80, 0x80), 0.5f);
            c.Blend(26, 23, Rgb(0xF0, 0x80, 0x80), 0.5f);
        }

        c.Shade(0.06f);
        c.Outline(Rgb(0x1E, 0x18, 0x1C));
        return c;
    }
}
