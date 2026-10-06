using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// An axis-aligned box for collisions, centered on the entity (plus <see cref="Offset"/>) and scaled by its transform.
/// Rotation is ignored. Without a <see cref="Body"/> the box is static: walls, floors, platforms.
/// </summary>
[ComponentName("BoxCollider")]
public sealed class BoxCollider : Component
{
    public Vector2 Size { get; set; } = Vector2.One;

    public Vector2 Offset { get; set; }

    /// <summary>Triggers don't block movement; they report OnTriggerEnter/Exit instead.</summary>
    public bool IsTrigger { get; set; }

    /// <summary>Only blocks bodies landing on top: jump up through it from below, stand on it from above.</summary>
    public bool OneWay { get; set; }
}

/// <summary>Makes an entity with a <see cref="BoxCollider"/> move under velocity and gravity and collide with solids.</summary>
[ComponentName("Body")]
public sealed class Body : Component
{
    /// <summary>World units per second.</summary>
    public Vector2 Velocity { get; set; }

    /// <summary>Multiplier on world gravity. 0 for top-down games or floating things.</summary>
    public float GravityScale { get; set; } = 1f;

    /// <summary>True if the body rested on something below it during the last physics step.</summary>
    [DoNotSerialize]
    public bool IsGrounded { get; internal set; }
}
