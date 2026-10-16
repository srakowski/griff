using System.Numerics;
using static Griff.Templates.TemplateArt;

namespace Griff.Modules.Rhythm;

/// <summary>
/// The Rhythm template's pictures, painted in code: a city at night behind everything (a striped sun setting behind its
/// towers, a neon grid running to the horizon, stars), a speaker that bops to the beat beside the lanes, and the glowing
/// frame round the song picked out on the song select.
/// </summary>
internal static class RhythmArt
{
    public const string Stage = "Textures/stage.png";
    public const string Speaker = "Textures/speaker.png";
    public const string Frame = "Textures/frame.png";

    /// <summary>How big the stage is in world units: a 16:9 view 11.25 high.</summary>
    public const float StageWidth = 20, StageHeight = 11.25f;

    /// <summary>How big a speaker is, and the frame round a song's card.</summary>
    public static readonly Vector2 SpeakerSize = new(2.2f, 3.1f), FrameSize = new(7.7f, 1.75f);

    // Pixels to a world unit.
    private const int PerUnit = 16;

    /// <summary>Every picture, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files() => new()
    {
        [Stage] = StageArt().Png(),
        [Speaker] = SpeakerArt().Png(),
        [Frame] = FrameArt().Png(),
    };

    // Where the ground meets the sky.
    private const float Horizon = -1.6f;

    // Night falling from violet at the horizon to ink at the top, a sun striped where it sinks, towers against it with a few
    // lit windows, and a grid of neon lines on the ground running away to the horizon.
    private static Canvas StageArt()
    {
        var c = Paint(StageWidth, StageHeight, p =>
        {
            if (p.Y < Horizon)
                return Ground(p);
            var up = (p.Y - Horizon) / (StageHeight / 2 - Horizon);
            var sky = Mix(Rgb(0x6A1E78), Rgb(0x0A0820), MathF.Pow(up, 0.6f));

            // The sun, yellow above and pink below, the stripes across it widening as it sinks.
            var sun = new Vector2(0, 0.9f);
            var d = Vector2.Distance(p, sun);
            if (d < 2.9f)
            {
                var t = Math.Clamp((sun.Y + 2.9f - p.Y) / 5.8f, 0, 1);
                var stripe = p.Y < sun.Y && ((sun.Y - p.Y) % 0.55f) < 0.08f + (sun.Y - p.Y) * 0.06f;
                if (!stripe)
                    sky = Mix(Rgb(0xFFD84A), Rgb(0xFF3E8E), t);
            }
            else
            {
                // A soft glow round it.
                sky = Mix(sky, Rgb(0xFF4FA3), Math.Clamp(1 - (d - 2.9f) / 3, 0, 1) * 0.25f);
            }

            // The towers, each its own height, in front of the sun.
            var tower = Tower(p.X);
            if (p.Y < Horizon + tower.Height)
            {
                var window = ((int)MathF.Floor((p.X + 10) * 4) % 3 == 1) && ((int)MathF.Floor((p.Y - Horizon) * 4) % 3 == 1);
                return window && tower.Lit && Hash(MathF.Floor((p.X + 10) * 4), MathF.Floor(p.Y * 4)) > 0.55f
                    ? Rgb(0xFFE6A0)
                    : Mix(Rgb(0x140A2A), Rgb(0x24104A), (p.Y - Horizon) / Math.Max(0.1f, tower.Height));
            }

            return sky;
        }, PerUnit);

        // Stars above the towers.
        var random = new Random(31);
        for (var i = 0; i < 90; i++)
        {
            var (x, y) = (random.Next(c.Width), random.Next((int)(c.Height * 0.4f)));
            if (c.Get(x, y).R < 0x40)
                c.Set(x, y, random.Next(4) == 0 ? new Pixel(0xFF, 0xD8, 0xF0, 0xFF) : new Pixel(0xC8, 0xC8, 0xFF, 0xFF));
        }

        return c;
    }

    // The ground: dark, with magenta lines across it closer together toward the horizon, and lines running to the middle of it.
    private static Vector4 Ground(Vector2 p)
    {
        var below = Horizon - p.Y;
        var ground = Mix(Rgb(0x2A0E40), Rgb(0x0C0618), below / 4);
        // Across: evenly spaced in depth, so bunched up far off.
        var depth = 1 / Math.Max(0.05f, below);
        var across = MathF.Abs(depth * 1.2f % 1 - 0.5f) > 0.5f - 0.06f * depth * 0.6f;
        // Along: lines through the vanishing point, evenly spaced where they reach the bottom.
        var along = p.X / Math.Max(0.05f, below) * 0.9f;
        var line = MathF.Abs(along - MathF.Round(along)) < 0.04f * (1 + 0.5f / Math.Max(0.2f, below));
        var glow = Math.Clamp(1 - below / 4.5f, 0.25f, 1);
        return across || line ? Mix(ground, Rgb(0xFF4FD0), 0.75f * glow) : ground;
    }

    // The tower standing at `x`: how high it is, and whether its windows are lit.
    private static (float Height, bool Lit) Tower(float x)
    {
        var slot = MathF.Floor((x + 10) / 0.9f);
        var tall = 0.5f + Hash(slot, 3) * 2.2f + (MathF.Abs(x) > 4 ? 0.6f : 0);
        return (tall, Hash(slot, 7) > 0.35f);
    }

    private static float Hash(float a, float b)
    {
        var h = MathF.Sin(a * 12.9898f + b * 78.233f) * 43758.547f;
        return h - MathF.Floor(h);
    }

    // A speaker: a dark cabinet with a lighter rim, a big cone below and a small one above, and a pink light.
    private static Canvas SpeakerArt() => Paint(SpeakerSize.X, SpeakerSize.Y, p =>
    {
        var half = SpeakerSize / 2;
        var edge = MathF.Min(half.X - MathF.Abs(p.X), half.Y - MathF.Abs(p.Y));
        if (edge < 0)
            return Vector4.Zero;
        var cabinet = edge < 0.08f ? Rgb(0x4A3A6A) : Mix(Rgb(0x1E1830), Rgb(0x141020), (half.Y - p.Y) / SpeakerSize.Y);
        cabinet = Cone(cabinet, p, new Vector2(0, -0.55f), 0.78f);
        cabinet = Cone(cabinet, p, new Vector2(0, 0.85f), 0.42f);
        if (Vector2.Distance(p, new Vector2(0.75f, 1.32f)) < 0.07f)
            cabinet = Rgb(0xFF4FA3);
        return cabinet;
    }, PerUnit * 2);

    private static Vector4 Cone(Vector4 under, Vector2 p, Vector2 at, float radius)
    {
        var d = Vector2.Distance(p, at) / radius;
        return d switch
        {
            > 1 => under,
            > 0.88f => Rgb(0x5A4A80),
            > 0.3f => Mix(Rgb(0x2A2440), Rgb(0x0E0A18), (d - 0.3f) / 0.6f),
            _ => Mix(Rgb(0x6A5A90), Rgb(0x2A2440), d / 0.3f),
        };
    }

    // The frame round the song picked out: a bright rim fading out into a glow on both sides of it.
    private static Canvas FrameArt() => Paint(FrameSize.X, FrameSize.Y, p =>
    {
        var half = FrameSize / 2 - new Vector2(0.18f);
        var outside = new Vector2(MathF.Abs(p.X) - half.X, MathF.Abs(p.Y) - half.Y);
        var distance = MathF.Abs(MathF.Max(outside.X, outside.Y));
        var rim = distance < 0.035f ? 1 : MathF.Max(0, 1 - (distance - 0.035f) / 0.15f) * 0.55f;
        return rim <= 0 ? Vector4.Zero : Rgb(0x7FF0FF, rim);
    }, PerUnit * 2);

    // ----- Painting: each pixel worked out from a shape in world units, edges softened -----

    // A picture `width` by `height` world units, `perUnit` pixels to one: `paint` gives each point's color (its alpha 0
    // outside), sampled four times a pixel so edges are smooth.
    private static Canvas Paint(float width, float height, Func<Vector2, Vector4> paint, int perUnit)
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
                c.Set(x, y, new Pixel(Pixel.Clamp(sum.X / sum.W * 255), Pixel.Clamp(sum.Y / sum.W * 255), Pixel.Clamp(sum.Z / sum.W * 255), Pixel.Clamp(sum.W / 4 * 255)));
            }
        }

        return c;
    }

    private static Vector4 Rgb(int hex, float alpha = 1) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, alpha);

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, Math.Clamp(t, 0, 1));
}
