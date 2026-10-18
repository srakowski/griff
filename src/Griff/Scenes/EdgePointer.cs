using System.Numerics;

namespace Griff.Scenes;

/// <summary>What an <see cref="EdgePointer"/> does while what it points at is in view.</summary>
public enum EdgePointerInView
{
    /// <summary>It hides, and shows again on the edge once the target goes out of view.</summary>
    Hide,

    /// <summary>It sits over the target (at its <see cref="EdgePointer.OverTarget"/>), pointing down at it: one marker on the screen and off it.</summary>
    OverTarget,
}

/// <summary>
/// Points the way to something off the screen, as a strategy game's objective marker does: while its
/// <see cref="Target"/> is out of the camera's view, the entity sits on the screen's edge (in by <see cref="Margin"/>)
/// where the line from the screen's middle to the target crosses it, turned to point at the target, and slides along the
/// edge as the camera pans or zooms. Once the target is in view it hides, or sits over the target (see
/// <see cref="InView"/>). Draw its sprite pointing right. A <see cref="Button"/> on it whose Do is "Glide" glides the
/// camera there on a click; a <see cref="Text"/> inside it showing its Distance output ("{0:0} m") says how far off the
/// target is, and stays upright.
/// </summary>
/// <remarks>
/// It's pinned to the main camera's screen as a <see cref="ScreenAnchor"/> is, laid out after them each frame, and keeps
/// its size as the camera zooms with the camera's <see cref="Camera.HudSize"/> and the player's UI Scale. Its target
/// goes by its position. It decides whether its entity is shown, so to hide it some other way, hide what it's inside. The
/// Scene view shows it where it's placed; it only moves while the game runs.
/// </remarks>
[Category("UI")]
[ComponentName("EdgePointer")]
[NetLocal]
[WorksWith(typeof(Sprite), Note = "an arrow drawn pointing right")]
[WorksWith(typeof(Button), Note = "Do: Glide, to glide the camera to the target on a click")]
public sealed class EdgePointer : Component
{
    // The entity it points at, and the Target name it was found by (one a script set keeps it until Target changes).
    private Entity? _target;
    private string? _foundBy;

    /// <summary>
    /// The name of the entity to point at, e.g. "Goal" (found as names find entities). A script, or a Trigger's Point At,
    /// changes it as the goal moves on; empty points at nothing, and it hides.
    /// </summary>
    [EntityName]
    public string? Target { get; set; }

    /// <summary>The entity it points at. Set it from a script to point at something without naming it; it holds until <see cref="Target"/> changes.</summary>
    [DoNotSerialize]
    public Entity? TargetEntity
    {
        get => _target;
        set => (_target, _foundBy) = (value, Target);
    }

    /// <summary>
    /// How far in from the screen's edges it sits, in world units of the HUD (as a <see cref="ScreenAnchor"/>'s Offset is):
    /// room for half of it, and for a bar along the edge.
    /// </summary>
    public float Margin { get; set; } = 0.75f;

    /// <summary>What it does while the target is in view: hide (the default), or sit over the target pointing down at it.</summary>
    public EdgePointerInView InView { get; set; }

    /// <summary>
    /// Where it sits from the target while that's in view, in world units: (0, 1) just over a worker's head. Past the
    /// screen's edge it stays on the edge.
    /// </summary>
    [ShownWhen(nameof(InView), Is = [EdgePointerInView.OverTarget])]
    public Vector2 OverTarget { get; set; } = new(0, 1);

    /// <summary>True while the target is out of view and it's on the screen's edge pointing at it.</summary>
    [DoNotSerialize]
    public bool IsOnEdge { get; internal set; }

    /// <summary>How far the target is from the middle of the screen, in world units: a Text inside it can show it ("{0:0} m").</summary>
    [Output]
    [DoNotSerialize]
    public float Distance { get; internal set; }

    // The camera it points across, as of the last frame laid out: the one Glide glides.
    internal Entity? ViewCamera { get; set; }

    /// <summary>Points at the entity with this name from now on: a Trigger or a script moving the goal on. Empty points at nothing.</summary>
    [Action]
    public void PointAt(string? name) => Target = name;

    /// <summary>
    /// Glides the camera over to the target with its <see cref="CameraControls"/> (see
    /// <see cref="CameraControls.GlideTo(Entity, float?, float?)"/>) and leaves it the player's there: what a Button on it
    /// does with Do set to Glide. Nothing happens without a target, or without Camera Controls on the camera.
    /// </summary>
    [Action]
    public void Glide()
    {
        if (FindTarget() is { } target && ViewCamera?.GetComponent<CameraControls>() is { } controls)
            controls.GlideTo(target);
    }

    /// <summary>The entity it points at, looked up by <see cref="Target"/> when that has changed or the one it had has gone.</summary>
    internal Entity? FindTarget()
    {
        if (Entity?.Scene is not { } scene)
            return null;
        if (_target?.Scene != scene || !string.Equals(_foundBy, Target, StringComparison.Ordinal))
        {
            _foundBy = Target;
            _target = string.IsNullOrWhiteSpace(Target) ? null : scene.FindNamed(Target.Trim(), near: Entity);
        }

        return _target;
    }

    /// <summary>
    /// Where it sits to point at <paramref name="target"/> on a view centered on <paramref name="center"/> that reaches
    /// <paramref name="reach"/> out to its edges (less its margin): where the line from the middle to the target crosses the
    /// edge, or null while the target is inside.
    /// </summary>
    public static Vector2? EdgePoint(Vector2 center, Vector2 reach, Vector2 target)
    {
        var away = target - center;
        if (MathF.Abs(away.X) <= reach.X && MathF.Abs(away.Y) <= reach.Y)
            return null;
        var across = away.X == 0 ? float.MaxValue : reach.X / MathF.Abs(away.X);
        var up = away.Y == 0 ? float.MaxValue : reach.Y / MathF.Abs(away.Y);
        return center + away * MathF.Min(across, up);
    }

    public override string? SetupNote() =>
        string.IsNullOrWhiteSpace(Target) ? "Points at nothing until its Target is named (or a script sets it)." : null;
}
