using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A shape for collisions: a <see cref="BoxCollider"/> or a <see cref="CircleCollider"/>. Without a <see cref="Body"/>
/// the shape is static: walls, floors, platforms. Use <c>GetComponent&lt;Collider&gt;()</c> to find either kind.
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
    public static string[] Parse(string? list) => string.IsNullOrWhiteSpace(list)
        ? []
        : list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>True if <paramref name="layer"/> is one of <paramref name="layers"/>.</summary>
    public static bool Contains(IEnumerable<string> layers, string? layer)
    {
        var name = NameOf(layer);
        return layers.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
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
/// Makes an entity with a <see cref="Collider"/> move under velocity and gravity and collide with solids. On an
/// isometric <see cref="Tilemap"/>, it meets the solid diamonds with the diamond under its collider (as wide as the
/// collider, half as tall, round its middle) and slides along them; on a hex one, its collider meets the solid
/// hexagons' six sides and slides along them. With <see cref="Rotates"/> on, it turns too: knocked off its middle it
/// spins, it tips over edges and falls flat, and its box collider turns with it.
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

    // Held still for the step: frozen, or paused with its group (see Entity.IsPaused). Others bump into it as into a wall.
    internal bool HeldStill => Frozen || (Entity?.IsPaused ?? false);

    /// <summary>
    /// How heavy it is, for pushes from a Thruster and for sharing movement with what it's welded or roped
    /// to: a heavy crate drags a light balloon down, a light one is lifted. 1 is a crate. Bodies that rotate share their
    /// knocks by it too: a heavy ball bowls a light plank over.
    /// </summary>
    public float Mass { get; set; } = 1;

    /// <summary>
    /// Lets it turn: hit or pushed off its middle it spins (see <see cref="Spin"/>), it tips over edges and falls flat, and
    /// its box collider turns with the entity, so others meet it turned. Off (the default), it stays upright whatever hits
    /// it, as players and enemies should, and so does its box. Welded to others, it moves with them without turning.
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
        _ => Math.Max(1e-4f, Math.Max(0.01f, Mass) / 6),
    };

    // Its inertia with a box collider of that half size in the world, or a circle of that radius.
    internal float BoxInertia(Vector2 half) => Math.Max(1e-4f, Math.Max(0.01f, Mass) * (half.X * half.X + half.Y * half.Y) / 3);

    internal float CircleInertia(float radius) => Math.Max(1e-4f, Math.Max(0.01f, Mass) * radius * radius / 2);

    /// <summary>What it turns round: its collider's middle in the world (its position, without a collider).</summary>
    public Vector2 CenterOfMass => Entity?.GetComponent<Collider>() is { } collider
        ? collider.WorldBounds.Center
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

    // Set by movement components each frame for the next physics step only: no gravity (climbing, dashing, top-down), and
    // passing through one-way platforms (climbing a ladder through one).
    internal bool FloatThisStep { get; set; }

    internal bool PassOneWayThisStep { get; set; }

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
