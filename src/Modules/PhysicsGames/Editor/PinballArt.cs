using System.Numerics;
using Griff.Audio;
using static Griff.Templates.TemplateArt;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// The pictures and sounds the pinball presets put in a project, made in code: a steel ball, a bumper, flippers, kickers,
/// drop targets, lane lights and a playfield; and the sounds of flipping, bumping, scoring, shooting, draining and tilting
/// (made by the Sound Maker's synth, so they open in it). Saved under Pinball/, so they can be repainted and remade.
/// </summary>
internal static class PinballArt
{
    public const string Ball = "Pinball/ball.png";
    public const string Bumper = "Pinball/bumper.png";
    public const string Flipper = "Pinball/flipper.png";
    public const string Kicker = "Pinball/kicker.png";
    public const string Target = "Pinball/target.png";
    public const string Light = "Pinball/light.png";
    public const string Playfield = "Pinball/playfield.png";

    public const string FlipSound = "Pinball/flip.wav";
    public const string BumperSound = "Pinball/bumper.wav";
    public const string KickerSound = "Pinball/kicker.wav";
    public const string TargetSound = "Pinball/target.wav";
    public const string LaneSound = "Pinball/lane.wav";
    public const string LaunchSound = "Pinball/launch.wav";
    public const string PullSound = "Pinball/pull.wav";
    public const string DrainSound = "Pinball/drain.wav";
    public const string SaveSound = "Pinball/save.wav";
    public const string NudgeSound = "Pinball/nudge.wav";
    public const string WarningSound = "Pinball/warning.wav";
    public const string TiltSound = "Pinball/tilt.wav";

    // Pixels to a world unit in the pieces' pictures.
    private const int PerUnit = 32;

    /// <summary>A flipper's shape: its two ends' middles (from its position, the middle of the paddle) and their radii.</summary>
    public const float FlipperLength = 1.6f;
    public const float FlipperRadius = 0.28f;
    public const float FlipperTipRadius = 0.13f;

    /// <summary>The kicker's corners, for one on the left (its kicking side faces up and to the right).</summary>
    public static readonly Vector2[] KickerCorners = [new(-0.45f, 0.8f), new(-0.45f, -0.8f), new(0.45f, -0.8f)];

    /// <summary>Every picture and sound, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files() => new()
    {
        [Ball] = BallArt().Png(),
        [Bumper] = BumperArt().Png(),
        [Flipper] = FlipperArt().Png(),
        [Kicker] = KickerArt().Png(),
        [Target] = TargetArt().Png(),
        [Light] = LightArt().Png(),
        [Playfield] = PlayfieldArt().Png(),
        [FlipSound] = Sound(SoundPreset.Shoot, 21, 0.18f, 0.35f),
        [BumperSound] = Sound(SoundPreset.Coin, 3, 0.3f, 0.45f),
        [KickerSound] = Sound(SoundPreset.Hit, 11, 0.2f, 0.45f),
        [TargetSound] = Sound(SoundPreset.Blip, 9, 0.15f, 0.45f),
        [LaneSound] = Sound(SoundPreset.Coin, 17, 0.35f, 0.4f),
        [LaunchSound] = Sound(SoundPreset.Jump, 14, 0.4f, 0.5f),
        [PullSound] = Sound(SoundPreset.Blip, 1, 0.15f, 0.3f),
        [DrainSound] = Sound(SoundPreset.Hit, 40, 0.5f, 0.5f),
        [SaveSound] = Sound(SoundPreset.PowerUp, 6, 0.7f, 0.5f),
        [NudgeSound] = Sound(SoundPreset.Hit, 2, 0.25f, 0.4f),
        [WarningSound] = Sound(SoundPreset.Blip, 13, 0.25f, 0.5f),
        [TiltSound] = Sound(SoundPreset.Explosion, 5, 0.9f, 0.55f),
    };

    /// <summary>The corners of a flipper's outline, from the middle of its paddle, round its thick end on the left (mirrored, the right).</summary>
    public static string FlipperPoints(bool mirrored)
    {
        // Where the straight sides meet each end's circle: the normal's angle off the paddle's line.
        var (half, big, small) = (FlipperLength / 2, FlipperRadius, FlipperTipRadius);
        var touch = MathF.Acos((big - small) / FlipperLength);
        var points = new List<Vector2>();
        const int pivotSteps = 7;
        const int tipSteps = 4;
        for (var i = 0; i <= pivotSteps; i++)
        {
            var angle = touch + (2 * MathF.PI - 2 * touch) * i / pivotSteps;
            points.Add(new Vector2(-half + big * MathF.Cos(angle), big * MathF.Sin(angle)));
        }

        for (var i = 0; i <= tipSteps; i++)
        {
            var angle = -touch + 2 * touch * i / tipSteps;
            points.Add(new Vector2(half + small * MathF.Cos(angle), small * MathF.Sin(angle)));
        }

        if (mirrored)
        {
            points = [.. points.Select(p => p with { X = -p.X })];
            points.Reverse();
        }

        return string.Join("; ", points.Select(p => FormattableString.Invariant($"{p.X:0.###} {p.Y:0.###}")));
    }

    /// <summary>A kicker's outline as polygon points, for one on the left or (mirrored) the right.</summary>
    public static string KickerPoints(bool mirrored) => string.Join("; ", (mirrored ? KickerCorners.Reverse().Select(p => p with { X = -p.X }) : KickerCorners)
        .Select(p => FormattableString.Invariant($"{p.X:0.###} {p.Y:0.###}")));

    // A sound of a preset kind, the first (counting seeds up from its own) short enough.
    internal static byte[] Sound(SoundPreset preset, int seed, float maxSeconds, float volume)
    {
        SoundRecipe recipe;
        for (var s = seed; ; s++)
        {
            recipe = SoundRecipe.Preset(preset, new Random(s));
            if (SoundSynth.Render(recipe).Seconds <= maxSeconds || s > seed + 200)
                break;
        }

        recipe = recipe with { Volume = volume };
        return Wav.Encode(SoundSynth.Render(recipe), recipe);
    }

    // ----- Pictures: each pixel worked out from a shape in world units, edges softened -----

    // Paints a picture `width` by `height` world units, PerUnit pixels to one: `paint` gives each point's color (its
    // alpha 0 outside), sampled four times a pixel so edges are smooth.
    internal static Canvas Paint(float width, float height, Func<Vector2, Vector4> paint, int perUnit = PerUnit)
    {
        var (w, h) = ((int)MathF.Round(width * perUnit), (int)MathF.Round(height * perUnit));
        var c = new Canvas(w, h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var sum = Vector4.Zero;
                for (var s = 0; s < 4; s++)
                {
                    var px = (x + 0.25f + 0.5f * (s % 2)) / perUnit - width / 2;
                    var py = height / 2 - (y + 0.25f + 0.5f * (s / 2)) / perUnit;
                    var color = paint(new Vector2(px, py));
                    sum += new Vector4(color.X * color.W, color.Y * color.W, color.Z * color.W, color.W);
                }

                if (sum.W <= 0)
                    continue;
                var a = sum.W / 4;
                c.Set(x, y, new Pixel(Pixel.Clamp(sum.X / sum.W * 255), Pixel.Clamp(sum.Y / sum.W * 255), Pixel.Clamp(sum.Z / sum.W * 255), Pixel.Clamp(a * 255)));
            }
        }

        return c;
    }

    internal static Vector4 Rgb(int hex, float alpha = 1) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, alpha);

    internal static Vector4 Shade(Vector4 color, float amount) => new(Math.Clamp(color.X * amount, 0, 1), Math.Clamp(color.Y * amount, 0, 1), Math.Clamp(color.Z * amount, 0, 1), color.W);

    internal static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, Math.Clamp(t, 0, 1));

    // A steel ball, lit from the top left.
    private static Canvas BallArt() => Paint(0.6f, 0.6f, p =>
    {
        var r = p.Length() / 0.3f;
        if (r > 1)
            return Vector4.Zero;
        var light = Math.Clamp(0.55f + 0.6f * Vector2.Dot(p / 0.3f, new Vector2(-0.6f, 0.6f)), 0, 1);
        var steel = Mix(Rgb(0x5A6270), Rgb(0xE8EEF4), light);
        var glint = MathF.Max(0, 1 - Vector2.Distance(p, new Vector2(-0.1f, 0.11f)) / 0.07f);
        return Mix(steel, Rgb(0xFFFFFF), glint) with { W = 1 };
    });

    // A pop bumper from above: a dark skirt, a white ring and a bright cap with a star.
    private static Canvas BumperArt() => Paint(1.2f, 1.2f, p =>
    {
        var r = p.Length();
        if (r > 0.6f)
            return Vector4.Zero;
        if (r > 0.5f)
            return Shade(Rgb(0x8C1C2C), 0.8f + 0.4f * (p.Y + 0.6f) / 1.2f);
        if (r > 0.42f)
            return Rgb(0xF4F0E6);
        var angle = MathF.Atan2(p.Y, p.X);
        var star = 0.2f + 0.1f * MathF.Cos(5 * (angle - MathF.PI / 2));
        var cap = Mix(Rgb(0xE8304A), Rgb(0xFF7A6A), (p.Y + 0.42f) / 0.84f);
        return r < star ? Mix(Rgb(0xFFD84A), Rgb(0xFFF2A8), (p.Y + 0.2f) / 0.4f) : cap;
    });

    // A flipper's paddle (its thick end on the left): white, with a red rubber edge.
    private static Canvas FlipperArt()
    {
        var (half, big, small) = (FlipperLength / 2, FlipperRadius, FlipperTipRadius);
        var width = 2 * (half + big);
        return Paint(width, 2 * big, p =>
        {
            // How far inside the paddle's outline (a circle at each end, joined smoothly) it is.
            var t = Math.Clamp((p.X + half) / FlipperLength, 0, 1);
            var along = new Vector2(-half + t * FlipperLength, 0);
            var radius = big + (small - big) * t;
            var inside = radius - Vector2.Distance(p, along);
            inside = MathF.Max(inside, MathF.Max(big - Vector2.Distance(p, new Vector2(-half, 0)), small - Vector2.Distance(p, new Vector2(half, 0))));
            if (inside < 0)
                return Vector4.Zero;
            if (inside < 0.06f)
                return Rgb(0xD8283C);
            if (Vector2.Distance(p, new Vector2(-half, 0)) < 0.08f)
                return Rgb(0x9098A8);
            return Mix(Rgb(0xC8CED8), Rgb(0xFFFFFF), (p.Y + big) / (2 * big));
        });
    }

    // A kicker (for the left: its kicking side faces up and to the right): red, with white rubber on its kicking side.
    private static Canvas KickerArt() => Paint(0.9f, 1.6f, p =>
    {
        var (a, b, c) = (KickerCorners[0], KickerCorners[1], KickerCorners[2]);
        var edges = MathF.Min(MathF.Min(Side(a, b, p), Side(b, c, p)), Side(c, a, p));
        if (edges < 0)
            return Vector4.Zero;
        if (Side(c, a, p) < 0.1f)
            return Rgb(0xF4F0E6);
        return edges < 0.05f ? Rgb(0x7A1424) : Mix(Rgb(0xB8203A), Rgb(0xE8505A), (p.Y + 0.8f) / 1.6f);
    });

    // How far inside the line from a to b (on its left, going round the corners as listed) a point is.
    internal static float Side(Vector2 a, Vector2 b, Vector2 p)
    {
        var along = Vector2.Normalize(b - a);
        return along.X * (p.Y - a.Y) - along.Y * (p.X - a.X);
    }

    // A drop target's face: a bright plate with a dark rim.
    private static Canvas TargetArt() => Paint(0.25f, 0.6f, p =>
    {
        var edge = MathF.Min(0.125f - MathF.Abs(p.X), 0.3f - MathF.Abs(p.Y));
        return edge < 0.03f ? Rgb(0x6A4A08) : Mix(Rgb(0xF0B020), Rgb(0xFFE070), (p.Y + 0.3f) / 0.6f);
    }, 64);

    // A lane's light: a glowing round insert with an arrow.
    private static Canvas LightArt() => Paint(0.5f, 0.5f, p =>
    {
        var r = p.Length();
        if (r > 0.25f)
            return Vector4.Zero;
        var arrow = p.Y < 0.1f && p.Y > -0.1f && MathF.Abs(p.X) < 0.12f - (p.Y + 0.1f) * 0.6f;
        if (arrow)
            return Rgb(0xFFFFFF);
        return r > 0.21f ? Rgb(0x1E6A8C) : Mix(Rgb(0x48D0FF), Rgb(0xC8F4FF), 1 - r / 0.21f);
    }, 64);

    // The playfield: deep blue at the bottom fading to violet at the top, with rings of stars and lines along the lanes.
    private static Canvas PlayfieldArt()
    {
        var c = Paint(10, 18, p =>
        {
            var t = (p.Y + 9) / 18;
            var color = Mix(Rgb(0x10183A), Rgb(0x34185A), t);
            var ring = MathF.Abs(Vector2.Distance(p, new Vector2(-0.7f, 4.2f)) - 3.4f);
            if (ring < 0.05f)
                color = Mix(color, Rgb(0x6A5ACD), 0.6f);
            var lower = MathF.Abs(Vector2.Distance(p, new Vector2(-0.7f, -4.5f)) - 2.2f);
            if (lower < 0.04f && p.Y > -5.6f)
                color = Mix(color, Rgb(0x3A8AC8), 0.5f);
            return color;
        }, 16);
        var random = new Random(11);
        for (var i = 0; i < 90; i++)
        {
            var (x, y) = (random.Next(c.Width), random.Next(c.Height));
            c.Set(x, y, random.Next(3) == 0 ? new Pixel(0xFF, 0xE8, 0x9A, 0xFF) : new Pixel(0xB8, 0xC8, 0xFF, 0xFF));
        }

        return c;
    }
}
