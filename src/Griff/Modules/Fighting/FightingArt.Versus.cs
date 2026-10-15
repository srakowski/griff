using Griff.Art;
using static Griff.Projects.TemplateArt;

namespace Griff.Modules.Fighting;

// The Fighter template's pictures: Kenta's and Mira's sheets (25 frames each in cells of 24 by 32, six across: standing,
// walking, crouching, jumping, blocking high and low, hurt, a jab, a kick and a low kick, two specials of their own, and
// knocked out), the dojo at dusk the fight is on, the select screen's backdrop and a cursor's frame.
internal static partial class FightingArt
{
    public const string Kenta = "Fighting/kenta.png";
    public const string Mira = "Fighting/mira.png";
    public const string Dojo = "Fighting/dojo.png";
    public const string SelectBackdrop = "Fighting/select.png";
    public const string Cursor = "Fighting/cursor.png";

    /// <summary>A versus fighter's sheet: six frames across, five down.</summary>
    public const int VersusColumns = 6, VersusRows = 5;

    /// <summary>The dojo picture's size in pixels (16 to a unit: 18 by 9 units), and the row its floor's top is on.</summary>
    public const int DojoWidth = 288, DojoHeight = 144, DojoFloor = 114;

    // The frames every versus fighter shares, as FighterClips lays them out: standing, walking, crouching, jumping, blocking,
    // hurt, the jab, the kick and the low kick.
    private static Pose[] Shared => FighterPoses[..18];

    private static readonly Pose KnockedOut = new(Down: true);

    /// <summary>Kenta's clips: the shared ones, his palm strike and uppercut, and knocked out.</summary>
    public const string KentaClips = FighterClips + ", KO 24";

    /// <summary>Mira's clips: the shared ones, her high kick and flash kick, and knocked out.</summary>
    public const string MiraClips =
        "Idle 0-1 @3, Walk 2-3 @7, Crouch 4, Jump 5, Block 6, CrouchBlock 7, Hurt 8, " +
        "Jab 9-11 @15 once, Kick 12-14 @12 once, LowKick 15-17 @12 once, HighKick 18-20 @12 once, FlashKick 21-23 @12 once, KO 24";

    // Kenta: a white gi with a red headband, palms and an uppercut (the training pair's fighter's frames).
    private static Look KentaLook => TrainingLook;

    // Mira: a teal top and dark trousers, her hair in a ponytail, kicks.
    private static readonly Look MiraLook = new(Rgb(0x2E, 0xA8, 0xA0), Rgb(0xF0, 0xC0, 0x40), null, Rgb(0xF6, 0xF0, 0xE4),
        Rgb(0xD8, 0x9A, 0x70), Rgb(0x22, 0x1A, 0x2A), Ponytail: true, Trousers: Rgb(0x2C, 0x2E, 0x4A));

    private static readonly Pose[] MiraSpecials =
    [
        // High kick: knee up, leg high, coming down.
        new(Lean: -1, Fist: (16, 13), Guard: (14, 10), Kick: (17, 17)),
        new(Lean: -3, Fist: (14, 14), Guard: (12, 11), Kick: (23, 10)),
        new(Lean: -1, Fist: (16, 13), Guard: (14, 10), Kick: (19, 21)),
        // Flash kick: crouched, the leg sweeping up, up in the air with it raised.
        new(Crouch: true, Fist: (16, 20), Guard: (14, 18)),
        new(Lean: -3, Bob: -2, Fist: (13, 14), Guard: (11, 12), Kick: (17, 2)),
        new(Tuck: true, Bob: -3, Fist: (14, 10), Guard: (12, 9), Kick: (19, 4)),
    ];

    /// <summary>The Fighter template's pictures, by their path in Assets.</summary>
    public static Dictionary<string, byte[]> VersusFiles() => new()
    {
        [Kenta] = Sheet([.. FighterPoses, KnockedOut], KentaLook, VersusColumns, VersusRows, seed: 91).Png(VersusLayout),
        [Mira] = Sheet([.. Shared, .. MiraSpecials, KnockedOut], MiraLook, VersusColumns, VersusRows, seed: 92).Png(VersusLayout),
        [Dojo] = DojoArt().Png(),
        [SelectBackdrop] = SelectArt().Png(),
        [Cursor] = CursorArt().Png(),
    };

    private static SheetLayout VersusLayout => new(SheetKind.Sprite, CellWidth, CellHeight, VersusColumns, VersusRows, VersusColumns * VersusRows - 5, 12);

    // A dojo yard at dusk: an orange sky going purple with the sun low, far mountains, the dojo's wall with its paper
    // screens and roof, lanterns, and a floor of planks.
    private static Canvas DojoArt()
    {
        var c = new Canvas(DojoWidth, DojoHeight, seed: 95);
        var (top, bottom) = (Rgb(0x3A, 0x2A, 0x6A), Rgb(0xF6, 0x9A, 0x50));
        for (var y = 0; y < DojoFloor; y++)
        {
            var t = Math.Clamp(y / 70f, 0, 1);
            c.Rect(0, y, DojoWidth, 1, Mix(top, bottom, t));
        }

        // The sun, low, and far mountains in front of it.
        c.Glow(206, 58, 26, Rgb(0xF8, 0xB0, 0x60), Rgb(0xFF, 0xE8, 0xA0));
        c.Ellipse(206, 58, 13, 13, Rgb(0xFF, 0xD8, 0x80));
        var far = Rgb(0x6A, 0x4A, 0x7A);
        var near = Rgb(0x4A, 0x34, 0x5E);
        for (var x = 0; x < DojoWidth; x++)
        {
            var ridge = 58 + (int)(9 * MathF.Sin(x * 0.045f) + 6 * MathF.Sin(x * 0.11f + 1.3f));
            c.Rect(x, ridge, 1, 80 - ridge + 10, far);
            var hill = 70 + (int)(6 * MathF.Sin(x * 0.07f + 2.1f) + 3 * MathF.Sin(x * 0.19f));
            c.Rect(x, hill, 1, 84 - hill + 10, near);
        }

        // The dojo's wall: dark beams between paper screens glowing warm, under a roof.
        var wood = Rgb(0x5A, 0x34, 0x24);
        var paper = Rgb(0xF4, 0xDE, 0xB0);
        c.Rect(0, 80, DojoWidth, DojoFloor - 80, wood);
        for (var x = 6; x < DojoWidth - 20; x += 36)
        {
            c.Rect(x, 86, 28, 22, paper);
            for (var gx = x + 7; gx < x + 28; gx += 7)
                c.Rect(gx, 86, 1, 22, wood.Times(1.2f));
            c.Rect(x, 96, 28, 1, wood.Times(1.2f));
        }

        c.Rect(0, 74, DojoWidth, 6, Rgb(0x2E, 0x22, 0x2A));
        c.Rect(0, 72, DojoWidth, 2, Rgb(0x44, 0x34, 0x3C));
        // Lanterns hanging from the roof.
        foreach (var x in (int[])[40, 148, 256])
        {
            c.Line(x, 80, x, 84, Dark);
            c.Ellipse(x, 89, 4, 5, Rgb(0xE0, 0x40, 0x30));
            c.Rect(x - 3, 89, 7, 1, Rgb(0xF8, 0xC0, 0x50));
        }

        // The floor: planks, lighter towards the front.
        for (var y = DojoFloor; y < DojoHeight; y++)
        {
            var t = (y - DojoFloor) / (float)(DojoHeight - DojoFloor);
            c.Rect(0, y, DojoWidth, 1, Mix(Rgb(0x8A, 0x5A, 0x34), Rgb(0xB8, 0x80, 0x4C), t));
        }

        for (var y = DojoFloor + 4; y < DojoHeight; y += 6)
            c.Rect(0, y, DojoWidth, 1, Rgb(0x6A, 0x42, 0x26));
        c.Rect(0, DojoFloor, DojoWidth, 1, Rgb(0xD8, 0xA0, 0x60));
        c.Noise(0.03f);
        return c;
    }

    // The select screen's backdrop: deep blue to red, with light stripes slanting across.
    private static Canvas SelectArt()
    {
        var c = new Canvas(DojoWidth, DojoHeight, seed: 96);
        for (var x = 0; x < DojoWidth; x++)
            c.Rect(x, 0, 1, DojoHeight, Mix(Rgb(0x1A, 0x2A, 0x6A), Rgb(0x7A, 0x1A, 0x2A), x / (float)DojoWidth));
        for (var y = 0; y < DojoHeight; y++)
        {
            for (var x = 0; x < DojoWidth; x++)
            {
                if ((x + y * 2) / 12 % 3 == 0)
                    c.Blend(x, y, Rgb(0xFF, 0xFF, 0xFF), 0.05f);
            }
        }

        return c;
    }

    // A cursor's frame: a white ring with notched corners, tinted each player's color.
    private static Canvas CursorArt()
    {
        var c = new Canvas(40, 48, seed: 97);
        var white = Rgb(0xFF, 0xFF, 0xFF);
        c.Rect(0, 0, 40, 3, white);
        c.Rect(0, 45, 40, 3, white);
        c.Rect(0, 0, 3, 48, white);
        c.Rect(37, 0, 3, 48, white);
        foreach (var (x, y) in ((int, int)[])[(3, 3), (31, 3), (3, 39), (31, 39)])
            c.Rect(x, y, 6, 6, white);
        return c;
    }

    private static Pixel Mix(Pixel a, Pixel b, float t) =>
        new(Pixel.Clamp(a.R + (b.R - a.R) * t), Pixel.Clamp(a.G + (b.G - a.G) * t), Pixel.Clamp(a.B + (b.B - a.B) * t), 255);
}
