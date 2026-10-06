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
    public abstract string? Layer { get; set; }

    /// <summary>
    /// Layers this collider passes through, e.g. "Bullet, Enemy": no blocking, no collisions, no triggers. It works both
    /// ways, so only one side has to list the other.
    /// </summary>
    public abstract string? IgnoreLayers { get; set; }
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
[ComponentName("BoxCollider")]
public sealed class BoxCollider : Collider
{
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
}

/// <summary>
/// A circle for collisions, centered on the entity (plus <see cref="Offset"/>). Its radius grows with the larger of the
/// transform's X and Y scale, so it stays round. Good for balls, coins, bullets and round enemies.
/// </summary>
/// <remarks>Circle bodies don't roll: one resting on a corner stays balanced there.</remarks>
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
}

/// <summary>Makes an entity with a <see cref="Collider"/> move under velocity and gravity and collide with solids.</summary>
[ComponentName("Body")]
public sealed class Body : Component
{
    /// <summary>World units per second.</summary>
    public Vector2 Velocity { get; set; }

    /// <summary>Multiplier on world gravity. 0 for top-down games or floating things.</summary>
    public float GravityScale { get; set; } = 1f;

    /// <summary>
    /// How much speed is kept when hitting something: 0 stops dead (the default), 1 bounces back as fast as it hit.
    /// Slow bounces settle to a stop, so a bouncing ball comes to rest.
    /// </summary>
    public float Bounciness { get; set; }

    /// <summary>True if the body rested on something below it during the last physics step.</summary>
    [DoNotSerialize]
    public bool IsGrounded { get; internal set; }

    /// <summary>
    /// What the body is standing on (a platform, the floor's tilemap), or null in the air. When that moves, the body rides
    /// along with it, so platforms moved by scripts or tweens carry whoever is on them.
    /// </summary>
    [DoNotSerialize]
    public Entity? Ground { get; internal set; }
}
