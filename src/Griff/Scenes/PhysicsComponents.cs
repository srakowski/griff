using System.Numerics;
using System.Runtime.CompilerServices;

namespace Griff.Scenes;

/// <summary>
/// A shape for collisions: a <see cref="BoxCollider"/>, a <see cref="CircleCollider"/> or a <see cref="PolygonCollider"/>.
/// Without a <see cref="Body"/> the shape is static: walls, floors, platforms. Use <c>GetComponent&lt;Collider&gt;()</c> to
/// find any kind.
/// </summary>
public abstract class Collider : Component
{
    /// <summary>How far the shape's center is from the entity, before the transform's scale.</summary>
    public abstract Vector2 Offset { get; set; }

    /// <summary>Triggers don't block movement; they report OnTriggerEnter/Exit instead.</summary>
    public abstract bool IsTrigger { get; set; }

    /// <summary>
    /// A name for what kind of thing this is, e.g. "Player", "Enemy" or "Bullet", for <see cref="IgnoreLayers"/> and physics
    /// queries. Empty is the "Default" layer.
    /// </summary>
    [Names(NameKind.Layer, Defines = true)]
    public abstract string? Layer { get; set; }

    /// <summary>
    /// Layers this collider passes through, e.g. "Bullet, Enemy": no blocking, no collisions, no triggers. It works both
    /// ways, so only one side has to list the other.
    /// </summary>
    [Names(NameKind.Layer, Many = true)]
    public abstract string? IgnoreLayers { get; set; }

    /// <summary>
    /// The box around the shape in the world, from its entity's position and scale; for a shape that
    /// <see cref="Turns"/>, the box round it as it's turned.
    /// </summary>
    [Area]
    public abstract Box WorldBounds { get; }

    /// <summary>
    /// True when the shape turns with its entity: its <see cref="Body"/> (the entity's own, or else the nearest one it's
    /// inside) <see cref="Body.Rotates"/>. Otherwise it stays upright whatever the entity's rotation, as colliders always have.
    /// </summary>
    public bool Turns
    {
        get
        {
            for (var entity = Entity; entity is not null; entity = entity.Parent)
            {
                if (entity.GetComponent<Body>() is { } body)
                    return body.Rotates;
            }

            return false;
        }
    }

    /// <summary>True if <paramref name="point"/> (in the world) is inside the shape, turned if it <see cref="Turns"/>.</summary>
    public abstract bool Contains(Vector2 point);

    // The step it's held out of the world for (see HoldOut).
    internal long HeldOutIn = -1;

    /// <summary>
    /// Holds it out of the world for the step <paramref name="step"/> (its scene's <see cref="Scene.Step"/>): a slingshot's
    /// shot waiting its turn or in the pouch. That step nothing bumps into it and it bumps into nothing; hold it again each
    /// step it's held.
    /// </summary>
    public void HoldOut(long step) => HeldOutIn = step;

    // Whether a world matrix turns things (so a shape that Turns needs turning).
    private protected static bool IsTurning(in Matrix4x4 world) => world.M12 != 0 || world.M21 != 0;
}

/// <summary>Matching collision layer names: case doesn't matter, and an empty layer is called "Default".</summary>
public static class CollisionLayers
{
    public const string Default = "Default";

    /// <summary>The layer's name, with empty meaning <see cref="Default"/>.</summary>
    public static string NameOf(string? layer) => string.IsNullOrWhiteSpace(layer) ? Default : layer.Trim();

    /// <summary>Reads a list of layer names like "Bullet, Enemy" (commas or semicolons between them).</summary>
    /// <remarks>Read once for the same string (see <see cref="Formats.Names"/>), shared: don't change the array.</remarks>
    public static string[] Parse(string? list) => Formats.Names(list);

    /// <summary>True if <paramref name="layer"/> is one of <paramref name="layers"/>.</summary>
    public static bool Contains(IEnumerable<string> layers, string? layer)
    {
        // Physics asks of every pair of things near each other, each step: an array is gone through without garbage.
        if (layers is not string[] array)
            return AnyIs(layers, NameOf(layer));
        var name = NameOf(layer);
        foreach (var l in array)
        {
            if (string.Equals(l, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool AnyIs(IEnumerable<string> layers, string name) =>
        layers.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// How a body gives way to what runs into it (see <see cref="Body.GivesWay"/>): to things on its <see cref="Layers"/> (all,
/// when there are none), at <see cref="Speed"/> times the speed they push it (0 to 1).
/// </summary>
internal readonly record struct Shove(string? Layers, float Speed)
{
    /// <summary>True if something on collision layer <paramref name="layer"/> can push it.</summary>
    public bool Admits(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }
}

/// <summary>
/// A box for collisions, centered on the entity (plus <see cref="Offset"/>) and scaled by its transform. It stays upright
/// whatever the entity's rotation, unless its <see cref="Body"/> <see cref="Body.Rotates"/>: then it turns with the entity.
/// Without a <see cref="Body"/> the box is static: walls, floors, platforms.
/// </summary>
[Category("Physics")]
[WorksWith(typeof(Body), Note = "to make it move and fall")]
[ComponentName("BoxCollider")]
public sealed class BoxCollider : Collider
{
    /// <summary>Width and height, in world units before the transform's scale.</summary>
    public Vector2 Size { get; set; } = Vector2.One;

    public override Vector2 Offset { get; set; }

    /// <inheritdoc/>
    public override bool IsTrigger { get; set; }

    /// <summary>Only blocks bodies landing on top: jump up through it from below, stand on it from above.</summary>
    public bool OneWay { get; set; }

    /// <inheritdoc/>
    public override string? Layer { get; set; }

    /// <inheritdoc/>
    public override string? IgnoreLayers { get; set; }

    /// <summary>
    /// The box it covers in the world, from its entity's position and scale. One that <see cref="Collider.Turns"/> is
    /// the box round it as it's turned (see <see cref="WorldShape"/> for the turned box itself).
    /// </summary>
    public Box WorldBox
    {
        get
        {
            var (center, half, axis) = WorldShape;
            if (axis == Vector2.UnitX)
                return new Box(center, half);
            var (x, y) = (MathF.Abs(axis.X), MathF.Abs(axis.Y));
            return new Box(center, new Vector2(x * half.X + y * half.Y, y * half.X + x * half.Y));
        }
    }

    /// <summary>
    /// The box in the world as it is: its middle, half its width and height, and which way its own X axis points (a unit
    /// vector, (1, 0) unless it <see cref="Collider.Turns"/>; its Y axis is that turned a quarter anticlockwise).
    /// </summary>
    public (Vector2 Center, Vector2 HalfSize, Vector2 Axis) WorldShape
    {
        get
        {
            var world = Entity!.Transform.WorldMatrix;
            var across = new Vector2(world.M11, world.M12);
            var scale = new Vector2(across.Length(), new Vector2(world.M21, world.M22).Length());
            if (!IsTurning(world) || scale.X <= 0 || !Turns)
                return (new Vector2(world.M41, world.M42) + Offset * scale, Size * scale / 2, Vector2.UnitX);
            return (Vector2.Transform(Offset, world), Vector2.Abs(Size) * scale / 2, across / scale.X);
        }
    }

    /// <summary>
    /// Its four corners in the world, anticlockwise from the bottom left (as it's turned, if it
    /// <see cref="Collider.Turns"/>).
    /// </summary>
    public (Vector2 A, Vector2 B, Vector2 C, Vector2 D) WorldCorners
    {
        get
        {
            var (center, half, axis) = WorldShape;
            var (x, y) = (axis * half.X, new Vector2(-axis.Y, axis.X) * half.Y);
            return (center - x - y, center + x - y, center + x + y, center - x + y);
        }
    }

    /// <inheritdoc/>
    public override Box WorldBounds => WorldBox;

    /// <inheritdoc/>
    public override bool Contains(Vector2 point)
    {
        var (center, half, axis) = WorldShape;
        var offset = point - center;
        var local = new Vector2(Vector2.Dot(offset, axis), axis.X * offset.Y - axis.Y * offset.X);
        return MathF.Abs(local.X) <= MathF.Abs(half.X) && MathF.Abs(local.Y) <= MathF.Abs(half.Y);
    }
}

/// <summary>
/// A circle for collisions, centered on the entity (plus <see cref="Offset"/>). Its radius grows with the larger of the
/// transform's X and Y scale, so it stays round. Good for balls, coins, bullets and round enemies.
/// </summary>
/// <remarks>
/// Circle bodies don't roll unless their <see cref="Body"/> <see cref="Body.Rotates"/>: one resting on a corner stays
/// balanced there. One that rotates rolls: along the ground, down slopes and over tile floors without slipping, slowed by
/// its <see cref="Body.RollingResistance"/> until it rests, with its spin carried into its bounces and its sprite turning
/// as it goes.
/// </remarks>
[Category("Physics")]
[WorksWith(typeof(Body), Note = "to make it move and fall")]
[ComponentName("CircleCollider")]
public sealed class CircleCollider : Collider
{
    /// <summary>World units before the transform's scale. 0.5 fits a 1×1 sprite.</summary>
    public float Radius { get; set; } = 0.5f;

    public override Vector2 Offset { get; set; }

    /// <inheritdoc/>
    public override bool IsTrigger { get; set; }

    /// <inheritdoc/>
    public override string? Layer { get; set; }

    /// <inheritdoc/>
    public override string? IgnoreLayers { get; set; }

    /// <summary>The circle it covers in the world. Its radius scales with the larger of the entity's X and Y scale.</summary>
    public Circle WorldCircle
    {
        get
        {
            var world = Entity!.Transform.WorldMatrix;
            var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
            // Turning with its entity, its offset turns too.
            var center = Offset != Vector2.Zero && IsTurning(world) && Turns
                ? Vector2.Transform(Offset, world)
                : new Vector2(world.M41, world.M42) + Offset * scale;
            return new Circle(center, Radius * Math.Max(scale.X, scale.Y));
        }
    }

    /// <inheritdoc/>
    public override Box WorldBounds => WorldCircle.Bounds;

    /// <inheritdoc/>
    public override bool Contains(Vector2 point) => WorldCircle.Contains(point);
}

/// <summary>
/// A shape for collisions with straight sides between its <see cref="Points"/>: a ramp, a wedge, a rock, a hill, a
/// boat's hull, a cup. Select it in the Scene view to drag its points, drag a plus on a side to add one, double-click one
/// to take it off (Ctrl snaps to half units); Entity &gt; Fit Polygon Collider to Sprite traces its sprite's outline. A
/// concave shape (an L, a cup) is split into convex pieces by itself. Points that don't make a proper shape (fewer than
/// three, all in a line, sides crossing) are fixed up and said so above its fields. Like a box, it stays upright whatever
/// the entity's rotation unless its <see cref="Body"/> <see cref="Body.Rotates"/>: then it turns with the entity, round
/// its middle, and tips, rolls over and stacks as it should.
/// </summary>
[Category("Physics")]
[WorksWith(typeof(Body), Note = "to make it move and fall")]
[ComponentName("PolygonCollider")]
public sealed class PolygonCollider : Collider, IPointOutline
{
    /// <summary>A new polygon's points: a triangle 1 across.</summary>
    public const string DefaultPoints = "-0.5 -0.5; 0.5 -0.5; 0 0.5";

    private string? _points = DefaultPoints;
    private List<Vector2> _given = PolygonParts.Parse(DefaultPoints);
    private PolygonParts _parts = PolygonParts.Of(PolygonParts.Parse(DefaultPoints));

    // Where it is in the world, worked out again when it moves, turns or changes.
    private Vector2[] _worldOutline = [];
    private Vector2[] _worldCorners = [];
    private int[] _pieceStart = [0];
    private Box _worldBounds;
    private Vector2 _worldCentroid;
    private bool _mirrored;
    private (Matrix4x4 World, Vector2 Offset, bool Turned, PolygonParts? Parts) _placed;

    // How it's built, for its size (worked out again when that changes).
    private (Vector2 Scale, PolygonParts? Parts) _sized;
    private float _inertiaPerMass;
    private float _reach;
    private float _halfThickness;

    /// <summary>
    /// Its corners in order round it, as "x y" pairs separated by semicolons, in world units before the transform's scale,
    /// round the entity (plus <see cref="Offset"/>): "-1 0; 1 0; 0 1" is a triangle 2 wide and 1 tall. They can go round
    /// either way.
    /// </summary>
    [Points(Least = 3)]
    public string? Points
    {
        get => _points;
        set
        {
            _points = value;
            _given = PolygonParts.Parse(value);
            _parts = PolygonParts.Of(_given);
        }
    }

    public override Vector2 Offset { get; set; }

    /// <inheritdoc/>
    public override bool IsTrigger { get; set; }

    /// <inheritdoc/>
    public override string? Layer { get; set; }

    /// <inheritdoc/>
    public override string? IgnoreLayers { get; set; }

    /// <summary>Points written as <see cref="Points"/> takes them: "x y; x y; x y", on hundredths.</summary>
    public static string FormatPoints(IEnumerable<Vector2> points) => PolygonParts.Format(points);

    /// <summary>The points as they're written in <see cref="Points"/>, in its own units.</summary>
    public IReadOnlyList<Vector2> GivenPoints => _given;

    /// <summary>
    /// The corners of the shape it is, in its own units, anticlockwise: its <see cref="Points"/>, without repeated
    /// points or points along a straight side, or as it's fixed up when they don't make a proper shape (see
    /// <see cref="Problem"/>).
    /// </summary>
    public IReadOnlyList<Vector2> Outline => _parts.Outline;

    /// <summary>
    /// What's wrong with its <see cref="Points"/> and how it's been fixed up, e.g. "Its sides cross each other, so it's taken
    /// as the shape round all its points."; null when they make a proper shape.
    /// </summary>
    public string? Problem => _parts.Problem;

    /// <summary>How many convex pieces it's made of: 1 for a convex shape, more for a concave one.</summary>
    public int PieceCount => _parts.Pieces.Length;

    /// <summary>Its corners in the world, anticlockwise (as it's turned, if it <see cref="Collider.Turns"/>).</summary>
    public IReadOnlyList<Vector2> WorldPoints
    {
        get
        {
            Place();
            return _worldOutline;
        }
    }

    /// <summary>
    /// The corners of one of its convex pieces in the world, anticlockwise (from 0 to <see cref="PieceCount"/> less one):
    /// a concave shape is split into these.
    /// </summary>
    public IReadOnlyList<Vector2> PieceCorners(int piece) => WorldPiece(piece).ToArray();

    /// <summary>Where it balances in the world: what it turns round.</summary>
    public Vector2 WorldCentroid
    {
        get
        {
            Place();
            return _worldCentroid;
        }
    }

    /// <inheritdoc/>
    public override Box WorldBounds
    {
        get
        {
            Place();
            return _worldBounds;
        }
    }

    /// <summary>Where a point in its own units (as in <see cref="Points"/>) is in the world, as it's placed and turned.</summary>
    public Vector2 ToWorld(Vector2 point)
    {
        var world = Entity!.Transform.WorldMatrix;
        return Turned(world) ? Vector2.Transform(Offset + point, world) : new Vector2(world.M41, world.M42) + (Offset + point) * ScaleOf(world);
    }

    /// <summary>Where a point in the world is in its own units (as in <see cref="Points"/>): the opposite of <see cref="ToWorld"/>.</summary>
    public Vector2 FromWorld(Vector2 point)
    {
        var world = Entity!.Transform.WorldMatrix;
        if (Turned(world))
        {
            var plane = new Matrix3x2(world.M11, world.M12, world.M21, world.M22, world.M41, world.M42);
            return Matrix3x2.Invert(plane, out var inverse) ? Vector2.Transform(point, inverse) - Offset : -Offset;
        }

        var scale = ScaleOf(world);
        var offset = point - new Vector2(world.M41, world.M42);
        return new Vector2(scale.X != 0 ? offset.X / scale.X : 0, scale.Y != 0 ? offset.Y / scale.Y : 0) - Offset;
    }

    /// <inheritdoc/>
    public override bool Contains(Vector2 point)
    {
        Place();
        for (var piece = 0; piece < _parts.Pieces.Length; piece++)
        {
            var corners = WorldPiece(piece);
            var inside = true;
            for (var i = 0; i < corners.Length && inside; i++)
            {
                var (a, b) = (corners[i], corners[(i + 1) % corners.Length]);
                inside = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X) >= 0;
            }

            if (inside)
                return true;
        }

        return false;
    }

    /// <summary>A line on its shape for the Inspector: what's wrong with its points, if anything.</summary>
    public override string? SetupNote() => Problem;

    // Piece `piece`'s corners in the world, anticlockwise.
    internal ReadOnlySpan<Vector2> WorldPiece(int piece)
    {
        Place();
        return _worldCorners.AsSpan(_pieceStart[piece], _pieceStart[piece + 1] - _pieceStart[piece]);
    }

    // Which sides of piece `piece` (bit i: from its corner i to the next, as WorldPiece has them) are inside the shape.
    internal uint InnerSides(int piece)
    {
        Place();
        return _mirrored ? _parts.InnerReversed[piece] : _parts.Inner[piece];
    }

    // How hard it is to turn for each unit of mass, how far its furthest corner is from its middle, and half how thick its
    // thinnest piece is, at its size in the world.
    internal float InertiaPerMass
    {
        get
        {
            Measure();
            return _inertiaPerMass;
        }
    }

    internal float Reach
    {
        get
        {
            Measure();
            return _reach;
        }
    }

    internal float HalfThickness
    {
        get
        {
            Measure();
            return _halfThickness;
        }
    }

    /// <summary>Half how wide the box round it is when it's upright, at its size in the world: the foot it stands on.</summary>
    internal float HalfWidth => (_parts.Max.X - _parts.Min.X) / 2 * ScaleOf(Entity!.Transform.WorldMatrix).X;

    private bool Turned(in Matrix4x4 world) => IsTurning(world) && ScaleOf(world).X > 0 && Turns;

    private static Vector2 ScaleOf(in Matrix4x4 world) =>
        new(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());

    // Works out where its corners are in the world, when that may have changed since it last did.
    private void Place()
    {
        var world = Entity!.Transform.WorldMatrix;
        var turned = Turned(world);
        var parts = _parts;
        if (_placed.Parts == parts && _placed.World == world && _placed.Offset == Offset && _placed.Turned == turned)
            return;
        _placed = (world, Offset, turned, parts);

        var outline = parts.Outline;
        var origin = new Vector2(world.M41, world.M42);
        var scale = ScaleOf(world);
        if (_worldOutline.Length != outline.Length)
            _worldOutline = new Vector2[outline.Length];
        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        for (var i = 0; i < outline.Length; i++)
        {
            var point = turned ? Vector2.Transform(Offset + outline[i], world) : origin + (Offset + outline[i]) * scale;
            _worldOutline[i] = point;
            (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
        }

        _worldBounds = new Box((min + max) / 2, (max - min) / 2);
        _worldCentroid = turned ? Vector2.Transform(Offset + parts.Centroid, world) : origin + (Offset + parts.Centroid) * scale;

        // Mirrored by its turning (a negative scale), its corners go round the other way: each piece is gone round
        // backwards, so it stays anticlockwise.
        _mirrored = turned && world.M11 * world.M22 - world.M12 * world.M21 < 0;
        if (_mirrored)
            Array.Reverse(_worldOutline);
        var pieces = parts.Pieces;
        var total = 0;
        foreach (var piece in pieces)
            total += piece.Length;
        if (_worldCorners.Length != total)
            _worldCorners = new Vector2[total];
        if (_pieceStart.Length != pieces.Length + 1)
            _pieceStart = new int[pieces.Length + 1];
        var at = 0;
        for (var p = 0; p < pieces.Length; p++)
        {
            _pieceStart[p] = at;
            var piece = pieces[p];
            for (var i = 0; i < piece.Length; i++)
            {
                var corner = outline[piece[_mirrored ? piece.Length - 1 - i : i]];
                _worldCorners[at++] = turned ? Vector2.Transform(Offset + corner, world) : origin + (Offset + corner) * scale;
            }
        }

        _pieceStart[pieces.Length] = at;
    }

    // Works out how it turns at its size, when that may have changed since it last did.
    private void Measure()
    {
        var scale = ScaleOf(Entity!.Transform.WorldMatrix);
        var parts = _parts;
        if (_sized.Parts == parts && _sized.Scale == scale)
            return;
        _sized = (scale, parts);

        // Its outline at its size, round its middle: the second moment of its area about its middle, over its area.
        var outline = parts.Outline;
        var middle = parts.Centroid * scale;
        var (area, moment, reach) = (0f, 0f, 0f);
        for (var i = 0; i < outline.Length; i++)
        {
            var (a, b) = (outline[i] * scale - middle, outline[(i + 1) % outline.Length] * scale - middle);
            var cross = a.X * b.Y - a.Y * b.X;
            area += cross;
            moment += cross * (Vector2.Dot(a, a) + Vector2.Dot(a, b) + Vector2.Dot(b, b));
            reach = MathF.Max(reach, a.Length());
        }

        _inertiaPerMass = MathF.Abs(area) > 1e-12f ? moment / 6 / area : 0;
        _reach = reach;

        // Half the narrowest width of its thinnest piece (but no thinner than a quarter of the whole), so a fast one moves
        // in steps small enough not to pass through anything.
        var (thinnest, whole) = (float.PositiveInfinity, 0f);
        foreach (var piece in parts.Pieces)
        {
            var width = float.PositiveInfinity;
            for (var i = 0; i < piece.Length; i++)
            {
                var (a, b) = (outline[piece[i]] * scale, outline[piece[(i + 1) % piece.Length]] * scale);
                var side = b - a;
                var length = side.Length();
                if (length < 1e-9f)
                    continue;
                var normal = new Vector2(side.Y, -side.X) / length;
                var deepest = 0f;
                foreach (var corner in piece)
                    deepest = MathF.Max(deepest, Vector2.Dot(normal, a - outline[corner] * scale));
                width = MathF.Min(width, deepest);
            }

            if (float.IsFinite(width))
                (thinnest, whole) = (MathF.Min(thinnest, width), MathF.Max(whole, width));
        }

        _halfThickness = MathF.Max(1e-3f, float.IsFinite(thinnest) ? MathF.Max(thinnest, whole / 4) / 2 : 0);
    }
}

/// <summary>
/// Makes an entity with a <see cref="Collider"/> move under velocity and gravity and collide with solids. On an
/// isometric <see cref="Tilemap"/>, it meets the solid diamonds with the diamond under its collider (as wide as the
/// collider, half as tall, round its middle) and slides along them; on a hex one, its collider meets the solid
/// hexagons' six sides and slides along them. With <see cref="Rotates"/> on, it turns too: knocked off its middle it
/// spins, it tips over edges and falls flat, and its box or polygon collider turns with it.
/// </summary>
[Category("Physics")]
[Requires(typeof(Collider), Note = "the shape that collides")]
[ComponentName("Body")]
public sealed class Body : Component
{
    /// <summary>World units per second.</summary>
    [Sent(ByMover = true)]
    public Vector2 Velocity { get; set; }

    /// <summary>How fast it's going, in world units per second, whichever way.</summary>
    [DoNotSerialize]
    [Output]
    public float Speed => Velocity.Length();

    /// <summary>How high it is: its entity's height in the world.</summary>
    [DoNotSerialize]
    [Output]
    public float Height => Entity?.Transform.WorldPosition.Y ?? 0;

    /// <summary>Multiplier on world gravity. 0 for top-down games or floating things.</summary>
    public float GravityScale { get; set; } = 1f;

    /// <summary>
    /// How much speed is kept when hitting something: 0 stops dead (the default), 1 bounces back as fast as it hit.
    /// Slow bounces settle to a stop, so a bouncing ball comes to rest.
    /// </summary>
    public float Bounciness { get; set; }

    /// <summary>
    /// How quickly it stops sliding along the ground, in world units per second lost each second: a crate shoved or
    /// thrown comes to rest instead of gliding on. 0 (the default) keeps sliding; movement components steer bodies
    /// themselves either way.
    /// </summary>
    public float Friction { get; set; }

    /// <summary>
    /// Holds it still where it is, like a wall: it doesn't fall or move, and other bodies bump into it. Untick it (or
    /// grab it with the Sandbox Tools) and it moves again. Welded to it, a whole contraption holds still.
    /// </summary>
    public bool Frozen { get; set; }

    /// <summary>Held still for the step: frozen, or paused with its group (see <see cref="Entity.IsPaused"/>). Others bump into it as into a wall.</summary>
    public bool HeldStill => Frozen || (Entity?.IsPaused ?? false);

    /// <summary>
    /// How heavy it is, for pushes from a Thruster and for sharing movement with what it's welded or roped
    /// to: a heavy crate drags a light balloon down, a light one is lifted. 1 is a crate. Bodies that rotate share their
    /// knocks by it too: a heavy ball bowls a light plank over.
    /// </summary>
    public float Mass { get; set; } = 1;

    /// <summary>
    /// Lets it turn: hit or pushed off its middle it spins (see <see cref="Spin"/>), it tips over edges and falls flat, and
    /// its box or polygon collider turns with the entity, so others meet it turned. Off (the default), it stays upright whatever hits
    /// it, as players and enemies should, and so does its box. Welded to others, they turn as one if they all rotate; hung on
    /// a <see cref="HingeJoint"/> without it, it hangs upright.
    /// </summary>
    public bool Rotates { get; set; }

    /// <summary>How fast it turns, in degrees per second: positive turns anticlockwise. Set it to start it spinning.</summary>
    [Sent(ByMover = true)]
    [ShownWhen(nameof(Rotates))]
    public float Spin { get; set; }

    /// <summary>
    /// How well it grips what it touches as it turns, from 0 (ice: it slides off an edge rather than tipping over it) to 1
    /// (it hardly ever slips). Sliding along something it grips slows it, and turns it.
    /// </summary>
    [ShownWhen(nameof(Rotates))]
    public float Grip { get; set; } = 0.6f;

    /// <summary>
    /// How much a round one (a <see cref="CircleCollider"/>) is slowed as it rolls, so a ball comes to rest: 0 rolls on and
    /// on, 0.1 (the default) stops a ball rolling at 5 units a second in about 3 seconds, and 1 hardly rolls at all. It
    /// rests on a slope up to about this steep (rise over run). With no gravity on it (a top-down game, Gravity Scale 0) it
    /// rolls on the ground under the view and slows the same way; set it to 0 for one floating in space.
    /// </summary>
    [ShownWhen(nameof(Rotates))]
    public float RollingResistance { get; set; } = 0.1f;

    /// <summary>
    /// True while it's asleep: once it and every body that turns touching it have been still for half a second, they fall
    /// asleep together and stay exactly where they are, costing a fraction of what they did, until something disturbs
    /// them: a hit, a push, its <see cref="Velocity"/> or <see cref="Spin"/> set, something pushed into it, or what holds it
    /// up moving or going. Tick it to start it asleep, so a tower built resting on itself stands until something knocks
    /// it; untick it to wake it. Only one that <see cref="Rotates"/> sleeps.
    /// </summary>
    [ShownWhen(nameof(Rotates))]
    public bool Asleep
    {
        get => _asleep;
        set
        {
            // Woken, it's been still for no time at all.
            if (_asleep && !value)
                StillFor = 0;
            _asleep = value;
        }
    }

    private bool _asleep;

    /// <summary>
    /// Seconds it has been all but still (see <see cref="Asleep"/>): at half a second, and every body that turns touching it
    /// with it, it falls asleep. The game counts it.
    /// </summary>
    [ShownWhen(nameof(Rotates))]
    [NotSent]
    public float StillFor { get; set; }

    /// <summary>
    /// How hard it is to turn (its moment of inertia), from its <see cref="Mass"/> and its collider's size: a long plank
    /// turns more slowly than a small crate of the same weight.
    /// </summary>
    public float Inertia => Entity?.GetComponent<Collider>() switch
    {
        BoxCollider box => BoxInertia(box.WorldShape.HalfSize),
        CircleCollider circle => CircleInertia(circle.WorldCircle.Radius),
        PolygonCollider polygon => PolygonInertia(polygon),
        _ => Math.Max(1e-4f, Math.Max(0.01f, Mass) / 6),
    };

    // Its inertia with a box collider of that half size in the world, or a circle of that radius.
    internal float BoxInertia(Vector2 half) => Math.Max(1e-4f, Math.Max(0.01f, Mass) * (half.X * half.X + half.Y * half.Y) / 3);

    internal float CircleInertia(float radius) => Math.Max(1e-4f, Math.Max(0.01f, Mass) * radius * radius / 2);

    internal float PolygonInertia(PolygonCollider polygon) => Math.Max(1e-4f, Math.Max(0.01f, Mass) * polygon.InertiaPerMass);

    /// <summary>
    /// What it turns round: its collider's middle in the world (where a polygon balances), or its position without a
    /// collider.
    /// </summary>
    public Vector2 CenterOfMass => Entity?.GetComponent<Collider>() is { } collider
        ? collider is PolygonCollider polygon ? polygon.WorldCentroid : collider.WorldBounds.Center
        : Entity is { } entity ? new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y) : Vector2.Zero;

    /// <summary>True if the body rested on something below it during the last physics step.</summary>
    [DoNotSerialize]
    public bool IsGrounded { get; internal set; }

    /// <summary>
    /// What the body is standing on (a platform, the floor's tilemap), or null in the air. When that moves, the body rides
    /// along with it, so platforms moved by scripts or tweens carry whoever is on them.
    /// </summary>
    [DoNotSerialize]
    public Entity? Ground { get; internal set; }

    /// <summary>
    /// -1 if a wall stopped the body moving left during the last physics step, 1 if one stopped it moving right, else 0:
    /// for wall jumps and slides, or turning round.
    /// </summary>
    [DoNotSerialize]
    public int WallSide { get; internal set; }

    /// <summary>Seconds since the body last stood on something: 0 while it's grounded.</summary>
    [DoNotSerialize]
    public float AirTime { get; internal set; } = float.PositiveInfinity;

    /// <summary>True once the body has moved upwards since it was last grounded: it jumped, or was knocked up.</summary>
    internal bool RoseSinceGrounded { get; set; }

    /// <summary>True while the body falls through one-way platforms (see <see cref="DropThrough"/>).</summary>
    public bool IsDroppingThrough => DropThroughLeft > 0 || PassOneWayThisStep;

    // Seconds left falling through one-way platforms.
    internal float DropThroughLeft { get; set; }

    // Set by movement components each frame for the next physics step only (see Float).
    internal bool FloatThisStep { get; set; }

    /// <summary>
    /// For the next physics step only: no gravity (climbing, dashing, top-down, a tractor beam's pull), and passing through
    /// one-way platforms (climbing a ladder through one). What floats it does so again each step.
    /// </summary>
    public void Float() => FloatThisStep = true;

    // The rope it hangs on (a player climbing it): the rope moves it.
    internal Rope? Climbing;

    // Off the rope it hangs on, if any.
    internal void LetGoOfRope()
    {
        if (Climbing is not { } rope)
            return;
        Climbing = null;
        var climbers = rope.Climbers;
        for (var i = 0; i < climbers.Count; i++)
        {
            if (climbers[i].Body == this)
            {
                climbers.RemoveAt(i);
                return;
            }
        }
    }

    internal bool PassOneWayThisStep { get; set; }

    // What the game's systems make of the body, set before each physics step by the one that owns what it is (not saved):
    // physics knows bodies and colliders, not crates or players.

    /// <summary>
    /// How it gives way when something runs into it, or null if it's in the way like anything else: a crate's (see
    /// <see cref="Pushable"/>), shoved along by its share of the move.
    /// </summary>
    [field: NotState("set again before each step")]
    internal Shove? GivesWay { get; set; }

    /// <summary>A player's: it can take hold of ropes, so a rope hangs in front of it (and anything inside it), for it to grab.</summary>
    [field: NotState("set again before each step")]
    internal bool HoldsRopes { get; set; }

    /// <summary>
    /// Falls through the one-way platforms it's standing on (and any it meets) for <paramref name="seconds"/>: down and Jump
    /// on a platform. <c>if (Input.Vertical &lt; -0.5f &amp;&amp; Input.WasPressed("Jump")) body.DropThrough();</c>
    /// </summary>
    public void DropThrough(float seconds = 0.25f) => DropThroughLeft = Math.Max(DropThroughLeft, seconds);

    /// <summary>
    /// True while grounded, and for <paramref name="seconds"/> after walking off an edge (but not after jumping): coyote
    /// time, so a jump pressed a moment too late still happens. <c>if (body.WasGrounded(0.1f) &amp;&amp; Input.WasPressed("Jump"))</c>
    /// </summary>
    public bool WasGrounded(float seconds) => IsGrounded || (!RoseSinceGrounded && AirTime <= seconds);

    /// <summary>Applies a push for one step: <paramref name="force"/> divided by its <see cref="Mass"/>, over <paramref name="seconds"/>.</summary>
    public void Push(Vector2 force, float seconds)
    {
        if (!Frozen)
            Velocity += force / Math.Max(0.01f, Mass) * seconds;
    }

    /// <summary>
    /// Pushes it at <paramref name="point"/> in the world, as <see cref="Push"/> does; one that <see cref="Rotates"/> also
    /// turns, the more the further from its middle the push is (a kick to a plank's end spins it).
    /// <c>body.PushAt(new Vector2(20, 0), corner, Time.Delta);</c>
    /// </summary>
    public void PushAt(Vector2 force, Vector2 point, float seconds)
    {
        if (Frozen)
            return;
        Push(force, seconds);
        var arm = point - CenterOfMass;
        Twist(arm.X * force.Y - arm.Y * force.X, seconds);
    }

    /// <summary>
    /// Turns it for one step with a torque (a twisting push, positive anticlockwise) over <paramref name="seconds"/>: its
    /// <see cref="Spin"/> changes by the torque divided by its <see cref="Inertia"/>. Only one that <see cref="Rotates"/> turns.
    /// <c>body.Twist(-Input.Horizontal * 20, Time.Delta);</c>
    /// </summary>
    public void Twist(float torque, float seconds)
    {
        if (!Frozen && Rotates)
            Spin += torque / Inertia * seconds * 180 / MathF.PI;
    }
}
