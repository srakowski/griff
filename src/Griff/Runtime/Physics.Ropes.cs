using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

// Ropes (Rope): a chain of light points, each a short piece from the next, worked out with the bodies that turn in the same
// parts of the step as their contacts and hinges. Each piece holds its points its length apart; each point is held within
// the rope's length of the nearest end that's tied, so it doesn't stretch under its own weight; and whatever hangs on it (a
// body at an end, or a player climbing it) is held no further from what holds it up than the rope between them, straight,
// or round the corner it's wrapped over. Its points meet the world as small balls, pushed out of what they'd go into and
// rubbing along it. What a rope knows (its points, how fast they go, how hard each piece pulls, who hangs on it) is kept in
// the scene's contact memory as well, so a rewind replays it to the bit.
public sealed partial class Physics
{
    // In the contact memory, a rope's state is kept under its own entity, the part saying it's a rope (beyond hinges').
    private const int RopePart = 6 << 28;

    // Its first feature, then two for each point.
    private const int RopeHeader = 0;
    private const int RopeHolds = 1;
    private const int RopePoints = 16;

    // How heavy a rope is, a unit of its length; how much of how fast its points go the air takes, a second; how hard it
    // grips what it lies on; how thick it is to the world at least.
    private const float RopeWeight = 0.05f;
    private const float RopeDrag = 1f;
    private const float RopeGrip = 0.5f;
    private const float RopeThickness = 0.04f;

    // How hard a player hanging on a rope swings themselves along (a second, every second), and how near a tied end they
    // can climb.
    private const float SwingPush = 9f;
    private const float ClimbGap = 0.35f;

    // A span between two things that don't move here pulled this much past its length snaps (if it can break).
    private const float OverStretch = 1.02f;

    // The step's ropes, and the bodies at their ends (for waking and islands).
    [NotState("gathered again each step")]
    private readonly List<Rope> _ropes = [];
    [NotState("gathered again each step")]
    private readonly List<(Body? Host, Body? Other)> _ropeEnds = [];

    // Players climbing ropes this step: their rope moves them.
    [NotState("gathered again each step")]
    private readonly HashSet<Body> _climbing = [];

    // What a step works out for its ropes: each one awake, its points (and players hanging on it, and corners it's wrapped
    // round), what holds them together, and where they touch the world.
    [NotState("gathered again each step")]
    private readonly List<RopeRun> _runs = [];
    [NotState("gathered again each step")]
    private readonly List<RopeDot> _dots = [];
    [NotState("gathered again each step")]
    private readonly List<RopeTie> _ties = [];
    [NotState("gathered again each step")]
    private readonly List<RopeTouch> _ropeTouches = [];
    [NotState("gathered again each step")]
    private readonly List<int> _ropeNear = [];
    [NotState("gathered again each step")]
    private readonly List<byte> _ropeNearEnd = [];
    [NotState("gathered again each step")]
    private readonly List<float> _stretch = [];
    [NotState("gathered again each step")]
    private Scene? _ropeScene;

    /// <summary>Ropes that broke in the last step, for their Break Messages.</summary>
    internal List<Rope> Snapped { get; } = [];

    /// <summary>
    /// Lays out the scene's ropes that haven't been yet (as they hang at rest, or as the scene's contact memory has them
    /// after a rewind), so what reads them before physics steps (a player grabbing one) finds them where they are.
    /// </summary>
    internal void ReadyRopes()
    {
        var scene = Scene;
        var ropes = scene.ActiveNow<Rope>();
        for (var i = 0; i < ropes.Count; i++)
        {
            if (ropes[i].Points is null)
                Ready(ropes[i], scene);
        }
    }

    // A rope's points: from the contact memory (a rewind), or laid out hanging at rest.
    private void Ready(Rope rope, Scene scene)
    {
        var memory = scene.Contacts;
        var id = rope.Entity!.Id;
        int count;
        var remembered = memory.TryGet(new ContactKey(id, Guid.Empty, RopePart, RopeHeader), out var header);
        if (remembered)
        {
            count = (int)header.X;
            if (count < 2 || count > Rope.MaxPoints)
                remembered = false;
        }

        if (!remembered)
        {
            if (!(rope.Length > 0))
                rope.Length = Math.Max(0.1f, rope.PlacedLength);
            count = Rope.PointsFor(rope.Length);
        }
        else
            count = (int)header.X;

        var points = new Vector2[count];
        var velocities = new Vector2[count];
        var pulls = new float[count];
        if (remembered)
        {
            for (var i = 0; i < count; i++)
            {
                memory.TryGet(new ContactKey(id, Guid.Empty, RopePart, RopePoints + 2 * i), out var at);
                memory.TryGet(new ContactKey(id, Guid.Empty, RopePart, RopePoints + 2 * i + 1), out var going);
                (points[i], pulls[i], velocities[i]) = (new Vector2(at.X, at.Y), at.Z, new Vector2(going.X, going.Y));
            }

            (rope.BrokenAt, rope.StillFor) = ((int)header.Y, header.Z);
            rope.Asleep = rope.StillFor >= SleepAfter;
            rope.Climbers.Clear();
            foreach (var body in scene.ActiveNow<Body>())
            {
                if (memory.TryGet(new ContactKey(id, body.Entity!.Id, RopePart, RopeHolds), out var hold))
                    Hold(rope, body, hold.X);
            }
        }
        else
        {
            var pull = Gravity * rope.GravityScale;
            rope.LayOut(points, pull == Vector2.Zero ? -Vector2.UnitY : Vector2.Normalize(pull));
            (rope.BrokenAt, rope.StillFor) = (-1, 0);
        }

        (rope.Points, rope.Velocities, rope.Pulls, rope.Was) = (points, velocities, pulls, (Vector2[])points.Clone());
        rope.SeenBroke = rope.Broke;
        rope.SeenLength = rope.Length;
    }

    /// <summary>
    /// The point on a climbable rope nearest <paramref name="point"/> within <paramref name="reach"/> of it, as it is now: the
    /// rope, and how far along it from its start; null if there's none.
    /// </summary>
    internal (Rope Rope, float At, Vector2 Point)? RopeNear(Vector2 point, float reach, Body except)
    {
        (Rope Rope, float At, Vector2 Point)? best = null;
        var bestDistance = reach * reach;
        foreach (var rope in Scene.ActiveNow<Rope>())
        {
            if (!rope.Climbable || rope.Points is not { } points || rope.Entity == except.Entity || rope.OtherEntity == except.Entity)
                continue;
            var piece = rope.Length / (points.Length - 1);
            var broken = rope.Broke ? rope.BreakIndex() : -1;
            for (var i = 0; i < points.Length - 1; i++)
            {
                if (i == broken)
                    continue;
                var (a, b) = (points[i], points[i + 1]);
                var along = b - a;
                var t = along.LengthSquared() > 1e-10f ? Math.Clamp(Vector2.Dot(point - a, along) / along.LengthSquared(), 0, 1) : 0;
                var at = a + along * t;
                var distance = Vector2.DistanceSquared(at, point);
                if (distance < bestDistance)
                    (best, bestDistance) = ((rope, (i + t) * piece, at), distance);
            }
        }

        return best;
    }

    /// <summary>Puts a player on a rope, holding it <paramref name="at"/> along from its start.</summary>
    internal static void Hold(Rope rope, Body body, float at)
    {
        Release(body);
        var climbers = rope.Climbers;
        var index = 0;
        while (index < climbers.Count && climbers[index].At <= at)
            index++;
        climbers.Insert(index, new RopeClimber { Body = body, At = at });
        body.Climbing = rope;
        (rope.StillFor, rope.Asleep) = (0, false);
    }

    /// <summary>Takes a player off the rope they hang on, if any.</summary>
    internal static void Release(Body body) => body.LetGoOfRope();

    /// <summary>Which way and how hard a player on a rope climbs (world units a second, positive up it) and swings (-1 to 1) this step.</summary>
    internal static void Steer(Body body, float climb, float swing)
    {
        if (body.Climbing is not { } rope)
            return;
        var climbers = CollectionsMarshal.AsSpan(rope.Climbers);
        foreach (ref var climber in climbers)
        {
            if (climber.Body == body)
                (climber.Climb, climber.Swing) = (climb, Math.Clamp(swing, -1, 1));
        }
    }

    // Where a player holds a rope: a little below the top of what they're made of.
    private static Vector2 HandsOf(Body body)
    {
        var entity = body.Entity!;
        if (entity.GetComponent<Collider>() is not { } collider)
            return PositionOf(entity);
        var bounds = BoundsOf(collider);
        return bounds.Center + new Vector2(0, bounds.HalfSize.Y * 0.6f);
    }

    // The step's ropes, at its start: laid out, their players still on them, and the bodies at their ends worked out with
    // those that turn (so the rope can pull them about).
    private void CollectRopes(Scene scene)
    {
        _ropes.Clear();
        _ropeEnds.Clear();
        _climbing.Clear();
        Snapped.Clear();
        _ropeScene = scene;
        var ropes = scene.ActiveNow<Rope>();
        if (ropes.Count == 0)
            return;

        foreach (var rope in ropes)
        {
            if (rope.Points is null)
                Ready(rope, scene);
            // Cut since the last step (by a wire or a script).
            if (rope.Broke && !rope.SeenBroke && rope.Entity!.RunsHere)
                Snapped.Add(rope);
            var host = rope.Entity!.GetComponent<Body>();
            var other = rope.EndIsLoose ? null : rope.OtherEntity?.GetComponent<Body>();
            if (host is not null)
                _jointed.Add(host);
            if (other is not null && other != host)
                _jointed.Add(other);

            var climbers = rope.Climbers;
            for (var i = climbers.Count - 1; i >= 0; i--)
            {
                var body = climbers[i].Body;
                if (body.Climbing != rope || body.Entity is not { IsActive: true } entity || entity.Scene != scene || !entity.MovesHere ||
                    body == host || body == other)
                {
                    if (body.Climbing == rope)
                        body.Climbing = null;
                    climbers.RemoveAt(i);
                    continue;
                }

                _climbing.Add(body);
            }

            _ropes.Add(rope);
            _ropeEnds.Add((host, other == host ? null : other));
        }
    }

    // Wakes a sleeper hinged or tied to something awake; true if it woke any.
    private bool WakeTied(Solids solids) => WakeJoined(solids) | WakeRoped(solids);

    // Whether a body is moved by the rope it climbs this step.
    private bool Climbs(Body body) => _climbing.Count > 0 && _climbing.Contains(body);

    // Wakes a sleeper tied by a rope to something awake, or on a rope someone climbs; true if it woke any.
    private bool WakeRoped(Solids solids)
    {
        var woke = false;
        for (var r = 0; r < _ropes.Count; r++)
        {
            var (host, other) = _ropeEnds[r];
            var rope = _ropes[r];
            var busy = rope.Climbers.Count > 0 || rope.Broke != rope.SeenBroke || rope.Length != rope.SeenLength;
            var hostAwake = host is not null && _moverOf.ContainsKey(host);
            var otherAwake = other is not null && _moverOf.ContainsKey(other);
            if (host is not null && !hostAwake && _sleepers.Contains(host) && (busy || otherAwake))
                (_, woke) = (Wake(host, solids), true);
            if (other is not null && !otherAwake && _sleepers.Contains(other) && (busy || hostAwake))
                (_, woke) = (Wake(other, solids), true);
        }

        return woke;
    }

    // The step's ropes that are awake: their points (an end tied to a body that moves here pulls it, and is pulled by it; one
    // tied to anything else goes where that does), the players on them, where they touch the world, and what holds them
    // together.
    private void SetUpRopes(Span<Mover> movers, Solids solids, float dt)
    {
        _runs.Clear();
        _dots.Clear();
        _ties.Clear();
        _ropeTouches.Clear();
        for (var r = 0; r < _ropes.Count; r++)
        {
            var rope = _ropes[r];
            if (rope.Asleep && !Disturbed(rope, solids))
                continue;
            (rope.Asleep, rope.SeenBroke, rope.SeenLength) = (false, rope.Broke, rope.Length);
            var (host, other) = _ropeEnds[r];
            var points = rope.Points!;
            var count = points.Length;
            var piece = Math.Max(0.01f, rope.Length) / (count - 1);
            var run = new RopeRun
            {
                Rope = rope,
                First = _dots.Count,
                Count = count,
                Piece = piece,
                Broken = rope.Broke ? rope.BreakIndex() : -1,
                Loose = rope.EndIsLoose,
                Radius = Math.Max(RopeThickness, rope.Width / 2),
                SleepingEnd = host is not null && _sleepers.Contains(host) || other is not null && _sleepers.Contains(other),
            };

            var pull = Gravity * rope.GravityScale;
            var weight = 1 / Math.Max(1e-3f, RopeWeight * piece);
            AddEnd(movers, host, rope.WorldAnchor, points[0], dt);
            for (var i = 1; i < count - 1; i++)
                _dots.Add(new RopeDot { Position = points[i], Start = points[i], Velocity = rope.Velocities![i], InverseMass = weight, Pull = pull, Mover = -1, Corner = -1 });
            if (run.Loose)
                _dots.Add(new RopeDot { Position = points[^1], Start = points[^1], Velocity = rope.Velocities![^1], InverseMass = weight, Pull = pull, Mover = -1, Corner = -1 });
            else
                AddEnd(movers, other, rope.WorldOtherAnchor, points[^1], dt);

            // The players on it: each climbs along it (up towards whichever way it rises there), never past a tied end, and
            // off a loose one.
            run.FirstHold = _dots.Count;
            var list = rope.Climbers;
            for (var c = list.Count - 1; c >= 0; c--)
            {
                var climber = list[c];
                var (low, high) = Reach(ref run, climber.At);
                var at = climber.At;
                if (climber.Climb != 0)
                {
                    var (before, after) = (RopePoint(points, piece, at - 0.15f), RopePoint(points, piece, at + 0.15f));
                    at += climber.Climb * ((after - before).Y > 0 ? 1 : -1) * dt;
                }

                // Climbed off a loose end (or where it broke): let go.
                if (at < low.At - 0.05f && !low.Tied || at > high.At + 0.05f && !high.Tied)
                {
                    climber.Body.Climbing = null;
                    list.RemoveAt(c);
                    continue;
                }

                climber.At = Math.Clamp(at, low.At, high.At);
                list[c] = climber;
            }

            // Nearest its start first.
            for (var c = 1; c < list.Count; c++)
            {
                for (var d = c; d > 0 && list[d - 1].At > list[d].At; d--)
                    (list[d - 1], list[d]) = (list[d], list[d - 1]);
            }

            var climbers = CollectionsMarshal.AsSpan(list);
            for (var c = 0; c < climbers.Length; c++)
            {
                ref var climber = ref climbers[c];
                var body = climber.Body;
                (climber.Climb, climber.Swing, var swing) = (0f, 0f, climber.Swing);
                var mass = Math.Max(0.01f, body.Mass);
                _dots.Add(new RopeDot
                {
                    Position = HandsOf(body),
                    Start = HandsOf(body),
                    Velocity = body.Velocity,
                    InverseMass = 1 / mass,
                    Pull = Gravity * body.GravityScale + new Vector2(swing * SwingPush, 0),
                    Mover = -1,
                    Climber = true,
                    Corner = -1,
                });
            }

            run.HoldCount = _dots.Count - run.FirstHold;
            run.Index = _runs.Count;
            (run.Softness, run.Springy) = (RopeSoftness(rope.Stiffness, dt / SolveParts), rope.Stiffness < 1);

            // Two bodies tied together fall asleep together.
            var (start, end) = (_dots[run.First].Mover, _dots[run.First + count - 1].Mover);
            if (start >= 0 && end >= 0 && start != end && run.Broken < 0)
                Join(start, end);
            _runs.Add(run);
        }

        // Where each touches the world, then what holds each together.
        var runs = CollectionsMarshal.AsSpan(_runs);
        for (var r = 0; r < runs.Length; r++)
        {
            if (runs[r].Rope.Collide)
                TouchWorld(ref runs[r], movers, solids, dt);
            Tie(ref runs[r], movers, dt);
        }
    }

    // How a rope as stiff as `stiffness` holds what hangs on it, in parts of the step `part` long: at 1 as stiffly as a hinge;
    // less, a spring that bounces more and gives more the lower it goes (at 0.5 a crate stretches it about 0.3, at 0 about 1.5).
    private static Softness RopeSoftness(float stiffness, float part)
    {
        var s = Math.Clamp(stiffness, 0, 1);
        var (hertz, damping) = s >= 1 ? (JointHertz, JointDamping) : (MathF.Min(JointHertz, 0.65f / MathF.Pow(1 - s, 1.16f)), 0.05f + 0.35f * s);
        var omega = 2 * MathF.PI * hertz;
        var a1 = 2 * damping + part * omega;
        var a2 = part * omega * a1;
        var a3 = 1 / (1 + a2);
        return new Softness(omega / a1, a2 * a3, a3);
    }

    // An end of a rope: tied to a body worked out here, it's a point on that; to anything else, it goes where that does.
    private void AddEnd(Span<Mover> movers, Body? body, Vector2 anchor, Vector2 last, float dt)
    {
        if (body is not null && _moverOf.TryGetValue(body, out var index) && movers[index].Turns)
        {
            _dots.Add(new RopeDot { Position = anchor, Start = anchor, Mover = index, Arm = anchor - movers[index].Center, Corner = -1 });
            return;
        }

        // How fast it's carried, from where it was the step before (something moved by a script, a platform, a player); a
        // jump is a jump.
        var moved = anchor - last;
        var velocity = dt > 0 && moved.LengthSquared() < 4 ? moved / dt : Vector2.Zero;
        _dots.Add(new RopeDot { Position = anchor, Start = anchor, Mover = -1, Fixed = true, End = anchor, Velocity = velocity, Corner = -1 });
    }

    // Whether something has disturbed a sleeping rope: its ends moved, someone hangs on it, it broke or was mended, its length
    // changed, or something moving is near it.
    private bool Disturbed(Rope rope, Solids solids)
    {
        var points = rope.Points!;
        if (rope.Climbers.Count > 0 || rope.Broke != rope.SeenBroke || rope.Length != rope.SeenLength ||
            Vector2.DistanceSquared(rope.WorldAnchor, points[0]) > 1e-8f ||
            !rope.EndIsLoose && Vector2.DistanceSquared(rope.WorldOtherAnchor, points[^1]) > 1e-8f)
            return true;
        if (!rope.Collide)
            return false;

        var bounds = BoundsOf(points, Math.Max(RopeThickness, rope.Width / 2) + 0.1f);
        _ropeNear.Clear();
        solids.Near(bounds, _ropeNear);
        foreach (var index in _ropeNear)
        {
            if (MovingBody(solids.All[index]) is { } body && (_moverOf.ContainsKey(body) || body.Velocity != Vector2.Zero) &&
                solids.AreaOf(index).Bounds.Overlaps(bounds))
                return true;
        }

        return false;
    }

    private static Box BoundsOf(Vector2[] points, float grow)
    {
        var (min, max) = (points[0], points[0]);
        for (var i = 1; i < points.Length; i++)
            (min, max) = (Vector2.Min(min, points[i]), Vector2.Max(max, points[i]));
        return new Box((min + max) / 2, (max - min) / 2 + new Vector2(grow));
    }

    // The point `at` along a rope from its start, as its points are.
    private static Vector2 RopePoint(Vector2[] points, float piece, float at)
    {
        var place = Math.Clamp(at / piece, 0, points.Length - 1);
        var i = Math.Min((int)place, points.Length - 2);
        return Vector2.Lerp(points[i], points[i + 1], place - i);
    }

    // How far along a rope someone at `at` can climb, back and on, within their half of a broken one: near a tied end no
    // closer than ClimbGap; a loose end (or where it broke) all the way to it.
    private static (RopeLimit Low, RopeLimit High) Reach(ref RopeRun run, float at)
    {
        var length = run.Piece * (run.Count - 1);
        var (low, high) = (new RopeLimit(ClimbGap, true), new RopeLimit(run.Loose ? length : length - ClimbGap, !run.Loose));
        if (run.Broken >= 0)
        {
            var cut = (run.Broken + 0.5f) * run.Piece;
            if (at < cut)
                high = new RopeLimit(run.Broken * run.Piece, false);
            else
                low = new RopeLimit((run.Broken + 1) * run.Piece, false);
        }

        if (high.At < low.At)
            high = low;
        return (low, high);
    }

    private readonly record struct RopeLimit(float At, bool Tied);

    // Where a rope's points touch the world (or could, this step): up to two places each, as small balls.
    private void TouchWorld(ref RopeRun run, Span<Mover> movers, Solids solids, float dt)
    {
        var rope = run.Rope;
        var dots = CollectionsMarshal.AsSpan(_dots);
        var (first, last) = (run.First + 1, run.First + run.Count - (run.Loose ? 1 : 2));
        if (last < first)
            return;
        var (min, max) = (dots[first].Position, dots[first].Position);
        var fastest = 0f;
        for (var i = first; i <= last; i++)
        {
            (min, max) = (Vector2.Min(min, dots[i].Position), Vector2.Max(max, dots[i].Position));
            fastest = MathF.Max(fastest, dots[i].Velocity.LengthSquared());
        }

        var margin = (MathF.Sqrt(fastest) + dots[first].Pull.Length() * dt) * dt + TouchSkin;
        var radius = run.Radius;
        var region = new Box((min + max) / 2, (max - min) / 2 + new Vector2(radius + margin + Margin));
        var (host, other) = (rope.Entity!, rope.EndIsLoose ? null : rope.OtherEntity);
        _ropeNear.Clear();
        solids.Near(region, _ropeNear);

        // Not players (it hangs in front of them, for them to grab); what it's tied to only further along it than that reaches
        // from where it's tied (so it drapes over a crate it's tied to, but isn't pushed out of it where it's tied on).
        var (hostClear, otherClear) = (ClearOf(host, radius), other is null ? 0 : ClearOf(other, radius));
        var length = run.Piece * (run.Count - 1);
        _ropeNearEnd.Clear();
        for (var n = _ropeNear.Count - 1; n >= 0; n--)
        {
            var entity = solids.All[_ropeNear[n]].Entity;
            if (HoldsRopes(entity))
                _ropeNear.RemoveAt(n);
        }

        foreach (var index in _ropeNear)
        {
            var entity = solids.All[index].Entity;
            _ropeNearEnd.Add(entity.IsSelfOrDescendantOf(host) ? (byte)1 : other is not null && entity.IsSelfOrDescendantOf(other) ? (byte)2 : (byte)0);
        }

        Hull self = default;
        Hull solid = default;
        run.FirstTouch = _ropeTouches.Count;
        for (var i = first; i <= last; i++)
        {
            ref var dot = ref dots[i];
            var reach = (dot.Velocity.Length() + dot.Pull.Length() * dt) * dt + TouchSkin;
            CircleHull(ref self, new Circle(dot.Position, radius));
            var near = new Box(dot.Position, new Vector2(radius + reach + Margin));
            var along = (i - run.First) * run.Piece;
            for (var n = 0; n < _ropeNear.Count; n++)
            {
                var end = _ropeNearEnd[n];
                if (end == 1 && along < hostClear || end == 2 && length - along < otherClear)
                    continue;
                var index = _ropeNear[n];
                var shape = solids.All[index];
                var area = solids.AreaOf(index);
                if (!area.Bounds.Overlaps(near))
                    continue;
                if (area.Polygon is { } polygon)
                {
                    for (var part = 0; part < polygon.PieceCount; part++)
                    {
                        PieceHull(polygon, part, ref solid);
                        if (HullBounds(ref solid).Overlaps(near) && Collide(ref self, ref solid, reach, out var pieceManifold))
                            AddTouch(movers, i, ref pieceManifold, MovingBody(shape));
                    }

                    continue;
                }

                HullOf(area, ref solid);
                if (!Collide(ref self, ref solid, reach, out var manifold))
                    continue;
                // One-way: only from above it.
                if (shape.OneWay && (manifold.Normal.Y < 0.7f || dot.Position.Y - radius < area.Bounds.Max.Y - 0.05f))
                    continue;
                AddTouch(movers, i, ref manifold, MovingBody(shape));
            }

            foreach (var slope in _slopes)
            {
                if (!slope.Cell.Overlaps(near))
                    continue;
                SlopeHull(ref solid, slope);
                if (Collide(ref self, ref solid, reach, out var manifold) && manifold.Normal.Y > -0.5f)
                    AddTouch(movers, i, ref manifold, null);
            }

            // Walls met by bodies' own shapes (a hex map's): those met by footprints are on the ground, which a rope hangs over.
            foreach (var (walls, _) in _walls)
            {
                if (walls.MetByFootprints || !walls.Bounds.Overlaps(near))
                    continue;
                var (x0, y0, x1, y1) = walls.CellsNear(near);
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        if (!walls.Blocked(x, y))
                            continue;
                        CellHull(walls, x, y, ref solid);
                        if (Collide(ref self, ref solid, reach, out var manifold) && !walls.SharedSide(x, y, manifold.Normal))
                            AddTouch(movers, i, ref manifold, null);
                    }
                }
            }
        }

        run.TouchCount = _ropeTouches.Count - run.FirstTouch;
    }

    // How far along a rope from where it's tied on an entity it's clear of it: as far as the entity reaches from its middle.
    private static float ClearOf(Entity entity, float radius) =>
        entity.GetComponent<Collider>() is { } collider ? BoundsOf(collider).HalfSize.Length() + radius : 0;

    // Whether an entity is (or is inside) a body that holds ropes (a player's, as its movement says: see Body.HoldsRopes).
    private static bool HoldsRopes(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Body>() is { HoldsRopes: true })
                return true;
        }

        return false;
    }

    // A place a rope's point touches: kept if it's one of the two nearest that point has.
    private void AddTouch(Span<Mover> movers, int dot, ref Manifold manifold, Body? body)
    {
        var mover = body is not null && _moverOf.TryGetValue(body, out var index) && movers[index].Turns ? index : -1;
        var touch = new RopeTouch
        {
            Dot = dot,
            Normal = manifold.Normal,
            Gap = manifold.Gap0,
            Point = manifold.Point0,
            Mover = mover,
            Arm = mover >= 0 ? manifold.Point0 - movers[mover].Center : default,
        };

        // Two at most for a point: the nearest.
        var touches = _ropeTouches;
        int same = 0, farthest = -1;
        for (var t = touches.Count - 1; t >= 0 && touches[t].Dot == dot; t--)
        {
            same++;
            if (farthest < 0 || touches[t].Gap > touches[farthest].Gap)
                farthest = t;
        }

        if (same < 2)
            touches.Add(touch);
        else if (touch.Gap < touches[farthest].Gap)
            touches[farthest] = touch;
    }

    // What holds a rope together: each piece its length, each point within reach of a tied end, each player on the rope
    // where they hold it, and whatever hangs on it no further than the rope from what holds it up, straight or round a
    // corner it's wrapped over.
    private void Tie(ref RopeRun run, Span<Mover> movers, float dt)
    {
        var rope = run.Rope;
        run.FirstTie = _ties.Count;
        var (first, count, piece, broken) = (run.First, run.Count, run.Piece, run.Broken);
        var length = piece * (count - 1);
        var stretch = _stretch;
        stretch.Clear();
        for (var i = 0; i < count - 1; i++)
            stretch.Add(1);

        // Corners it's wrapped round: a point touching something that the rope bends round, both ways from it going into
        // what it touches.
        for (var t = run.FirstTouch; t < run.FirstTouch + run.TouchCount; t++)
        {
            var touch = _ropeTouches[t];
            var i = touch.Dot - first;
            if (i <= 0 || i >= count - 1 || touch.Gap > 0.02f || _dots[touch.Dot].Corner >= 0)
                continue;
            var here = _dots[touch.Dot].Position;
            var (back, on) = (_dots[touch.Dot - 1].Position - here, _dots[touch.Dot + 1].Position - here);
            if (back.LengthSquared() < 1e-10f || on.LengthSquared() < 1e-10f)
                continue;
            if (Vector2.Dot(Vector2.Normalize(back) + Vector2.Normalize(on), touch.Normal) < -0.5f)
            {
                var corner = AddCorner(touch.Point + touch.Normal * run.Radius);
                CollectionsMarshal.AsSpan(_dots)[touch.Dot].Corner = corner;
            }
        }

        var dots = CollectionsMarshal.AsSpan(_dots);

        // What hangs on it and what holds it, in order along it: the ends (when tied) and the players on it.
        Span<(int Dot, float At, bool Still)> held = stackalloc (int, float, bool)[2 + Math.Min(run.HoldCount, 16)];
        var heldCount = 0;
        held[heldCount++] = (first, 0, dots[first].Fixed);
        var climbers = rope.Climbers;
        for (var c = 0; c < run.HoldCount && heldCount < held.Length - 1; c++)
            held[heldCount++] = (run.FirstHold + c, climbers[c].At, false);
        if (!run.Loose)
            held[heldCount++] = (first + count - 1, length, dots[first + count - 1].Fixed);

        var cut = broken >= 0 ? (broken + 0.5f) * piece : float.PositiveInfinity;
        for (var h = 0; h + 1 < heldCount; h++)
        {
            var (x, y) = (held[h], held[h + 1]);
            if (x.At < cut != y.At < cut || y.At - x.At < 1e-4f)
                continue;
            var rest = y.At - x.At;
            var apart = Vector2.Distance(DotAt(dots[x.Dot], movers, dt, 0), DotAt(dots[y.Dot], movers, dt, 0));
            var span = Math.Max(1, apart / rest);
            var (from, to) = (Math.Max(0, (int)(x.At / piece)), Math.Min(count - 2, (int)MathF.Ceiling(y.At / piece) - 1));
            for (var i = from; i <= to; i++)
                stretch[i] = MathF.Max(stretch[i], span);
            if (x.Still && y.Still)
            {
                if (apart > rest * OverStretch)
                    run.Overstretched = true;
                continue;
            }

            // Round the first and last corners between them, if it's wrapped round any.
            var (firstCorner, lastCorner) = (-1, -1);
            var (firstAt, lastAt) = (0f, 0f);
            for (var i = (int)(x.At / piece) + 1; i < count - 1 && i * piece < y.At; i++)
            {
                if (dots[first + i].Corner < 0)
                    continue;
                if (firstCorner < 0)
                    (firstCorner, firstAt) = (dots[first + i].Corner, i * piece);
                (lastCorner, lastAt) = (dots[first + i].Corner, i * piece);
            }

            if (firstCorner < 0)
                AddTie(RopeTieKind.Hang, x.Dot, y.Dot, rest, run.Index);
            else
            {
                if (!x.Still && firstAt - x.At > 1e-3f)
                    AddTie(RopeTieKind.Hang, x.Dot, firstCorner, firstAt - x.At, run.Index);
                if (!y.Still && y.At - lastAt > 1e-3f)
                    AddTie(RopeTieKind.Hang, lastCorner, y.Dot, y.At - lastAt, run.Index);
            }
        }

        // Each piece, warm from how hard it pulled the step before.
        var pulls = rope.Pulls!;
        for (var i = 0; i < count - 1; i++)
        {
            if (i == broken)
                continue;
            var tie = AddTie(RopeTieKind.Piece, first + i, first + i + 1, piece * stretch[i], run.Index);
            CollectionsMarshal.AsSpan(_ties)[tie].Pulled = pulls[i];
        }

        // Each free point no further from the nearest tied end of its half than the rope between them.
        var startTied = !dots[first].Free;
        var endTied = !run.Loose;
        for (var i = 1; i < count - (run.Loose ? 0 : 1); i++)
        {
            var onStartSide = broken < 0 || i <= broken;
            var onEndSide = broken < 0 || i > broken;
            var fromStart = startTied && onStartSide ? i : int.MaxValue;
            var fromEnd = endTied && onEndSide ? count - 1 - i : int.MaxValue;
            if (fromStart == int.MaxValue && fromEnd == int.MaxValue)
                continue;
            var (end, pieces) = fromStart <= fromEnd ? (first, fromStart) : (first + count - 1, fromEnd);
            var (lo, hi) = end == first ? (0, i) : (i, count - 1);
            var most = 1f;
            for (var s = lo; s < hi; s++)
                most = MathF.Max(most, stretch[s]);
            AddTie(RopeTieKind.Reach, end, first + i, pieces * piece * most, run.Index);
        }

        // Each player where they hold it, between the two points either side.
        for (var c = 0; c < run.HoldCount; c++)
        {
            var place = Math.Clamp(climbers[c].At / piece, 0, count - 1 - 1e-4f);
            var k = Math.Min((int)place, count - 2);
            var tie = AddTie(RopeTieKind.Hold, first + k, first + k + 1, 0, run.Index);
            ref var hold = ref CollectionsMarshal.AsSpan(_ties)[tie];
            (hold.C, hold.Share) = (run.FirstHold + c, place - k);
        }

        run.TieCount = _ties.Count - run.FirstTie;
    }

    private int AddCorner(Vector2 at)
    {
        _dots.Add(new RopeDot { Position = at, Start = at, Mover = -1, Fixed = true, End = at, Corner = -1 });
        return _dots.Count - 1;
    }

    private int AddTie(RopeTieKind kind, int a, int b, float rest, int run)
    {
        _ties.Add(new RopeTie { Kind = kind, A = a, B = b, C = -1, Rest = rest, Run = run });
        return _ties.Count - 1;
    }

    // Where a rope's point is now, as far into the step as `elapsed`: its own, or on the body it's tied to, or where what
    // it's tied to has got to.
    private static Vector2 DotAt(in RopeDot dot, Span<Mover> movers, float dt, float elapsed)
    {
        if (dot.Mover >= 0)
        {
            ref var mover = ref movers[dot.Mover];
            return mover.Center + mover.Moved + Rotate(dot.Arm, mover.TurnCos, mover.TurnSin);
        }

        return dot.Fixed ? dot.End - dot.Velocity * (dt - elapsed) : dot.Position;
    }

    // How fast a rope's point goes, and from its body's middle to it (for a point on a body).
    private static Vector2 DotVelocity(in RopeDot dot, Span<Mover> movers, out Vector2 arm)
    {
        arm = default;
        if (dot.Mover < 0)
            return dot.Velocity;
        ref var mover = ref movers[dot.Mover];
        arm = Rotate(dot.Arm, mover.TurnCos, mover.TurnSin);
        return mover.Velocity + new Vector2(-mover.Spin * arm.Y, mover.Spin * arm.X);
    }

    // How easily a rope's point moves along `normal` (its body's, for a point on one).
    private static float Give(in RopeDot dot, Span<Mover> movers, Vector2 arm, Vector2 normal)
    {
        if (dot.Mover < 0)
            return dot.Free ? dot.InverseMass : 0;
        ref var mover = ref movers[dot.Mover];
        var turn = Cross(arm, normal);
        return mover.InverseMass + mover.InverseInertia * turn * turn;
    }

    // Whether a rope's point is moved by what pulls on it: its own, or (with `bodies`) one on a body.
    private static bool Moves(in RopeDot dot, bool bodies) => dot.Free || bodies && dot.Mover >= 0;

    // A push on a rope's point (its body, for a point on one).
    private static void Shove(ref RopeDot dot, Span<Mover> movers, Vector2 arm, Vector2 impulse)
    {
        if (dot.Mover >= 0)
        {
            ref var mover = ref movers[dot.Mover];
            mover.Velocity += impulse * mover.InverseMass;
            mover.Spin += mover.InverseInertia * Cross(arm, impulse);
        }
        else if (dot.Free)
            dot.Velocity += impulse * dot.InverseMass;
    }

    // Gravity and the air on the ropes' points, for a part of the step.
    private void PullRopes(float part)
    {
        var drag = 1 / (1 + RopeDrag * part);
        foreach (ref var dot in CollectionsMarshal.AsSpan(_dots))
        {
            if (!dot.Free)
                continue;
            dot.Velocity += dot.Pull * part;
            if (!dot.Climber)
                dot.Velocity *= drag;
        }
    }

    // The ropes' pushes of the part before (of the step before, for its pieces, to begin with), as a first guess at this part's.
    private void WarmRopes(Span<Mover> movers, float dt, float elapsed)
    {
        var dots = CollectionsMarshal.AsSpan(_dots);
        foreach (ref var tie in CollectionsMarshal.AsSpan(_ties))
        {
            if (tie.Kind == RopeTieKind.Hold)
            {
                if (tie.Held == Vector2.Zero)
                    continue;
                ref var climber = ref dots[tie.C];
                climber.Velocity += tie.Held * climber.InverseMass;
                Shove(ref dots[tie.A], movers, default, -tie.Held * (1 - tie.Share));
                Shove(ref dots[tie.B], movers, default, -tie.Held * tie.Share);
                continue;
            }

            if (tie.Pulled == 0)
                continue;
            var (a, b) = (DotAt(dots[tie.A], movers, dt, elapsed), DotAt(dots[tie.B], movers, dt, elapsed));
            var between = b - a;
            var length = between.Length();
            if (length < 1e-6f)
                continue;
            var normal = between / length;
            DotVelocity(dots[tie.A], movers, out var armA);
            DotVelocity(dots[tie.B], movers, out var armB);
            var bodies = tie.Kind == RopeTieKind.Hang;
            if (tie.Kind != RopeTieKind.Reach && Moves(dots[tie.A], bodies))
                Shove(ref dots[tie.A], movers, armA, normal * tie.Pulled);
            if (Moves(dots[tie.B], bodies))
                Shove(ref dots[tie.B], movers, armB, -normal * tie.Pulled);
        }

        foreach (ref var touch in CollectionsMarshal.AsSpan(_ropeTouches))
        {
            if (touch.Pushed == 0 && touch.Rubbed == 0)
                continue;
            ref var dot = ref dots[touch.Dot];
            dot.Velocity += (touch.Normal * touch.Pushed + new Vector2(-touch.Normal.Y, touch.Normal.X) * touch.Rubbed) * dot.InverseMass;
        }
    }

    // Once round everything holding the ropes together, then where they touch the world.
    private void SolveRopes(Span<Mover> movers, Softness rigid, float inversePart, bool ease, float dt, float elapsed)
    {
        var dots = CollectionsMarshal.AsSpan(_dots);
        var ties = CollectionsMarshal.AsSpan(_ties);
        var runs = CollectionsMarshal.AsSpan(_runs);

        // Twice along the pieces, one way then the other, so a pull travels along the rope.
        for (var sweep = 0; sweep < 2; sweep++)
        {
            for (var n = 0; n < ties.Length; n++)
            {
                ref var tie = ref ties[sweep == 0 ? n : ties.Length - 1 - n];
                switch (tie.Kind)
                {
                    case RopeTieKind.Piece:
                        Pull(ref tie, dots, movers, rigid, inversePart, ease, dt, elapsed, both: true, slack: true, bodies: false);
                        break;
                    case RopeTieKind.Reach:
                        Pull(ref tie, dots, movers, rigid, inversePart, ease, dt, elapsed, both: false, slack: true, bodies: false);
                        break;
                    case RopeTieKind.Hang when sweep == 1:
                        Pull(ref tie, dots, movers, runs[tie.Run].Softness, inversePart, ease || runs[tie.Run].Springy, dt, elapsed, both: true, slack: true, bodies: true);
                        break;
                    case RopeTieKind.Hold when sweep == 1:
                        Hold(ref tie, dots, movers, rigid, ease, dt, elapsed);
                        break;
                }
            }
        }

        foreach (ref var touch in CollectionsMarshal.AsSpan(_ropeTouches))
            Press(ref touch, dots, movers, inversePart, ease);
    }

    // Two points held `Rest` apart (`slack`: no further apart, never pushed together), soft as `softness` says, pulled back
    // together where they've come apart with `ease`. Only the second point is pushed when not `both`, and a point on a body
    // only with `bodies` (a body is held by what hangs it, not the rope's pieces: they go where it does).
    private static void Pull(ref RopeTie tie, Span<RopeDot> dots, Span<Mover> movers, Softness softness, float inversePart, bool ease,
        float dt, float elapsed, bool both, bool slack, bool bodies)
    {
        ref var a = ref dots[tie.A];
        ref var b = ref dots[tie.B];
        var between = DotAt(b, movers, dt, elapsed) - DotAt(a, movers, dt, elapsed);
        var length = between.Length();
        if (length < 1e-6f)
            return;
        var normal = between / length;
        var apart = length - tie.Rest;
        var velocityA = DotVelocity(a, movers, out var armA);
        var velocityB = DotVelocity(b, movers, out var armB);
        var (useA, useB) = (both && Moves(a, bodies), Moves(b, bodies));
        var give = (useA ? Give(a, movers, armA, normal) : 0) + (useB ? Give(b, movers, armB, normal) : 0);
        if (!(give > 0))
            return;

        // How fast they go apart, and how fast they may: a slack rope may close its gap this part, no more.
        var opening = Vector2.Dot(velocityB - velocityA, normal);
        float bias, massScale = 1, impulseScale = 0;
        if (slack && apart < 0)
            bias = apart * inversePart;
        else if (ease)
            (bias, massScale, impulseScale) = (apart * softness.BiasRate, softness.MassScale, softness.ImpulseScale);
        else
            bias = 0;

        var pull = massScale * (opening + bias) / give - impulseScale * tie.Pulled;
        var pulled = tie.Pulled + pull;
        if (slack)
            pulled = Math.Max(pulled, 0);
        pull = pulled - tie.Pulled;
        tie.Pulled = pulled;
        if (useA)
            Shove(ref a, movers, armA, normal * pull);
        if (useB)
            Shove(ref b, movers, armB, -normal * pull);
    }

    // A player held where they hold the rope: at their share of the way between the two points either side.
    private static void Hold(ref RopeTie tie, Span<RopeDot> dots, Span<Mover> movers, Softness softness, bool ease, float dt, float elapsed)
    {
        ref var climber = ref dots[tie.C];
        ref var a = ref dots[tie.A];
        ref var b = ref dots[tie.B];
        var share = tie.Share;
        var (wa, wb) = (a.Free ? a.InverseMass : a.Mover >= 0 ? movers[a.Mover].InverseMass : 0, b.Free ? b.InverseMass : b.Mover >= 0 ? movers[b.Mover].InverseMass : 0);
        var give = climber.InverseMass + (1 - share) * (1 - share) * wa + share * share * wb;
        if (!(give > 0))
            return;
        var at = Vector2.Lerp(DotAt(a, movers, dt, elapsed), DotAt(b, movers, dt, elapsed), share);
        var going = climber.Velocity - Vector2.Lerp(DotVelocity(a, movers, out _), DotVelocity(b, movers, out _), share);
        var (bias, massScale, impulseScale) = ease ? ((climber.Position - at) * softness.BiasRate, softness.MassScale, softness.ImpulseScale) : (Vector2.Zero, 1f, 0f);
        var hold = -massScale * (going + bias) / give - impulseScale * tie.Held;
        tie.Held += hold;
        climber.Velocity += hold * climber.InverseMass;
        Shove(ref a, movers, default, -hold * (1 - share));
        Shove(ref b, movers, default, -hold * share);
    }

    // A rope's point kept out of what it touches, closing a gap only as far as it goes, and rubbing along it.
    private static void Press(ref RopeTouch touch, Span<RopeDot> dots, Span<Mover> movers, float inversePart, bool ease)
    {
        ref var dot = ref dots[touch.Dot];
        if (!dot.Free)
            return;
        var (solidMoved, solidVelocity) = (Vector2.Zero, Vector2.Zero);
        if (touch.Mover >= 0)
        {
            ref var mover = ref movers[touch.Mover];
            var arm = Rotate(touch.Arm, mover.TurnCos, mover.TurnSin);
            solidMoved = mover.Moved + arm - touch.Arm;
            solidVelocity = mover.Velocity + new Vector2(-mover.Spin * arm.Y, mover.Spin * arm.X);
        }

        var normal = touch.Normal;
        var gap = touch.Gap + Vector2.Dot(dot.Position - dot.Start - solidMoved, normal);
        var relative = dot.Velocity - solidVelocity;
        var approach = Vector2.Dot(relative, normal);
        var bias = gap > 0 ? gap * inversePart : ease ? MathF.Max(gap * inversePart * 0.5f, -PushApartSpeed) : 0;
        var mass = 1 / dot.InverseMass;
        var pushed = Math.Max(touch.Pushed - mass * (approach + bias), 0);
        dot.Velocity += normal * (pushed - touch.Pushed) * dot.InverseMass;
        touch.Pushed = pushed;

        // Rubbing along it, as hard as it's pressed.
        var tangent = new Vector2(-normal.Y, normal.X);
        var most = RopeGrip * pushed;
        var rubbed = Math.Clamp(touch.Rubbed - mass * Vector2.Dot(dot.Velocity - solidVelocity, tangent), -most, most);
        dot.Velocity += tangent * (rubbed - touch.Rubbed) * dot.InverseMass;
        touch.Rubbed = rubbed;
    }

    // The ropes' points move on through a part of the step.
    private void MoveRopes(float part)
    {
        foreach (ref var dot in CollectionsMarshal.AsSpan(_dots))
        {
            if (dot.Free)
                dot.Position += dot.Velocity * part;
        }
    }

    // After the step: each rope's points kept (its ends where what they're tied to has got to), the players on it moved to
    // where they hold it, how hard it's pulled, whether it snapped, whether it's still enough to sleep; and all of it kept in
    // the contact memory.
    private void FinishRopes(ContactMemory memory, Span<Mover> movers, Solids solids, float dt, Action<Entity, Entity> onCollision)
    {
        var dots = CollectionsMarshal.AsSpan(_dots);
        var ties = CollectionsMarshal.AsSpan(_ties);
        var part = dt / SolveParts;
        var step = _ropeScene?.Step ?? 0;
        foreach (ref var run in CollectionsMarshal.AsSpan(_runs))
        {
            var rope = run.Rope;
            var (points, velocities, pulls, was) = (rope.Points!, rope.Velocities!, rope.Pulls!, rope.Was!);
            Array.Copy(points, was, points.Length);
            var fastest = 0f;
            for (var i = 0; i < run.Count; i++)
            {
                ref var dot = ref dots[run.First + i];
                if (dot.Free)
                {
                    (points[i], velocities[i]) = (dot.Position, dot.Velocity);
                    fastest = MathF.Max(fastest, dot.Velocity.LengthSquared());
                }
                else
                {
                    velocities[i] = DotVelocity(dot, movers, out _);
                    fastest = MathF.Max(fastest, velocities[i].LengthSquared());
                }
            }

            points[0] = rope.WorldAnchor;
            if (!run.Loose)
                points[^1] = rope.WorldOtherAnchor;
            rope.MovedIn = step;

            // How hard it's pulled: what hangs on it, or (with nothing hanging on it) its pieces.
            var (hung, pieces, worstTie) = (0f, 0f, -1);
            Array.Clear(pulls);
            for (var t = run.FirstTie; t < run.FirstTie + run.TieCount; t++)
            {
                ref var tie = ref ties[t];
                var force = tie.Pulled / part;
                if (tie.Kind == RopeTieKind.Piece)
                    (pulls[tie.A - run.First], pieces) = (tie.Pulled, MathF.Max(pieces, force));
                else if (tie.Kind == RopeTieKind.Hang && (worstTie < 0 || force > hung))
                    (hung, worstTie) = (force, t);
            }

            var tension = worstTie >= 0 ? hung : pieces;

            // With a body at an end asleep (hanging on it, or resting), it pulls as it did as it fell asleep.
            if (!run.SleepingEnd || worstTie >= 0)
                rope.Tension = tension;
            if (rope.BreakForce > 0 && !rope.Broke && rope.Entity!.RunsHere && (tension > rope.BreakForce || run.Overstretched))
            {
                // It snaps in the middle of what pulled it hardest (between a player and what holds them up, or the whole rope).
                var at = run.Count / 2 - 1;
                if (worstTie >= 0)
                {
                    var (from, to) = (SpanAt(ref run, ties[worstTie].A, rope), SpanAt(ref run, ties[worstTie].B, rope));
                    at = (int)((from + to) / 2 / run.Piece);
                }

                (rope.Broke, rope.BrokenAt, rope.SeenBroke) = (true, Math.Clamp(at, 0, run.Count - 2), true);
                Snapped.Add(rope);
            }

            // The players on it, moved to where they hold it.
            for (var c = 0; c < run.HoldCount; c++)
            {
                ref var dot = ref dots[run.FirstHold + c];
                var body = rope.Climbers[c].Body;
                if (body.Entity!.GetComponent<Collider>() is not { } collider)
                    continue;
                (body.IsGrounded, body.Ground, body.WallSide) = (false, null, 0);
                body.Velocity = dot.Velocity;
                var move = dot.Position - HandsOf(body);
                MoveAxis(body, collider, solids, new Vector2(move.X, 0), onCollision);
                MoveAxis(body, collider, solids, new Vector2(0, move.Y), onCollision);
                EndStep(body, dt);
            }

            // Still a while (and nobody on it), it falls asleep.
            var still = run.HoldCount == 0 && fastest < SleepSpeed * SleepSpeed;
            rope.StillFor = still ? rope.StillFor + dt : 0;
            if (rope.StillFor >= SleepAfter)
            {
                rope.Asleep = true;
                Array.Clear(velocities);
            }
        }

        foreach (var rope in _ropes)
            RememberRope(memory, rope);
    }

    // How far along a rope one of its points (or a player on it) is.
    private float SpanAt(ref RopeRun run, int dot, Rope rope)
    {
        if (dot >= run.FirstHold && dot < run.FirstHold + run.HoldCount)
            return rope.Climbers[dot - run.FirstHold].At;
        if (dot >= run.First && dot < run.First + run.Count)
            return (dot - run.First) * run.Piece;
        // A corner: where the point touching it is.
        for (var i = run.First; i < run.First + run.Count; i++)
        {
            if (_dots[i].Corner == dot)
                return (i - run.First) * run.Piece;
        }

        return run.Piece * (run.Count - 1) / 2;
    }

    // A rope's state, kept with the scene for a rewind.
    private static void RememberRope(ContactMemory memory, Rope rope)
    {
        if (rope.Points is not { } points)
            return;
        var id = rope.Entity!.Id;
        memory.Set(new ContactKey(id, Guid.Empty, RopePart, RopeHeader), new Vector3(points.Length, rope.BrokenAt, rope.StillFor));
        var (velocities, pulls) = (rope.Velocities!, rope.Pulls!);
        for (var i = 0; i < points.Length; i++)
        {
            memory.Set(new ContactKey(id, Guid.Empty, RopePart, RopePoints + 2 * i), new Vector3(points[i], pulls[i]));
            memory.Set(new ContactKey(id, Guid.Empty, RopePart, RopePoints + 2 * i + 1), new Vector3(velocities[i], 0));
        }

        foreach (var climber in rope.Climbers)
            memory.Set(new ContactKey(id, climber.Body.Entity!.Id, RopePart, RopeHolds), new Vector3(climber.At, 0, 0));
    }

    // A rope as a step works it out.
    private struct RopeRun
    {
        public Rope Rope;
        public int Index;

        // Its points in _dots (from its start), and the players on it after them; the places it touches the world.
        public int First;
        public int Count;
        public int FirstHold;
        public int HoldCount;
        public int FirstTouch;
        public int FirstTie;
        public int TieCount;
        public int TouchCount;

        // How long each piece is; the piece it's broken at (-1 for none); whether its other end hangs free; how thick it is.
        public float Piece;
        public int Broken;
        public bool Loose;
        public float Radius;

        // How it holds what hangs on it: as stiff as it is; less than stiff, a spring (soft every time round, not just while
        // easing apart).
        public bool Springy;
        public Softness Softness;

        // A body at an end is asleep.
        public bool SleepingEnd;

        // Pulled past its length between two things that don't move here.
        public bool Overstretched;
    }

    // A point a step works out for a rope: one of its own (Free), one on a body (Mover), one where something it's tied to goes
    // (Fixed: at End as the step ends, going at Velocity), or a player hanging on it (Climber, free too).
    private struct RopeDot
    {
        public Vector2 Position;
        public Vector2 Start;
        public Vector2 Velocity;
        public float InverseMass;
        public Vector2 Pull;
        public int Mover;
        public Vector2 Arm;
        public bool Fixed;
        public Vector2 End;
        public bool Climber;

        // The corner it's wrapped round (in _dots), if it touches one.
        public int Corner;

        public readonly bool Free => Mover < 0 && !Fixed;
    }

    private enum RopeTieKind : byte
    {
        // A piece of rope between two points of it.
        Piece,

        // A point no further from a tied end than the rope between them (only the point is pulled).
        Reach,

        // What hangs on the rope no further from what holds it up than the rope between them.
        Hang,

        // A player where they hold the rope, between two of its points.
        Hold,
    }

    private struct RopeTie
    {
        public RopeTieKind Kind;
        public int Run;
        public int A;
        public int B;

        // A player held between A and B, Share of the way along.
        public int C;
        public float Share;

        public float Rest;

        // This part's pull (Hold: push) so far.
        public float Pulled;
        public Vector2 Held;
    }

    private struct RopeTouch
    {
        public int Dot;
        public Vector2 Normal;
        public float Gap;
        public Vector2 Point;

        // What it touches, when that's a body worked out here, and from its middle to the point as the step began.
        public int Mover;
        public Vector2 Arm;

        public float Pushed;
        public float Rubbed;
    }
}
