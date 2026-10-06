using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>An axis-aligned rectangle in world space.</summary>
public readonly record struct Box(Vector2 Center, Vector2 HalfSize)
{
    public Vector2 Min => Center - HalfSize;

    public Vector2 Max => Center + HalfSize;

    public bool Overlaps(Box other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
}

/// <summary>
/// Simple, predictable 2D physics for jam games. Bodies move one axis at a time and are pushed out of
/// solid colliders, which gives tight platformer and top-down movement without tunnelling at normal speeds.
/// </summary>
public sealed class Physics
{
    private HashSet<(Entity Trigger, Entity Other)> _triggerPairs = [];

    /// <summary>World units per second squared. Negative Y is down.</summary>
    public Vector2 Gravity { get; set; } = new(0, -25f);

    /// <summary>World-space box of a collider, from its entity's position and scale.</summary>
    public static Box BoundsOf(BoxCollider collider)
    {
        var transform = collider.Entity!.Transform;
        var world = transform.WorldMatrix;
        var scale = new Vector2(
            new Vector2(world.M11, world.M12).Length(),
            new Vector2(world.M21, world.M22).Length());
        var center = new Vector2(world.M41, world.M42) + collider.Offset * scale;
        return new Box(center, collider.Size * scale / 2);
    }

    /// <summary>Entities whose colliders contain <paramref name="point"/>.</summary>
    public IEnumerable<Entity> OverlapPoint(Scene scene, Vector2 point) =>
        ActiveColliders(scene).Where(c => BoundsOf(c).Contains(point)).Select(c => c.Entity!).Distinct();

    /// <summary>Entities whose colliders overlap the box centered on <paramref name="center"/>.</summary>
    public IEnumerable<Entity> OverlapBox(Scene scene, Vector2 center, Vector2 size)
    {
        var box = new Box(center, size / 2);
        return ActiveColliders(scene).Where(c => BoundsOf(c).Overlaps(box)).Select(c => c.Entity!).Distinct();
    }

    internal void Step(Scene scene, float dt, Action<Entity, Entity> onCollision, Action<Entity, Entity, bool> onTrigger)
    {
        var colliders = ActiveColliders(scene).ToList();
        var solids = colliders.Where(c => !c.IsTrigger).ToList();

        foreach (var body in scene.Entities.Where(SceneRenderer.IsActive).Select(e => e.GetComponent<Body>()).OfType<Body>().ToList())
        {
            var collider = body.Entity!.GetComponent<BoxCollider>();
            body.Velocity += Gravity * body.GravityScale * dt;
            body.IsGrounded = false;

            if (collider is null || collider.IsTrigger)
            {
                Translate(body.Entity, body.Velocity * dt);
                continue;
            }

            MoveAxis(body, collider, solids, new Vector2(body.Velocity.X * dt, 0), onCollision);
            MoveAxis(body, collider, solids, new Vector2(0, body.Velocity.Y * dt), onCollision);
        }

        UpdateTriggers(colliders, onTrigger);
    }

    private static void MoveAxis(Body body, BoxCollider collider, List<BoxCollider> solids, Vector2 delta, Action<Entity, Entity> onCollision)
    {
        if (delta == Vector2.Zero)
            return;

        var entity = body.Entity!;
        Translate(entity, delta);
        var box = BoundsOf(collider);

        foreach (var solid in solids)
        {
            if (solid.Entity == entity || solid.Entity!.IsSelfOrDescendantOf(entity))
                continue;

            var other = BoundsOf(solid);
            if (!box.Overlaps(other))
                continue;

            // Push back out along the axis we moved, then stop that component of velocity.
            Vector2 push;
            if (delta.X != 0)
            {
                push = new Vector2(delta.X > 0 ? other.Min.X - box.Max.X : other.Max.X - box.Min.X, 0);
                body.Velocity = body.Velocity with { X = 0 };
            }
            else
            {
                push = new Vector2(0, delta.Y > 0 ? other.Min.Y - box.Max.Y : other.Max.Y - box.Min.Y);
                if (delta.Y < 0)
                    body.IsGrounded = true;
                body.Velocity = body.Velocity with { Y = 0 };
            }

            Translate(entity, push);
            box = BoundsOf(collider);
            onCollision(entity, solid.Entity);
        }
    }

    private void UpdateTriggers(List<BoxCollider> colliders, Action<Entity, Entity, bool> onTrigger)
    {
        var current = new HashSet<(Entity, Entity)>();
        foreach (var trigger in colliders.Where(c => c.IsTrigger))
        {
            var box = BoundsOf(trigger);
            foreach (var other in colliders)
            {
                if (other == trigger || other.Entity == trigger.Entity || !box.Overlaps(BoundsOf(other)))
                    continue;
                current.Add((trigger.Entity!, other.Entity!));
            }
        }

        foreach (var pair in current.Except(_triggerPairs))
            onTrigger(pair.Item1, pair.Item2, true);
        foreach (var pair in _triggerPairs.Except(current))
            onTrigger(pair.Item1, pair.Item2, false);
        _triggerPairs = current;
    }

    private static IEnumerable<BoxCollider> ActiveColliders(Scene scene) =>
        scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<BoxCollider>());

    // Moves by a world-space delta, converting into the parent's space so nested bodies work.
    private static void Translate(Entity entity, Vector2 worldDelta)
    {
        var delta = new Vector3(worldDelta, 0);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            delta = Vector3.TransformNormal(delta, inverse);
        entity.Transform.Position += delta;
    }
}
