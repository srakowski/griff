using System.Globalization;
using System.Numerics;

namespace Griff.Modules.Fighting;

/// <summary>What a <see cref="FrameBox"/> does: deal a hit, or take one.</summary>
public enum FrameBoxKind
{
    /// <summary>A hurtbox: where it can be hit.</summary>
    Hurt,

    /// <summary>A hitbox: where it hits.</summary>
    Hit,
}

/// <summary>
/// One box (or circle) on one frame of an animation clip, as a <see cref="Hitboxes"/> holds them: a hitbox that deals
/// <see cref="Damage"/>, <see cref="Knockback"/> and <see cref="Stun"/>, or a hurtbox that takes hits times its
/// <see cref="Multiplier"/>. Its place and size are in the entity's own units, facing right, as its picture is drawn.
/// </summary>
/// <param name="Clip">The clip it's on, e.g. "Punch"; <see cref="OwnFrames"/> for the animation's own frames (or no animation).</param>
/// <param name="Frame">Which of the clip's frames, counting from 0.</param>
/// <param name="Center">Where its middle is, from the entity's middle, facing right.</param>
/// <param name="Size">How wide and high a box is; for a circle, as wide and high as the circle.</param>
/// <param name="Radius">A circle's radius; 0 for a box.</param>
/// <param name="Damage">A hitbox's damage, before the hurtbox it lands on multiplies it.</param>
/// <param name="Knockback">How fast a hitbox sends what it hits flying, in units a second: X away from it, Y up.</param>
/// <param name="Stun">How many steps of the game (frames, at 60 a second) a hitbox leaves what it hits in hitstun.</param>
/// <param name="Group">A hitbox's group: a swing hits something once per group, however many of its frames touch it.</param>
/// <param name="Multiplier">A hurtbox's multiplier for the damage of hits it takes: 1.5 for a head that takes more.</param>
public readonly record struct FrameBox(
    string Clip,
    int Frame,
    FrameBoxKind Kind,
    Vector2 Center,
    Vector2 Size,
    float Radius = 0,
    float Damage = 1,
    Vector2 Knockback = default,
    int Stun = 0,
    int Group = 1,
    float Multiplier = 1)
{
    /// <summary>What a line calls the animation's own frames, played without a clip (and an entity with no animation's one frame).</summary>
    public const string OwnFrames = "*";

    /// <summary>True for a circle (it has a <see cref="Radius"/>); false for a box.</summary>
    public bool IsCircle => Radius > 0;

    public bool IsHit => Kind == FrameBoxKind.Hit;

    /// <summary>A new hurtbox, <paramref name="size"/> big.</summary>
    public static FrameBox Hurtbox(string clip, int frame, Vector2 center, Vector2 size, float multiplier = 1) =>
        new(clip, frame, FrameBoxKind.Hurt, center, size, Multiplier: multiplier);

    /// <summary>A new hitbox, <paramref name="size"/> big.</summary>
    public static FrameBox Hitbox(string clip, int frame, Vector2 center, Vector2 size, float damage = 1, Vector2 knockback = default, int stun = 0, int group = 1) =>
        new(clip, frame, FrameBoxKind.Hit, center, size, 0, damage, knockback, stun, group);

    /// <summary>The same box as a circle of <paramref name="radius"/>.</summary>
    public FrameBox AsCircle(float radius) => this with { Radius = radius, Size = new Vector2(radius * 2) };

    /// <summary>
    /// The line <see cref="Hitboxes.Boxes"/> holds it as, e.g. <c>Punch 2 hit at 0.6,0.3 size 0.5x0.25 damage 3 knockback 4,2 stun 12</c>
    /// or <c>Idle 0 hurt at 0,0.7 radius 0.3 multiplier 1.5</c>: what <see cref="TryParse"/> reads back. Values left as they
    /// are by default (damage 1, group 1, multiplier 1, no knockback or stun) are left out.
    /// </summary>
    public override string ToString()
    {
        var text = $"{Clip} {Frame} {(IsHit ? "hit" : "hurt")} at {Number(Center.X)},{Number(Center.Y)}";
        text += IsCircle ? $" radius {Number(Radius)}" : $" size {Number(Size.X)}x{Number(Size.Y)}";
        if (IsHit)
        {
            if (Damage != 1)
                text += $" damage {Number(Damage)}";
            if (Knockback != Vector2.Zero)
                text += $" knockback {Number(Knockback.X)},{Number(Knockback.Y)}";
            if (Stun != 0)
                text += $" stun {Stun}";
            if (Group != 1)
                text += $" group {Group}";
        }
        else if (Multiplier != 1)
            text += $" multiplier {Number(Multiplier)}";
        return text;
    }

    /// <summary>
    /// Reads a line written like <see cref="ToString"/>: the clip, the frame, <c>hit</c> or <c>hurt</c>, then <c>at X,Y</c> and
    /// <c>size WxH</c> (or <c>radius R</c>), and for a hitbox any of <c>damage</c>, <c>knockback X,Y</c>, <c>stun</c> and
    /// <c>group</c>, for a hurtbox <c>multiplier</c>. False (with why in <paramref name="problem"/>) for a line that doesn't make sense.
    /// </summary>
    public static bool TryParse(string? line, out FrameBox box, out string? problem)
    {
        box = default;
        var words = (line ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            problem = "it's empty";
            return false;
        }

        if (words.Length < 3 || !int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) || frame < 0)
        {
            problem = "it starts with a clip, a frame counting from 0 and hit or hurt, e.g. \"Punch 2 hit\"";
            return false;
        }

        FrameBoxKind kind;
        if (words[2].Equals("hit", StringComparison.OrdinalIgnoreCase) || words[2].Equals("hitbox", StringComparison.OrdinalIgnoreCase))
            kind = FrameBoxKind.Hit;
        else if (words[2].Equals("hurt", StringComparison.OrdinalIgnoreCase) || words[2].Equals("hurtbox", StringComparison.OrdinalIgnoreCase))
            kind = FrameBoxKind.Hurt;
        else
        {
            problem = $"\"{words[2]}\" isn't hit or hurt";
            return false;
        }

        box = new FrameBox(words[0], frame, kind, Vector2.Zero, Vector2.One);
        var sized = false;
        for (var i = 3; i < words.Length; i++)
        {
            var word = words[i].ToLowerInvariant();
            if (i + 1 >= words.Length)
            {
                problem = $"\"{words[i]}\" needs a value after it";
                return false;
            }

            var value = words[++i];
            var ok = word switch
            {
                "at" => TryVector(value, out var at) && Set(ref box, box with { Center = at }),
                "size" => TryVector(value, out var size) && size.X > 0 && size.Y > 0 && Set(ref box, box with { Size = size, Radius = 0 }) && (sized = true),
                "radius" => TryNumber(value, out var radius) && radius > 0 && Set(ref box, box.AsCircle(radius)) && (sized = true),
                "damage" => TryNumber(value, out var damage) && Set(ref box, box with { Damage = damage }),
                "knockback" => TryVector(value, out var knockback) && Set(ref box, box with { Knockback = knockback }),
                "stun" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stun) && stun >= 0 && Set(ref box, box with { Stun = stun }),
                "group" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) && Set(ref box, box with { Group = group }),
                "multiplier" => TryNumber(value, out var multiplier) && multiplier >= 0 && Set(ref box, box with { Multiplier = multiplier }),
                _ => false,
            };
            if (!ok)
            {
                problem = $"\"{words[i - 1]} {value}\" doesn't make sense";
                return false;
            }
        }

        if (!sized)
        {
            problem = "it needs a size (size 1x2) or a radius (radius 0.5)";
            return false;
        }

        problem = null;
        return true;

        static bool Set(ref FrameBox box, FrameBox value)
        {
            box = value;
            return true;
        }
    }

    /// <summary>Reads lines written like <see cref="ToString"/>, skipping those that don't make sense.</summary>
    public static IReadOnlyList<FrameBox> ParseAll(IEnumerable<string>? lines)
    {
        var boxes = new List<FrameBox>();
        foreach (var line in lines ?? [])
        {
            if (TryParse(line, out var box, out _))
                boxes.Add(box);
        }

        return boxes;
    }

    /// <summary>Writes boxes as <see cref="Hitboxes.Boxes"/> holds them, a line each.</summary>
    public static IReadOnlyList<string> FormatAll(IEnumerable<FrameBox> boxes) => [.. boxes.Select(b => b.ToString())];

    /// <summary>A number as lines write it: up to three decimals, with a point whatever the language.</summary>
    public static string Number(float value) => MathF.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    // "1,2", "1x2" or "1;2".
    private static bool TryVector(string text, out Vector2 value)
    {
        value = default;
        var parts = text.Split([',', 'x', 'X', ';'], StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryNumber(parts[0], out var x) || !TryNumber(parts[1], out var y))
            return false;
        value = new Vector2(x, y);
        return true;
    }
}
