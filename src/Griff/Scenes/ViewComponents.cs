using System.Numerics;
using Griff.Runtime;

namespace Griff.Scenes;

/// <summary>
/// Moves the entity after another one, smoothly: a camera after the player, a pet after its owner. No code needed: put it
/// on the camera and name the <see cref="Target"/>. With <see cref="StayInside"/>, a camera never shows past the edges
/// of the level. Applied while the game runs, after physics, so it follows where things ended up this frame.
/// </summary>
[Category("Movement")]
[WorksWith(typeof(Camera), Note = "a camera that follows the player")]
[ComponentName("Follow")]
public sealed class Follow : Component
{
    private Entity? _target;
    private bool _placed;
    private Vector2? _lastTargetPosition;
    private Vector2 _lead;

    // A slide over to a new room: where it set off from, how long it's been going, and how long it takes (real seconds).
    private (Vector2 From, float Elapsed, float Seconds)? _slide;

    /// <summary>The name of the entity to follow, e.g. "Player".</summary>
    public string? Target { get; set; } = "Player";

    /// <summary>How quickly it catches up: higher is snappier, 0 sticks exactly to the target.</summary>
    public float Smoothing { get; set; } = 6;

    /// <summary>Where to sit relative to the target, in world units, e.g. (0, 2) to see more above a platformer's hero.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// A box around the follower, in world units, that the target can move about in without the follower moving.
    /// (0, 0) follows every step; something like (2, 1) keeps a platformer's camera still for small hops.
    /// </summary>
    public Vector2 DeadZone { get; set; }

    /// <summary>Seconds of the target's movement to look ahead by, so a camera shows more of where the player is heading.</summary>
    public float LookAhead { get; set; }

    /// <summary>
    /// The name of an entity whose area the follower stays inside: a tilemap's grid, a box collider, or a sprite. On a
    /// camera the whole view stays inside, so the game never shows past the level's edges.
    /// </summary>
    public string? StayInside { get; set; }

    /// <summary>The entity being followed. Set it from a script to follow something without looking it up by name.</summary>
    [DoNotSerialize]
    public Entity? TargetEntity
    {
        get => _target;
        set
        {
            _target = value;
            _lastTargetPosition = null;
            _lead = Vector2.Zero;
        }
    }

    /// <summary>
    /// Jumps straight to the target next time instead of gliding over, e.g. after a teleport or a respawn (doors and
    /// checkpoints do this by themselves).
    /// </summary>
    public void Snap()
    {
        _placed = false;
        _slide = null;
    }

    /// <summary>
    /// The <see cref="Room"/> a camera is keeping to, in place of <see cref="StayInside"/>; set by the game as the target
    /// moves from room to room.
    /// </summary>
    [DoNotSerialize]
    public Room? CurrentRoom { get; internal set; }

    /// <summary>True while the camera is sliding over to a new room.</summary>
    public bool IsChangingRoom => _slide is not null;

    /// <summary>The entity it's following, looked up by <see cref="Target"/> if the one it had has gone.</summary>
    internal Entity? FindTarget()
    {
        var scene = Entity?.Scene;
        // In a network game, this player's own character comes first, once it's there.
        if (scene is not null && (_target?.Scene != scene || (scene.PreferNamed is { } prefer && _target.Name == Target && !prefer(_target))))
            TargetEntity = string.IsNullOrWhiteSpace(Target) ? null : scene.FindNamed(Target) ?? (_target?.Scene == scene ? _target : null);
        return _target;
    }

    /// <summary>
    /// Glides over to where it should be in <paramref name="seconds"/> of real time, rather than following smoothly: into a
    /// new room. Before it has been placed, or with no time, it jumps straight there.
    /// </summary>
    internal void SlideOver(float seconds)
    {
        if (_placed && seconds > 0 && Entity is not null)
            _slide = (Flat(Entity.Transform.WorldPosition), 0, seconds);
        else
            Snap();
    }

    /// <summary>Moves toward the target by <paramref name="seconds"/> of smoothing. Called by the game after physics.</summary>
    /// <param name="viewAspect">The view's width over its height, for keeping a camera's view inside <see cref="StayInside"/>.</param>
    /// <param name="realSeconds">
    /// Real time passed, for sliding between rooms while the game is held still; <paramref name="seconds"/> if not given.
    /// </param>
    public void Advance(float seconds, float viewAspect, float? realSeconds = null)
    {
        var scene = Entity?.Scene;
        if (scene is null)
            return;
        FindTarget();

        var transform = Entity!.Transform;
        var current = Flat(transform.WorldPosition);
        var goal = current;

        if (_target is not null)
        {
            var target = Flat(_target.Transform.WorldPosition);
            UpdateLead(target, seconds);
            goal = target + Offset + _lead;

            // Snap there the first time, so a level doesn't open with the camera sliding over from wherever it was placed.
            if (_placed)
                goal = ApplyDeadZone(current, goal);
        }

        var bounds = CurrentRoom?.Entity?.Scene == scene ? CurrentRoom!.Area : Bounds(scene);
        if (bounds is { } area)
            goal = Clamp(goal, area, ViewHalfSize(viewAspect));

        if (_slide is { } slide && _placed)
        {
            var elapsed = slide.Elapsed + (realSeconds ?? seconds);
            var t = Math.Clamp(elapsed / slide.Seconds, 0, 1);
            SetWorld(transform, Vector2.Lerp(slide.From, goal, t * t * (3 - 2 * t)));
            _slide = t >= 1 ? null : slide with { Elapsed = elapsed };
            return;
        }

        var next = !_placed || Smoothing <= 0 ? goal : Vector2.Lerp(current, goal, 1 - MathF.Exp(-Smoothing * seconds));
        if (_target is not null)
            _placed = true;
        if (next != current)
            SetWorld(transform, next);
    }

    // A target's speed, from its body if it has one, else from how far it moved; smoothed so a stop doesn't jolt the view.
    private void UpdateLead(Vector2 target, float seconds)
    {
        if (LookAhead <= 0 || seconds <= 0)
        {
            _lead = Vector2.Zero;
            _lastTargetPosition = target;
            return;
        }

        var velocity = _target!.GetComponent<Body>()?.Velocity
                       ?? (_lastTargetPosition is { } last ? (target - last) / seconds : Vector2.Zero);
        _lastTargetPosition = target;
        _lead = Vector2.Lerp(_lead, velocity * LookAhead, 1 - MathF.Exp(-4 * seconds));
    }

    private Vector2 ApplyDeadZone(Vector2 current, Vector2 goal)
    {
        var half = Vector2.Abs(DeadZone) / 2;
        var delta = goal - current;
        return current + new Vector2(Outside(delta.X, half.X), Outside(delta.Y, half.Y));

        static float Outside(float delta, float half) => MathF.Abs(delta) <= half ? 0 : delta - MathF.CopySign(half, delta);
    }

    // Half the visible area for a camera; a plain follower is a point.
    private Vector2 ViewHalfSize(float aspect) =>
        Entity!.GetComponent<Camera>() is { } camera ? new Vector2(camera.Size * (aspect > 0 ? aspect : 1), camera.Size) : Vector2.Zero;

    private Box? Bounds(Scene scene)
    {
        if (string.IsNullOrWhiteSpace(StayInside) || scene.Entities.FirstOrDefault(e => e.Name == StayInside) is not { } area)
            return null;
        return AreaOf(area);
    }

    /// <summary>The world-space box an entity covers: its tilemap's grid, else its box collider, else its sprite.</summary>
    public static Box? AreaOf(Entity entity)
    {
        var world = entity.Transform.WorldMatrix;
        if (entity.GetComponent<Tilemap>() is { Width: > 0 } tilemap)
            return Transformed(new Vector2(0, -tilemap.Height * tilemap.TileSize), new Vector2(tilemap.Width * tilemap.TileSize, 0), world);
        if (entity.GetComponent<BoxCollider>() is { } box)
            return Physics.BoundsOf(box);
        if (entity.GetComponent<Sprite>() is { } sprite)
            return Transformed(-sprite.Size / 2, sprite.Size / 2, world);
        return null;
    }

    // The box around a local-space box's corners once placed in the world.
    private static Box Transformed(Vector2 min, Vector2 max, Matrix4x4 world)
    {
        Span<Vector2> corners = [min, max, new(min.X, max.Y), new(max.X, min.Y)];
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);
        foreach (var corner in corners)
        {
            var point = Vector2.Transform(corner, world);
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }

        return new Box((low + high) / 2, (high - low) / 2);
    }

    // Keeps a view of half-size `half` centered on `point` inside `bounds`; a view bigger than the bounds is centered on them.
    private static Vector2 Clamp(Vector2 point, Box bounds, Vector2 half) =>
        new(ClampAxis(point.X, bounds.Min.X + half.X, bounds.Max.X - half.X),
            ClampAxis(point.Y, bounds.Min.Y + half.Y, bounds.Max.Y - half.Y));

    private static float ClampAxis(float value, float min, float max) => min > max ? (min + max) / 2 : Math.Clamp(value, min, max);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private static void SetWorld(Transform transform, Vector2 world)
    {
        var target = new Vector3(world, transform.WorldPosition.Z);
        if (transform.Entity?.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            target = Vector3.Transform(target, inverse);
        transform.Position = new Vector3(target.X, target.Y, transform.Position.Z);
    }
}

/// <summary>
/// Makes a background layer scroll slower than the world as the camera moves, for depth: far hills at 0.2, near trees at
/// 0.7. Everything the entity and its children draw shifts with it. With <see cref="RepeatX"/> or <see cref="RepeatY"/>,
/// its sprite repeats to fill the view, so a short strip of sky or hills never runs out.
/// </summary>
/// <remarks>
/// Applied when the game is drawn, so it never changes the entity's transform: colliders, buttons and the Scene view
/// all use where the transform puts it. The layer is drawn exactly there when the camera is at the world's origin.
/// </remarks>
[Category("Rendering")]
[WorksWith(typeof(Sprite), Note = "to repeat a strip of background")]
[ComponentName("Parallax")]
public sealed class Parallax : Component
{
    /// <summary>
    /// How much the layer moves with the world on X and Y: 1 like everything else, 0 stuck to the camera (a distant sky),
    /// and in between for layers further back.
    /// </summary>
    public Vector2 Factor { get; set; } = new(0.5f, 0.5f);

    /// <summary>Repeats the entity's sprite side by side to fill the view's width.</summary>
    public bool RepeatX { get; set; }

    /// <summary>Repeats the entity's sprite top to bottom to fill the view's height.</summary>
    public bool RepeatY { get; set; }

    /// <summary>How far the layer is shifted when seen from a camera at <paramref name="camera"/>.</summary>
    public Vector2 OffsetFor(Vector2 camera) => camera * (Vector2.One - Factor);
}
