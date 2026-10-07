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

    /// <summary>True if the circle and the box share any area.</summary>
    public bool Overlaps(Vector2 center, float radius) =>
        Vector2.DistanceSquared(Vector2.Clamp(center, Min, Max), center) < radius * radius;
}

/// <summary>A circle in world space.</summary>
public readonly record struct Circle(Vector2 Center, float Radius)
{
    /// <summary>The smallest box around the circle.</summary>
    public Box Bounds => new(Center, new Vector2(Radius));

    public bool Contains(Vector2 point) => Vector2.DistanceSquared(Center, point) <= Radius * Radius;

    public bool Overlaps(Circle other) =>
        Vector2.DistanceSquared(Center, other.Center) < (Radius + other.Radius) * (Radius + other.Radius);

    public bool Overlaps(Box box) => box.Overlaps(Center, Radius);
}

/// <summary>Where a ray first touched a collider or a solid tilemap.</summary>
/// <param name="Normal">The surface direction at the hit, pointing back toward the ray (e.g. up for a floor).</param>
/// <param name="Distance">World units from the ray's origin to <paramref name="Point"/>.</param>
public readonly record struct RaycastHit(Entity Entity, Vector2 Point, Vector2 Normal, float Distance);

/// <summary>
/// Simple, predictable 2D physics for jam games. Bodies move one axis at a time and are pushed out of
/// solid colliders and solid tilemaps, which gives tight platformer and top-down movement without tunnelling
/// at normal speeds. Boxes and circles collide with each other in any combination.
/// </summary>
public sealed class Physics
{
    // Bounces slower than this (world units per second) settle to a stop, so resting bodies don't jitter.
    private const float SettleSpeed = 1f;

    // Circles touching this little are treated as just touching, so they slide over seams instead of snagging.
    private const float CircleSkin = 1e-4f;

    private HashSet<(Entity Trigger, Entity Other)> _triggerPairs = [];

    // Where each grounded body's ground was at the end of the last step, so riders follow it when it moves.
    private Dictionary<Body, (Entity Ground, Vector2 Position)> _grounds = [];

    /// <summary>Each trigger and the collider overlapping it, as of the last step.</summary>
    internal IReadOnlyCollection<(Entity Trigger, Entity Other)> TriggerPairs => _triggerPairs;

    /// <summary>World units per second squared. Negative Y is down.</summary>
    public Vector2 Gravity { get; set; } = new(0, -25f);

    /// <summary>Forgets trigger overlaps, e.g. when the scene changes.</summary>
    public void Reset()
    {
        _triggerPairs = [];
        _grounds = [];
    }

    /// <summary>World-space box of a collider, from its entity's position and scale.</summary>
    public static Box BoundsOf(BoxCollider collider)
    {
        var world = collider.Entity!.Transform.WorldMatrix;
        var scale = ScaleOf(world);
        var center = new Vector2(world.M41, world.M42) + collider.Offset * scale;
        return new Box(center, collider.Size * scale / 2);
    }

    /// <summary>World-space circle of a collider. The radius scales with the larger of the entity's X and Y scale.</summary>
    public static Circle BoundsOf(CircleCollider collider)
    {
        var world = collider.Entity!.Transform.WorldMatrix;
        var scale = ScaleOf(world);
        var center = new Vector2(world.M41, world.M42) + collider.Offset * scale;
        return new Circle(center, collider.Radius * Math.Max(scale.X, scale.Y));
    }

    /// <summary>
    /// World-space boxes covering a tilemap's colliding tiles, one per horizontal run. Like colliders, they follow the
    /// entity's position and scale but ignore its rotation.
    /// </summary>
    public static IEnumerable<Box> BoundsOf(Tilemap tilemap) => TileShapes(tilemap).Select(t => t.Box);

    /// <summary>
    /// Like <see cref="BoundsOf(Tilemap)"/>, also saying which runs are one-way platforms. Empty unless the map is
    /// <see cref="Tilemap.Solid"/>; background tiles are left out.
    /// </summary>
    public static IEnumerable<(Box Box, bool OneWay)> TileShapes(Tilemap tilemap)
    {
        if (!tilemap.Solid)
            yield break;

        var world = tilemap.Entity!.Transform.WorldMatrix;
        var scale = ScaleOf(world);
        var origin = new Vector2(world.M41, world.M42);
        var size = tilemap.TileSize;
        foreach (var (x, y, length, oneWay) in tilemap.CollisionRuns())
        {
            var center = new Vector2((x + length / 2f) * size, -(y + 0.5f) * size);
            yield return (new Box(origin + center * scale, new Vector2(length * size, size) * scale / 2), oneWay);
        }
    }

    /// <summary>
    /// Entities whose colliders (or solid tiles) contain <paramref name="point"/>. With <paramref name="layers"/>
    /// (e.g. "Enemy, Crate"), only colliders and tiles on those layers count.
    /// </summary>
    public IEnumerable<Entity> OverlapPoint(Scene scene, Vector2 point, string? layers = null) =>
        Shapes(scene, includeTriggers: true, layers).Where(s => s.Area.Contains(point)).Select(s => s.Entity).Distinct();

    /// <summary>Entities whose colliders (or solid tiles) overlap the box centered on <paramref name="center"/>.</summary>
    public IEnumerable<Entity> OverlapBox(Scene scene, Vector2 center, Vector2 size, string? layers = null)
    {
        var box = Area.Of(new Box(center, size / 2));
        return Shapes(scene, includeTriggers: true, layers).Where(s => s.Area.Overlaps(box)).Select(s => s.Entity).Distinct();
    }

    /// <summary>Entities whose colliders (or solid tiles) overlap the circle, e.g. for an explosion's reach.</summary>
    public IEnumerable<Entity> OverlapCircle(Scene scene, Vector2 center, float radius, string? layers = null)
    {
        var circle = Area.Of(new Circle(center, radius));
        return Shapes(scene, includeTriggers: true, layers).Where(s => s.Area.Overlaps(circle)).Select(s => s.Entity).Distinct();
    }

    /// <summary>
    /// The first solid collider or tile along a ray, or null if nothing is within <paramref name="maxDistance"/>.
    /// Colliders the ray starts inside are skipped, as are <paramref name="ignore"/> and its children
    /// (typically the entity casting the ray). Triggers are skipped unless <paramref name="includeTriggers"/> is set.
    /// With <paramref name="layers"/> (e.g. "Default, Player" for an enemy's line of sight), only those layers are hit.
    /// </summary>
    public RaycastHit? Raycast(Scene scene, Vector2 origin, Vector2 direction, float maxDistance = float.PositiveInfinity,
        Entity? ignore = null, bool includeTriggers = false, string? layers = null)
    {
        if (direction == Vector2.Zero || !(maxDistance >= 0))
            return null;
        direction = Vector2.Normalize(direction);

        RaycastHit? best = null;
        foreach (var shape in Shapes(scene, includeTriggers, layers))
        {
            if (ignore is not null && shape.Entity.IsSelfOrDescendantOf(ignore))
                continue;
            if (shape.Area.Intersect(origin, direction) is not var (distance, normal) || distance > maxDistance)
                continue;
            if (best is null || distance < best.Value.Distance)
                best = new RaycastHit(shape.Entity, origin + direction * distance, normal, distance);
        }

        return best;
    }

    internal void Step(Scene scene, float dt, Action<Entity, Entity> onCollision, Action<Entity, Entity, bool> onTrigger)
    {
        var colliders = ActiveColliders(scene).ToList();
        var solids = Shapes(scene, includeTriggers: false).ToList();

        foreach (var body in scene.Entities.Where(SceneRenderer.IsActive).Select(e => e.GetComponent<Body>()).OfType<Body>().ToList())
        {
            var collider = body.Entity!.GetComponent<Collider>();
            body.Velocity += Gravity * body.GravityScale * dt;
            body.IsGrounded = false;
            body.Ground = null;

            if (collider is null || collider.IsTrigger)
            {
                Translate(body.Entity, body.Velocity * dt);
                continue;
            }

            // Ride along with whatever it stood on, if that has moved since (a moving platform).
            if (_grounds.TryGetValue(body, out var ground) && ground.Ground.Scene == scene && SceneRenderer.IsActive(ground.Ground))
            {
                var carry = PositionOf(ground.Ground) - ground.Position;
                MoveAxis(body, collider, solids, new Vector2(carry.X, 0), onCollision, carrier: ground.Ground);
                MoveAxis(body, collider, solids, new Vector2(0, carry.Y), onCollision, carrier: ground.Ground);
            }

            MoveAxis(body, collider, solids, new Vector2(body.Velocity.X * dt, 0), onCollision);
            MoveAxis(body, collider, solids, new Vector2(0, body.Velocity.Y * dt), onCollision);
        }

        _grounds = scene.Entities.Select(e => e.GetComponent<Body>()).OfType<Body>().Where(b => b.Ground is not null)
            .ToDictionary(b => b, b => (b.Ground!, PositionOf(b.Ground!)));
        UpdateTriggers(colliders, onTrigger);
    }

    private static Vector2 PositionOf(Entity entity)
    {
        var position = entity.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }

    /// <param name="carrier">
    /// When set, the body is being carried by this entity: it doesn't collide with it, and hitting something else
    /// just stops the carry without touching the body's velocity or reporting a collision.
    /// </param>
    private static void MoveAxis(Body body, Collider collider, List<Shape> solids, Vector2 delta, Action<Entity, Entity> onCollision,
        Entity? carrier = null)
    {
        if (delta == Vector2.Zero)
            return;

        var entity = body.Entity!;
        var axis = delta.X != 0 ? 0 : 1;
        var sign = MathF.Sign(axis == 0 ? delta.X : delta.Y);
        var bottomBefore = Area.Of(collider).Bounds.Min.Y;
        var filter = LayerFilter.Of(collider);
        Translate(entity, delta);
        var area = Area.Of(collider);
        var hit = false;

        foreach (var solid in solids)
        {
            if (solid.Entity == entity || solid.Entity.IsSelfOrDescendantOf(entity) || solid.Entity == carrier ||
                filter.IgnoresEachOther(solid.Filter))
                continue;

            var other = solid.Area;
            if (!area.Overlaps(other, CircleSkin))
                continue;

            // One-way: only catch bodies falling onto the top that started above it.
            if (solid.OneWay && (delta.Y >= 0 || bottomBefore < other.Bounds.Max.Y - 0.001f))
                continue;

            // Push back along the axis we moved, to where the two shapes just touch.
            var target = Component(other.Center, axis) - sign * area.ContactDistance(other, axis);
            var push = target - Component(area.Center, axis);
            Translate(entity, axis == 0 ? new Vector2(push, 0) : new Vector2(0, push));
            area = Area.Of(collider);
            hit = true;
            if (carrier is not null)
                continue;
            if (axis == 1 && sign < 0)
            {
                body.IsGrounded = true;
                body.Ground = solid.Entity;
            }

            onCollision(entity, solid.Entity);
        }

        // Bounce (or stop) once per axis, however many solids were touched.
        if (hit && carrier is null)
        {
            var velocity = body.Velocity;
            var speed = (axis == 0 ? velocity.X : velocity.Y) * -Math.Clamp(body.Bounciness, 0, 1);
            if (MathF.Abs(speed) < SettleSpeed)
                speed = 0;
            body.Velocity = axis == 0 ? velocity with { X = speed } : velocity with { Y = speed };
        }
    }

    private void UpdateTriggers(List<Collider> colliders, Action<Entity, Entity, bool> onTrigger)
    {
        var current = new HashSet<(Entity, Entity)>();
        var filters = colliders.ToDictionary(c => c, LayerFilter.Of);
        foreach (var trigger in colliders.Where(c => c.IsTrigger))
        {
            var area = Area.Of(trigger);
            foreach (var other in colliders)
            {
                if (other == trigger || other.Entity == trigger.Entity || filters[trigger].IgnoresEachOther(filters[other]) ||
                    !area.Overlaps(Area.Of(other)))
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

    // Everything physics can touch: colliders, and the tiles of solid tilemaps. With layers, only shapes on those layers.
    private static IEnumerable<Shape> Shapes(Scene scene, bool includeTriggers, string? layers = null)
    {
        var only = layers is null ? null : CollisionLayers.Parse(layers);
        foreach (var entity in scene.Entities.Where(SceneRenderer.IsActive))
        {
            foreach (var component in entity.Components)
            {
                if (component is Collider collider && (includeTriggers || !collider.IsTrigger))
                {
                    if (only is null || CollisionLayers.Contains(only, collider.Layer))
                        yield return new Shape(entity, collider, default, false, LayerFilter.Of(collider));
                }
                else if (component is Tilemap { Solid: true } tilemap && (only is null || CollisionLayers.Contains(only, tilemap.Layer)))
                {
                    var filter = new LayerFilter(tilemap.Layer, CollisionLayers.Parse(tilemap.IgnoreLayers));
                    foreach (var (box, oneWay) in TileShapes(tilemap))
                        yield return new Shape(entity, null, box, oneWay, filter);
                }
            }
        }
    }

    private static IEnumerable<Collider> ActiveColliders(Scene scene) =>
        scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Collider>());

    private static Vector2 ScaleOf(Matrix4x4 world) => new(
        new Vector2(world.M11, world.M12).Length(),
        new Vector2(world.M21, world.M22).Length());

    private static float Component(Vector2 v, int axis) => axis == 0 ? v.X : v.Y;

    // Moves by a world-space delta, converting into the parent's space so nested bodies work.
    private static void Translate(Entity entity, Vector2 worldDelta)
    {
        var delta = new Vector3(worldDelta, 0);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            delta = Vector3.TransformNormal(delta, inverse);
        entity.Transform.Position += delta;
    }

    // A collider's area follows its entity as bodies move during a step; tile boxes stay put.
    private readonly record struct Shape(Entity Entity, Collider? Collider, Box TileBox, bool TileOneWay, LayerFilter Filter)
    {
        public Area Area => Collider is null ? Area.Of(TileBox) : Area.Of(Collider);

        public bool OneWay => Collider is null ? TileOneWay : Collider is BoxCollider { OneWay: true };
    }

    // A shape's layer and the layers it passes through.
    private readonly record struct LayerFilter(string? Layer, string[] Ignores)
    {
        public static LayerFilter Of(Collider collider) => new(collider.Layer, CollisionLayers.Parse(collider.IgnoreLayers));

        public bool IgnoresEachOther(LayerFilter other) =>
            (Ignores.Length > 0 && CollisionLayers.Contains(Ignores, other.Layer)) ||
            (other.Ignores.Length > 0 && CollisionLayers.Contains(other.Ignores, Layer));
    }

    // A box or a circle in world space. For a circle, Bounds is the box around it.
    private readonly record struct Area(Box Bounds, float Radius, bool IsCircle)
    {
        public Vector2 Center => Bounds.Center;

        public Circle Circle => new(Bounds.Center, Radius);

        public static Area Of(Box box) => new(box, 0, false);

        public static Area Of(Circle circle) => new(circle.Bounds, circle.Radius, true);

        public static Area Of(Collider collider) => collider switch
        {
            BoxCollider box => Of(BoundsOf(box)),
            CircleCollider circle => Of(BoundsOf(circle)),
            _ => throw new NotSupportedException($"Unknown collider {collider.GetType().Name}."),
        };

        public bool Contains(Vector2 point) => IsCircle ? Circle.Contains(point) : Bounds.Contains(point);

        /// <param name="skin">Overlaps shallower than this don't count, for shapes involving a circle.</param>
        public bool Overlaps(Area other, float skin = 0)
        {
            if (!IsCircle && !other.IsCircle)
                return Bounds.Overlaps(other.Bounds);
            if (IsCircle && other.IsCircle)
                return new Circle(Center, Radius - skin).Overlaps(other.Circle);
            var (box, circle) = IsCircle ? (other.Bounds, Circle) : (Bounds, other.Circle);
            return box.Overlaps(circle.Center, circle.Radius - skin);
        }

        /// <summary>
        /// How far apart the centers are along <paramref name="axis"/> (0 = X, 1 = Y) when the shapes just touch,
        /// keeping their offset on the other axis.
        /// </summary>
        public float ContactDistance(Area other, int axis)
        {
            var across = 1 - axis;
            if (!IsCircle && !other.IsCircle)
                return Component(Bounds.HalfSize, axis) + Component(other.Bounds.HalfSize, axis);

            if (IsCircle && other.IsCircle)
            {
                var offset = Component(Center, across) - Component(other.Center, across);
                var reach = Radius + other.Radius;
                return MathF.Sqrt(MathF.Max(0, reach * reach - offset * offset));
            }

            // A box and a circle: the circle meets the box's face, or its corner when it's off to the side.
            var (box, circle) = IsCircle ? (other.Bounds, Circle) : (Bounds, other.Circle);
            var c =Component(circle.Center, across);
            var beyond = MathF.Max(0, MathF.Max(Component(box.Min, across) - c, c - Component(box.Max, across)));
            return Component(box.HalfSize, axis) + MathF.Sqrt(MathF.Max(0, circle.Radius * circle.Radius - beyond * beyond));
        }

        /// <summary>The entry distance and surface normal along a normalized ray, or null on a miss or when starting inside.</summary>
        public (float Distance, Vector2 Normal)? Intersect(Vector2 origin, Vector2 direction) =>
            IsCircle ? IntersectCircle(Circle, origin, direction) : IntersectBox(Bounds, origin, direction);

        private static (float, Vector2)? IntersectCircle(Circle circle, Vector2 origin, Vector2 direction)
        {
            var m = origin - circle.Center;
            var b = Vector2.Dot(m, direction);
            var c = m.LengthSquared() - circle.Radius * circle.Radius;
            if (c < 0)
                return null; // starts inside
            if (b > 0)
                return null; // starts outside, pointing away
            var discriminant = b * b - c;
            if (discriminant < 0)
                return null;

            var distance = MathF.Max(0, -b - MathF.Sqrt(discriminant));
            var normal = origin + direction * distance - circle.Center;
            return (distance, normal == Vector2.Zero ? -direction : Vector2.Normalize(normal));
        }

        // Slab test. Returns the entry distance and the face's normal, or null on a miss or when starting inside.
        private static (float, Vector2)? IntersectBox(Box box, Vector2 origin, Vector2 direction)
        {
            var enter = float.NegativeInfinity;
            var exit = float.PositiveInfinity;
            var normal = Vector2.Zero;

            for (var axis = 0; axis < 2; axis++)
            {
                var o = Component(origin, axis);
                var d = Component(direction, axis);
                var min = Component(box.Min, axis);
                var max = Component(box.Max, axis);

                if (d == 0)
                {
                    if (o < min || o > max)
                        return null;
                    continue;
                }

                var t1 = (min - o) / d;
                var t2 = (max - o) / d;
                if (t1 > t2)
                    (t1, t2) = (t2, t1);
                if (t1 > enter)
                {
                    enter = t1;
                    normal = axis == 0 ? new Vector2(-MathF.Sign(d), 0) : new Vector2(0, -MathF.Sign(d));
                }

                exit = Math.Min(exit, t2);
            }

            // enter < 0 means the origin is inside (touching an edge counts as a hit at distance 0).
            if (enter > exit || enter < 0 || normal == Vector2.Zero)
                return null;
            return (enter, normal);
        }
    }
}
