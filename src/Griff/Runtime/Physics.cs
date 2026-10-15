using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>Where a ray first touched a collider or a solid tilemap.</summary>
/// <param name="Normal">The surface direction at the hit, pointing back toward the ray (e.g. up for a floor).</param>
/// <param name="Distance">World units from the ray's origin to <paramref name="Point"/>.</param>
public readonly record struct RaycastHit(Entity Entity, Vector2 Point, Vector2 Normal, float Distance);

/// <summary>
/// Simple, predictable 2D physics for jam games. Bodies move one axis at a time and are pushed out of
/// solid colliders and solid tilemaps, which gives tight platformer and top-down movement without tunnelling
/// at normal speeds. Boxes, circles and polygons collide with each other in any combination. On an isometric tilemap a body
/// stands on the diamond under its collider and slides along the solid diamonds' edges (see <see cref="IsometricShapes"/>);
/// on a hex tilemap its collider meets the solid hexagons' six sides and slides along them (see <see cref="HexShapes"/>).
/// A body that <see cref="Body.Rotates"/> is a rigid shape instead: it's pushed apart from what it touches at the points
/// where they touch, so it spins, tips over and falls flat, and its box or polygon turns with it; everything else meets it
/// turned, walks up its slant, and knocks it about.
/// </summary>
public sealed partial class Physics
{
    // Bounces slower than this (world units per second) settle to a stop, so resting bodies don't jitter.
    private const float SettleSpeed = 1f;

    // Shapes touching this little are treated as just touching, so circles slide over seams instead of snagging and boxes
    // resting at a rounded-off height don't catch on the floor they stand on.
    private const float CircleSkin = 1e-4f;

    // A crate can push a crate that pushes a crate, and no further.
    private const int MaxPushChain = 4;

    private HashSet<(Entity Trigger, Entity Other)> _triggerPairs = [];
    private HashSet<(Entity Trigger, Entity Other)> _lastTriggerPairs = [];

    // Where each grounded body's ground was at the end of the last step, so riders follow it when it moves.
    private readonly Dictionary<Body, (Entity Ground, Vector2 Position)> _grounds = [];

    // The slope tiles bodies walk on during a step.
    private readonly List<Slope> _slopes = [];

    // What a step works with, kept from one to the next so a step allocates as little as it can.
    private readonly Solids _solids = new();
    private readonly Dictionary<Body, BodyGroup> _groupOf = [];
    private readonly HashSet<BodyGroup> _movedGroups = [];
    private readonly ShapeGrid _triggerGrid = new();
    private readonly List<(Collider Collider, Area Area, LayerFilter Filter)> _triggerShapes = [];
    private readonly List<int> _near = [];
    private Func<int, Box>? _triggerBounds;

    /// <summary>Each trigger and the collider overlapping it, as of the last step.</summary>
    internal HashSet<(Entity Trigger, Entity Other)> TriggerPairs => _triggerPairs;

    /// <summary>The <see cref="Gravity"/> a game starts with: 25 units per second squared, down.</summary>
    public static readonly Vector2 DefaultGravity = new(0, -25f);

    /// <summary>World units per second squared. Negative Y is down.</summary>
    public Vector2 Gravity { get; set; } = DefaultGravity;

    /// <summary>Forgets trigger overlaps, e.g. when the scene changes.</summary>
    public void Reset()
    {
        _triggerPairs = [];
        _lastTriggerPairs = [];
        _grounds.Clear();
    }

    /// <summary>
    /// World-space box of a collider, from its entity's position and scale: for one that turns with its body, the box round
    /// it as it's turned (see <see cref="BoxCollider.WorldShape"/>).
    /// </summary>
    public static Box BoundsOf(BoxCollider collider) => collider.WorldBox;

    /// <summary>World-space circle of a collider. The radius scales with the larger of the entity's X and Y scale.</summary>
    public static Circle BoundsOf(CircleCollider collider) => collider.WorldCircle;

    /// <summary>The world-space box around any kind of collider.</summary>
    public static Box BoundsOf(Collider collider) => collider switch
    {
        BoxCollider box => BoundsOf(box),
        CircleCollider circle => BoundsOf(circle).Bounds,
        PolygonCollider polygon => polygon.WorldBounds,
        _ => throw new NotSupportedException($"Unknown collider {collider.GetType().Name}."),
    };

    /// <summary>
    /// World-space boxes covering a tilemap's colliding tiles, one per horizontal run. Like colliders that don't turn, they
    /// follow the entity's position and scale but ignore its rotation.
    /// </summary>
    public static IEnumerable<Box> BoundsOf(Tilemap tilemap) => TileShapes(tilemap).Select(t => t.Box);

    /// <summary>
    /// Like <see cref="BoundsOf(Tilemap)"/>, also saying which runs are one-way platforms. Empty unless the map is
    /// <see cref="Tilemap.Solid"/> and square (an isometric map's are diamonds: see <see cref="IsometricShapes"/>, and a hex
    /// map's hexagons: see <see cref="HexShapes"/>);
    /// background tiles are left out.
    /// </summary>
    public static IEnumerable<(Box Box, bool OneWay)> TileShapes(Tilemap tilemap)
    {
        if (!tilemap.Solid || tilemap.Shape != TileShape.Square)
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
    /// World-space cells of a tilemap's slope tiles and which way each rises (see <see cref="Tilemap.SlopeUpTiles"/>);
    /// each is solid below the line across it. Empty unless the map is <see cref="Tilemap.Solid"/> (and square).
    /// </summary>
    public static IEnumerable<(Box Cell, bool Rising)> SlopeShapes(Tilemap tilemap)
    {
        if (!tilemap.Solid || tilemap.Shape != TileShape.Square)
            yield break;

        var world = tilemap.Entity!.Transform.WorldMatrix;
        var scale = ScaleOf(world);
        var origin = new Vector2(world.M41, world.M42);
        var size = tilemap.TileSize;
        foreach (var (x, y, rising) in tilemap.SlopeCells())
        {
            var center = new Vector2((x + 0.5f) * size, -(y + 0.5f) * size);
            yield return (new Box(origin + center * scale, new Vector2(size) * scale / 2), rising);
        }
    }

    /// <summary>
    /// Entities whose colliders (or solid tiles) contain <paramref name="point"/>. With <paramref name="layers"/>
    /// (e.g. "Enemy, Crate"), only colliders and tiles on those layers count.
    /// </summary>
    public IEnumerable<Entity> OverlapPoint(Scene scene, Vector2 point, string? layers = null) =>
        Shapes(scene, includeTriggers: true, layers).Where(s => s.Area.Contains(point)).Select(s => s.Entity)
            .Concat(Isometric(scene, layers).Where(w => w.BlockedAt(point)).Select(w => w.Map.Entity!))
            .Concat(Hex(scene, layers).Where(w => w.BlockedAt(point)).Select(w => w.Map.Entity!)).Distinct();

    /// <summary>Entities whose colliders (or solid tiles) overlap the box centered on <paramref name="center"/>.</summary>
    public IEnumerable<Entity> OverlapBox(Scene scene, Vector2 center, Vector2 size, string? layers = null)
    {
        var box = Area.Of(new Box(center, size / 2));
        return Shapes(scene, includeTriggers: true, layers).Where(s => s.Area.Overlaps(box)).Select(s => s.Entity)
            .Concat(Isometric(scene, layers).Where(w => w.Overlaps(box.Bounds)).Select(w => w.Map.Entity!))
            .Concat(Hex(scene, layers).Where(w => w.Overlaps(box.Bounds)).Select(w => w.Map.Entity!)).Distinct();
    }

    /// <summary>Entities whose colliders (or solid tiles) overlap the circle, e.g. for an explosion's reach.</summary>
    public IEnumerable<Entity> OverlapCircle(Scene scene, Vector2 center, float radius, string? layers = null)
    {
        var circle = Area.Of(new Circle(center, radius));
        return Shapes(scene, includeTriggers: true, layers).Where(s => s.Area.Overlaps(circle)).Select(s => s.Entity)
            .Concat(Isometric(scene, layers).Where(w => w.Overlaps(circle.Circle)).Select(w => w.Map.Entity!))
            .Concat(Hex(scene, layers).Where(w => w.Overlaps(circle.Circle)).Select(w => w.Map.Entity!)).Distinct();
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

        foreach (var slope in Slopes(scene, layers))
        {
            if (ignore is not null && slope.Entity.IsSelfOrDescendantOf(ignore))
                continue;
            if (slope.Intersect(origin, direction) is not var (distance, normal) || distance > maxDistance)
                continue;
            if (best is null || distance < best.Value.Distance)
                best = new RaycastHit(slope.Entity, origin + direction * distance, normal, distance);
        }

        foreach (var walls in Isometric(scene, layers))
        {
            if (ignore is not null && walls.Map.Entity!.IsSelfOrDescendantOf(ignore))
                continue;
            var reach = best is { } nearest ? Math.Min(maxDistance, nearest.Distance) : maxDistance;
            if (walls.Raycast(origin, direction, reach) is not var (distance, normal))
                continue;
            if (best is null || distance < best.Value.Distance)
                best = new RaycastHit(walls.Map.Entity!, origin + direction * distance, normal, distance);
        }

        foreach (var walls in Hex(scene, layers))
        {
            if (ignore is not null && walls.Map.Entity!.IsSelfOrDescendantOf(ignore))
                continue;
            var reach = best is { } nearest ? Math.Min(maxDistance, nearest.Distance) : maxDistance;
            if (walls.Raycast(origin, direction, reach) is not var (distance, normal))
                continue;
            if (best is null || distance < best.Value.Distance)
                best = new RaycastHit(walls.Map.Entity!, origin + direction * distance, normal, distance);
        }

        return best;
    }

    /// <summary>
    /// Bodies welded together (a contraption's Welds), set by the game before each step: each group is one rigid body, worked
    /// out with the bodies that turn (it turns as one when all its members rotate, round where its weight balances), and its
    /// members don't collide with each other. An anchored group (welded to something without a body, or with a frozen
    /// member) holds still.
    /// </summary>
    internal IReadOnlyList<BodyGroup> Groups { get; set; } = [];

    internal void Step(Scene scene, float dt, Action<Entity, Entity> onCollision, Action<Entity, Entity, bool> onTrigger)
    {
        var solids = _solids;
        solids.Fill(scene);
        _slopes.Clear();
        _isometric.Clear();
        _hex.Clear();
        if (scene.Active<Tilemap>().Count > 0)
        {
            _slopes.AddRange(Slopes(scene));
            CollectIsometric(scene, null, _isometric);
            CollectHex(scene, null, _hex);
        }
        _groupOf.Clear();
        var groups = Groups;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var (still, turns) = (group.Anchored, true);
            foreach (var member in group.Bodies)
            {
                _groupOf[member] = group;
                if (member.Entity is { IsActive: true })
                    (still, turns) = (still || member.Frozen, turns && member.Rotates);
            }

            (group.Still, group.Turns) = (still, turns);
        }

        CollectJoints(scene);
        CollectRopes(scene);
        _movedGroups.Clear();
        _turning.Clear();
        _turningGroups.Clear();
        foreach (var body in scene.Active<Body>())
        {
            // An entity's first body is the one that moves it.
            if (body.Entity!.GetComponent<Body>() != body)
                continue;

            // Paused with its group (see Entity.IsPaused): it stays where it is, keeping its velocity for when it goes on.
            if (body.Entity.IsPaused)
                continue;

            // In a network game, another machine moves this one (a player's own character, or the host's copy of
            // anything): leave it where it's put. On the host, what another player's character touches still counts.
            if (!body.Entity!.MovesHere)
            {
                if (body.Entity!.NetFlags.HasFlag(NetFlags.RemoteMoved))
                    ReportContacts(body, solids, onCollision);
                continue;
            }

            // A player on a rope goes where they hold it: the rope moves them, with the bodies that turn.
            if (Climbs(body))
                continue;

            // A welded group is one body: held still with what holds it, or worked out with the bodies that turn.
            if (_groupOf.TryGetValue(body, out var group))
            {
                if (_movedGroups.Add(group) && !AnyPaused(group))
                {
                    if (group.Still)
                        HoldStill(group, dt);
                    else
                        _turningGroups.Add(group);
                }

                continue;
            }

            if (body.Frozen)
            {
                body.Velocity = Vector2.Zero;
                body.Spin = 0;
                body.IsGrounded = false;
                body.Ground = null;
                body.WallSide = 0;
                EndStep(body, dt);
                continue;
            }

            // Bodies that turn (or hang on a hinge) are worked out together, once the rest have moved.
            var collider = body.Entity!.GetComponent<Collider>();
            if ((body.Rotates || _jointed.Count > 0 && _jointed.Contains(body)) && collider is { IsTrigger: false })
            {
                _turning.Add(body);
                continue;
            }

            if (!body.FloatThisStep)
                body.Velocity += Gravity * body.GravityScale * dt;
            var wasGrounded = body.IsGrounded;
            var lastGround = body.Ground;
            body.IsGrounded = false;
            body.Ground = null;
            body.WallSide = 0;

            if (collider is null || collider.IsTrigger)
            {
                Translate(solids, body.Entity, body.Velocity * dt);
                if (body.Rotates)
                    Turn(solids, body.Entity, collider, body.Spin * dt * MathF.PI / 180);
                EndStep(body, dt);
                continue;
            }

            // Where it started, for keeping it off isometric and hex maps' walls once it has moved.
            var from = _isometric.Count > 0 || _hex.Count > 0 ? Area.Of(collider).Bounds.Center : default;

            // Ride along with whatever it stood on, if that has moved since (a moving platform).
            if (_grounds.TryGetValue(body, out var ground) && ground.Ground.Scene == scene && ground.Ground.IsActive)
            {
                var carry = PositionOf(ground.Ground) - ground.Position;
                MoveAxis(body, collider, solids, new Vector2(carry.X, 0), onCollision, carrier: ground.Ground);
                MoveAxis(body, collider, solids, new Vector2(0, carry.Y), onCollision, carrier: ground.Ground);
            }

            MoveAxis(body, collider, solids, new Vector2(body.Velocity.X * dt, 0), onCollision);
            MoveAxis(body, collider, solids, new Vector2(0, body.Velocity.Y * dt), onCollision);
            if (body.IsGrounded)
                Rub(body, body.Friction, dt);

            // Walking down a slope, keep to it rather than skipping off into the air a little each step.
            if (wasGrounded && !body.IsGrounded && body.Velocity.Y <= 0 && _slopes.Count > 0)
            {
                var reach = MathF.Abs(body.Velocity.X * dt) * 1.05f + 0.05f;
                if (SlopeBelow(collider, reach))
                    MoveAxis(body, collider, solids, new Vector2(0, -reach), onCollision);
            }

            // So too down a turned plank or crate, or a polygon's slant: down onto it, if it's still there.
            if (wasGrounded && !body.IsGrounded && body.Velocity.Y <= 0 && lastGround?.GetComponent<Collider>() is { Turns: true } or PolygonCollider)
            {
                var reach = MathF.Abs(body.Velocity.X * dt) * TurnedStepSlope + 0.05f;
                MoveAxis(body, collider, solids, new Vector2(0, -reach), onCollision);
                if (!body.IsGrounded)
                    Translate(solids, body.Entity, new Vector2(0, reach));
            }

            if (_isometric.Count > 0)
                KeepOffIsometricWalls(body, collider, solids, from, onCollision, carrier: null, pusher: null);
            if (_hex.Count > 0)
                KeepOffHexWalls(body, collider, solids, from, onCollision, carrier: null, pusher: null);
            EndStep(body, dt);
        }

        if (_turning.Count > 0 || _turningGroups.Count > 0 || _ropes.Count > 0)
        {
            StepTurning(scene, solids, dt, onCollision);
            foreach (var body in _turning)
                EndStep(body, dt);
            foreach (var group in _turningGroups)
            {
                var members = group.Bodies;
                for (var i = 0; i < members.Count; i++)
                {
                    if (members[i].Entity is { IsActive: true })
                        EndStep(members[i], dt);
                }
            }

            _turning.Clear();
            _turningGroups.Clear();
        }

        scene.Vacated.Clear();
        scene.VacatedEverywhere = false;

        _grounds.Clear();
        foreach (var body in scene.All<Body>())
        {
            if (body.Ground is not null && body.Entity!.GetComponent<Body>() == body)
                _grounds[body] = (body.Ground, PositionOf(body.Ground));
        }

        UpdateTriggers(scene.Active<Collider>(), onTrigger);
    }

    // Whatever a body moved by someone else is touching (or all but touching), as if it had bumped into it.
    private static void ReportContacts(Body body, Solids solids, Action<Entity, Entity> onCollision)
    {
        if (body.Entity!.GetComponent<Collider>() is not { IsTrigger: false } collider)
            return;
        var area = Area.Of(collider);
        var filter = LayerFilter.Of(collider);
        var near = solids.Near(Grow(area.Bounds, 0.1f));
        foreach (var index in near)
        {
            var solid = solids.All[index];
            if (solid.Entity.IsSelfOrDescendantOf(body.Entity) || filter.IgnoresEachOther(solid.Filter) || !area.Overlaps(solid.Area, -0.06f))
                continue;
            onCollision(body.Entity, solid.Entity);
        }

        solids.Return(near);
    }

    // A welded group held still (welded to something that doesn't move, or with a member frozen): it stays where it is.
    private static void HoldStill(BodyGroup group, float dt)
    {
        var members = group.Bodies;
        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i];
            if (body.Entity is not { IsActive: true })
                continue;
            (body.IsGrounded, body.Ground, body.WallSide) = (false, null, 0);
            (body.Velocity, body.Spin) = (Vector2.Zero, 0);
            EndStep(body, dt);
        }
    }

    // Sliding along the ground slows by `friction` units per second, every second, down to a stop.
    private static void Rub(Body body, float friction, float dt)
    {
        if (!(friction > 0))
            return;
        var x = body.Velocity.X;
        body.Velocity = body.Velocity with { X = MathF.Sign(x) * Math.Max(0, MathF.Abs(x) - friction * dt) };
    }

    /// <summary>
    /// Moves <paramref name="bodies"/> together by a world-space delta, stopping where the first of them meets something
    /// solid (not each other). Velocities are left alone and nothing is reported: for pulling things along a rope.
    /// </summary>
    internal static void Nudge(Scene scene, IReadOnlyList<Body> bodies, Vector2 delta)
    {
        if (delta == Vector2.Zero || bodies.Count == 0)
            return;
        var solids = new Solids();
        solids.Fill(scene);
        NudgeAxis(bodies, solids, new Vector2(delta.X, 0));
        NudgeAxis(bodies, solids, new Vector2(0, delta.Y));
    }

    // Like MoveAxis for a group, without bumping into anything: everyone moves, then everyone is pushed back by the deepest
    // overlap any of them made.
    private static void NudgeAxis(IReadOnlyList<Body> members, Solids solids, Vector2 delta)
    {
        if (delta == Vector2.Zero)
            return;
        var axis = delta.X != 0 ? 0 : 1;
        var sign = MathF.Sign(axis == 0 ? delta.X : delta.Y);
        foreach (var body in members)
            Translate(solids, body.Entity!, delta);

        var push = 0f;
        foreach (var body in members)
        {
            if (body.Entity!.GetComponent<Collider>() is not { IsTrigger: false } collider)
                continue;
            var area = Area.Of(collider);
            var bottomBefore = area.Bounds.Min.Y - delta.Y;
            var filter = LayerFilter.Of(collider);
            var near = solids.Near(Grow(Union(area.Bounds, Offset(area.Bounds, -delta)), Margin));
            foreach (var index in near)
            {
                var solid = solids.All[index];
                if (IsInside(solid.Entity, members) || filter.IgnoresEachOther(solid.Filter))
                    continue;
                var other = solid.Area;
                if (!area.Overlaps(other, CircleSkin))
                    continue;
                if (solid.OneWay && (delta.Y >= 0 || bottomBefore < other.Bounds.Max.Y - 0.001f || body.IsDroppingThrough))
                    continue;
                var target = Component(other.Center, axis) - sign * area.ContactDistance(other, axis, sign);
                var back = target - Component(area.Center, axis);
                if (back * sign > 0)
                    continue; // Already overlapping from the far side: not something this move ran into.
                if (MathF.Abs(back) > MathF.Abs(push))
                    push = back;
            }

            solids.Return(near);
        }

        if (push != 0)
        {
            foreach (var body in members)
                Translate(solids, body.Entity!, axis == 0 ? new Vector2(push, 0) : new Vector2(0, push));
        }
    }

    // Movement's requests last one step; dropping through platforms lasts its time.
    // A welded group holds still while any of it is paused (see Entity.IsPaused).
    private static bool AnyPaused(BodyGroup group)
    {
        var bodies = group.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].Entity is { IsPaused: true })
                return true;
        }

        return false;
    }

        private static void EndStep(Body body, float dt)
    {
        TrackAirTime(body, dt);
        body.FloatThisStep = false;
        body.PassOneWayThisStep = false;
        body.DropThroughLeft = Math.Max(0, body.DropThroughLeft - dt);
    }

    // For Body.WasGrounded: how long since it stood on something, and whether it has gone up (jumped) since.
    private static void TrackAirTime(Body body, float dt)
    {
        if (body.IsGrounded)
        {
            body.AirTime = 0;
            body.RoseSinceGrounded = false;
            return;
        }

        body.AirTime += dt;
        if (body.Velocity.Y > 0)
            body.RoseSinceGrounded = true;
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
    /// <param name="pusher">
    /// When set, the body is being shoved out of the way by this entity (see <see cref="Pushable"/>): it doesn't collide
    /// with it, and its layer is <paramref name="pusherLayer"/>'s for any crates it pushes in turn.
    /// </param>
    private void MoveAxis(Body body, Collider collider, Solids solids, Vector2 delta, Action<Entity, Entity> onCollision,
        Entity? carrier = null, Entity? pusher = null, string? pusherLayer = null, int depth = 0)
    {
        if (delta == Vector2.Zero)
            return;

        var entity = body.Entity!;
        var axis = delta.X != 0 ? 0 : 1;
        var sign = MathF.Sign(axis == 0 ? delta.X : delta.Y);
        var boundsBefore = Area.Of(collider).Bounds;
        var bottomBefore = boundsBefore.Min.Y;
        var filter = LayerFilter.Of(collider);
        pusherLayer ??= collider.Layer;
        Translate(solids, entity, delta);

        // A crate shoved along is kept off isometric and hex maps' walls straight away, so whatever shoved it is stopped by it.
        if (pusher is not null && _isometric.Count > 0)
            KeepOffIsometricWalls(body, collider, solids, boundsBefore.Center, onCollision, carrier, pusher);
        if (pusher is not null && _hex.Count > 0)
            KeepOffHexWalls(body, collider, solids, boundsBefore.Center, onCollision, carrier, pusher);
        var hit = _slopes.Count > 0 && (axis == 0 || sign < 0 || delta.Y > 0) && MoveOnSlopes(body, collider, solids, delta, filter, carrier);
        var area = Area.Of(collider);

        // Only the solids near where it went, in the order they'd be met going through them all.
        var region = Grow(Union(boundsBefore, area.Bounds), Margin);
        var near = solids.Near(region);
        for (var n = 0; n < near.Count; n++)
        {
            var index = near[n];
            var solid = solids.All[index];
            if (solid.Entity == entity || solid.Entity.IsSelfOrDescendantOf(entity) || solid.Entity == carrier ||
                solid.Entity == pusher || filter.IgnoresEachOther(solid.Filter) || Unbumped(entity, solid.Entity))
                continue;

            var other = solid.Area;
            if (!area.Overlaps(other, CircleSkin))
                continue;

            // One-way: only catch bodies falling onto the top that started above it, and not while dropping through.
            if (solid.OneWay && (delta.Y >= 0 || bottomBefore < other.Bounds.Max.Y - 0.001f || body.IsDroppingThrough))
                continue;

            // A body that turns which has moved into it, rather than it into that, isn't got out of along this move: that
            // would lift it out of the top of a tower whose crate rocked into its side, and up the tower. The turning body's
            // own step eases them apart.
            if (MovedIntoBy(solid, area, other, axis, sign, delta))
                continue;

            // A crate in the way is shoved along first, by its share of the move; whatever's left of the overlap blocks as
            // usual, but a crate that gave way doesn't stop the pusher dead (it keeps leaning into it at its own speed).
            var gaveWay = false;
            if (carrier is null && depth < MaxPushChain && CanPush(solid, axis, pusherLayer) is var (pushed, crate, pushable))
            {
                var before = Component(other.Center, axis);
                var overlap = Component(area.Center, axis) + sign * area.ContactDistance(other, axis, sign) - before;
                var shove = overlap * Math.Clamp(pushable.Speed, 0, 1);
                MoveAxis(pushed, crate, solids, axis == 0 ? new Vector2(shove, 0) : new Vector2(0, shove), onCollision,
                    pusher: entity, pusherLayer: pusherLayer, depth: depth + 1);
                other = solid.Area;
                gaveWay = MathF.Abs(Component(other.Center, axis) - before) > 1e-5f;
                if (!area.Overlaps(other, CircleSkin))
                {
                    onCollision(entity, solid.Entity);
                    continue;
                }
            }

            // Walking into a turned plank or crate where it slants gently enough to walk up: up onto it, rather than stopped.
            var climbed = false;
            if (axis == 0 && (other.IsTurned || other.IsPolygon) && carrier is null && body.GravityScale > 0 && Gravity.Y < 0)
            {
                var lift = other.Center.Y + area.ContactDistance(other, 1, -1) - area.Center.Y;
                if (lift > 0 && lift <= MathF.Abs(delta.X) * TurnedStepSlope + 0.02f)
                {
                    Translate(solids, entity, new Vector2(0, lift));
                    climbed = true;
                }
            }

            // Push back along the axis we moved, to where the two shapes just touch.
            if (!climbed)
            {
                var target = Component(other.Center, axis) - sign * area.ContactDistance(other, axis, sign);
                var push = target - Component(area.Center, axis);
                Translate(solids, entity, axis == 0 ? new Vector2(push, 0) : new Vector2(0, push));
                if (carrier is null && !gaveWay)
                    Knock(body, area, solid, axis, sign);
            }

            area = Area.Of(collider);

            // Pushed somewhere it hadn't looked: look there too, carrying on after this solid.
            if (!Contains(region, area.Bounds))
            {
                region = Grow(Union(region, area.Bounds), Margin);
                solids.Near(region, near);
                n = near.BinarySearch(index);
                if (n < 0)
                    n = ~n - 1;
            }

            hit |= !gaveWay && !climbed;
            if (carrier is not null)
                continue;
            if ((axis == 1 && sign < 0) || climbed)
            {
                body.IsGrounded = true;
                body.Ground = solid.Entity;
            }
            else if (axis == 0 && !gaveWay)
            {
                body.WallSide = sign;
            }

            onCollision(entity, solid.Entity);
        }

        solids.Return(near);

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

    // Slopes under (or beside) a body that has just moved: it's lifted onto one it walked or fell into (a little at a time:
    // no more than it moved, as on a 45 degree slope), and blocked by one it walked into the tall side of or jumped into
    // from below. True if it landed on one or was stopped by one, for the caller to stop its velocity along the axis.
    private bool MoveOnSlopes(Body body, Collider collider, Solids solids, Vector2 delta, LayerFilter filter, Entity? carrier)
    {
        var entity = body.Entity!;
        var bounds = Area.Of(collider).Bounds;
        var lift = 0f;
        Entity? ground = null;
        var stopped = false;
        foreach (var slope in _slopes)
        {
            if (filter.IgnoresEachOther(slope.Filter) || slope.Entity.IsSelfOrDescendantOf(entity) || slope.Entity == carrier)
                continue;
            var cell = slope.Cell;
            if (!(bounds.Min.X < cell.Max.X && bounds.Max.X > cell.Min.X && bounds.Min.Y < cell.Max.Y && bounds.Max.Y > cell.Min.Y))
                continue;

            if (delta.Y > 0)
            {
                // Jumping up into it from underneath: its flat bottom is a ceiling.
                if (bounds.Min.Y < cell.Min.Y)
                {
                    Translate(solids, entity, new Vector2(0, cell.Min.Y - bounds.Max.Y));
                    bounds = Area.Of(collider).Bounds;
                    stopped = true;
                }

                continue;
            }

            var surface = slope.SurfaceUnder(bounds);
            var depth = surface - bounds.Min.Y;
            if (depth <= 0)
                continue;
            var step = delta.X != 0 ? MathF.Abs(delta.X) * cell.HalfSize.Y / cell.HalfSize.X + 0.02f : MathF.Abs(delta.Y) + 0.02f;
            if (depth <= step)
            {
                if (depth > lift)
                    (lift, ground) = (depth, slope.Entity);
            }
            else if (delta.X != 0)
            {
                // The tall side of the slope, walked into: a wall.
                var push = delta.X > 0 ? cell.Min.X - bounds.Max.X : cell.Max.X - bounds.Min.X;
                Translate(solids, entity, new Vector2(push, 0));
                bounds = Area.Of(collider).Bounds;
                if (carrier is null)
                    body.WallSide = MathF.Sign(delta.X);
                stopped = true;
            }
        }

        if (ground is null)
            return stopped;
        Translate(solids, entity, new Vector2(0, lift));
        if (carrier is null)
        {
            body.IsGrounded = true;
            body.Ground = ground;
        }

        return stopped || delta.Y < 0;
    }

    /// <summary>True if <paramref name="collider"/> is standing on (or touching) a slope tile.</summary>
    public bool IsOnSlope(Scene scene, Collider collider)
    {
        var bounds = Area.Of(collider).Bounds;
        var feet = new Box(bounds.Center - new Vector2(0, 0.05f), bounds.HalfSize);
        var filter = LayerFilter.Of(collider);
        return Slopes(scene).Any(s => !filter.IgnoresEachOther(s.Filter) && !s.Entity.IsSelfOrDescendantOf(collider.Entity!) && s.Cell.Overlaps(feet));
    }

    // True if a slope's surface is within `reach` below the collider's bottom.
    private bool SlopeBelow(Collider collider, float reach)
    {
        var bounds = Area.Of(collider).Bounds;
        var filter = LayerFilter.Of(collider);
        foreach (var slope in _slopes)
        {
            var cell = slope.Cell;
            if (filter.IgnoresEachOther(slope.Filter) || slope.Entity.IsSelfOrDescendantOf(collider.Entity!) ||
                bounds.Min.X >= cell.Max.X || bounds.Max.X <= cell.Min.X)
                continue;
            var gap = bounds.Min.Y - slope.SurfaceUnder(bounds);
            if (gap >= -0.001f && gap <= reach)
                return true;
        }

        return false;
    }

    private static IEnumerable<Slope> Slopes(Scene scene, string? layers = null)
    {
        var only = layers is null ? null : CollisionLayers.Parse(layers);
        foreach (var tilemap in scene.Active<Tilemap>())
        {
            if (!tilemap.Solid || (only is not null && !CollisionLayers.Contains(only, tilemap.Layer)))
                continue;
            var filter = new LayerFilter(tilemap.Layer, CollisionLayers.Parse(tilemap.IgnoreLayers));
            foreach (var (cell, rising) in SlopeShapes(tilemap))
                yield return new Slope(tilemap.Entity!, cell, rising, filter);
        }
    }

    // A slope tile: solid below the line across its cell, rising to the right or falling.
    private readonly record struct Slope(Entity Entity, Box Cell, bool Rising, LayerFilter Filter)
    {
        /// <summary>The surface's height at <paramref name="x"/> (clamped to the cell).</summary>
        public float SurfaceAt(float x)
        {
            var t = Math.Clamp((x - Cell.Min.X) / (Cell.HalfSize.X * 2), 0, 1);
            return Cell.Min.Y + (Rising ? t : 1 - t) * Cell.HalfSize.Y * 2;
        }

        /// <summary>The highest point of the surface under a box: at its corner furthest up the slope.</summary>
        public float SurfaceUnder(Box box) => SurfaceAt(Rising ? Math.Min(box.Max.X, Cell.Max.X) : Math.Max(box.Min.X, Cell.Min.X));

        /// <summary>Where a ray first enters the solid triangle, and the face's normal; null on a miss or starting inside.</summary>
        public (float Distance, Vector2 Normal)? Intersect(Vector2 origin, Vector2 direction)
        {
            var (min, max) = (Cell.Min, Cell.Max);
            var (low, high) = Rising ? (new Vector2(min.X, min.Y), new Vector2(max.X, max.Y)) : (new Vector2(min.X, max.Y), new Vector2(max.X, min.Y));
            var tall = Rising ? new Vector2(max.X, min.Y) : new Vector2(min.X, min.Y);
            if (Inside(origin))
                return null;

            (float, Vector2)? best = null;
            var slopeNormal = Vector2.Normalize(Rising ? new Vector2(-1, 1) : new Vector2(1, 1));
            Edge(low, high, slopeNormal);
            Edge(new Vector2(min.X, min.Y), new Vector2(max.X, min.Y), -Vector2.UnitY);
            Edge(tall, Rising ? high : low, Rising ? Vector2.UnitX : -Vector2.UnitX);
            return best;

            void Edge(Vector2 a, Vector2 b, Vector2 normal)
            {
                if (Vector2.Dot(direction, normal) >= 0)
                    return;
                var edge = b - a;
                var denominator = direction.X * edge.Y - direction.Y * edge.X;
                if (MathF.Abs(denominator) < 1e-8f)
                    return;
                var offset = a - origin;
                var t = (offset.X * edge.Y - offset.Y * edge.X) / denominator;
                var u = (offset.X * direction.Y - offset.Y * direction.X) / denominator;
                if (t < 0 || u < -1e-5f || u > 1 + 1e-5f)
                    return;
                if (best is null || t < best.Value.Item1)
                    best = (t, normal);
            }
        }

        private bool Inside(Vector2 point) =>
            point.X > Cell.Min.X && point.X < Cell.Max.X && point.Y > Cell.Min.Y && point.Y < SurfaceAt(point.X);
    }

    // Whether a solid is a body that turns (and moves) which this move can't have taken it into: getting out of it along the
    // move takes further than the move went.
    private bool MovedIntoBy(in Shape solid, Area area, Area other, int axis, float sign, Vector2 delta)
    {
        if (solid.Collider is null || solid.Entity.GetComponent<Body>() is not { HeldStill: false } body || !Solves(body))
            return false;
        var push = Component(other.Center, axis) - sign * area.ContactDistance(other, axis, sign) - Component(area.Center, axis);
        return MathF.Abs(push) > MathF.Abs(Component(delta, axis)) + 0.05f;
    }

    // A pushable crate's body and collider, if the solid is one that something on this layer can shove along this axis:
    // sideways always, up and down only where it doesn't fall (top-down), so landing on a crate doesn't drive it into the floor.
    // A crate on a grid is only pushed a cell at a time, by grid movers.
    private (Body Body, Collider Collider, Pushable Pushable)? CanPush(Shape solid, int axis, string? layer)
    {
        if (solid.Collider is not { } collider || solid.Entity.GetComponent<Pushable>() is not { } pushable ||
            solid.Entity.GetComponent<Body>() is not { } body || Solves(body) || !pushable.AcceptsLayer(layer) ||
            solid.Entity.GetComponent<GridMovement>() is not null)
            return null;
        if (axis == 1 && body.GravityScale != 0 && Gravity != Vector2.Zero)
            return null;
        return (body, collider, pushable);
    }

    /// <summary>
    /// True if what <paramref name="body"/> stands on is only one-way platforms, so dropping through them would fall:
    /// for drop-through on down and Jump.
    /// </summary>
    public bool OnlyOneWayBelow(Scene scene, Body body) => OneWayFloorBottom(scene, body) is not null;

    /// <summary>
    /// When <paramref name="body"/> stands only on one-way platforms, the lowest of their bottoms (what's under the floor
    /// it's on, for climbing down a ladder below it); otherwise null.
    /// </summary>
    internal static float? OneWayFloorBottom(Scene scene, Body body)
    {
        if (!body.IsGrounded || body.Entity?.GetComponent<Collider>() is not { } collider)
            return null;
        var bounds = BoundsOf(collider);
        var feet = new Box(new Vector2(bounds.Center.X, bounds.Min.Y - 0.02f), new Vector2(Math.Max(0, bounds.HalfSize.X - 0.01f), 0.03f));
        float? bottom = null;
        foreach (var shape in ShapesTouching(scene, collider, feet))
        {
            if (!shape.OneWay)
                return null;
            bottom = Math.Min(bottom ?? float.PositiveInfinity, shape.Area.Bounds.Min.Y);
        }

        return bottom;
    }

    /// <summary>True if the bottom edge of <paramref name="body"/>'s collider is inside a one-way platform (climbing up through it).</summary>
    internal static bool FeetInOneWay(Scene scene, Body body)
    {
        if (body.Entity?.GetComponent<Collider>() is not { } collider)
            return false;
        var bounds = BoundsOf(collider);
        var feet = new Box(new Vector2(bounds.Center.X, bounds.Min.Y + 0.02f), new Vector2(Math.Max(0, bounds.HalfSize.X - 0.01f), 0.01f));
        return ShapesTouching(scene, collider, feet).Any(s => s.OneWay);
    }

    // Solid shapes overlapping a box, other than the collider's own entity (and its children) and layers it passes through.
    private static IEnumerable<Shape> ShapesTouching(Scene scene, Collider collider, Box box)
    {
        var filter = LayerFilter.Of(collider);
        var area = Area.Of(box);
        return Shapes(scene, includeTriggers: false).Where(s =>
            !s.Entity.IsSelfOrDescendantOf(collider.Entity!) && !filter.IgnoresEachOther(s.Filter) && s.Area.Overlaps(area));
    }

    private void UpdateTriggers(ComponentList<Collider> colliders, Action<Entity, Entity, bool> onTrigger)
    {
        var current = _lastTriggerPairs;
        current.Clear();
        var shapes = _triggerShapes;
        shapes.Clear();
        var anyTriggers = false;
        foreach (var collider in colliders)
        {
            shapes.Add((collider, Area.Of(collider), LayerFilter.Of(collider)));
            anyTriggers |= collider.IsTrigger;
        }

        // Each trigger tests the colliders near it, in order, as if it went through them all.
        if (anyTriggers)
        {
            _triggerGrid.Build(shapes.Count, _triggerBounds ??= i => _triggerShapes[i].Area.Bounds);
            foreach (var (trigger, area, filter) in shapes)
            {
                if (!trigger.IsTrigger)
                    continue;
                _triggerGrid.Near(Grow(area.Bounds, Margin), _near);
                foreach (var o in _near)
                {
                    var (other, otherArea, otherFilter) = shapes[o];
                    if (other == trigger || other.Entity == trigger.Entity || filter.IgnoresEachOther(otherFilter) ||
                        !area.Overlaps(otherArea))
                        continue;
                    current.Add((trigger.Entity!, other.Entity!));
                }
            }
        }

        foreach (var pair in current)
        {
            if (!_triggerPairs.Contains(pair))
                onTrigger(pair.Item1, pair.Item2, true);
        }

        foreach (var pair in _triggerPairs)
        {
            if (!current.Contains(pair))
                onTrigger(pair.Item1, pair.Item2, false);
        }

        (_triggerPairs, _lastTriggerPairs) = (current, _triggerPairs);
    }

    // Everything physics can touch: colliders, and the tiles of solid tilemaps. With layers, only shapes on those layers.
    private static List<Shape> Shapes(Scene scene, bool includeTriggers, string? layers = null)
    {
        var shapes = new List<Shape>();
        CollectShapes(scene, includeTriggers, layers, shapes);
        return shapes;
    }

    // The same, into a list, in hierarchy order: each entity's colliders and solid tilemaps as they're attached.
    private static void CollectShapes(Scene scene, bool includeTriggers, string? layers, List<Shape> into)
    {
        var only = layers is null ? null : CollisionLayers.Parse(layers);
        var colliders = scene.Active<Collider>();
        var tilemaps = scene.Active<Tilemap>();
        if (tilemaps.Count > 0)
            scene.Order();

        var (c, t) = (0, 0);
        while (c < colliders.Count || t < tilemaps.Count)
        {
            if (t == tilemaps.Count || (c < colliders.Count && Before(colliders[c], tilemaps[t])))
            {
                var collider = colliders[c++];
                if ((includeTriggers || !collider.IsTrigger) && (only is null || CollisionLayers.Contains(only, collider.Layer)))
                    into.Add(new Shape(collider.Entity!, collider, default, false, LayerFilter.Of(collider)));
                continue;
            }

            var tilemap = tilemaps[t++];
            if (!tilemap.Solid || (only is not null && !CollisionLayers.Contains(only, tilemap.Layer)))
                continue;
            var filter = new LayerFilter(tilemap.Layer, CollisionLayers.Parse(tilemap.IgnoreLayers));
            foreach (var (box, oneWay) in TileShapes(tilemap))
                into.Add(new Shape(tilemap.Entity!, null, box, oneWay, filter));
        }
    }

    // True if a comes before b in the hierarchy (once the scene has numbered its entities).
    private static bool Before(Component a, Component b)
    {
        if (a.Entity != b.Entity)
            return a.Entity!.Order < b.Entity!.Order;
        foreach (var component in a.Entity!.Components)
        {
            if (component == a)
                return true;
            if (component == b)
                return false;
        }

        return false;
    }

    // A little more than nothing, so shapes just touching are near each other.
    private const float Margin = 0.01f;

    private static Box Grow(Box box, float by) => new(box.Center, box.HalfSize + new Vector2(by));

    private static Box Offset(Box box, Vector2 by) => new(box.Center + by, box.HalfSize);

    private static Box Union(Box a, Box b)
    {
        var min = Vector2.Min(a.Min, b.Min);
        var max = Vector2.Max(a.Max, b.Max);
        return new Box((min + max) / 2, (max - min) / 2);
    }

    private static bool Contains(Box outer, Box inner) =>
        inner.Min.X >= outer.Min.X && inner.Min.Y >= outer.Min.Y && inner.Max.X <= outer.Max.X && inner.Max.Y <= outer.Max.Y;

    private static bool IsInside(Entity entity, IReadOnlyList<Body> members)
    {
        foreach (var member in members)
        {
            if (entity.IsSelfOrDescendantOf(member.Entity!))
                return true;
        }

        return false;
    }

    private static Vector2 ScaleOf(Matrix4x4 world) => new(
        new Vector2(world.M11, world.M12).Length(),
        new Vector2(world.M21, world.M22).Length());

    private static float Component(Vector2 v, int axis) => axis == 0 ? v.X : v.Y;

    // Moves by a world-space delta, converting into the parent's space so nested bodies work, and moves its shapes in the
    // solids' grid.
    private static void Translate(Solids solids, Entity entity, Vector2 worldDelta)
    {
        var delta = new Vector3(worldDelta, 0);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            delta = Vector3.TransformNormal(delta, inverse);
        entity.Transform.Position += delta;
        solids.Moved(entity);
    }

    // The solid shapes of a step, in a grid so a moving body only tests those near it.
    private sealed class Solids
    {
        private readonly ShapeGrid _grid = new();

        // Where each entity's own shapes are in All (together, in hierarchy order).
        private readonly Dictionary<Entity, (int First, int Count)> _shapesOf = [];
        private readonly Stack<List<int>> _spare = [];
        private Func<int, Box>? _boundsOf;

        // Where each shape is now, kept as they move.
        private Area[] _areas = [];

        public List<Shape> All { get; } = [];

        public void Fill(Scene scene)
        {
            All.Clear();
            CollectShapes(scene, includeTriggers: false, null, All);
            _shapesOf.Clear();
            for (var i = 0; i < All.Count; i++)
            {
                var entity = All[i].Entity;
                _shapesOf[entity] = _shapesOf.TryGetValue(entity, out var range) ? (range.First, range.Count + 1) : (i, 1);
            }

            if (_areas.Length < All.Count)
                _areas = new Area[Math.Max(All.Count, _areas.Length * 2)];
            for (var i = 0; i < All.Count; i++)
                _areas[i] = All[i].Area;
            _grid.Build(All.Count, _boundsOf ??= i => _areas[i].Bounds);
        }

        /// <summary>The solids near <paramref name="region"/>, in order, in a list to give back with <see cref="Return"/>.</summary>
        public List<int> Near(Box region)
        {
            var near = _spare.Count > 0 ? _spare.Pop() : [];
            _grid.Near(region, near);
            return near;
        }

        public void Near(Box region, List<int> into) => _grid.Near(region, into);

        /// <summary>Which of its entity's shapes shape <paramref name="index"/> of <see cref="All"/> is: 0 for the first.</summary>
        public int PartOf(int index) => _shapesOf.TryGetValue(All[index].Entity, out var range) ? index - range.First : 0;

        /// <summary>Where shape <paramref name="index"/> of <see cref="All"/> is now.</summary>
        public Area AreaOf(int index) => _areas[index];

        public void Return(List<int> near) => _spare.Push(near);

        /// <summary>A collider's layer and the layers it passes through, as this step found them.</summary>
        public LayerFilter FilterOf(Collider collider)
        {
            if (_shapesOf.TryGetValue(collider.Entity!, out var range))
            {
                for (var i = range.First; i < range.First + range.Count; i++)
                {
                    if (All[i].Collider == collider)
                        return All[i].Filter;
                }
            }

            return LayerFilter.Of(collider);
        }

        // An entity has moved, and everything inside it with it. Tile boxes stay where the step found them.
        public void Moved(Entity entity)
        {
            if (_shapesOf.TryGetValue(entity, out var range))
            {
                for (var i = range.First; i < range.First + range.Count; i++)
                {
                    if (All[i].Collider is not null)
                    {
                        _areas[i] = All[i].Area;
                        _grid.Move(i, _areas[i].Bounds);
                    }
                }
            }

            foreach (var child in entity.Children)
                Moved(child);
        }
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

    // A box, a circle or a polygon in world space. For a circle, Bounds is the box around it. A turned box (one whose body
    // Rotates) has its own Axis, a unit vector, and its own Half size; Bounds is then the box round it. A polygon is its
    // collider's convex pieces (see Physics.Polygons.cs); Bounds is the box round them.
    private readonly partial record struct Area(Box Bounds, float Radius, bool IsCircle)
    {
        /// <summary>A turned box's X axis (its Y is that turned a quarter anticlockwise); zero for one that isn't turned.</summary>
        public Vector2 Axis { get; init; }

        /// <summary>Half a turned box's own width and height.</summary>
        public Vector2 Half { get; init; }

        /// <summary>A polygon collider's shape: its convex pieces, where it is now. Bounds is the box round it.</summary>
        public PolygonCollider? Polygon { get; init; }

        public bool IsTurned => Axis != Vector2.Zero;

        public Vector2 Center => Bounds.Center;

        public Circle Circle => new(Bounds.Center, Radius);

        public static Area Of(Box box) => new(box, 0, false);

        public static Area Of(Circle circle) => new(circle.Bounds, circle.Radius, true);

        public static Area Of(Collider collider) => collider switch
        {
            BoxCollider box => Of(box),
            CircleCollider circle => Of(BoundsOf(circle)),
            PolygonCollider polygon => new Area(polygon.WorldBounds, 0, false) { Polygon = polygon },
            _ => throw new NotSupportedException($"Unknown collider {collider.GetType().Name}."),
        };

        private static Area Of(BoxCollider collider)
        {
            var (center, half, axis) = collider.WorldShape;
            if (axis == Vector2.UnitX)
                return Of(new Box(center, half));
            var (x, y) = (MathF.Abs(axis.X), MathF.Abs(axis.Y));
            return new Area(new Box(center, new Vector2(x * half.X + y * half.Y, y * half.X + x * half.Y)), 0, false)
            {
                Axis = axis,
                Half = half,
            };
        }

        // A world point in a turned box's own frame, round its middle.
        private Vector2 ToLocal(Vector2 point)
        {
            var offset = point - Center;
            return new Vector2(Vector2.Dot(offset, Axis), Axis.X * offset.Y - Axis.Y * offset.X);
        }

        private Vector2 ToWorldNormal(Vector2 local) => Axis * local.X + new Vector2(-Axis.Y, Axis.X) * local.Y;

        public bool Contains(Vector2 point)
        {
            if (Polygon is { } polygon)
                return polygon.Contains(point);
            if (IsTurned)
                return new Box(Vector2.Zero, Half).Contains(ToLocal(point));
            return IsCircle ? Circle.Contains(point) : Bounds.Contains(point);
        }

        /// <param name="skin">Overlaps shallower than this don't count, for shapes involving a circle.</param>
        public bool Overlaps(Area other, float skin = 0)
        {
            if (IsPolygon || other.IsPolygon)
                return Bounds.Overlaps(other.Bounds) && PolygonOverlaps(other, skin);
            if (IsTurned || other.IsTurned)
                return TurnedOverlaps(other, skin);

            // Boxes get the skin too: a box resting at y = 0.39999998 on a floor whose top is at 0 is touching, not inside it.
            if (!IsCircle && !other.IsCircle)
                return new Box(Center, Vector2.Max(Bounds.HalfSize - new Vector2(skin), Vector2.Zero)).Overlaps(other.Bounds);
            if (IsCircle && other.IsCircle)
                return new Circle(Center, Radius - skin).Overlaps(other.Circle);
            var (box, circle) = IsCircle ? (other.Bounds, Circle) : (Bounds, other.Circle);
            return box.Overlaps(circle.Center, circle.Radius - skin);
        }

        // With a turned box: a circle against it in its own frame, or two boxes by their four sides' directions.
        private bool TurnedOverlaps(Area other, float skin)
        {
            if (IsCircle || other.IsCircle)
            {
                var (box, circle) = IsCircle ? (other, this) : (this, other);
                return new Box(Vector2.Zero, box.Half).Overlaps(box.ToLocal(circle.Center), circle.Radius - skin);
            }

            var between = other.Center - Center;
            for (var i = 0; i < 4; i++)
            {
                var axis = SideNormal(i < 2 ? this : other, i % 2);
                if (MathF.Abs(Vector2.Dot(between, axis)) >= Reach(axis) + other.Reach(axis) - skin)
                    return false;
            }

            return true;
        }

        // A box's side directions: its X axis (0) or Y axis (1), the world's for one that isn't turned.
        private static Vector2 SideNormal(Area box, int side) =>
            box.IsTurned ? (side == 0 ? box.Axis : new Vector2(-box.Axis.Y, box.Axis.X)) : (side == 0 ? Vector2.UnitX : Vector2.UnitY);

        // How far a box reaches from its middle along a unit direction.
        private float Reach(Vector2 direction)
        {
            if (!IsTurned)
                return MathF.Abs(direction.X) * Bounds.HalfSize.X + MathF.Abs(direction.Y) * Bounds.HalfSize.Y;
            return MathF.Abs(Vector2.Dot(direction, Axis)) * Half.X + MathF.Abs(Axis.X * direction.Y - Axis.Y * direction.X) * Half.Y;
        }

        /// <summary>
        /// How far apart the centers are along <paramref name="axis"/> (0 = X, 1 = Y) when the shapes just touch, keeping
        /// their offset on the other axis, with this one on the side it came from: below or to the left of
        /// <paramref name="other"/> moving the way of <paramref name="sign"/> 1, above or to the right moving the way of -1.
        /// </summary>
        public float ContactDistance(Area other, int axis, float sign)
        {
            if (IsPolygon || other.IsPolygon)
            {
                if (PolygonChord(other, axis) is not var (lowest, highest))
                    return Component(Bounds.HalfSize, axis) + Component(other.Bounds.HalfSize, axis);
                return sign > 0 ? -lowest : highest;
            }

            if (IsTurned || other.IsTurned)
            {
                var (low, high) = Chord(other, axis);
                return sign > 0 ? -low : high;
            }

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

        // With a turned box: the stretch of offsets along the axis (this one's middle less the other's) over which the two
        // overlap, keeping the offset across it they have now.
        private (float Low, float High) Chord(Area other, int axis)
        {
            var along = axis == 0 ? Vector2.UnitX : Vector2.UnitY;
            var offset = Center - other.Center;
            var across = offset - along * Vector2.Dot(offset, along);
            if (IsCircle || other.IsCircle)
            {
                // The circle's middle against the box grown by its radius (rounded at the corners), in the box's own frame.
                var (box, circle, sign) = IsCircle ? (other, this, 1f) : (this, other, -1f);
                return RoundedChord(box, circle.Radius, across * sign, along * sign);
            }

            var (low, high) = (float.NegativeInfinity, float.PositiveInfinity);
            for (var i = 0; i < 4; i++)
            {
                var normal = SideNormal(i < 2 ? this : other, i % 2);
                var facing = Vector2.Dot(along, normal);
                if (MathF.Abs(facing) < 1e-6f)
                    continue;
                var reach = Reach(normal) + other.Reach(normal);
                var at = Vector2.Dot(across, normal);
                var (a, b) = ((-reach - at) / facing, (reach - at) / facing);
                if (a > b)
                    (a, b) = (b, a);
                (low, high) = (MathF.Max(low, a), MathF.Min(high, b));
            }

            return (low, high);
        }

        // The stretch of t over which a circle's middle at start + t * way (from a turned box's middle) is within its
        // radius of the box.
        private static (float Low, float High) RoundedChord(Area box, float radius, Vector2 start, Vector2 way)
        {
            var from = new Vector2(Vector2.Dot(start, box.Axis), box.Axis.X * start.Y - box.Axis.Y * start.X);
            var along = new Vector2(Vector2.Dot(way, box.Axis), box.Axis.X * way.Y - box.Axis.Y * way.X);
            var half = box.Half;
            var (low, high) = (float.NegativeInfinity, float.PositiveInfinity);
            for (var axis = 0; axis < 2; axis++)
            {
                var (s, d, reach) = axis == 0 ? (from.X, along.X, half.X + radius) : (from.Y, along.Y, half.Y + radius);
                if (MathF.Abs(d) < 1e-6f)
                    continue;
                var (a, b) = ((-reach - s) / d, (reach - s) / d);
                if (a > b)
                    (a, b) = (b, a);
                (low, high) = (MathF.Max(low, a), MathF.Min(high, b));
            }

            // An end out past a corner is on that corner's rounding instead.
            return (Corner(low, false), Corner(high, true));

            float Corner(float t, bool far)
            {
                var p = from + along * t;
                if (MathF.Abs(p.X) <= half.X || MathF.Abs(p.Y) <= half.Y)
                    return t;
                var corner = new Vector2(MathF.CopySign(half.X, p.X), MathF.CopySign(half.Y, p.Y));
                var m = from - corner;
                var b = Vector2.Dot(m, along);
                var discriminant = b * b - (m.LengthSquared() - radius * radius);
                if (discriminant < 0)
                    return t;
                var root = MathF.Sqrt(discriminant);
                return far ? -b + root : -b - root;
            }
        }

        /// <summary>The entry distance and surface normal along a normalized ray, or null on a miss or when starting inside.</summary>
        public (float Distance, Vector2 Normal)? Intersect(Vector2 origin, Vector2 direction)
        {
            if (Polygon is { } polygon)
                return IntersectPolygon(polygon, origin, direction);
            if (!IsTurned)
                return IsCircle ? IntersectCircle(Circle, origin, direction) : IntersectBox(Bounds, origin, direction);
            var local = ToLocal(origin);
            var way = new Vector2(Vector2.Dot(direction, Axis), Axis.X * direction.Y - Axis.Y * direction.X);
            return IntersectBox(new Box(Vector2.Zero, Half), local, way) is var (distance, normal) ? (distance, ToWorldNormal(normal)) : null;
        }

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

/// <summary>Bodies welded into one; see <see cref="Physics.Groups"/>.</summary>
internal sealed class BodyGroup(List<Body> bodies, bool anchored)
{
    public List<Body> Bodies { get; } = bodies;

    /// <summary>Welded to something without a body (a wall, the ground): the whole group holds still.</summary>
    public bool Anchored { get; } = anchored;

    /// <summary>For this step: it holds still (anchored, or a member is frozen).</summary>
    internal bool Still { get; set; }

    /// <summary>For this step: it turns as one, every member being a body that rotates.</summary>
    internal bool Turns { get; set; }
}
