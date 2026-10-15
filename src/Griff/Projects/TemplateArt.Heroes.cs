namespace Griff.Projects;

// Characters more than one template uses: a mage, a rogue, a cleric, a skeleton and a wyvern, facing right (the RPG
// Stats template's classes and foes, and the Cards template's heroes and creatures).
internal static partial class TemplateArt
{
    private static readonly Pixel Skin = Rgb(0xF2, 0xC2, 0x96);
    private static readonly Pixel Boots = Rgb(0x4A, 0x3A, 0x2E);
    private static readonly Pixel Eye = Rgb(0x1A, 0x1C, 0x22);

    // A mage in a purple robe and pointed hat with a staff topped by a glowing orb, facing right.
    internal static Canvas MageArt()
    {
        var c = new Canvas(32, 32, 121);
        var robe = Rgb(0x7A, 0x4A, 0xC8);
        c.Rect(11, 16, 10, 12, robe);
        c.Rect(10, 24, 12, 4, robe.Times(0.85f));
        c.Ellipse(16, 12, 4, 4, Skin);
        c.Set(18, 12, Eye);
        c.Rect(11, 8, 10, 2, robe.Times(0.75f));
        for (var y = 1; y < 8; y++)
            c.Rect(16 - (8 - y) / 2 + 1, y, (8 - y) + 1, 1, robe.Times(0.8f));
        c.Set(12, 12, Skin.Times(0.9f));
        c.Rect(23, 9, 2, 19, Rgb(0x8A, 0x5A, 0x30));
        c.Glow(24, 7, 4, Rgb(0x6A, 0xC8, 0xFF), Rgb(0xF0, 0xFF, 0xFF));
        c.Shade(0.2f);
        c.Outline(Rgb(0x1A, 0x10, 0x2A));
        return c;
    }

    // A rogue in a green hood and a dark cloak, a dagger in each hand, facing right.
    internal static Canvas RogueArt()
    {
        var c = new Canvas(32, 32, 122);
        var hood = Rgb(0x3A, 0x7A, 0x4A);
        var cloak = Rgb(0x34, 0x34, 0x3E);
        var steel = Rgb(0xC4, 0xCC, 0xDC);
        c.Rect(11, 16, 10, 9, cloak);
        c.Rect(12, 25, 3, 4, Boots);
        c.Rect(17, 25, 3, 4, Boots);
        c.Ellipse(16, 11, 6, 6, hood);
        c.Rect(14, 10, 6, 4, Skin.Times(0.85f));
        c.Set(18, 11, Eye);
        c.Line(22, 16, 25, 13, steel);
        c.Line(8, 20, 6, 17, steel);
        c.Rect(21, 17, 2, 2, Skin);
        c.Shade(0.2f);
        c.Outline(Rgb(0x10, 0x1A, 0x12));
        return c;
    }

    // A cleric in a white robe trimmed with gold, with a mace, facing right.
    internal static Canvas ClericArt()
    {
        var c = new Canvas(32, 32, 123);
        var robe = Rgb(0xE8, 0xE4, 0xD8);
        var gold = Rgb(0xE0, 0xB8, 0x40);
        c.Rect(11, 16, 10, 12, robe);
        c.Rect(15, 16, 2, 12, gold);
        c.Ellipse(16, 11, 5, 5, Skin);
        c.Rect(11, 6, 10, 3, robe.Times(0.9f));
        c.Set(18, 11, Eye);
        c.Rect(22, 12, 2, 12, Rgb(0x8A, 0x6A, 0x3A));
        c.Ellipse(23, 11, 3, 3, Rgb(0xB8, 0xBE, 0xC8));
        c.Glow(16, 4, 3, gold, Rgb(0xFF, 0xF4, 0xC0));
        c.Shade(0.2f);
        c.Outline(Rgb(0x2A, 0x24, 0x18));
        return c;
    }

    // A skeleton with a rusty sword, facing right.
    internal static Canvas SkeletonArt()
    {
        var c = new Canvas(32, 32, 124);
        var bone = Rgb(0xE8, 0xE4, 0xD4);
        c.Ellipse(16, 9, 5, 5, bone);
        c.Rect(17, 8, 2, 2, Eye);
        c.Rect(13, 8, 2, 2, Eye.Times(2));
        c.Rect(14, 13, 5, 2, bone.Times(0.9f));
        c.Rect(15, 15, 2, 9, bone);
        for (var y = 16; y <= 21; y += 2)
            c.Rect(12, y, 8, 1, bone);
        c.Rect(13, 24, 2, 5, bone);
        c.Rect(17, 24, 2, 5, bone);
        c.Line(20, 17, 24, 12, bone);
        c.Line(24, 12, 28, 4, Rgb(0xA0, 0x6A, 0x40));
        c.Shade(0.15f);
        c.Outline(Rgb(0x22, 0x20, 0x1C));
        return c;
    }

    // A red wyvern, wings up, facing right.
    internal static Canvas WyvernArt()
    {
        var c = new Canvas(32, 32, 125);
        var scales = Rgb(0xC0, 0x3A, 0x34);
        var wing = Rgb(0x8A, 0x2A, 0x30);
        Polygon(c, [(14f, 14f), (4f, 2f), (8f, 16f)], wing);
        Polygon(c, [(18f, 14f), (20f, 1f), (12f, 12f)], wing.Times(1.15f));
        c.Ellipse(15, 19, 8, 6, scales);
        c.Ellipse(24, 12, 4, 3, scales);
        c.Line(21, 15, 23, 13, scales);
        c.Set(25, 11, Rgb(0xF8, 0xD0, 0x30));
        c.Line(7, 21, 2, 26, scales.Times(0.8f));
        c.Rect(12, 24, 2, 4, scales.Times(0.7f));
        c.Rect(17, 24, 2, 4, scales.Times(0.7f));
        c.Ellipse(15, 21, 5, 2, Rgb(0xF0, 0xC0, 0x80));
        c.Shade(0.25f);
        c.Outline(Rgb(0x2A, 0x0E, 0x10));
        return c;
    }
}
