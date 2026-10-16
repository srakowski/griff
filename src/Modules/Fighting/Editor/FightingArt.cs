using Griff.Art;
using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Fighting;

/// <summary>
/// The pictures Create Sparring Pair and Create Training Pair put in a project, painted in code as pixel art: a brawler's
/// sprite sheet (two frames standing, two running and four of a punch, in cells of 24 by 32), a sandbag on a stand, and a
/// fighter's sheet (see FightingArt.Fighter.cs). Saved as PNGs under Fighting/, so they open (and can be repainted) in the
/// Sprite Painter.
/// </summary>
internal static partial class FightingArt
{
    public const string Brawler = "Fighting/brawler.png";
    public const string Sandbag = "Fighting/sandbag.png";

    /// <summary>A cell of the brawler's sheet, in pixels.</summary>
    public const int CellWidth = 24, CellHeight = 32;

    /// <summary>The brawler's clips on its sheet: standing, running and a punch that plays once.</summary>
    public const string BrawlerClips = "Idle 0-1 @3, Run 2-3 @8, Attack 4-7 @12 once";

    private static readonly Pixel Skin = Rgb(0xE8, 0xB0, 0x88);
    private static readonly Pixel Hair = Rgb(0x3A, 0x2A, 0x20);
    private static readonly Pixel Shirt = Rgb(0x3A, 0x6E, 0xC8);
    private static readonly Pixel Pants = Rgb(0x2E, 0x3A, 0x5C);
    private static readonly Pixel Glove = Rgb(0xE0, 0x40, 0x30);
    private static readonly Pixel Dark = Rgb(0x1A, 0x16, 0x1E);

    /// <summary>Every picture, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files() => new()
    {
        [Brawler] = BrawlerSheet().Png(new SheetLayout(SheetKind.Sprite, CellWidth, CellHeight, 4, 2, 8, 12)),
        [Sandbag] = SandbagArt().Png(),
    };

    // Eight frames, four across: standing (bobbing), running (legs apart, then together) and a punch (pulled back, out, all
    // the way out, coming back). Facing right, as Griff draws sprites.
    private static Canvas BrawlerSheet()
    {
        var c = new Canvas(CellWidth * 4, CellHeight * 2, seed: 81);
        (int Bob, int Legs, int FistX, int FistY)[] frames =
        [
            (0, 0, 17, 12), (1, 0, 17, 13),
            (0, 1, 17, 12), (1, 2, 17, 13),
            (0, 0, 9, 14), (0, 0, 19, 12), (0, 0, 21, 12), (0, 0, 18, 12),
        ];
        for (var i = 0; i < frames.Length; i++)
        {
            // Each frame painted and outlined on its own, so nothing spills into the next.
            var (bob, legs, fistX, fistY) = frames[i];
            var cell = new Canvas(CellWidth, CellHeight, seed: 81 + i);
            Figure(cell, 0, 0, bob, legs, fistX, fistY);
            cell.Outline(Dark);
            c.Paste(cell, i % 4 * CellWidth, i / 4 * CellHeight);
        }

        return c;
    }

    private static void Figure(Canvas c, int x, int y, int bob, int legs, int fistX, int fistY)
    {
        // Legs: apart in a stance, wider or together as it runs.
        var (back, front) = legs switch { 1 => (6, 15), 2 => (9, 12), _ => (8, 13) };
        c.Rect(x + back, y + 22, 3, 10, Pants);
        c.Rect(x + front, y + 22, 3, 10, Pants);
        // Body, head and hair.
        c.Rect(x + 8, y + 12 + bob, 8, 10, Shirt);
        c.Ellipse(x + 12, y + 7 + bob, 4, 4, Skin);
        c.Ellipse(x + 12, y + 4 + bob, 4, 2, Hair);
        c.Set(x + 14, y + 7 + bob, Dark);
        // The guard hand up by its face, and the punching arm out to its fist.
        c.Line(x + 10, y + 14 + bob, x + 13, y + 11 + bob, Skin);
        c.Ellipse(x + 14, y + 10 + bob, 1, 1, Glove);
        c.Line(x + 14, y + 13 + bob, x + fistX, y + fistY + bob, Skin);
        c.Ellipse(x + fistX, y + fistY + bob, 2, 2, Glove);
    }

    // A sandbag hanging on a stand.
    private static Canvas SandbagArt()
    {
        var c = new Canvas(CellWidth, CellHeight, seed: 82);
        c.Rect(6, 30, 12, 2, Rgb(0x55, 0x55, 0x60));
        c.Rect(11, 24, 2, 7, Rgb(0x70, 0x70, 0x7A));
        c.Ellipse(12, 14, 7, 11, Rgb(0xB8, 0x8A, 0x58));
        c.Rect(5, 9, 15, 1, Rgb(0x7A, 0x52, 0x33));
        c.Rect(5, 19, 15, 1, Rgb(0x7A, 0x52, 0x33));
        c.Noise(0.05f);
        c.Outline(Dark);
        return c;
    }
}
