using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Tactics;

// The Tactics template's pictures: a field of grass with trees and rocks to fight around, the blue side's knights and
// archers, and the red side's orcs and goblin archers.
internal static class TacticsArt
{
    public const string FieldTiles = "Textures/field.png";
    public const string Knight = "Textures/knight.png";
    public const string Archer = "Textures/archer.png";
    public const string Orc = "Textures/orc.png";
    public const string Goblin = "Textures/goblin.png";

    /// <summary>The tiles across <see cref="FieldTiles"/>: grass, a tree and a rock.</summary>
    public const int FieldColumns = 3;

    public const int GrassTile = 0, TreeTile = 1, RockTile = 2;

    internal static Dictionary<string, byte[]> Files() => new()
    {
        [FieldTiles] = FieldSheet().Png(),
        [Knight] = HeroArt().Png(),
        [Archer] = ArcherArt().Png(),
        [Orc] = OrcArt().Png(),
        [Goblin] = GoblinArt().Png(),
    };

    // Three 32-pixel tiles: grass, a tree on grass, a rock on grass.
    private static Canvas FieldSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * FieldColumns, size);
        sheet.Paste(FieldGrass(), GrassTile * size, 0);
        sheet.Paste(Over(FieldGrass(), TreeArt()), TreeTile * size, 0);
        sheet.Paste(Over(FieldGrass(), RockArt()), RockTile * size, 0);
        return sheet;
    }

    // One picture over another, where it isn't see-through.
    private static Canvas Over(Canvas under, Canvas over)
    {
        for (var y = 0; y < over.Height; y++)
        {
            for (var x = 0; x < over.Width; x++)
            {
                if (over.Get(x, y) is { A: > 0 } pixel)
                    under.Set(x, y, pixel);
            }
        }

        return under;
    }

    // Short green grass with a few lighter blades.
    private static Canvas FieldGrass()
    {
        var c = new Canvas(32, 32, 90);
        var grass = Rgb(0x4A, 0x8A, 0x3C);
        c.Fill(grass);
        var random = new Random(90);
        for (var i = 0; i < 26; i++)
        {
            var (x, y) = (random.Next(32), random.Next(1, 32));
            c.Set(x, y, grass.Times(1.25f));
            c.Set(x, y - 1, grass.Times(1.12f));
        }

        c.Noise(0.05f);
        return c;
    }

    // A round leafy tree on a short trunk.
    private static Canvas TreeArt()
    {
        var c = new Canvas(32, 32, 91);
        var leaves = Rgb(0x2C, 0x6A, 0x2E);
        c.Rect(14, 20, 4, 9, Rgb(0x6A, 0x46, 0x28));
        c.Ellipse(16, 13, 11, 10, leaves);
        c.Ellipse(12, 10, 5, 4, leaves.Times(1.25f));
        c.Ellipse(20, 15, 4, 3, leaves.Times(0.8f));
        c.Shade(0.25f);
        c.Outline(Rgb(0x14, 0x2A, 0x14));
        return c;
    }

    // A grey boulder with a lighter top.
    private static Canvas RockArt()
    {
        var c = new Canvas(32, 32, 92);
        var stone = Rgb(0x8C, 0x8A, 0x94);
        c.Ellipse(16, 19, 12, 9, stone);
        c.Ellipse(13, 15, 6, 4, stone.Times(1.2f));
        c.Line(18, 14, 22, 22, stone.Times(0.65f));
        c.Shade(0.25f);
        c.Outline(Rgb(0x2A, 0x2A, 0x32));
        return c;
    }

    // An archer in a blue hood with a bow, facing right.
    private static Canvas ArcherArt()
    {
        var c = new Canvas(32, 32, 93);
        var tunic = Rgb(0x3E, 0x9A, 0xD8);
        var skin = Rgb(0xF2, 0xC2, 0x96);
        var wood = Rgb(0x9A, 0x6A, 0x3A);
        c.Rect(11, 16, 9, 9, tunic);
        c.Rect(12, 25, 3, 4, Rgb(0x4A, 0x3A, 0x2E));
        c.Rect(16, 25, 3, 4, Rgb(0x4A, 0x3A, 0x2E));
        c.Ellipse(15, 11, 6, 6, tunic.Times(0.8f));
        c.Rect(13, 10, 6, 4, skin);
        c.Rect(17, 11, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        for (var y = 8; y <= 26; y++)
        {
            var bend = Math.Abs(y - 17) < 5 ? 3 : Math.Abs(y - 17) < 8 ? 2 : 1;
            c.Set(21 + bend, y, wood);
        }

        c.Line(22, 8, 22, 26, Rgb(0xE8, 0xE8, 0xE8));
        c.Rect(8, 14, 3, 9, wood.Times(0.8f));
        c.Shade(0.2f);
        c.Outline(Rgb(0x10, 0x18, 0x28));
        return c;
    }
}
