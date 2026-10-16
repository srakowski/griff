using System.Numerics;
using static Griff.Modules.PhysicsGames.PinballArt;
using static Griff.Templates.TemplateArt;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// The Pinball template's own pictures, made in code, besides the pinball pieces' (<see cref="PinballArt"/>): its
/// playfield (a night carnival in navy and violet, with a ring of bulbs round the bumpers and arrows to the orbit and the
/// skyway), a round insert to light (the lanes' letters and the multiplier), a brass bell, and the midway at night behind
/// the table and the title.
/// </summary>
internal static class MidwayArt
{
    public const string Playfield = "Textures/midway-playfield.png";
    public const string Insert = "Textures/insert.png";
    public const string Bell = "Textures/bell.png";
    public const string Backdrop = "Textures/midway.png";

    /// <summary>Every picture, by its path in Assets.</summary>
    public static Dictionary<string, byte[]> Files() => new()
    {
        [Playfield] = PlayfieldArt().Png(),
        [Insert] = InsertArt().Png(),
        [Bell] = BellArt().Png(),
        [Backdrop] = BackdropArt().Png(),
    };

    // The ring of bulbs round the bumpers: its middle and radius.
    private static readonly Vector2 RingAt = new(-0.15f, 5.0f);
    private const float RingRadius = 2.3f;

    // The playfield, 10 by 18: navy at the drain fading to violet at the top, faint tent stripes fanning out from the
    // flippers, a ring of gold bulbs round the bumpers, chevrons pointing up the orbit (left) and into the skyway (right), and
    // stars.
    private static Canvas PlayfieldArt()
    {
        var c = Paint(10, 18, p =>
        {
            var t = (p.Y + 9) / 18;
            var color = Mix(Rgb(0x0C1230), Rgb(0x3A1660), t);

            // Stripes fanning out from below the flippers, every other one a little lighter.
            var from = p - new Vector2(-0.7f, -9.5f);
            var angle = MathF.Atan2(from.X, from.Y);
            if (((int)MathF.Floor(angle * 9 / MathF.PI + 20) & 1) == 0)
                color = Shade(color, 1.18f);

            // A ring of bulbs round the bumpers.
            var ring = Vector2.Distance(p, RingAt);
            if (MathF.Abs(ring - RingRadius) < 0.05f)
                color = Mix(color, Rgb(0xB08A30), 0.7f);
            var bulbAngle = MathF.Atan2(p.Y - RingAt.Y, p.X - RingAt.X);
            var bulb = RingAt + RingRadius * new Vector2(MathF.Cos(MathF.Round(bulbAngle * 16 / MathF.PI) * MathF.PI / 16), MathF.Sin(MathF.Round(bulbAngle * 16 / MathF.PI) * MathF.PI / 16));
            if (Vector2.Distance(p, bulb) < 0.11f)
                color = Rgb(0xFFE08A);

            // Chevrons up the orbit and into the skyway, and a row of them over the flippers.
            color = Chevron(color, p, new Vector2(-4.5f, 2.0f), Vector2.UnitY, Rgb(0x48D0FF));
            color = Chevron(color, p, new Vector2(-4.5f, 2.7f), Vector2.UnitY, Rgb(0x48D0FF));
            color = Chevron(color, p, new Vector2(2.2f, -0.2f), Vector2.Normalize(new Vector2(0.6f, 1)), Rgb(0xFF6AD5));
            color = Chevron(color, p, new Vector2(1.85f, -0.75f), Vector2.Normalize(new Vector2(0.6f, 1)), Rgb(0xFF6AD5));

            // A soft glow under the multiplier's inserts and the shoot-again light.
            var glow = MathF.Max(0, 1 - Vector2.Distance(p, new Vector2(-0.7f, -4.7f)) / 2.4f);
            return Mix(color, Rgb(0x6A3A9A), glow * 0.35f);
        }, 16);
        var random = new Random(23);
        for (var i = 0; i < 120; i++)
        {
            var (x, y) = (random.Next(c.Width), random.Next(c.Height));
            c.Set(x, y, random.Next(3) == 0 ? new Pixel(0xFF, 0xE8, 0x9A, 0xFF) : new Pixel(0xC8, 0xC0, 0xFF, 0xFF));
        }

        return c;
    }

    // A chevron of `color` at `at`, pointing `way`, over `under`.
    private static Vector4 Chevron(Vector4 under, Vector2 p, Vector2 at, Vector2 way, Vector4 color)
    {
        var across = new Vector2(way.Y, -way.X);
        var local = p - at;
        var (along, side) = (Vector2.Dot(local, way), Vector2.Dot(local, across));
        if (MathF.Abs(side) > 0.35f)
            return under;
        var line = along + MathF.Abs(side) * 0.8f;
        return line is > -0.08f and < 0.08f ? Mix(under, color, 0.85f) : under;
    }

    // A round insert to light: white glass with a rim (tinted by whatever lights it), 0.5 across.
    private static Canvas InsertArt() => Paint(0.5f, 0.5f, p =>
    {
        var r = p.Length();
        if (r > 0.25f)
            return Vector4.Zero;
        if (r > 0.215f)
            return Rgb(0x8A8A9A);
        var shine = MathF.Max(0, 1 - Vector2.Distance(p, new Vector2(-0.07f, 0.08f)) / 0.12f);
        return Mix(Mix(Rgb(0xD8D8E0), Rgb(0xFFFFFF), 1 - r / 0.215f), Rgb(0xFFFFFF), shine);
    }, 64);

    // A brass bell, 0.7 across, its mouth down and a clapper showing under it.
    private static Canvas BellArt() => Paint(0.7f, 0.7f, p =>
    {
        // Its outline: a dome on top, flaring out to the lip at the bottom.
        var y = p.Y;
        var halfWidth = y > 0.05f ? MathF.Sqrt(MathF.Max(0, 0.22f * 0.22f - (y - 0.05f) * (y - 0.05f) * 1.6f)) : 0.22f + (0.05f - y) * 0.45f;
        if (y < -0.25f && y > -0.33f && MathF.Abs(p.X) < 0.07f)
            return Rgb(0x6A4A10);
        if (y < -0.25f || y > 0.33f || MathF.Abs(p.X) > halfWidth)
            return Vector4.Zero;
        if (y > 0.28f && MathF.Abs(p.X) < 0.05f)
            return Rgb(0x8A6A20);
        var light = Math.Clamp(0.6f - p.X * 2.2f + y * 0.6f, 0, 1);
        var brass = Mix(Rgb(0x9A6A10), Rgb(0xFFE07A), light);
        return y < -0.2f ? Shade(brass, 0.75f) : brass;
    });

    // The midway at night, 40 by 22: a violet sky with stars, a ferris wheel lit with bulbs, striped tents and a string of
    // bulbs along the top.
    private static Canvas BackdropArt()
    {
        var c = Paint(40, 22, p =>
        {
            var t = (p.Y + 11) / 22;
            var color = Mix(Rgb(0x2A1240), Rgb(0x080A1E), t);

            // The ferris wheel on the right: its rim and spokes, with bulbs round it.
            var wheel = new Vector2(13, -1);
            var d = Vector2.Distance(p, wheel);
            var a = MathF.Atan2(p.Y - wheel.Y, p.X - wheel.X);
            var spoke = MathF.Abs(MathF.Sin(a * 6)) * d;
            if (MathF.Abs(d - 6.5f) < 0.12f || (d < 6.5f && spoke < 0.06f))
                color = Rgb(0x3A2A5A);
            var bulb = wheel + 6.5f * new Vector2(MathF.Cos(MathF.Round(a * 12 / MathF.PI) * MathF.PI / 12), MathF.Sin(MathF.Round(a * 12 / MathF.PI) * MathF.PI / 12));
            if (Vector2.Distance(p, bulb) < 0.18f)
                color = Rgb(0xFFD86A);
            if (p.Y < wheel.Y && MathF.Abs(MathF.Abs(p.X - wheel.X) - (wheel.Y - p.Y) * 0.35f) < 0.12f)
                color = Rgb(0x3A2A5A);

            // Tents on the left and middle: striped red and cream cones over the ground.
            foreach (var (x, width, height) in new[] { (-13f, 7f, 7f), (-4f, 5f, 5f), (4f, 4f, 4f) })
            {
                var top = -11 + height + 1.5f;
                var across = MathF.Abs(p.X - x);
                if (p.Y < -11 + 1.5f + 0.01f || across > width / 2 || p.Y > top - across * (height / (width / 2)) * 0.6f)
                    continue;
                var stripe = ((int)MathF.Floor((p.X - x) * 2.2f / (1 + (top - p.Y) * 0.1f) + 50) & 1) == 0;
                color = Shade(stripe ? Rgb(0xA8283C) : Rgb(0xE8D8B0), 0.55f + 0.1f * (p.Y + 11) / height);
            }

            // The ground.
            if (p.Y < -9.5f)
                color = Rgb(0x120A1A);

            // A string of bulbs sagging across the top.
            var sag = 9 - 0.6f * MathF.Cos((p.X % 8 + 8) % 8 / 8 * 2 * MathF.PI);
            if (MathF.Abs(p.Y - sag) < 0.04f)
                color = Rgb(0x2A2A3A);
            var bulbX = MathF.Round(p.X);
            var bulbY = 9 - 0.6f * MathF.Cos((bulbX % 8 + 8) % 8 / 8 * 2 * MathF.PI) - 0.15f;
            if (Vector2.Distance(p, new Vector2(bulbX, bulbY)) < 0.13f)
                color = ((int)bulbX & 1) == 0 ? Rgb(0xFF7A9A) : Rgb(0x8AE0FF);
            return color;
        }, 8);
        var random = new Random(5);
        for (var i = 0; i < 160; i++)
        {
            var (x, y) = (random.Next(c.Width), random.Next(c.Height / 2));
            if (c.Get(x, y).R < 0x30)
                c.Set(x, y, random.Next(4) == 0 ? new Pixel(0xFF, 0xF0, 0xC0, 0xFF) : new Pixel(0xA8, 0xB0, 0xE8, 0xFF));
        }

        return c;
    }
}
