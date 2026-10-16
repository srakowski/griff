using Griff.Art;
using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Fighting;

// The training pair's fighter: standing, walking, crouching, jumping, blocking high and low, hurt, and a jab, a kick, a low
// kick, a palm strike and an uppercut, in cells of 24 by 32, six across.
internal static partial class FightingArt
{
    public const string Fighter = "Fighting/fighter.png";

    /// <summary>The fighter's sheet: six frames across, four down.</summary>
    public const int FighterColumns = 6, FighterRows = 4;

    /// <summary>The fighter's clips on its sheet: what it plays standing, moving and blocking, and its five moves.</summary>
    public const string FighterClips =
        "Idle 0-1 @3, Walk 2-3 @6, Crouch 4, Jump 5, Block 6, CrouchBlock 7, Hurt 8, " +
        "Jab 9-11 @15 once, Kick 12-14 @12 once, LowKick 15-17 @12 once, Palm 18-20 @12 once, Uppercut 21-23 @12 once";

    /// <summary>
    /// How a fighter is dressed: its gi (or top and trousers), belt, headband (or none), hand wraps, skin and hair, and a
    /// ponytail instead of short hair.
    /// </summary>
    internal sealed record Look(Pixel Gi, Pixel Belt, Pixel? Band, Pixel Wrap, Pixel Skin, Pixel Hair, bool Ponytail = false, Pixel? Trousers = null);

    /// <summary>The training pair's fighter: a white gi and a red headband.</summary>
    internal static Look TrainingLook => new(Rgb(0xEE, 0xEA, 0xDC), Rgb(0x2A, 0x24, 0x2E), Rgb(0xD8, 0x30, 0x2C), Rgb(0xF4, 0xD8, 0xB8), Skin, Hair);

    /// <summary>The fighter's pictures, by their path in Assets.</summary>
    public static Dictionary<string, byte[]> FighterFiles() => new()
    {
        [Fighter] = FighterSheet().Png(new SheetLayout(SheetKind.Sprite, CellWidth, CellHeight, FighterColumns, FighterRows, FighterPoses.Length, 12)),
    };

    // How the fighter stands in a frame, in the cell's pixels facing right: crouching or tucked in a jump, leaning, its legs
    // apart or together, where its punching fist, its guard hand and its kicking foot are, palms out, or hurt.
    internal readonly record struct Pose(int Bob = 0, bool Crouch = false, int Lean = 0, int Stance = 0, (int X, int Y)? Fist = null,
        (int X, int Y)? Guard = null, (int X, int Y)? Kick = null, bool Palms = false, bool Hurt = false, bool Tuck = false, bool Down = false);

    // The fighter's frames in order, as FighterClips names them.
    private static readonly Pose[] FighterPoses =
    [
        // Idle, walking, crouching, jumping.
        new(Fist: (17, 13), Guard: (15, 10)),
        new(Bob: 1, Fist: (17, 13), Guard: (15, 10)),
        new(Stance: 1, Fist: (17, 13), Guard: (15, 10)),
        new(Stance: 2, Bob: 1, Fist: (17, 13), Guard: (15, 10)),
        new(Crouch: true, Fist: (17, 20), Guard: (15, 17)),
        new(Tuck: true, Fist: (17, 12), Guard: (15, 10)),
        // Blocking high and low, and hurt.
        new(Lean: -1, Fist: (16, 9), Guard: (16, 13)),
        new(Crouch: true, Lean: -1, Fist: (16, 16), Guard: (16, 20)),
        new(Hurt: true, Lean: -2, Fist: (13, 20), Guard: (9, 20)),
        // Jab: pulled back, out, coming back.
        new(Fist: (14, 14), Guard: (15, 10)),
        new(Lean: 1, Fist: (21, 12), Guard: (15, 10)),
        new(Fist: (18, 13), Guard: (15, 10)),
        // Kick: knee up, leg out, coming down.
        new(Lean: -1, Fist: (16, 13), Guard: (14, 10), Kick: (17, 20)),
        new(Lean: -2, Fist: (15, 13), Guard: (13, 10), Kick: (22, 19)),
        new(Lean: -1, Fist: (16, 13), Guard: (14, 10), Kick: (17, 25)),
        // Low kick: crouched, sweeping out, back.
        new(Crouch: true, Fist: (17, 20), Guard: (15, 17), Kick: (17, 29)),
        new(Crouch: true, Lean: -1, Fist: (16, 20), Guard: (14, 17), Kick: (22, 30)),
        new(Crouch: true, Fist: (17, 20), Guard: (15, 17), Kick: (19, 30)),
        // Palm strike: drawn back, both palms out, held.
        new(Lean: -1, Fist: (10, 15), Guard: (11, 17)),
        new(Lean: 2, Stance: 1, Fist: (21, 14), Palms: true),
        new(Lean: 1, Stance: 1, Fist: (20, 14), Palms: true),
        // Uppercut: crouched low, fist rising, up and off the ground.
        new(Crouch: true, Fist: (16, 24), Guard: (14, 18)),
        new(Lean: 1, Stance: 2, Fist: (17, 4), Guard: (14, 12)),
        new(Tuck: true, Bob: -1, Fist: (15, 1), Guard: (14, 11)),
    ];

    private static Canvas FighterSheet() => Sheet(FighterPoses, TrainingLook, FighterColumns, FighterRows, seed: 83);

    /// <summary>A fighter's sheet: each pose a cell of 24 by 32, <paramref name="columns"/> across, dressed as <paramref name="look"/> says.</summary>
    internal static Canvas Sheet(IReadOnlyList<Pose> poses, Look look, int columns, int rows, int seed)
    {
        var c = new Canvas(CellWidth * columns, CellHeight * rows, seed: seed);
        for (var i = 0; i < poses.Count; i++)
        {
            // Each frame painted and outlined on its own, so nothing spills into the next.
            var cell = new Canvas(CellWidth, CellHeight, seed: seed + i);
            FighterFigure(cell, poses[i], look);
            cell.Outline(Dark);
            c.Paste(cell, i % columns * CellWidth, i / columns * CellHeight);
        }

        return c;
    }

    private static void FighterFigure(Canvas c, Pose p, Look look)
    {
        var (gi, belt, wrap, skin, hair) = (look.Gi, look.Belt, look.Wrap, look.Skin, look.Hair);
        var legs = look.Trousers ?? gi;
        if (p.Down)
        {
            Lying(c, look, legs);
            return;
        }

        var top = (p.Crouch ? 19 : 12) + p.Bob;
        var lean = p.Lean;

        // Legs: standing in a stance (wider or together as it walks), bent in a crouch, tucked in a jump; a kicking leg out to its foot.
        if (p.Tuck)
        {
            c.Rect(7, top + 10, 4, 4, legs);
            c.Rect(13, top + 10, 4, 4, legs);
            c.Rect(6, top + 14, 4, 2, skin);
            c.Rect(14, top + 14, 4, 2, skin);
        }
        else if (p.Crouch)
        {
            c.Rect(5, 26, 5, 3, legs);
            c.Rect(5, 29, 3, 2, legs);
            c.Rect(4, 31, 4, 1, skin);
            if (p.Kick is null)
            {
                c.Rect(13, 26, 5, 3, legs);
                c.Rect(16, 29, 3, 2, legs);
                c.Rect(16, 31, 4, 1, skin);
            }
        }
        else
        {
            var (back, front) = p.Stance switch { 1 => (6, 15), 2 => (9, 12), _ => (7, 14) };
            c.Rect(back, 22, 3, 9, legs);
            c.Rect(back - 1, 31, 4, 1, skin);
            if (p.Kick is null)
            {
                c.Rect(front, 22, 3, 9, legs);
                c.Rect(front, 31, 4, 1, skin);
            }
        }

        if (p.Kick is { } foot)
        {
            var hipY = p.Crouch ? 27 : 23;
            for (var d = -1; d <= 1; d++)
                c.Line(12, hipY + d, foot.X - 1, foot.Y + d, legs);
            c.Ellipse(foot.X, foot.Y, 1, 1, skin);
        }

        // Body, belt, the gi's open collar, head, hair and headband (its tails flying behind).
        c.Rect(8 + lean, top, 8, p.Crouch ? 8 : 10, gi);
        c.Rect(8 + lean, top + (p.Crouch ? 6 : 8), 8, 1, belt);
        c.Line(11 + lean, top, 12 + lean, top + 3, skin);
        var (hx, hy) = (12 + lean, top - 5);
        c.Ellipse(hx, hy, 4, 4, skin);
        c.Ellipse(hx, hy - 3, 4, 2, hair);
        if (look.Ponytail)
        {
            // Hair down the back of the head, tied in a tail that swings behind.
            c.Rect(hx - 4, hy - 3, 2, 4, hair);
            c.Line(hx - 5, hy - 2, hx - 8, hy + 3, hair);
            c.Line(hx - 6, hy - 2, hx - 8, hy + 2, hair);
        }

        if (look.Band is { } band)
        {
            c.Rect(hx - 4, hy - 2, 9, 1, band);
            c.Set(hx - 5, hy - 1, band);
            c.Set(hx - 6, hy, band);
        }
        if (p.Hurt)
        {
            // Eyes shut, mouth open.
            c.Rect(hx + 1, hy, 2, 1, Dark);
            c.Set(hx + 3, hy + 2, Dark);
        }
        else
        {
            c.Set(hx + 2, hy, Dark);
        }

        // Arms from the shoulders: the guard hand and the punching fist, or both palms out.
        var (sx, sy) = (13 + lean, top + 2);
        if (p.Guard is { } guard && !p.Palms)
        {
            c.Line(sx - 2, sy, guard.X, guard.Y, skin);
            c.Ellipse(guard.X, guard.Y, 1, 1, wrap);
        }

        if (p.Fist is { } fist)
        {
            c.Line(sx, sy, fist.X, fist.Y, skin);
            c.Ellipse(fist.X, fist.Y, p.Palms ? 1 : 2, 2, wrap);
            if (p.Palms)
            {
                c.Line(sx, sy + 2, fist.X - 1, fist.Y + 3, skin);
                c.Ellipse(fist.X - 1, fist.Y + 3, 1, 2, wrap);
            }
        }
    }

    // Knocked out: flat on its back along the bottom of the cell, head to the left.
    private static void Lying(Canvas c, Look look, Pixel legs)
    {
        c.Rect(13, 27, 9, 3, legs);
        c.Rect(21, 26, 2, 4, look.Skin);
        c.Rect(6, 26, 8, 4, look.Gi);
        c.Rect(11, 26, 1, 4, look.Belt);
        c.Line(7, 25, 12, 23, look.Skin);
        c.Ellipse(12, 23, 1, 1, look.Wrap);
        c.Ellipse(4, 27, 3, 3, look.Skin);
        c.Ellipse(3, 26, 2, 3, look.Hair);
        if (look.Band is { } band)
            c.Rect(5, 24, 1, 6, band);
        c.Rect(4, 27, 2, 1, Dark);
    }
}
