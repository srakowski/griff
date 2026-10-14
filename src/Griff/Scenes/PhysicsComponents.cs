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

    /// <summary>The box around the shape in the world, from its entity's position and scale (not its rotation).</summary>
    public abstract Box WorldBounds { get; }
}

/// <summary>Matching collision layer names: case doesn't matter, and an empty layer is called "Default".</summary>
public static class CollisionLayers
{
    public const string Default = "Default";

    /// <summary>The layer's name, with empty meaning <see cref="Default"/>.</summary>
    public static string NameOf(string? layer) => string.IsNullOrWhiteSpace(layer) ? Default : layer.Trim();

    /// <summary>Reads a list of layer names like "Bullet, Enemy" (commas or semicolons between them).</summary>
    public static string[] Parse(string? list) =>
        (list ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>True if <paramref name="layer"/> is one of <paramref name="layers"/>.</summary>
    public static bool Contains(IEnumerable<string> layers, string? layer)
    {
        var name = NameOf(layer);
        return layers.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// An axis-aligned box for collisions, centered on the entity (plus <see cref="Offset"/>) and scaled by its transform.
/// Rotation is ignored. Without a <see cref="Body"/> the box is static: walls, floors, platforms.
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

    /// <summary>The box it covers in the world, from its entity's position and scale (not its rotation).</summary>
    public Box WorldBox
    {
        get
        {
            var world = Entity!.Transform.WorldMatrix;
            var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
            return new Box(new Vector2(world.M41, world.M42) + Offset * scale, Size * scale / 2);
        }
    }

    /// <inheritdoc/>
    public override Box WorldBounds => WorldBox;
}

/// <summary>
/// A circle for collisions, centered on the entity (plus <see cref="Offset"/>). Its radius grows with the larger of the
/// transform's X and Y scale, so it stays round. Good for balls, coins, bullets and round enemies.
/// </summary>
/// <remarks>Circle bodies don't roll: one resting on a corner stays balanced there.</remarks>
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
            var center = new Vector2(world.M41, world.M42) + Offset * scale;
            return new Circle(center, Radius * Math.Max(scale.X, scale.Y));
        }
    }

    /// <inheritdoc/>
    public override Box WorldBounds => WorldCircle.Bounds;
}

/// <summary>Makes an entity with a <see cref="Collider"/> move under velocity and gravity and collide with solids.</summary>
[Category("Physics")]
[Requires(typeof(Collider), Note = "the shape that collides")]
[ComponentName("Body")]
public sealed class Body : Component
{
    /// <summary>World units per second.</summary>
    [Sent(ByMover = true)]
    public Vector2 Velocity { get; set; }

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
    /// grab it with <see cref="SandboxTools"/>) and it moves again. Welded to it, a whole contraption holds still.
    /// </summary>
    public bool Frozen { get; set; }

    /// <summary>
    /// How heavy it is, for pushes from a <see cref="Thruster"/> and for sharing movement with what it's welded or roped
    /// to: a heavy crate drags a light balloon down, a light one is lifted. 1 is a crate.
    /// </summary>
    public float Mass { get; set; } = 1;

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
}
