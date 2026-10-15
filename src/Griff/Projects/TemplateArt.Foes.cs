namespace Griff.Projects;

// Foes more than one template fights: a broad orc and a small goblin, facing right.
internal static partial class TemplateArt
{
    // A broad green orc with tusks and an axe, facing right.
    internal static Canvas OrcArt()
    {
        var c = new Canvas(32, 32, 94);
        var skin = Rgb(0x6A, 0xA8, 0x4A);
        var armor = Rgb(0x8A, 0x3A, 0x30);
        var steel = Rgb(0xB8, 0xBE, 0xC8);
        c.Rect(9, 15, 13, 10, armor);
        c.Rect(10, 25, 4, 4, Rgb(0x3A, 0x2E, 0x26));
        c.Rect(17, 25, 4, 4, Rgb(0x3A, 0x2E, 0x26));
        c.Ellipse(15, 10, 7, 6, skin);
        c.Rect(18, 9, 2, 1, Rgb(0xE8, 0x30, 0x20));
        c.Set(18, 13, Rgb(0xF4, 0xF0, 0xE0));
        c.Set(14, 13, Rgb(0xF4, 0xF0, 0xE0));
        c.Rect(23, 6, 2, 20, Rgb(0x6A, 0x46, 0x28));
        c.Ellipse(26, 9, 4, 4, steel);
        c.Rect(7, 16, 3, 7, skin.Times(0.9f));
        c.Shade(0.2f);
        c.Outline(Rgb(0x16, 0x22, 0x10));
        return c;
    }

    // A small goblin in a red cap with a short bow, facing right.
    internal static Canvas GoblinArt()
    {
        var c = new Canvas(32, 32, 95);
        var skin = Rgb(0x9A, 0xC8, 0x4A);
        var cloth = Rgb(0xA8, 0x40, 0x3A);
        var wood = Rgb(0x8A, 0x5A, 0x30);
        c.Rect(12, 18, 8, 7, cloth.Times(0.85f));
        c.Rect(13, 25, 2, 4, Rgb(0x3A, 0x2E, 0x26));
        c.Rect(17, 25, 2, 4, Rgb(0x3A, 0x2E, 0x26));
        c.Ellipse(16, 14, 5, 5, skin);
        c.Rect(9, 13, 3, 2, skin);
        c.Rect(20, 13, 3, 2, skin);
        c.Rect(12, 8, 8, 3, cloth);
        c.Rect(18, 13, 1, 1, Rgb(0xF0, 0xE0, 0x30));
        for (var y = 12; y <= 26; y++)
            c.Set(22 + (Math.Abs(y - 19) < 4 ? 2 : 1), y, wood);
        c.Line(23, 12, 23, 26, Rgb(0xE0, 0xE0, 0xE0));
        c.Shade(0.2f);
        c.Outline(Rgb(0x1A, 0x22, 0x10));
        return c;
    }
}
