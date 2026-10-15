using Griff.Projects;
using static Griff.Projects.TemplateArt;

namespace Griff.Modules.Adventures;

/// <summary>
/// The Adventure template's pictures, painted in code as pixel art at 16 pixels to a world unit: three rooms (a cosy
/// kitchen, a back garden with an apple tree, a cluttered shed), the hero walking, Mabel over the fence, Pip the cat,
/// their portraits for the dialogue box, the things in the rooms and the items' icons. Saved as PNGs under Adventure/,
/// so they open (and can be repainted) in the Sprite Painter.
/// </summary>
internal static class AdventureArt
{
    public const string Kitchen = "Adventure/kitchen.png";
    public const string Garden = "Adventure/garden.png";
    public const string Shed = "Adventure/shed.png";
    public const string Hero = "Adventure/hero.png";
    public const string Mabel = "Adventure/mabel.png";
    public const string Pip = "Adventure/pip.png";
    public const string HeroFace = "Adventure/hero-face.png";
    public const string MabelFace = "Adventure/mabel-face.png";
    public const string PipFace = "Adventure/pip-face.png";
    public const string Items = "Adventure/items.png";
    public const string Table = "Adventure/table.png";
    public const string ShedOpen = "Adventure/shed-open.png";
    public const string Rod = "Adventure/rod.png";
    public const string Gnome = "Adventure/gnome.png";

    /// <summary>The hero's frames across its sheet: standing, then four of walking.</summary>
    public const int HeroFrames = 5;

    /// <summary>The items on the items sheet, in order: its frame is the item's place here.</summary>
    public static readonly string[] ItemNames = ["Cup", "Tea", "Shed Key", "Fishing Rod", "Sardines", "Fishy Rod"];

    /// <summary>Every picture, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files() => new()
    {
        [Kitchen] = KitchenArt().Png(),
        [Garden] = GardenArt().Png(),
        [Shed] = ShedArt().Png(),
        [Hero] = HeroArt().Png(),
        [Mabel] = MabelArt().Png(),
        [Pip] = PipArt().Png(),
        [HeroFace] = HeroFaceArt().Png(),
        [MabelFace] = MabelFaceArt().Png(),
        [PipFace] = PipFaceArt().Png(),
        [Items] = ItemsArt().Png(),
        [Table] = TableArt().Png(),
        [ShedOpen] = ShedOpenArt().Png(),
        [Rod] = RodArt().Png(),
        [Gnome] = GnomeArt().Png(),
    };

    private static readonly Pixel Ink = Rgb(0x2A, 0x20, 0x24);
    private static readonly Pixel Skin = Rgb(0xF0, 0xC2, 0x9A);
    private static readonly Pixel Ginger = Rgb(0xE8, 0x8A, 0x34);
    private static readonly Pixel GingerDark = Rgb(0xB8, 0x5E, 0x22);
    private static readonly Pixel Wood = Rgb(0x9C, 0x68, 0x3E);
    private static readonly Pixel DarkWood = Rgb(0x6A, 0x44, 0x28);

    private static Pixel Mix(Pixel a, Pixel b, float t) =>
        new(Pixel.Clamp(a.R + (b.R - a.R) * t), Pixel.Clamp(a.G + (b.G - a.G) * t), Pixel.Clamp(a.B + (b.B - a.B) * t), 255);

    // Top to bottom from one color to another, over rows y0 to y1.
    private static void Gradient(Canvas c, int x, int y0, int w, int y1, Pixel top, Pixel bottom)
    {
        for (var y = y0; y < y1; y++)
            c.Rect(x, y, w, 1, Mix(top, bottom, (y - y0) / (float)Math.Max(1, y1 - y0 - 1)));
    }

    // ----- Rooms (288 by 160: 18 by 10 units) -----

    // A warm kitchen: striped wallpaper over wood panelling, a black stove with the copper kettle on it, a counter under a
    // wall cupboard, a window onto the garden, Pip's photo and bowl, the back door, and a tiled floor with a rug.
    private static Canvas KitchenArt()
    {
        var c = new Canvas(288, 160, seed: 101);

        // The wall: wallpaper stripes above, panelling below a rail.
        c.Rect(0, 0, 288, 96, Rgb(0xF0, 0xDF, 0xBE));
        for (var x = 4; x < 288; x += 16)
            c.Rect(x, 0, 6, 70, Rgb(0xE6, 0xCF, 0xA8));
        for (var x = 8; x < 288; x += 16)
            for (var y = 6; y < 70; y += 16)
                c.Set(x + 4, y, Rgb(0xD9, 0x8E, 0x7A));
        c.Rect(0, 70, 288, 26, Rgb(0xA8, 0x74, 0x48));
        for (var x = 0; x < 288; x += 18)
            c.Rect(x, 72, 1, 24, Rgb(0x86, 0x5A, 0x36));
        c.Rect(0, 68, 288, 3, Rgb(0x7C, 0x52, 0x32));

        // The floor: rows of terracotta and cream tiles, deeper towards us, and a skirting board.
        var top = 96;
        for (var row = 0; top < 160; row++)
        {
            var height = 6 + row;
            var width = 16 + row * 3;
            for (var x = -(row % 2) * width / 2; x < 288; x += width)
            {
                var light = ((x + width * 4) / width + row) % 2 == 0;
                c.Rect(x, top, width - 1, height - 1, light ? Rgb(0xE6, 0xDA, 0xC4) : Rgb(0xC0, 0x6E, 0x4E));
            }

            c.Rect(0, top + height - 1, 288, 1, Rgb(0x8E, 0x6A, 0x5A));
            top += height;
        }

        c.Rect(0, 94, 288, 3, DarkWood);
        c.Ellipse(150, 134, 56, 14, Rgb(0x8E, 0x3E, 0x3A));
        c.Ellipse(150, 134, 50, 11, Rgb(0xB0, 0x55, 0x45));
        c.Ellipse(150, 134, 40, 8, Rgb(0xC9, 0x7A, 0x52));

        // The stove: black iron with a glowing grate, its pipe up into the ceiling, the kettle on top.
        c.Rect(28, 0, 7, 62, Rgb(0x3A, 0x3A, 0x42));
        c.Rect(27, 0, 1, 62, Rgb(0x55, 0x55, 0x60));
        c.Rect(13, 61, 48, 4, Rgb(0x4A, 0x4A, 0x54));
        c.Rect(15, 65, 44, 33, Rgb(0x2E, 0x2E, 0x36));
        c.Bevel(15, 65, 44, 33, Rgb(0x50, 0x50, 0x5A), Rgb(0x1C, 0x1C, 0x22));
        c.Rect(20, 70, 16, 12, Rgb(0x1E, 0x1E, 0x24));
        c.Rect(22, 74, 12, 6, Rgb(0xE8, 0x7A, 0x2C));
        c.Rect(24, 76, 8, 3, Rgb(0xFF, 0xC8, 0x4A));
        c.Rect(40, 70, 15, 22, Rgb(0x26, 0x26, 0x2E));
        c.Rect(46, 79, 4, 2, Rgb(0xB0, 0xB0, 0xBA));
        var copper = Rgb(0xC8, 0x60, 0x3C);
        c.Ellipse(44, 55, 8, 6, copper);
        c.Rect(36, 58, 17, 3, copper.Times(0.8f));
        c.Rect(42, 47, 5, 2, copper.Times(0.7f));
        c.Line(52, 54, 57, 50, copper.Times(0.9f));
        c.Rect(39, 44, 2, 6, Ink);
        c.Rect(39, 44, 11, 2, Ink);
        c.Rect(48, 44, 2, 6, Ink);
        c.Rect(40, 52, 3, 2, copper.Times(1.35f));

        // The counter, with the wall cupboard over it.
        c.Rect(67, 72, 42, 4, Rgb(0xD8, 0xB0, 0x78));
        c.Rect(67, 76, 42, 22, Wood);
        c.Bevel(69, 78, 18, 18, Wood.Times(1.2f), Wood.Times(0.7f));
        c.Bevel(89, 78, 18, 18, Wood.Times(1.2f), Wood.Times(0.7f));
        c.Rect(84, 85, 2, 4, Rgb(0xE0, 0xC0, 0x60));
        c.Rect(91, 85, 2, 4, Rgb(0xE0, 0xC0, 0x60));
        c.Rect(98, 64, 9, 8, Rgb(0xC8, 0x48, 0x40)); // the bread bin
        c.Rect(98, 64, 9, 2, Rgb(0xE0, 0x6A, 0x5A));
        c.Rect(65, 22, 46, 32, Wood);
        c.Rect(65, 20, 46, 3, DarkWood);
        c.Bevel(68, 25, 19, 26, Wood.Times(1.2f), Wood.Times(0.7f));
        c.Bevel(89, 25, 19, 26, Wood.Times(1.2f), Wood.Times(0.7f));
        c.Rect(84, 36, 2, 5, Rgb(0xE0, 0xC0, 0x60));
        c.Rect(91, 36, 2, 5, Rgb(0xE0, 0xC0, 0x60));
        c.Rect(72, 12, 6, 8, Rgb(0xF4, 0xF0, 0xE8)); // jars on top
        c.Rect(80, 14, 6, 6, Rgb(0x6A, 0x9C, 0xC8));
        c.Rect(98, 10, 8, 10, Rgb(0xE8, 0xC8, 0x5A));

        // The window onto the garden, with curtains.
        c.Rect(118, 22, 52, 46, Rgb(0xF4, 0xEE, 0xE0));
        Gradient(c, 122, 26, 44, 52, Rgb(0x86, 0xC0, 0xF0), Rgb(0xC8, 0xE6, 0xF8));
        c.Ellipse(136, 46, 16, 10, Rgb(0x4E, 0x96, 0x48));
        c.Rect(122, 52, 44, 12, Rgb(0x5C, 0xA8, 0x4E));
        c.Rect(122, 50, 44, 3, Rgb(0x3E, 0x7E, 0x3C));
        c.Rect(143, 26, 2, 38, Rgb(0xF4, 0xEE, 0xE0));
        c.Rect(122, 44, 44, 2, Rgb(0xF4, 0xEE, 0xE0));
        c.Rect(114, 66, 60, 3, Rgb(0xE8, 0xE0, 0xD0));
        foreach (var x in new[] { 110, 166 })
        {
            c.Rect(x, 18, 12, 50, Rgb(0xC8, 0x40, 0x3C));
            for (var y = 18; y < 68; y += 4)
                for (var i = x + (y / 4 % 2) * 2; i < x + 12; i += 4)
                    c.Rect(i, y, 2, 2, Rgb(0xF0, 0xE6, 0xDA));
        }

        c.Rect(106, 16, 76, 3, DarkWood);

        // Pip's photo: a ginger face in a gold frame, and his bowl on the floor below.
        c.Rect(182, 28, 26, 22, Rgb(0xC8, 0x9A, 0x3A));
        c.Rect(185, 31, 20, 16, Rgb(0xC8, 0xE2, 0xD0));
        CatFace(c, 195, 41, 1);
        c.Ellipse(202, 106, 8, 3, Rgb(0x3E, 0x6E, 0xB8));
        c.Rect(195, 103, 15, 2, Rgb(0x5A, 0x8C, 0xD8));

        // The back door, with a window in its top.
        c.Rect(226, 30, 40, 68, DarkWood);
        c.Rect(230, 33, 32, 65, Rgb(0x4E, 0x8A, 0x5A));
        Gradient(c, 236, 38, 20, 52, Rgb(0x96, 0xCC, 0xF4), Rgb(0xC8, 0xE6, 0xF8));
        c.Rect(245, 38, 2, 14, Rgb(0x3E, 0x70, 0x48));
        c.Bevel(234, 58, 24, 16, Rgb(0x66, 0xA4, 0x72), Rgb(0x36, 0x62, 0x40));
        c.Bevel(234, 77, 24, 18, Rgb(0x66, 0xA4, 0x72), Rgb(0x36, 0x62, 0x40));
        c.Rect(256, 68, 3, 3, Rgb(0xE8, 0xC4, 0x4A));
        c.Rect(240, 92, 14, 6, Rgb(0x7A, 0x5A, 0x3A)); // the mat
        c.Noise(0.025f);
        return c;
    }

    // A cat's face as in a photo: ginger, with ears, green eyes and a pink nose, `scale` pixels to its own.
    private static void CatFace(Canvas c, int cx, int cy, int scale)
    {
        c.Ellipse(cx, cy, 6 * scale, 5 * scale, Ginger);
        TemplateArt.Polygon(c, [(cx - 6f * scale, cy - 2f * scale), (cx - 5f * scale, cy - 8f * scale), (cx - 1f * scale, cy - 4f * scale)], Ginger);
        TemplateArt.Polygon(c, [(cx + 6f * scale, cy - 2f * scale), (cx + 5f * scale, cy - 8f * scale), (cx + 1f * scale, cy - 4f * scale)], Ginger);
        c.Rect(cx - 1 * scale, cy - 5 * scale, 2 * scale, 3 * scale, GingerDark);
        c.Rect(cx - 3 * scale, cy - 1 * scale, 2 * scale, 2 * scale, Rgb(0x4E, 0xA0, 0x4A));
        c.Rect(cx + 1 * scale, cy - 1 * scale, 2 * scale, 2 * scale, Rgb(0x4E, 0xA0, 0x4A));
        c.Rect(cx, cy + 1 * scale, scale, scale, Rgb(0xE8, 0x80, 0x90));
        c.Rect(cx - 4 * scale, cy + 2 * scale, 8 * scale, scale, Rgb(0xF4, 0xD8, 0xB0));
    }

    // The back garden on a bright morning: the cottage's back door, the apple tree, a picket fence with Mabel's house
    // behind it, a lawn with a stone path, and the old shed with its door shut.
    private static Canvas GardenArt()
    {
        var c = new Canvas(288, 160, seed: 102);
        Gradient(c, 0, 0, 288, 76, Rgb(0x7A, 0xB8, 0xF0), Rgb(0xD0, 0xEA, 0xF8));
        c.Glow(250, 14, 12, Rgb(0xFF, 0xF2, 0xB0), Rgb(0xFF, 0xFA, 0xE0));
        foreach (var (x, y, r) in new[] { (110, 14, 8), (122, 11, 10), (136, 15, 7), (180, 24, 6), (190, 21, 8) })
            c.Ellipse(x, y, r + 3, r, Rgb(0xF6, 0xFA, 0xFF));

        // Mabel's house over the fence: brick, a window with a window box of roses.
        var bricks = new Canvas(66, 50, seed: 7);
        bricks.Fill(Rgb(0xC8, 0xB8, 0xA4));
        TemplateArt.Courses(bricks, 10, 5, Rgb(0xB8, 0x5C, 0x46), 0.1f, 1);
        c.Paste(bricks, 150, 28);
        TemplateArt.Polygon(c, [(144, 30), (183, 10), (222, 30)], Rgb(0x5A, 0x4A, 0x5E));
        c.Rect(196, 40, 16, 16, Rgb(0xF4, 0xEE, 0xE0));
        c.Rect(198, 42, 12, 12, Rgb(0x9C, 0xC8, 0xE8));
        c.Rect(194, 56, 20, 4, Rgb(0x6A, 0x44, 0x28));
        foreach (var x in new[] { 196, 200, 204, 208 })
            c.Rect(x, 54, 3, 3, Rgb(0xE0, 0x46, 0x5A));

        // The hedge and the picket fence across the back of the lawn.
        c.Rect(0, 62, 288, 14, Rgb(0x3E, 0x7E, 0x3C));
        for (var x = 0; x < 288; x += 7)
            c.Ellipse(x, 63, 5, 4, Rgb(0x4A, 0x8E, 0x44));
        for (var x = 60; x < 214; x += 8)
        {
            c.Rect(x, 72, 6, 20, Rgb(0xF2, 0xEE, 0xE4));
            c.Set(x + 2, 71, Rgb(0xF2, 0xEE, 0xE4));
            c.Set(x + 3, 71, Rgb(0xF2, 0xEE, 0xE4));
            c.Rect(x + 5, 73, 1, 19, Rgb(0xC8, 0xC2, 0xB4));
        }

        c.Rect(60, 77, 154, 2, Rgb(0xDC, 0xD6, 0xCA));
        c.Rect(60, 86, 154, 2, Rgb(0xDC, 0xD6, 0xCA));

        // The lawn, mown in stripes, and a stone path from the door to the shed.
        c.Rect(0, 90, 288, 70, Rgb(0x6A, 0xB0, 0x52));
        for (var y = 90; y < 160; y++)
        {
            var band = (y - 90) / (4 + (y - 90) / 10);
            if (band % 2 == 0)
                c.Rect(0, y, 288, 1, Rgb(0x74, 0xBA, 0x5A));
        }

        foreach (var (x, y, rx) in new[] { (44, 104, 9), (66, 112, 10), (92, 118, 11), (122, 121, 11), (154, 121, 11), (186, 118, 11), (214, 112, 10), (238, 106, 9) })
        {
            c.Ellipse(x, y, rx, 4, Rgb(0x9C, 0x96, 0x8A));
            c.Ellipse(x, y - 1, rx - 2, 2, Rgb(0xB8, 0xB2, 0xA4));
        }

        foreach (var (x, y, color) in new[] { (20, 140, Rgb(0xF0, 0xE0, 0x50)), (30, 146, Rgb(0xF4, 0xF4, 0xF4)), (262, 140, Rgb(0xE0, 0x60, 0x8A)), (250, 150, Rgb(0xF0, 0xE0, 0x50)), (140, 150, Rgb(0xF4, 0xF4, 0xF4)) })
        {
            c.Rect(x, y, 2, 2, color);
            c.Set(x, y + 2, Rgb(0x3E, 0x7E, 0x3C));
        }

        // The cottage's back wall and its door, on the left.
        c.Rect(0, 8, 62, 90, Rgb(0xEC, 0xE2, 0xCC));
        c.Rect(0, 2, 70, 7, Rgb(0x7A, 0x3E, 0x34));
        c.Rect(0, 96, 62, 3, Rgb(0xA8, 0x9C, 0x86));
        c.Rect(6, 14, 14, 16, Rgb(0xF4, 0xEE, 0xE0));
        c.Rect(8, 16, 10, 12, Rgb(0x9C, 0xC8, 0xE8));
        c.Rect(18, 36, 36, 62, DarkWood);
        c.Rect(22, 39, 28, 59, Rgb(0x4E, 0x8A, 0x5A));
        c.Bevel(25, 44, 22, 20, Rgb(0x66, 0xA4, 0x72), Rgb(0x36, 0x62, 0x40));
        c.Bevel(25, 68, 22, 24, Rgb(0x66, 0xA4, 0x72), Rgb(0x36, 0x62, 0x40));
        c.Rect(44, 66, 3, 3, Rgb(0xE8, 0xC4, 0x4A));
        c.Rect(56, 80, 8, 18, Rgb(0xB8, 0x6A, 0x40)); // a flowerpot by the door
        c.Ellipse(60, 76, 6, 5, Rgb(0x4E, 0x96, 0x48));
        c.Rect(58, 72, 3, 3, Rgb(0xE0, 0x46, 0x5A));

        // The apple tree: a thick trunk, a round crown with apples, and a branch out to the right where Pip sits.
        c.Rect(92, 40, 12, 58, Rgb(0x7A, 0x52, 0x34));
        c.Rect(92, 40, 3, 58, Rgb(0x92, 0x66, 0x42));
        c.Rect(88, 94, 20, 4, Rgb(0x6A, 0x46, 0x2C));
        c.Line(102, 46, 126, 38, Rgb(0x7A, 0x52, 0x34));
        c.Line(102, 47, 126, 39, Rgb(0x7A, 0x52, 0x34));
        c.Line(110, 44, 142, 36, Rgb(0x7A, 0x52, 0x34));
        foreach (var (x, y, rx, ry, shade) in new[] { (98, 26, 34, 20, 0.85f), (78, 32, 18, 14, 0.95f), (118, 30, 18, 13, 0.95f), (96, 14, 20, 12, 1.05f), (88, 22, 14, 10, 1.1f) })
            c.Ellipse(x, y, rx, ry, Rgb(0x4A, 0x96, 0x44).Times(shade));
        var apples = new Random(5);
        for (var i = 0; i < 16; i++)
        {
            var (x, y) = (70 + apples.Next(56), 10 + apples.Next(36));
            if (c.Get(x, y).G > 0x70)
                c.Rect(x, y, 2, 2, Rgb(0xD8, 0x3A, 0x34));
        }

        c.Rect(68, 104, 4, 3, Rgb(0xD8, 0x3A, 0x34)); // a windfall
        c.Rect(112, 101, 3, 2, Rgb(0xD8, 0x3A, 0x34));

        // The shed: weathered planks, a slanted roof, a cobwebbed window and its door shut with a padlock.
        TemplateArt.Polygon(c, [(210, 26), (286, 16), (286, 26), (210, 34)], Rgb(0x4A, 0x4A, 0x54));
        c.Rect(214, 28, 70, 70, Rgb(0x8C, 0x6E, 0x4A));
        for (var x = 214; x < 284; x += 6)
        {
            c.Rect(x, 28, 1, 70, Rgb(0x6E, 0x54, 0x36));
            c.Rect(x + 2, 34 + x % 20, 1, 4, Rgb(0x7A, 0x5E, 0x40));
        }

        c.Rect(260, 42, 16, 14, Rgb(0x5A, 0x46, 0x30));
        c.Rect(262, 44, 12, 10, Rgb(0x8C, 0xA0, 0xA8));
        c.Line(262, 44, 268, 50, Rgb(0xD8, 0xDC, 0xE0));
        c.Rect(230, 48, 28, 50, Rgb(0x5E, 0x46, 0x2C));
        c.Rect(232, 50, 24, 48, Rgb(0x7A, 0x5A, 0x38));
        for (var x = 236; x < 256; x += 5)
            c.Rect(x, 50, 1, 48, Rgb(0x66, 0x4A, 0x2E));
        c.Line(233, 56, 254, 90, Rgb(0x66, 0x4A, 0x2E));
        c.Rect(250, 70, 6, 7, Rgb(0x9A, 0x9A, 0xA6));
        c.Rect(251, 66, 4, 4, Rgb(0x6A, 0x6A, 0x76));
        c.Rect(252, 67, 2, 3, Rgb(0x7A, 0x5A, 0x38));
        c.Rect(210, 96, 78, 3, Rgb(0x5A, 0x7E, 0x40));
        c.Noise(0.025f);
        return c;
    }

    // The shed inside: plank walls, the doorway back out to the garden, a workbench under hanging tools, a dusty window,
    // the lawnmower, paint tins on a shelf and a spider's web in the corner.
    private static Canvas ShedArt()
    {
        var c = new Canvas(288, 160, seed: 103);
        c.Rect(0, 0, 288, 100, Rgb(0x7A, 0x5C, 0x3C));
        var random = new Random(9);
        for (var x = 0; x < 288; x += 12)
        {
            c.Rect(x, 0, 12, 100, Rgb(0x7A, 0x5C, 0x3C).Times(0.9f + random.NextSingle() * 0.2f));
            c.Rect(x, 0, 1, 100, Rgb(0x52, 0x3C, 0x26));
            c.Rect(x + 5, 20 + random.Next(60), 2, 2, Rgb(0x4A, 0x36, 0x22));
        }

        c.Rect(0, 8, 288, 4, Rgb(0x5A, 0x42, 0x2A));

        // The floor boards, deeper towards us.
        var top = 100;
        for (var row = 0; top < 160; row++)
        {
            var height = 5 + row * 2;
            c.Rect(0, top, 288, height, Rgb(0x8C, 0x6A, 0x46).Times(row % 2 == 0 ? 1 : 0.92f));
            c.Rect(0, top + height - 1, 288, 1, Rgb(0x5E, 0x46, 0x2C));
            for (var x = (row * 37) % 50; x < 288; x += 50)
                c.Rect(x, top, 1, height, Rgb(0x5E, 0x46, 0x2C));
            top += height;
        }

        // The doorway out: the bright garden, and light falling across the boards.
        c.Rect(6, 34, 36, 66, Rgb(0x4A, 0x36, 0x22));
        Gradient(c, 10, 38, 28, 70, Rgb(0x9C, 0xD0, 0xF4), Rgb(0xD0, 0xEA, 0xF8));
        c.Rect(10, 70, 28, 30, Rgb(0x6A, 0xB0, 0x52));
        c.Rect(10, 68, 28, 4, Rgb(0x3E, 0x7E, 0x3C));
        TemplateArt.Polygon(c, [(10, 100), (38, 100), (70, 130), (20, 130)], Rgb(0xB0, 0x8A, 0x5C));

        // The pegboard of tools over the workbench.
        c.Rect(64, 28, 78, 34, Rgb(0xC4, 0xA8, 0x7A));
        for (var x = 68; x < 140; x += 6)
            for (var y = 32; y < 60; y += 6)
                c.Set(x, y, Rgb(0x8E, 0x74, 0x50));
        c.Rect(72, 34, 4, 18, Rgb(0x8A, 0x5A, 0x34)); // a hammer
        c.Rect(68, 32, 12, 5, Rgb(0x6A, 0x6A, 0x76));
        TemplateArt.Polygon(c, [(88, 34), (104, 34), (104, 46), (88, 52)], Rgb(0xA8, 0xAC, 0xB8)); // a saw
        c.Rect(102, 32, 6, 8, Rgb(0x8A, 0x5A, 0x34));
        c.Rect(116, 34, 3, 20, Rgb(0x9A, 0x9A, 0xA6)); // a spanner
        c.Ellipse(117, 33, 3, 3, Rgb(0x9A, 0x9A, 0xA6));
        c.Rect(126, 36, 10, 2, Rgb(0x3A, 0x6E, 0xC0)); // a screwdriver
        c.Rect(126, 38, 4, 14, Rgb(0xA8, 0xAC, 0xB8));

        // The workbench.
        c.Rect(58, 66, 92, 6, Rgb(0xA8, 0x7C, 0x4E));
        c.Rect(58, 72, 92, 2, Rgb(0x6A, 0x4A, 0x2C));
        c.Rect(62, 74, 5, 26, Rgb(0x6A, 0x4A, 0x2C));
        c.Rect(141, 74, 5, 26, Rgb(0x6A, 0x4A, 0x2C));
        c.Rect(66, 86, 76, 3, Rgb(0x6A, 0x4A, 0x2C));
        c.Rect(78, 60, 12, 6, Rgb(0x3E, 0x7E, 0xB0)); // a box of nails
        c.Rect(118, 58, 8, 8, Rgb(0xD8, 0xB8, 0x4A)); // a twine reel

        // The dusty window.
        c.Rect(156, 22, 44, 32, Rgb(0x52, 0x3C, 0x26));
        Gradient(c, 159, 25, 38, 51, Rgb(0xA8, 0xBC, 0xC0), Rgb(0xC8, 0xD4, 0xC8));
        c.Rect(177, 25, 2, 26, Rgb(0x52, 0x3C, 0x26));
        c.Line(160, 26, 170, 34, Rgb(0xE8, 0xEC, 0xEE));
        c.Line(160, 34, 170, 26, Rgb(0xE8, 0xEC, 0xEE));

        // The lawnmower: red, with a long handle.
        c.Rect(170, 82, 34, 12, Rgb(0xC8, 0x3C, 0x34));
        c.Rect(170, 82, 34, 3, Rgb(0xE0, 0x5A, 0x4A));
        c.Ellipse(176, 96, 5, 5, Rgb(0x2A, 0x2A, 0x30));
        c.Ellipse(198, 96, 5, 5, Rgb(0x2A, 0x2A, 0x30));
        c.Line(200, 82, 214, 56, Rgb(0x6A, 0x6A, 0x76));
        c.Rect(210, 54, 10, 2, Rgb(0x2A, 0x2A, 0x30));

        // A shelf of paint tins, and the spider's web in the corner.
        c.Rect(222, 46, 60, 3, Rgb(0x5A, 0x42, 0x2A));
        foreach (var (x, color) in new[] { (226, Rgb(0x3E, 0x7E, 0xC8)), (238, Rgb(0xE8, 0xC8, 0x4A)), (250, Rgb(0xE8, 0xE8, 0xE0)), (262, Rgb(0x5A, 0xA8, 0x5A)) })
        {
            c.Rect(x, 36, 9, 10, Rgb(0xA8, 0xAC, 0xB8));
            c.Rect(x, 39, 9, 5, color);
        }

        var web = Rgb(0xE8, 0xEC, 0xF0);
        foreach (var (x, y) in new[] { (288, 30), (252, 0), (262, 26), (276, 30) })
            c.Line(287, 0, x, y, web);
        for (var r = 8; r < 34; r += 8)
        {
            for (var a = 0; a <= 12; a++)
            {
                var angle = MathF.PI / 2 + a * (MathF.PI / 2) / 12;
                c.Set((int)(287 + MathF.Cos(angle) * r * 1.2f), (int)(MathF.Sin(angle) * r), web);
            }
        }

        c.Ellipse(271, 17, 2, 2, Ink); // the spider
        c.Rect(268, 15, 1, 5, Ink);
        c.Rect(274, 15, 1, 5, Ink);
        c.Noise(0.03f);
        return c;
    }

    // ----- People and the cat -----

    // The hero side on, facing right, in a yellow raincoat, jeans and red boots: standing, then four steps of a walk.
    private static Canvas HeroArt()
    {
        var c = new Canvas(24 * HeroFrames, 40, seed: 104);
        // Each walking frame's legs apart (front, back) and how far the body bobs.
        var steps = new (int Front, int Back, int Bob)[] { (0, 0, 0), (3, -3, 0), (1, -1, 1), (-3, 3, 0), (-1, 1, 1) };
        for (var frame = 0; frame < HeroFrames; frame++)
        {
            var (front, back, bob) = steps[frame];
            HeroFigure(c, frame * 24, bob, front, back);
        }

        return c;
    }

    private static void HeroFigure(Canvas c, int left, int bob, int front, int back)
    {
        var coat = Rgb(0xF2, 0xC1, 0x3C);
        var jeans = Rgb(0x3E, 0x5C, 0x9A);
        var boots = Rgb(0xC8, 0x3C, 0x34);
        var hair = Rgb(0x6B, 0x42, 0x26);
        var x = left;
        var y = bob;

        // Legs and boots (the back one darker).
        c.Rect(x + 10 + back, y + 29, 4, 7, jeans.Times(0.8f));
        c.Rect(x + 10 + back, y + 35 - bob, 5, 4 + bob, boots.Times(0.8f));
        c.Rect(x + 11 + front, y + 29, 4, 7, jeans);
        c.Rect(x + 11 + front, y + 35 - bob, 6, 4 + bob, boots);

        // The coat, its collar and buttons, and an arm swinging with the walk.
        c.Rect(x + 7, y + 16, 11, 15, coat);
        c.Rect(x + 6, y + 26, 13, 5, coat.Times(0.92f));
        c.Rect(x + 7, y + 16, 11, 2, coat.Times(1.12f));
        c.Rect(x + 16, y + 20, 1, 1, Rgb(0x8A, 0x6A, 0x20));
        c.Rect(x + 16, y + 24, 1, 1, Rgb(0x8A, 0x6A, 0x20));
        c.Rect(x + 10 - front / 2, y + 18, 4, 10, coat.Times(0.8f));
        c.Rect(x + 10 - front / 2, y + 28, 4, 2, Skin);
        c.Rect(x + 8, y + 15, 9, 2, Rgb(0xC8, 0x3C, 0x34)); // a red scarf
        c.Rect(x + 8, y + 17, 3, 4, Rgb(0xC8, 0x3C, 0x34));

        // The head: a face to the right, brown hair, an eye, a rosy cheek.
        c.Ellipse(x + 13, y + 9, 5, 6, Skin);
        c.Rect(x + 17, y + 9, 2, 2, Skin);
        c.Rect(x + 8, y + 2, 10, 4, hair);
        c.Rect(x + 7, y + 4, 4, 8, hair);
        c.Rect(x + 15, y + 3, 4, 2, hair);
        c.Rect(x + 15, y + 8, 2, 2, Ink);
        c.Rect(x + 14, y + 11, 2, 1, Rgb(0xE8, 0x90, 0x80));
        c.Rect(x + 16, y + 13, 2, 1, Rgb(0xA0, 0x52, 0x48));
        Outline(c, left, 24);
    }

    // A one-pixel dark edge round what's painted in one frame of a sheet.
    private static void Outline(Canvas c, int left, int width)
    {
        var frame = new Canvas(width, c.Height);
        for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < width; x++)
                frame.Set(x, y, c.Get(left + x, y));
        frame.Outline(Rgb(0x3A, 0x2C, 0x2A));
        c.Paste(frame, left, 0);
    }

    // Mabel from the waist up, as she leans on the fence: grey hair in a bun, round glasses, a lilac cardigan and pearls.
    private static Canvas MabelArt()
    {
        var c = new Canvas(24, 28, seed: 105);
        var hair = Rgb(0xC8, 0xC8, 0xD0);
        var cardigan = Rgb(0x9A, 0x7A, 0xC8);
        c.Rect(4, 18, 16, 10, cardigan);
        c.Rect(2, 22, 4, 6, cardigan.Times(0.85f));
        c.Rect(18, 22, 4, 6, cardigan.Times(0.85f));
        c.Rect(10, 18, 4, 10, Rgb(0xF4, 0xEE, 0xE0));
        foreach (var x in new[] { 8, 10, 12, 14 })
            c.Set(x + 1, 18, Rgb(0xFA, 0xF6, 0xEE));
        c.Ellipse(12, 11, 6, 6, Skin);
        c.Ellipse(12, 3, 4, 3, hair);
        c.Rect(6, 5, 12, 4, hair);
        c.Rect(5, 7, 3, 6, hair);
        c.Rect(16, 7, 3, 6, hair);
        c.Rect(8, 10, 3, 3, Rgb(0x6A, 0x6A, 0x76));
        c.Rect(13, 10, 3, 3, Rgb(0x6A, 0x6A, 0x76));
        c.Rect(9, 11, 1, 1, Ink);
        c.Rect(14, 11, 1, 1, Ink);
        c.Rect(11, 11, 2, 1, Rgb(0x6A, 0x6A, 0x76));
        c.Rect(10, 15, 4, 1, Rgb(0xC0, 0x5A, 0x60));
        c.Rect(7, 13, 2, 1, Rgb(0xE8, 0x9A, 0x90));
        c.Rect(15, 13, 2, 1, Rgb(0xE8, 0x9A, 0x90));
        c.Outline(Rgb(0x3A, 0x2C, 0x2A));
        return c;
    }

    // Pip, a ginger cat, sitting and looking out: stripes, a curled tail, green eyes.
    private static Canvas PipArt()
    {
        var c = new Canvas(20, 16, seed: 106);
        c.Ellipse(9, 11, 6, 4, Ginger);
        c.Rect(15, 10, 3, 2, Ginger);
        c.Rect(17, 6, 2, 5, Ginger);
        c.Rect(17, 6, 2, 1, GingerDark);
        foreach (var x in new[] { 6, 9, 12 })
            c.Rect(x, 8, 1, 4, GingerDark);
        c.Rect(5, 14, 3, 2, Rgb(0xF4, 0xD8, 0xB0));
        c.Rect(10, 14, 3, 2, Rgb(0xF4, 0xD8, 0xB0));
        c.Ellipse(7, 5, 5, 4, Ginger);
        TemplateArt.Polygon(c, [(2.5f, 4), (3, -0.5f), (6, 2)], Ginger);
        TemplateArt.Polygon(c, [(11.5f, 4), (11, -0.5f), (8, 2)], Ginger);
        c.Rect(4, 4, 2, 2, Rgb(0x4E, 0xA0, 0x4A));
        c.Rect(8, 4, 2, 2, Rgb(0x4E, 0xA0, 0x4A));
        c.Set(5, 5, Ink);
        c.Set(9, 5, Ink);
        c.Set(7, 6, Rgb(0xE8, 0x80, 0x90));
        c.Rect(5, 7, 5, 1, Rgb(0xF4, 0xD8, 0xB0));
        c.Outline(Rgb(0x3A, 0x2C, 0x2A));
        return c;
    }

    // ----- Portraits for the dialogue box (24 by 24, framed) -----

    private static Canvas Framed(Pixel back, Action<Canvas> paint, int seed)
    {
        var c = new Canvas(24, 24, seed);
        var frame = Rgb(0x2A, 0x2F, 0x3A);
        c.Fill(frame);
        c.Rect(2, 2, 20, 20, back);
        paint(c);
        c.Bevel(0, 0, 24, 24, frame.Times(1.6f), frame.Times(0.6f));
        return c;
    }

    private static Canvas HeroFaceArt() => Framed(Rgb(0x6A, 0xA8, 0xD8), c =>
    {
        c.Rect(4, 18, 16, 4, Rgb(0xF2, 0xC1, 0x3C));
        c.Rect(7, 16, 10, 3, Rgb(0xC8, 0x3C, 0x34));
        c.Ellipse(12, 11, 6, 6, Skin);
        c.Rect(6, 4, 12, 4, Rgb(0x6B, 0x42, 0x26));
        c.Rect(5, 6, 3, 6, Rgb(0x6B, 0x42, 0x26));
        c.Rect(16, 6, 3, 5, Rgb(0x6B, 0x42, 0x26));
        c.Rect(9, 10, 2, 2, Ink);
        c.Rect(13, 10, 2, 2, Ink);
        c.Rect(10, 14, 4, 1, Rgb(0xA0, 0x52, 0x48));
        c.Rect(7, 12, 2, 1, Rgb(0xE8, 0x90, 0x80));
        c.Rect(15, 12, 2, 1, Rgb(0xE8, 0x90, 0x80));
    }, 107);

    private static Canvas MabelFaceArt() => Framed(Rgb(0xE0, 0xB8, 0xC8), c =>
    {
        var hair = Rgb(0xC8, 0xC8, 0xD0);
        c.Rect(4, 18, 16, 4, Rgb(0x9A, 0x7A, 0xC8));
        c.Ellipse(12, 12, 6, 6, Skin);
        c.Ellipse(12, 4, 4, 2, hair);
        c.Rect(6, 5, 12, 3, hair);
        c.Rect(5, 7, 3, 6, hair);
        c.Rect(16, 7, 3, 6, hair);
        c.Rect(8, 10, 3, 3, Rgb(0x6A, 0x6A, 0x76));
        c.Rect(13, 10, 3, 3, Rgb(0x6A, 0x6A, 0x76));
        c.Set(9, 11, Ink);
        c.Set(14, 11, Ink);
        c.Rect(10, 15, 4, 1, Rgb(0xC0, 0x5A, 0x60));
    }, 108);

    private static Canvas PipFaceArt() => Framed(Rgb(0x9C, 0xD0, 0x8C), c => CatFace(c, 12, 14, 1), 109);

    // ----- Things in the rooms, and the items' icons -----

    // Each item's icon, 16 by 16, along one sheet: Cup, Tea, Shed Key, Fishing Rod, Sardines, Fishy Rod.
    private static Canvas ItemsArt()
    {
        var c = new Canvas(16 * ItemNames.Length, 16, seed: 110);
        var mug = Rgb(0xF4, 0xF0, 0xE6);
        var band = Rgb(0x4C, 0x8D, 0xF0);
        void Mug(int x)
        {
            c.Rect(x + 3, 6, 9, 9, mug);
            c.Rect(x + 3, 9, 9, 2, band);
            c.Rect(x + 12, 8, 3, 1, mug);
            c.Rect(x + 14, 8, 1, 5, mug);
            c.Rect(x + 12, 12, 3, 1, mug);
        }

        Mug(0);
        Mug(16);
        c.Rect(16 + 4, 6, 7, 2, Rgb(0xA0, 0x6A, 0x3E));
        c.Line(16 + 6, 1, 16 + 6, 4, Rgb(0xE0, 0xE4, 0xEA));
        c.Line(16 + 9, 0, 16 + 9, 3, Rgb(0xE0, 0xE4, 0xEA));

        var iron = Rgb(0xA8, 0xA0, 0x90);
        c.Ellipse(32 + 4, 7, 3, 3, iron);
        c.Rect(32 + 4, 7, 1, 1, Rgb(0x2A, 0x20, 0x24));
        c.Rect(32 + 7, 6, 8, 2, iron);
        c.Rect(32 + 11, 8, 2, 3, iron);
        c.Rect(32 + 14, 8, 1, 2, iron);

        void RodShape(int x)
        {
            c.Line(x + 1, 15, x + 13, 1, Rgb(0x8A, 0x5A, 0x34));
            c.Rect(x + 3, 11, 3, 3, Rgb(0x6A, 0x6A, 0x76));
            c.Line(x + 14, 1, x + 14, 9, Rgb(0xE8, 0xEC, 0xF0));
        }

        RodShape(48);
        c.Rect(48 + 13, 9, 3, 2, Rgb(0xE0, 0x46, 0x46)); // a float

        c.Rect(64 + 2, 6, 12, 8, Rgb(0x3E, 0x6E, 0xB8));
        c.Rect(64 + 2, 6, 12, 2, Rgb(0x9A, 0xA4, 0xB0));
        c.Ellipse(64 + 8, 10, 4, 2, Rgb(0xB8, 0xC4, 0xCC));
        c.Set(64 + 5, 10, Rgb(0x2A, 0x20, 0x24));
        c.Rect(64 + 12, 9, 2, 3, Rgb(0xB8, 0xC4, 0xCC));

        RodShape(80);
        c.Ellipse(80 + 14, 11, 1, 3, Rgb(0xB8, 0xC4, 0xCC));
        c.Set(80 + 14, 9, Rgb(0x2A, 0x20, 0x24));
        for (var i = 0; i < ItemNames.Length; i++)
            Outline(c, i * 16, 16);
        return c;
    }

    // The kitchen table, front on, with a jug of flowers on it.
    private static Canvas TableArt()
    {
        var c = new Canvas(64, 36, seed: 111);
        var leg = DarkWood;
        c.Rect(14, 18, 3, 12, leg.Times(0.8f));
        c.Rect(47, 18, 3, 12, leg.Times(0.8f));
        c.Rect(2, 12, 60, 6, Rgb(0xC8, 0x9A, 0x62));
        c.Rect(2, 12, 60, 1, Rgb(0xE0, 0xB8, 0x80));
        c.Rect(2, 18, 60, 3, Wood);
        c.Rect(5, 21, 4, 15, leg);
        c.Rect(55, 21, 4, 15, leg);
        c.Rect(28, 6, 7, 8, Rgb(0x4C, 0x8D, 0xF0));
        c.Rect(28, 6, 7, 1, Rgb(0x8C, 0xB8, 0xF8));
        foreach (var (x, y, color) in new[] { (27, 2, Rgb(0xE0, 0x46, 0x5A)), (31, 0, Rgb(0xF0, 0xE0, 0x50)), (35, 2, Rgb(0xF4, 0xF4, 0xF4)) })
        {
            c.Line(31, 6, x + 1, y + 2, Rgb(0x3E, 0x7E, 0x3C));
            c.Rect(x, y, 3, 3, color);
        }

        c.Outline(Rgb(0x3A, 0x2C, 0x2A));
        return c;
    }

    // The shed's doorway once it's unlocked: dark inside, the door swung open against the wall.
    private static Canvas ShedOpenArt()
    {
        var c = new Canvas(24, 48, seed: 112);
        c.Rect(0, 0, 24, 48, Rgb(0x5E, 0x46, 0x2C));
        c.Rect(2, 2, 20, 46, Rgb(0x2A, 0x20, 0x18));
        c.Rect(6, 14, 8, 2, Rgb(0x4A, 0x36, 0x22));
        c.Rect(15, 20, 3, 14, Rgb(0x5A, 0x5A, 0x64));
        c.Rect(2, 40, 20, 8, Rgb(0x3A, 0x2C, 0x1E));
        c.Rect(0, 2, 4, 46, Rgb(0x7A, 0x5A, 0x38));
        c.Rect(1, 6, 1, 38, Rgb(0x66, 0x4A, 0x2E));
        return c;
    }

    // Grandad's fishing rod, leaning on the wall: a long cane, a reel and its line.
    private static Canvas RodArt()
    {
        var c = new Canvas(12, 48, seed: 113);
        c.Line(2, 47, 8, 1, Rgb(0x8A, 0x5A, 0x34));
        c.Rect(2, 34, 4, 4, Rgb(0x6A, 0x6A, 0x76));
        c.Line(9, 1, 10, 22, Rgb(0xE8, 0xEC, 0xF0));
        c.Rect(9, 22, 2, 3, Rgb(0xE0, 0x46, 0x46));
        c.Outline(Rgb(0x3A, 0x2C, 0x2A));
        return c;
    }

    // A garden gnome with a red hat and a fishing line of his own.
    private static Canvas GnomeArt()
    {
        var c = new Canvas(12, 16, seed: 114);
        TemplateArt.Polygon(c, [(2, 6), (6, -1), (10, 6)], Rgb(0xD8, 0x3A, 0x34));
        c.Ellipse(6, 7, 2, 2, Skin);
        c.Rect(3, 8, 6, 3, Rgb(0xF4, 0xF4, 0xF4));
        c.Rect(3, 10, 6, 4, Rgb(0x3E, 0x6E, 0xB8));
        c.Rect(3, 14, 2, 2, Rgb(0x6A, 0x44, 0x28));
        c.Rect(7, 14, 2, 2, Rgb(0x6A, 0x44, 0x28));
        c.Outline(Rgb(0x3A, 0x2C, 0x2A));
        return c;
    }
}
