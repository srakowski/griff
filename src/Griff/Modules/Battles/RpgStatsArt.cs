using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Battles;

// The RPG Stats template's pictures: a meadow at dusk to fight on, the four classes (a knight, a mage, a rogue and a
// cleric, facing right) and the five enemies (a slime, a goblin, a skeleton, an orc and a wyvern, drawn facing right and
// turned to face the hero in the game).
internal static class RpgStatsArt
{
    public const string Meadow = "Textures/meadow.png";
    public const string Knight = "Textures/knight.png";
    public const string Mage = "Textures/mage.png";
    public const string Rogue = "Textures/rogue.png";
    public const string Cleric = "Textures/cleric.png";
    public const string Slime = "Textures/slime.png";
    public const string Goblin = "Textures/goblin.png";
    public const string Skeleton = "Textures/skeleton.png";
    public const string Orc = "Textures/orc.png";
    public const string Wyvern = "Textures/wyvern.png";

    internal static Dictionary<string, byte[]> Files() => new()
    {
        [Meadow] = MeadowArt().Png(),
        [Knight] = HeroArt().Png(),
        [Mage] = MageArt().Png(),
        [Rogue] = RogueArt().Png(),
        [Cleric] = ClericArt().Png(),
        [Slime] = SlimeArt().Png(),
        [Goblin] = GoblinArt().Png(),
        [Skeleton] = SkeletonArt().Png(),
        [Orc] = OrcArt().Png(),
        [Wyvern] = WyvernArt().Png(),
    };

    // A dusky sky over far hills and a meadow, 128 by 72, stretched across the view.
    private static Canvas MeadowArt()
    {
        const int width = 128, height = 72, horizon = 44;
        var c = new Canvas(width, height, 120);
        var top = Rgb(0x2A, 0x2C, 0x5A);
        var low = Rgb(0xE0, 0x8C, 0x6A);
        for (var y = 0; y < horizon; y++)
        {
            var t = y / (horizon - 1f);
            c.Rect(0, y, width, 1, new Pixel(Pixel.Clamp(top.R + (low.R - top.R) * t), Pixel.Clamp(top.G + (low.G - top.G) * t), Pixel.Clamp(top.B + (low.B - top.B) * t), 255));
        }

        var random = new Random(120);
        for (var i = 0; i < 30; i++)
            c.Blend(random.Next(width), random.Next(22), Rgb(0xF0, 0xF0, 0xFF), 0.4f + random.NextSingle() * 0.5f);
        c.Glow(100, 16, 9, Rgb(0xE8, 0xC8, 0xA8), Rgb(0xFA, 0xF0, 0xDC));

        // Far hills, then the meadow.
        var hill = Rgb(0x3A, 0x4C, 0x5A);
        foreach (var (x, r) in new[] { (12, 20), (48, 16), (80, 24), (120, 18) })
            c.Ellipse(x, horizon, r, 9, hill);
        foreach (var (x, r) in new[] { (30, 14), (66, 12), (104, 13) })
            c.Ellipse(x, horizon + 1, r, 5, hill.Times(1.2f));
        var grass = Rgb(0x3E, 0x7A, 0x3A);
        for (var y = horizon; y < height; y++)
            c.Rect(0, y, width, 1, grass.Times(0.8f + (y - horizon) * 0.01f));
        for (var i = 0; i < 260; i++)
        {
            var (x, y) = (random.Next(width), random.Next(horizon + 1, height));
            c.Set(x, y, grass.Times(1.25f));
            c.Set(x, y - 1, grass.Times(1.1f));
        }

        c.Noise(0.03f);
        return c;
    }
}
