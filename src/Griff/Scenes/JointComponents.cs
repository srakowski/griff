using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Pins its <see cref="Body"/> to another body (<see cref="To"/>), or to a point in the world, at one point (its
/// <see cref="Anchor"/>), so it turns there: a wheel on a cart, a door, a flipper, a seesaw, a windmill. It can be kept
/// within a range of angles (<see cref="Limit"/>) and turned by a motor (<see cref="Motor"/>), and the two it joins
/// don't bump into each other unless <see cref="Collide"/> is ticked. Tick <see cref="Body.Rotates"/> on the body for it to
/// swing round (Entity &gt; Add Hinge does); without it, it hangs upright from the hinge, as a Ferris wheel's seats do.
/// What it's hinged to turns with it if that rotates too, and is pulled about by it if it moves under physics; anything
/// else (a player, a wall, a platform moved by a script) just carries the hinge along.
/// </summary>
/// <remarks>
/// The two are joined as they stand when the game starts (see <see cref="Joined"/>): the hinge stays at that point on
/// each of them, and its angles count from how they're turned then. In the Scene view, drag the hinge's knob to move it
/// and the ends of its arc to set its limits.
/// </remarks>
[Category("Physics")]
[Requires(typeof(Body), Note = "what turns on the hinge")]
[ComponentName("HingeJoint")]
public sealed class HingeJoint : Component
{
    /// <summary>
    /// What it's hinged to, by name: the nearest entity called that. Empty pins it to the world, where the hinge is when
    /// the game starts.
    /// </summary>
    [EntityName]
    public string? To { get; set; }

    /// <summary>
    /// Where the hinge is on this entity, from its position, in its own units (before its scale, turned with it): (0, 0) is
    /// its middle, as a wheel's; (-2, 0) one end of a plank 4 long, as a door's.
    /// </summary>
    public Vector2 Anchor { get; set; }

    /// <summary>Let the two it joins bump into each other. Off (the default), they pass through each other where they meet.</summary>
    public bool Collide { get; set; }

    /// <summary>
    /// How stiffly it turns, from 0 (it swings on and on) to 1 (a stiff hinge that stops a swinging door soon, a little off
    /// straight down): the more weight on it, the harder it is to turn. The default, 0.05, lets wheels roll on and slows a
    /// pendulum over a while. A running <see cref="Motor"/> takes no notice of it.
    /// </summary>
    public float Friction { get; set; } = 0.05f;

    /// <summary>Keep it within <see cref="LowerAngle"/> and <see cref="UpperAngle"/>: a door that opens one way, a flipper.</summary>
    public bool Limit { get; set; }

    /// <summary>
    /// How far it can turn clockwise (negative) from how it started, in degrees, as far as -180 (half a turn).
    /// </summary>
    [ShownWhen(nameof(Limit))]
    public float LowerAngle { get; set; } = -45;

    /// <summary>How far it can turn anticlockwise (positive) from how it started, in degrees, as far as 180 (half a turn).</summary>
    [ShownWhen(nameof(Limit))]
    public float UpperAngle { get; set; } = 45;

    /// <summary>
    /// Turn it with a motor, at <see cref="MotorSpeed"/> as far as <see cref="MotorStrength"/> lets it: a wheel that drives
    /// a cart, a windmill. A switch or a wire into its "Power" turns the motor on and off (a flipper on a button).
    /// </summary>
    [Powered]
    public bool Motor { get; set; }

    /// <summary>How fast the motor turns it, in degrees a second: positive turns it anticlockwise.</summary>
    [ShownWhen(nameof(Motor))]
    public float MotorSpeed { get; set; } = 180;

    /// <summary>
    /// How hard the motor can turn it (the most torque it has): 100 turns a crate at full speed in an instant, and lifts a
    /// plank 4 long by its end; a heavy load turns more slowly or stalls.
    /// </summary>
    [ShownWhen(nameof(Motor))]
    public float MotorStrength { get; set; } = 100;

    /// <summary>
    /// How far it has turned on the hinge from how it started, in degrees (positive anticlockwise), against what it's
    /// hinged to: from -180 to 180.
    /// </summary>
    [DoNotSerialize]
    [Output]
    public float Angle => Entity is null ? 0 : Wrap(TurnOf(Entity) - (OtherEntity is { } other ? TurnOf(other) : 0) - (Joined ? StartAngle : TurnNow()));

    /// <summary>Where the hinge is in the world.</summary>
    [DoNotSerialize]
    public Vector2 WorldAnchor => Entity is null ? Anchor : Vector2.Transform(Anchor, Entity.Transform.WorldMatrix);

    /// <summary>
    /// True once the game has joined the two as they stood (it sets <see cref="OtherAnchor"/> and
    /// <see cref="StartAngle"/>). Untick it to join them again as they are now, where <see cref="Anchor"/> is.
    /// </summary>
    [Group("Joined")]
    public bool Joined { get; set; }

    /// <summary>
    /// Where the hinge is on what it's hinged to, from its position in its own units (or, hinged to the world, where it is in
    /// the world): the game sets it as it joins them.
    /// </summary>
    [ShownWhen(nameof(Joined))]
    public Vector2 OtherAnchor { get; set; }

    /// <summary>How the two were turned against each other as they were joined, in degrees: the game sets it.</summary>
    [ShownWhen(nameof(Joined))]
    public float StartAngle { get; set; }

    /// <summary>Turns the motor the other way.</summary>
    [Action]
    public void Reverse() => MotorSpeed = -MotorSpeed;

    // What it's hinged to, found by name and kept while it's there under that name.
    internal Entity? Other;
    private string? _otherName;

    /// <summary>What it's hinged to (see <see cref="To"/>), or null for the world.</summary>
    public Entity? OtherEntity
    {
        get
        {
            var entity = Entity;
            var name = To?.Trim();
            if (entity?.Scene is not { } scene || string.IsNullOrEmpty(name))
                return null;
            if (Other is { } kept && kept.Scene == scene && kept.Name == name && ReferenceEquals(_otherName, To))
                return kept;
            (Other, _otherName) = (Nearest(scene, entity, name), To);
            return Other;
        }
    }

    // The entity called `name` nearest this one (not itself).
    internal static Entity? Nearest(Scene scene, Entity from, string name)
    {
        var here = Flat(from.Transform.WorldPosition);
        Entity? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var entity in scene.Entities)
        {
            if (entity == from || entity.Name != name)
                continue;
            var distance = Vector2.DistanceSquared(Flat(entity.Transform.WorldPosition), here);
            if (distance < bestDistance)
                (best, bestDistance) = (entity, distance);
        }

        return best;
    }

    /// <summary>Hinges it to <paramref name="other"/> (null: the world), that one even if others share its name.</summary>
    internal void HingeTo(Entity? other) => (To, Other, _otherName) = (other?.Name, other, other?.Name);

    /// <summary>Joins the two as they stand now: where the hinge is on what it's hinged to, and how they're turned.</summary>
    internal void Join()
    {
        var anchor = WorldAnchor;
        var other = OtherEntity;
        OtherAnchor = other is not null && Matrix4x4.Invert(other.Transform.WorldMatrix, out var inverse) ? Vector2.Transform(anchor, inverse) : anchor;
        StartAngle = TurnNow();
        Joined = true;
    }

    /// <summary>Where the hinge is on what it's hinged to, in the world (where it was put in the world, hinged to nothing).</summary>
    internal Vector2 OtherWorldAnchor(Entity? other) => other is null ? OtherAnchor : Vector2.Transform(OtherAnchor, other.Transform.WorldMatrix);

    // How the two are turned against each other now, in degrees.
    private float TurnNow() => Entity is null ? 0 : Wrap(TurnOf(Entity) - (OtherEntity is { } other ? TurnOf(other) : 0));

    /// <summary>Which way an entity faces in the world, in degrees anticlockwise.</summary>
    internal static float TurnOf(Entity entity)
    {
        var world = entity.Transform.WorldMatrix;
        return MathF.Atan2(world.M12, world.M11) * 180 / MathF.PI;
    }

    /// <summary>An angle in degrees brought within -180 to 180.</summary>
    internal static float Wrap(float degrees) => degrees - 360 * MathF.Floor((degrees + 180) / 360);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
