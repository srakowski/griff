using static Griff.Templates.TemplateArt;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// The Visual Novel template's pictures, painted in code as pixel art: the café by night and its roof under the stars (the
/// dawn is plain colors), June who runs the café and Felix who watches the stars, four faces each, and Comet, the star
/// that fell in, whose faces twinkle (two frames each on one sheet). Saved as PNGs under Story/, so they open (and can be
/// repainted) in the Sprite Painter.
/// </summary>
internal static class VisualNovelArt
{
    public const string Cafe = "Story/cafe.png";
    public const string Roof = "Story/roof.png";
    public const string CometSheet = "Story/comet.png";

    /// <summary>June's and Felix's faces, each a picture.</summary>
    public static readonly string[] JuneFaces = ["Neutral", "Happy", "Surprised", "Sad"];

    public static readonly string[] FelixFaces = ["Neutral", "Happy", "Flustered", "Sad"];

    /// <summary>Comet's faces, each a clip of two frames on its sheet, in order.</summary>
    public static readonly string[] CometFaces = ["Neutral", "Happy", "Sleepy", "Sad", "Surprised"];

    public static string Face(string who, string expression) => $"Story/{who.ToLowerInvariant()}-{expression.ToLowerInvariant()}.png";

    /// <summary>Every picture, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files()
    {
        var files = new Dictionary<string, byte[]>
        {
            [Cafe] = CafeArt().Png(),
            [Roof] = RoofArt().Png(),
            [CometSheet] = CometArt().Png(),
        };
        foreach (var face in JuneFaces)
            files[Face("June", face)] = June(face).Png();
        foreach (var face in FelixFaces)
            files[Face("Felix", face)] = Felix(face).Png();
        return files;
    }

    // ----- Backgrounds

    // The café after closing: a warm wall, a rainy window onto the night, a chalkboard menu, lamps, shelves of loaves and
    // jars, the counter with a cake under a dome, and the flour sacks in the corner.
    private static Canvas CafeArt()
    {
        var c = new Canvas(160, 90, seed: 91);
        c.Fill(Rgb(0xF1, 0xD9, 0xB5));
        for (var x = 0; x < 160; x += 10)
            c.Rect(x, 0, 5, 56, Rgb(0xEC, 0xD0, 0xA8));
        c.Rect(0, 52, 160, 4, Rgb(0x8E, 0x5A, 0x38));

        // The window: rain on a night sky, a star or two.
        c.Rect(10, 9, 42, 36, Rgb(0x6E, 0x47, 0x2C));
        for (var y = 0; y < 32; y++)
        {
            var t = y / 32f;
            c.Rect(12, 11 + y, 38, 1, new Pixel(Pixel.Clamp(0x1C + 0x18 * t), Pixel.Clamp(0x24 + 0x22 * t), Pixel.Clamp(0x4E + 0x26 * t), 255));
        }

        foreach (var (x, y) in new[] { (17, 15), (30, 13), (44, 18), (24, 22), (39, 26) })
            c.Set(x, y, Rgb(0xFF, 0xF4, 0xC8));
        var rain = new Random(5);
        for (var i = 0; i < 26; i++)
        {
            var (x, y) = (13 + rain.Next(36), 11 + rain.Next(28));
            c.Set(x, y, Rgb(0x9C, 0xB8, 0xE0));
            c.Set(x, y + 1, Rgb(0x7C, 0x98, 0xC8));
        }

        c.Rect(30, 11, 2, 32, Rgb(0x6E, 0x47, 0x2C));
        c.Rect(12, 26, 38, 2, Rgb(0x6E, 0x47, 0x2C));
        c.Rect(8, 45, 46, 3, Rgb(0x8E, 0x5A, 0x38));

        // The chalkboard: today's menu, scribbled.
        c.Rect(66, 8, 30, 24, Rgb(0x6E, 0x47, 0x2C));
        c.Rect(68, 10, 26, 20, Rgb(0x2E, 0x45, 0x3A));
        foreach (var (y, w) in new[] { (13, 16), (17, 20), (21, 12), (25, 18) })
            c.Rect(71, y, w, 1, Rgb(0xE8, 0xEE, 0xE4));
        c.Rect(89, 21, 2, 2, Rgb(0xF2, 0xA0, 0xA0));

        // Two lamps hanging from the ceiling, glowing.
        foreach (var x in new[] { 60, 104 })
        {
            c.Rect(x, 0, 1, 6, Rgb(0x4A, 0x3A, 0x30));
            c.Glow(x, 10, 13, Rgb(0xF6, 0xD0, 0x8A), Rgb(0xFF, 0xF6, 0xD8));
            c.Rect(x - 5, 5, 11, 4, Rgb(0x3E, 0x6E, 0x5E));
            c.Rect(x - 6, 8, 13, 1, Rgb(0x2E, 0x56, 0x48));
        }

        // Shelves of loaves and jars.
        foreach (var y in new[] { 20, 36 })
            c.Rect(112, y, 40, 2, Rgb(0x8E, 0x5A, 0x38));
        foreach (var x in new[] { 115, 125, 135 })
        {
            c.Ellipse(x + 3, 16, 5, 3, Rgb(0xC6, 0x86, 0x44));
            c.Rect(x + 1, 15, 5, 1, Rgb(0xE2, 0xA8, 0x62));
        }

        foreach (var (x, color) in new[] { (114, Rgb(0xE5, 0x6B, 0x5A)), (122, Rgb(0xF2, 0xC9, 0x4C)), (130, Rgb(0x8C, 0xC0, 0x6A)), (138, Rgb(0xB0, 0x86, 0xD8)), (146, Rgb(0xE5, 0x6B, 0x5A)) })
        {
            c.Rect(x, 27, 6, 9, Rgb(0xD8, 0xE8, 0xF0));
            c.Rect(x + 1, 30, 4, 6, color);
            c.Rect(x, 26, 6, 2, Rgb(0x8E, 0x5A, 0x38));
        }

        // The floor, the counter, and what's on it.
        c.Rect(0, 56, 160, 34, Rgb(0xA8, 0x72, 0x48));
        for (var y = 59; y < 90; y += 5)
            c.Rect(0, y, 160, 1, Rgb(0x92, 0x60, 0x3C));
        c.Rect(18, 62, 112, 28, Rgb(0x7A, 0x4C, 0x30));
        for (var x = 22; x < 128; x += 18)
            c.Rect(x, 67, 14, 20, Rgb(0x6A, 0x40, 0x28));
        c.Rect(16, 58, 116, 5, Rgb(0xE8, 0xDC, 0xC8));
        c.Rect(16, 62, 116, 1, Rgb(0xC0, 0xB0, 0x98));

        c.Ellipse(36, 50, 9, 8, Rgb(0xDC, 0xEC, 0xF4)); // the cake under its dome
        c.Rect(28, 51, 17, 7, Rgb(0xF1, 0xD9, 0xB5));
        c.Ellipse(36, 51, 6, 4, Rgb(0xF4, 0xB8, 0xC8));
        c.Rect(30, 51, 13, 6, Rgb(0xF4, 0xB8, 0xC8));
        c.Rect(30, 54, 13, 1, Rgb(0xFF, 0xF4, 0xF4));
        c.Set(36, 46, Rgb(0xD8, 0x3A, 0x48));
        c.Rect(27, 57, 19, 1, Rgb(0xB8, 0xC4, 0xCC));

        c.Rect(98, 44, 14, 14, Rgb(0xA8, 0xB4, 0xBC)); // the coffee machine
        c.Rect(100, 46, 10, 4, Rgb(0x58, 0x64, 0x6C));
        c.Rect(103, 52, 4, 6, Rgb(0xF4, 0xEC, 0xDC));
        foreach (var x in new[] { 70, 78 })
        {
            c.Rect(x, 53, 5, 5, Rgb(0xF4, 0xEC, 0xDC)); // cups
            c.Set(x + 5, 55, Rgb(0xF4, 0xEC, 0xDC));
        }

        // Flour sacks in the corner, where something is going to land.
        foreach (var (x, y) in new[] { (136, 66), (146, 70), (140, 76) })
        {
            c.Ellipse(x + 6, y + 6, 8, 7, Rgb(0xE8, 0xDE, 0xC8));
            c.Rect(x + 2, y - 1, 8, 2, Rgb(0xC8, 0xB8, 0x98));
            c.Rect(x + 3, y + 5, 6, 1, Rgb(0xB8, 0x8A, 0x5A));
        }

        c.Noise(0.025f);
        return c;
    }

    // The café's roof at night: a sky full of stars, the two bright ones with a gap between them where Comet belongs, a
    // crescent moon, the town asleep below, a string of lights, and the chimney.
    private static Canvas RoofArt()
    {
        var c = new Canvas(160, 90, seed: 92);
        for (var y = 0; y < 90; y++)
        {
            var t = y / 70f;
            c.Rect(0, y, 160, 1, new Pixel(Pixel.Clamp(0x0E + 0x30 * t), Pixel.Clamp(0x14 + 0x1C * t), Pixel.Clamp(0x38 + 0x3A * t), 255));
        }

        var sky = new Random(11);
        for (var i = 0; i < 90; i++)
        {
            var (x, y) = (sky.Next(160), sky.Next(56));
            c.Set(x, y, sky.Next(3) == 0 ? Rgb(0xFF, 0xF0, 0xC0) : Rgb(0xC8, 0xD4, 0xFF));
        }

        // The two bright stars, and the gap between them.
        foreach (var x in new[] { 66, 94 })
        {
            c.Glow(x, 12, 5, Rgb(0x60, 0x70, 0xC0), Rgb(0xFF, 0xF8, 0xD8));
            c.Rect(x - 3, 12, 7, 1, Rgb(0xFF, 0xF8, 0xD8));
            c.Rect(x, 9, 1, 7, Rgb(0xFF, 0xF8, 0xD8));
        }

        // The moon, a crescent: the part of its disc outside another a little up and to the right.
        for (var y = 7; y <= 25; y++)
        {
            for (var x = 15; x <= 33; x++)
            {
                if ((x - 24) * (x - 24) + (y - 16) * (y - 16) <= 72 && (x - 28) * (x - 28) + (y - 13) * (y - 13) > 56)
                    c.Set(x, y, Rgb(0xFF, 0xF2, 0xC8));
            }
        }

        // The town below, asleep but for a few windows.
        var town = new Random(17);
        for (var x = 0; x < 160;)
        {
            var (w, h) = (8 + town.Next(10), 10 + town.Next(14));
            c.Rect(x, 64 - h, w, h + 6, Rgb(0x1A, 0x1C, 0x36));
            c.Rect(x + w / 2 - 1, 64 - h - 3, 2, 3, Rgb(0x1A, 0x1C, 0x36));
            for (var i = 0; i < 2; i++)
            {
                if (town.Next(3) > 0)
                    c.Rect(x + 2 + town.Next(Math.Max(1, w - 4)), 66 - h + 3 + town.Next(Math.Max(1, h - 6)), 2, 2, Rgb(0xF6, 0xC8, 0x6A));
            }

            x += w + town.Next(3);
        }

        // The roof: rows of tiles, the chimney with its smoke, and a string of lights across.
        c.Rect(0, 66, 160, 24, Rgb(0xA4, 0x4A, 0x36));
        for (var y = 66; y < 90; y += 4)
        {
            c.Rect(0, y, 160, 1, Rgb(0x7E, 0x34, 0x28));
            for (var x = (y / 4 % 2) * 4; x < 160; x += 8)
                c.Rect(x, y + 1, 1, 3, Rgb(0x8C, 0x3C, 0x2E));
        }

        c.Rect(0, 64, 160, 2, Rgb(0x6A, 0x2C, 0x22));
        c.Rect(144, 44, 12, 22, Rgb(0x8A, 0x52, 0x40));
        c.Rect(142, 42, 16, 3, Rgb(0x6A, 0x3C, 0x30));
        foreach (var (x, y, r) in new[] { (150, 36, 3), (153, 30, 4), (149, 23, 5) })
            c.Glow(x, y, r + 2, Rgb(0x50, 0x58, 0x80), Rgb(0x9C, 0xA4, 0xC4));
        for (var x = 0; x < 160; x++)
        {
            var y = 46 + (int)MathF.Round(6 * MathF.Sin(x / 160f * MathF.PI));
            c.Set(x, y, Rgb(0x30, 0x2C, 0x30));
            if (x % 12 == 6)
                c.Glow(x, y + 2, 2, Rgb(0xC0, 0x80, 0x40), x % 24 == 6 ? Rgb(0xFF, 0xD8, 0x7A) : Rgb(0xFF, 0x9C, 0x8A));
        }

        c.Noise(0.02f);
        return c;
    }

    // ----- People

    // June, who runs the café: warm brown skin, her curls up in a bun with a pencil through it, a white shirt and a mustard
    // apron.
    private static Canvas June(string face)
    {
        var (skin, hair) = (Rgb(0x9A, 0x62, 0x40), Rgb(0x2B, 0x1D, 0x1A));
        var c = new Canvas(40, 72, seed: 93);
        Body(c, skin, Rgb(0xF4, 0xF0, 0xE8));
        // The apron over the shirt: a bib, its straps and a pocket.
        c.Rect(10, 44, 20, 28, Rgb(0xE0, 0xA8, 0x3A));
        c.Rect(13, 38, 14, 7, Rgb(0xE0, 0xA8, 0x3A));
        c.Rect(13, 36, 2, 3, Rgb(0xC8, 0x92, 0x2C));
        c.Rect(25, 36, 2, 3, Rgb(0xC8, 0x92, 0x2C));
        c.Rect(15, 54, 10, 6, Rgb(0xC8, 0x92, 0x2C));
        c.Rect(10, 46, 20, 1, Rgb(0xC8, 0x92, 0x2C));
        Head(c, skin);
        // Curls round the face, and the bun on top with a pencil through it.
        c.Ellipse(20, 10, 11, 6, hair);
        c.Ellipse(20, 3, 6, 4, hair);
        foreach (var (x, y) in new[] { (10, 14), (30, 14), (11, 19), (29, 19), (14, 7), (26, 7) })
            c.Ellipse(x, y, 2, 2, hair);
        c.Line(24, 1, 30, 4, Rgb(0xF2, 0xC9, 0x4C));
        c.Set(31, 4, Rgb(0xF0, 0x90, 0x90));
        Face(c, face, hair, glasses: false);
        return Finish(c);
    }

    // Felix, who watches the stars: pale, with messy ginger hair, round glasses, a navy coat and a red scarf with cream
    // stripes.
    private static Canvas Felix(string face)
    {
        var (skin, hair) = (Rgb(0xF4, 0xD2, 0xB8), Rgb(0xC8, 0x62, 0x2A));
        var c = new Canvas(40, 72, seed: 94);
        Body(c, skin, Rgb(0x2E, 0x3E, 0x6A));
        c.Line(17, 38, 14, 70, Rgb(0x24, 0x30, 0x54)); // the coat's opening
        c.Rect(22, 50, 2, 2, Rgb(0xE3, 0xB3, 0x41));
        c.Rect(22, 58, 2, 2, Rgb(0xE3, 0xB3, 0x41));
        Head(c, skin);
        // The scarf round his neck, one end hanging down, striped.
        c.Rect(12, 33, 16, 6, Rgb(0xC8, 0x3A, 0x3A));
        c.Rect(22, 38, 5, 16, Rgb(0xC8, 0x3A, 0x3A));
        foreach (var y in new[] { 35, 41, 47, 52 })
            c.Rect(y == 35 ? 12 : 22, y, y == 35 ? 16 : 5, 1, Rgb(0xF4, 0xEC, 0xDC));
        // Messy hair, sticking up.
        c.Ellipse(20, 10, 11, 5, hair);
        foreach (var (x, y) in new[] { (12, 6), (17, 4), (23, 4), (28, 6), (10, 12), (30, 12) })
            c.Ellipse(x, y, 2, 2, hair);
        c.Line(20, 3, 22, 0, hair);
        Face(c, face, hair, glasses: true);
        return Finish(c);
    }

    // From the knees up, facing us: body and arms in their top, hands, and a neck.
    private static void Body(Canvas c, Pixel skin, Pixel top)
    {
        c.Rect(8, 36, 24, 36, top);
        c.Rect(4, 40, 5, 26, top.Times(0.9f));
        c.Rect(31, 40, 5, 26, top.Times(0.9f));
        c.Rect(4, 64, 5, 4, skin);
        c.Rect(31, 64, 5, 4, skin);
        c.Rect(17, 31, 6, 6, skin.Times(0.92f));
    }

    private static void Head(Canvas c, Pixel skin) => c.Ellipse(20, 20, 10, 12, skin);

    // Eyes, brows, mouth and cheeks as the face says: Neutral, Happy, Surprised, Sad or Flustered.
    private static void Face(Canvas c, string face, Pixel hair, bool glasses)
    {
        var ink = Rgb(0x2A, 0x20, 0x24);
        var mouth = Rgb(0xA0, 0x48, 0x48);
        var (eyeY, browY) = (20, 16);
        switch (face)
        {
            case "Happy":
                c.Line(14, eyeY, 15, eyeY - 1, ink);
                c.Line(16, eyeY - 1, 17, eyeY, ink);
                c.Line(22, eyeY, 23, eyeY - 1, ink);
                c.Line(24, eyeY - 1, 25, eyeY, ink);
                c.Rect(17, 26, 6, 1, mouth);
                c.Set(16, 25, mouth);
                c.Set(23, 25, mouth);
                c.Rect(18, 27, 4, 1, mouth.Times(0.8f));
                break;
            case "Surprised":
                c.Rect(14, eyeY - 2, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
                c.Rect(23, eyeY - 2, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
                c.Set(15, eyeY - 1, ink);
                c.Set(24, eyeY - 1, ink);
                c.Rect(13, browY - 2, 5, 1, hair);
                c.Rect(22, browY - 2, 5, 1, hair);
                c.Ellipse(20, 27, 1, 2, mouth.Times(0.7f));
                break;
            case "Sad":
                c.Rect(15, eyeY, 2, 2, ink);
                c.Rect(23, eyeY, 2, 2, ink);
                c.Line(13, browY + 1, 16, browY, hair);
                c.Line(23, browY, 26, browY + 1, hair);
                c.Rect(18, 26, 4, 1, mouth);
                c.Set(17, 27, mouth);
                c.Set(22, 27, mouth);
                break;
            case "Flustered":
                c.Rect(15, eyeY - 1, 2, 2, ink);
                c.Rect(23, eyeY - 1, 2, 2, ink);
                c.Line(13, browY, 16, browY + 1, hair);
                c.Line(23, browY + 1, 26, browY, hair);
                // A wobbly smile.
                c.Set(17, 26, mouth);
                c.Set(18, 25, mouth);
                c.Set(19, 26, mouth);
                c.Set(20, 25, mouth);
                c.Set(21, 26, mouth);
                c.Set(22, 25, mouth);
                break;
            default:
                c.Rect(15, eyeY - 1, 2, 3, ink);
                c.Rect(23, eyeY - 1, 2, 3, ink);
                c.Rect(13, browY, 5, 1, hair);
                c.Rect(22, browY, 5, 1, hair);
                c.Rect(18, 26, 4, 1, mouth);
                break;
        }

        if (face is "Happy" or "Flustered")
        {
            var blush = face == "Flustered" ? 0.8f : 0.5f;
            foreach (var (x, y) in new[] { (13, 23), (14, 23), (26, 23), (25, 23) })
                c.Blend(x, y, Rgb(0xF0, 0x70, 0x70), blush);
        }

        if (glasses)
        {
            var rim = Rgb(0x3A, 0x30, 0x34);
            foreach (var x in new[] { 15, 24 })
            {
                c.Rect(x - 3, eyeY - 3, 7, 1, rim);
                c.Rect(x - 3, eyeY + 3, 7, 1, rim);
                c.Rect(x - 3, eyeY - 3, 1, 7, rim);
                c.Rect(x + 3, eyeY - 3, 1, 7, rim);
            }

            c.Rect(19, eyeY - 1, 2, 1, rim);
            c.Set(13, eyeY - 2, Rgb(0xFF, 0xFF, 0xFF));
            c.Set(22, eyeY - 2, Rgb(0xFF, 0xFF, 0xFF));
        }
    }

    private static Canvas Finish(Canvas c)
    {
        c.Shade(0.06f);
        c.Outline(Rgb(0x1E, 0x18, 0x1C));
        return c;
    }

    // ----- Comet

    /// <summary>How many frames across Comet's sheet is: two for each face.</summary>
    public static int CometFrames => CometFaces.Length * 2;

    /// <summary>Comet's clips, one per face, two frames each: "Neutral 0-1 @3, Happy 2-3 @5, ...".</summary>
    public static string CometClips => string.Join(", ", CometFaces.Select((face, i) => $"{face} {i * 2}-{i * 2 + 1} @{(face == "Sleepy" ? 2 : face == "Happy" ? 5 : 3)}"));

    // A little five-pointed star with a face, glowing, in two frames for each face: the second twinkles brighter, its
    // points a touch longer.
    private static Canvas CometArt()
    {
        const int size = 32;
        var sheet = new Canvas(size * CometFrames, size, seed: 95);
        for (var i = 0; i < CometFrames; i++)
            sheet.Paste(CometFrame(CometFaces[i / 2], twinkle: i % 2 == 1), i * size, 0);
        return sheet;
    }

    private static Canvas CometFrame(string face, bool twinkle)
    {
        var c = new Canvas(32, 32, seed: 96);
        var dim = face is "Sad" or "Sleepy";
        c.Glow(16, 17, twinkle ? 15 : 13, dim ? Rgb(0x80, 0x88, 0xC0) : Rgb(0xF6, 0xC8, 0x50), Rgb(0xFF, 0xF4, 0xC0));
        var body = dim ? Rgb(0xE6, 0xCC, 0x6A) : Rgb(0xFF, 0xD8, 0x4A);
        Polygon(c, Star(16, 17.5f, twinkle ? 14.5f : 13.5f, 6.5f), body.Times(0.82f));
        Polygon(c, Star(16, 17, twinkle ? 13 : 12, 5.6f), body);
        c.Ellipse(13, 13, 2, 1, Rgb(0xFF, 0xF8, 0xE0)); // a shine

        var ink = Rgb(0x5A, 0x34, 0x20);
        var mouth = Rgb(0xC0, 0x4A, 0x3A);
        switch (face)
        {
            case "Happy":
                c.Line(12, 17, 13, 16, ink);
                c.Line(18, 16, 19, 17, ink);
                c.Rect(14, 20, 4, 1, mouth);
                c.Set(13, 19, mouth);
                c.Set(18, 19, mouth);
                break;
            case "Sleepy":
                c.Rect(12, 17, 3, 1, ink);
                c.Rect(18, 17, 3, 1, ink);
                c.Ellipse(16, 21, 1, 1, mouth.Times(0.8f));
                break;
            case "Sad":
                c.Rect(13, 17, 2, 2, ink);
                c.Rect(18, 17, 2, 2, ink);
                c.Line(12, 15, 14, 14, ink);
                c.Line(18, 14, 20, 15, ink);
                c.Rect(15, 21, 3, 1, mouth);
                c.Set(14, 22, mouth);
                c.Set(18, 22, mouth);
                break;
            case "Surprised":
                c.Rect(12, 15, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
                c.Rect(18, 15, 3, 3, Rgb(0xFF, 0xFF, 0xFF));
                c.Set(13, 16, ink);
                c.Set(19, 16, ink);
                c.Ellipse(16, 21, 1, 1, mouth);
                break;
            default:
                c.Rect(13, 16, 2, 2, ink);
                c.Rect(18, 16, 2, 2, ink);
                c.Rect(15, 20, 3, 1, mouth);
                break;
        }

        if (face is "Happy" or "Neutral")
        {
            c.Blend(11, 19, Rgb(0xF8, 0x8A, 0x6A), 0.6f);
            c.Blend(21, 19, Rgb(0xF8, 0x8A, 0x6A), 0.6f);
        }

        if (twinkle)
        {
            foreach (var (x, y) in new[] { (3, 4), (28, 6), (27, 27) })
            {
                c.Set(x, y, Rgb(0xFF, 0xFF, 0xF0));
                c.Set(x - 1, y, Rgb(0xFF, 0xF0, 0xB0));
                c.Set(x + 1, y, Rgb(0xFF, 0xF0, 0xB0));
                c.Set(x, y - 1, Rgb(0xFF, 0xF0, 0xB0));
                c.Set(x, y + 1, Rgb(0xFF, 0xF0, 0xB0));
            }
        }

        return c;
    }
}
