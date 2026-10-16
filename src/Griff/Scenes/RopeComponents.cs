using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A rope from its entity to another (<see cref="To"/>), to a point in the world, or hanging loose: it hangs and sags under
/// gravity, swings, lies over what it falls on and wraps round corners, and holds what's tied to it no further than its
/// <see cref="Length"/>: hang a crate or a lamp from the ceiling, tie a balloon to a box, tow a cart, swing a wrecking ball.
/// Either end can be a body (pulled about by it, and turned if it rotates), something that doesn't move, or the world. A
/// player with Platformer Movement grabs a <see cref="Climbable"/> one with up, climbs it, swings on it and jumps off.
/// </summary>
/// <remarks>
/// It's a chain of short pieces worked out with the bodies that rotate, and drawn as a smooth curve. In the Scene view, drag
/// the knobs at its ends to move them and the one at its middle to make it longer or shorter. Give it a
/// <see cref="BreakForce"/> to have it snap when it's pulled too hard (it then says <see cref="Broke"/> and sends its
/// <see cref="BreakMessage"/>), or <see cref="Cut"/> it from a wire or a script.
/// </remarks>
[Category("Physics")]
[ComponentName("Rope")]
[FormerName("Griff.Rope")]
public sealed class Rope : Component, IDrawable
{
    /// <summary>
    /// What its other end is tied to, by name: the nearest entity called that. Empty ties it to a point in the world (see
    /// <see cref="OtherAnchor"/>), or leaves it hanging free with <see cref="LooseEnd"/>.
    /// </summary>
    [EntityName]
    public string? To { get; set; }

    /// <summary>
    /// Where it's tied on this entity, from its position, in its own units (before its scale, turned with it): (0, 0) is its
    /// middle; (0, 0.5) the top of a crate 1 high.
    /// </summary>
    [FormerName("Offset")]
    public Vector2 Anchor { get; set; }

    /// <summary>
    /// Where its other end is tied on what it's tied to, in that one's own units; tied to nothing, where that end is in the
    /// world (with <see cref="LooseEnd"/>, where it starts).
    /// </summary>
    [FormerName("OtherOffset")]
    public Vector2 OtherAnchor { get; set; }

    /// <summary>
    /// With nothing in <see cref="To"/>, its other end hangs free rather than tied to the world: a rope to climb and swing on,
    /// a bell pull. It starts laid out towards <see cref="OtherAnchor"/> (and straight down from there for any length left
    /// over), and falls and swings from there.
    /// </summary>
    public bool LooseEnd { get; set; }

    /// <summary>
    /// How long it is, in world units: the two ends can come closer (it sags) but no further apart. 0 makes it as long as the
    /// ends are apart as the game starts. It can change while the game runs, to winch something up or let it down.
    /// </summary>
    [Distance]
    public float Length { get; set; }

    /// <summary>
    /// How much it gives, from 0 to 1: 1 (the default) doesn't stretch; lower stretches like elastic, so what hangs on it
    /// bounces (0.5 is a bungee cord, 0 a rubber band).
    /// </summary>
    public float Stiffness { get; set; } = 1;

    /// <summary>How strongly gravity pulls it: 1 hangs and sags; 0 (a top-down game) lies where it's put.</summary>
    public float GravityScale { get; set; } = 1;

    /// <summary>It meets the world: it lies on the ground, drapes over things and wraps round corners. Off, it passes through everything.</summary>
    public bool Collide { get; set; } = true;

    /// <summary>A player with Platformer Movement grabs it with up, climbs it at its Climb Speed, swings on it left and right, and jumps off.</summary>
    public bool Climbable { get; set; } = true;

    /// <summary>
    /// How hard it can be pulled before it snaps, in the units of gravity's pull on a body (a crate of Mass 1 hanging still
    /// pulls 25). 0 never breaks.
    /// </summary>
    [Group("Breaking")]
    public float BreakForce { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it breaks, with the entity as its data, e.g. "RopeSnapped".</summary>
    [Names(NameKind.Message)]
    public string? BreakMessage { get; set; }

    /// <summary>
    /// True once it has broken (pulled harder than its <see cref="BreakForce"/>, or <see cref="Cut"/>): its two halves hang
    /// from their ends. Wire it to a lamp or a gate; untick it to mend it.
    /// </summary>
    [Output]
    public bool Broke { get; set; }

    /// <summary>How hard it's pulled as the last step ended, in the units of <see cref="BreakForce"/>.</summary>
    [DoNotSerialize]
    [Output]
    public float Tension { get; internal set; }

    /// <summary>The rope's color.</summary>
    [Group("Look")]
    public Color Color { get; set; } = Color.FromBytes(0xC8, 0xA9, 0x6E);

    /// <summary>How thick it's drawn, in world units: it meets the world this thick too (at least 0.08).</summary>
    public float Width { get; set; } = 0.08f;

    /// <summary>Breaks it: in the middle, or above the lowest player hanging on it. Its halves hang from their ends.</summary>
    [Action]
    public void Cut()
    {
        if (Broke)
            return;
        Broke = true;
        BrokenAt = -1;
    }

    /// <summary>Joins it up again, as it was before it broke.</summary>
    [Action]
    public void Mend() => Broke = false;

    // What it's tied to, found by name and kept while it's there under that name.
    private Entity? _other;
    private string? _otherName;

    /// <summary>What its other end is tied to (see <see cref="To"/>): null for the world or a loose end, or when nothing has that name.</summary>
    public Entity? OtherEntity
    {
        get
        {
            var entity = Entity;
            var name = To?.Trim();
            if (entity?.Scene is not { } scene || string.IsNullOrEmpty(name))
                return null;
            if (_other is { } kept && kept.Scene == scene && kept.Name == name && ReferenceEquals(_otherName, To))
                return kept;
            (_other, _otherName) = (HingeJoint.Nearest(scene, entity, name), To);
            return _other;
        }
    }

    /// <summary>Ties its other end to <paramref name="other"/> (null: the world), that one even if others share its name.</summary>
    public void TieTo(Entity? other) => (To, _other, _otherName) = (other?.Name, other, other?.Name);

    /// <summary>Where it's tied on its entity, in the world.</summary>
    public Vector2 WorldAnchor => Entity is null ? Anchor : Vector2.Transform(Anchor, Entity.Transform.WorldMatrix);

    /// <summary>
    /// Where its other end is in the world as it's placed: on what it's tied to, or the point in the world (a loose end: where
    /// it starts).
    /// </summary>
    public Vector2 WorldOtherAnchor => !string.IsNullOrWhiteSpace(To) && OtherEntity is { } other ? Vector2.Transform(OtherAnchor, other.Transform.WorldMatrix) : OtherAnchor;

    /// <summary>True when its other end hangs free: <see cref="LooseEnd"/> with nothing in <see cref="To"/>, or tied to something that has gone.</summary>
    public bool EndIsLoose => string.IsNullOrWhiteSpace(To) ? LooseEnd : OtherEntity is not { IsActive: true } other || other == Entity;

    /// <summary>How long it is as placed: its <see cref="Length"/>, or (at 0) as far apart as its ends are.</summary>
    public float PlacedLength => Length > 0 ? Length : Vector2.Distance(WorldAnchor, WorldOtherAnchor);

    // ----- As the game runs (kept by physics, and in the scene's contact memory for rewinds) -----

    // Its points from this end to the other as the last step ended, how fast each goes, how hard each piece (from that point
    // to the next) pulled, and where they were as the step began (for drawing between steps).
    internal Vector2[]? Points;
    internal Vector2[]? Velocities;
    internal float[]? Pulls;
    internal Vector2[]? Was;
    internal long MovedIn;

    // The piece it broke at (from that point to the next), or -1 for where Cut breaks it.
    internal int BrokenAt = -1;

    // How long it has been still, and whether it's asleep (not worked out until something disturbs it).
    internal float StillFor;
    internal bool Asleep;

    // Whether it was broken, and how long it was, as it was last worked out: a sleeping one wakes when they change.
    internal bool SeenBroke;
    internal float SeenLength;

    // The players hanging on it.
    internal readonly List<RopeClimber> Climbers = [];

    /// <summary>How many points it's laid out as while the game runs (0 before), from this end to the other.</summary>
    public int PointCount => Points?.Length ?? 0;

    /// <summary>Where point <paramref name="index"/> (0 at this end, <see cref="PointCount"/> - 1 at the other) is while the game runs.</summary>
    public Vector2 PointAt(int index) => Points is { } points ? points[Math.Clamp(index, 0, points.Length - 1)] : WorldAnchor;

    /// <summary>The players hanging on it now.</summary>
    public IEnumerable<Entity> Hanging => Climbers.Select(c => c.Body.Entity!);

    // Over the world, from this end to the other.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.OverWorld;

    void IDrawable.Draw(Drawing drawing)
    {
        if (!(Width > 0) || Entity is null)
            return;
        Span<Vector2> points = stackalloc Vector2[MaxPoints];
        var count = Shape(points, drawing.Blend);
        if (count < 2)
            return;

        // A smooth curve through the points (a Catmull-Rom spline), each piece a few short straight lines, each running on a
        // little past its ends so the bends have no gaps.
        var width = Width;
        var reach = width / 2;
        var broken = Broke ? BreakIndex() : -1;
        for (var i = 0; i < count - 1; i++)
        {
            if (i == broken)
                continue;
            var p0 = points[i > 0 && i - 1 != broken ? i - 1 : i];
            var (p1, p2) = (points[i], points[i + 1]);
            var p3 = points[i + 2 < count && i + 1 != broken ? i + 2 : i + 1];
            var parts = Vector2.Distance(p1, p2) > 3 * width ? 3 : 1;
            var from = p1;
            for (var part = 1; part <= parts; part++)
            {
                var to = part == parts ? p2 : CatmullRom(p0, p1, p2, p3, (float)part / parts);
                var along = to - from;
                var length = along.Length();
                if (length > 1e-5f)
                {
                    var past = along / length * reach;
                    drawing.Line(from - past, to + past, width, Color);
                }

                from = to;
            }
        }
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var (t2, t3) = (t * t, t * t * t);
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    /// <summary>The most points a rope is laid out as.</summary>
    public const int MaxPoints = 49;

    // About how long each piece is, in world units.
    private const float PieceLength = 0.25f;

    /// <summary>How many points a rope this long is laid out as: a piece about every quarter unit, 4 pieces at least.</summary>
    internal static int PointsFor(float length) => Math.Clamp((int)MathF.Ceiling(length / PieceLength), 4, MaxPoints - 1) + 1;

    /// <summary>
    /// Its shape now into <paramref name="points"/> (at least <see cref="MaxPoints"/> long): as the game has it (drawn between
    /// steps as <paramref name="blend"/> says, its ends where what they're tied to is drawn), or before the game runs, hanging
    /// at rest. How many points.
    /// </summary>
    public int Shape(Span<Vector2> points, StepBlend blend)
    {
        if (Points is not { } laid || Was is not { } was)
        {
            var count = PointsFor(PlacedLength);
            LayOut(points[..count], -Vector2.UnitY);
            return count;
        }

        var n = laid.Length;
        var between = blend.For(MovedIn);
        for (var i = 0; i < n; i++)
            points[i] = between.Between(was[i], laid[i]);
        points[0] = WorldAnchor;
        if (!EndIsLoose)
            points[n - 1] = WorldOtherAnchor;
        return n;
    }

    /// <summary>The piece it's broken at (from that point to the next).</summary>
    internal int BreakIndex()
    {
        var pieces = (Points?.Length ?? PointsFor(PlacedLength)) - 1;
        return BrokenAt >= 0 && BrokenAt < pieces ? BrokenAt : pieces / 2;
    }

    /// <summary>
    /// Lays it out into <paramref name="points"/> as it hangs at rest with gravity <paramref name="down"/> (a unit vector, or
    /// zero to lie straight): from where it's tied here along a catenary to the other end, or (loose) towards where its end
    /// starts and straight down from there.
    /// </summary>
    internal void LayOut(Span<Vector2> points, Vector2 down)
    {
        var (from, to) = (WorldAnchor, WorldOtherAnchor);
        var length = PlacedLength;
        if (EndIsLoose)
            LayLoose(points, from, to, length, down == Vector2.Zero ? -Vector2.UnitY : down);
        else
            Hang(points, from, to, length, down);
    }

    // Straight from `from` towards `to`, then on down for the length left over.
    private static void LayLoose(Span<Vector2> points, Vector2 from, Vector2 to, float length, Vector2 down)
    {
        var reach = Vector2.Distance(from, to);
        var way = reach > 1e-5f ? (to - from) / reach : down;
        var pieces = points.Length - 1;
        for (var i = 0; i <= pieces; i++)
        {
            var along = length * i / pieces;
            points[i] = along <= reach ? from + way * along : from + way * reach + down * (along - reach);
        }
    }

    /// <summary>
    /// Points evenly along a rope <paramref name="length"/> long hanging from <paramref name="from"/> to <paramref name="to"/>
    /// under gravity <paramref name="down"/> (a unit vector): a catenary, or a straight line when it's taut or nothing pulls it.
    /// </summary>
    internal static void Hang(Span<Vector2> points, Vector2 from, Vector2 to, float length, Vector2 down)
    {
        var pieces = points.Length - 1;
        if (pieces < 1)
            return;
        var span = to - from;
        if (down == Vector2.Zero || length <= span.Length() * 1.0001f)
        {
            for (var i = 0; i <= pieces; i++)
                points[i] = Vector2.Lerp(from, to, (float)i / pieces);
            return;
        }

        // In a frame where down is -Y: x across (towards the other end), y up.
        var up = -down;
        var across = new Vector2(up.Y, -up.X);
        var (h, v) = (Vector2.Dot(span, across), Vector2.Dot(span, up));
        if (h < 0)
            (across, h) = (-across, -h);

        if (h < 1e-3f)
        {
            // One end above the other: down from the first to a bottom between them, and up to the other.
            var bottom = Math.Min(0, v) - (length - MathF.Abs(v)) / 2;
            for (var i = 0; i <= pieces; i++)
            {
                var along = length * i / pieces;
                var y = along <= -bottom ? -along : bottom + (along + bottom);
                points[i] = from + up * y + across * (h * along / length);
            }

            points[pieces] = to;
            return;
        }

        // sinh(z) / z = sqrt(L² - v²) / h for z = h / 2a, by Newton's method.
        var ratio = MathF.Sqrt(length * length - v * v) / h;
        var z = ratio < 3 ? MathF.Sqrt(6 * (ratio - 1)) : MathF.Log(2 * ratio) + MathF.Log(MathF.Log(2 * ratio));
        z = Math.Clamp(z, 1e-3f, 40);
        for (var i = 0; i < 30; i++)
        {
            var slope = MathF.Cosh(z) - ratio;
            if (!(MathF.Abs(slope) > 1e-9f))
                break;
            var next = Math.Clamp(z - (MathF.Sinh(z) - ratio * z) / slope, 1e-4f, 40);
            var done = MathF.Abs(next - z) < 1e-6f;
            z = next;
            if (done)
                break;
        }

        var a = h / (2 * z);
        var x0 = h / 2 - a * MathF.Atanh(Math.Clamp(v / length, -0.999999f, 0.999999f));
        var first = MathF.Sinh(-x0 / a);
        var c = -a * MathF.Cosh(-x0 / a);
        for (var i = 0; i <= pieces; i++)
        {
            var x = x0 + a * MathF.Asinh(length * i / pieces / a + first);
            points[i] = from + across * x + up * (a * MathF.Cosh((x - x0) / a) + c);
        }

        points[pieces] = to;
    }
}

/// <summary>A player hanging on a rope: how far along it (from its start) they hold it, and which way they climb and swing this step.</summary>
internal struct RopeClimber
{
    public Body Body;
    public float At;

    // Climbing up it (positive) or down, in world units a second; swinging left (-1) or right (1).
    public float Climb;
    public float Swing;
}
