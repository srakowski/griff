using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A hex map's walls: its solid hexagons, each a wall with six sides. A body's collider, box or circle as it is, is kept off
/// them as it moves, pushed back out across the side it went in by, so it slides along a wall's slanted sides as well as its
/// straight ones. Rays and overlaps meet the hexagons too.
/// </summary>
internal sealed class HexWalls : TileWalls
{
    // Touching isn't overlapping: an edge this close (in tiles) to a side counts as on it.
    private const float Skin = 1e-4f;

    // A move is looked along in steps of at most this much of a tile, so nothing goes through a hexagon (each is more
    // than four tenths of a tile from its middle to every side).
    private const float HexStep = 0.25f;

    // The most steps a move is looked along in (a body moving so fast it would take more goes in bigger ones).
    private const int HexReach = 256;

    // How many times a step pushes a body out of the hexagons round it, one at a time (for a notch between two of them).
    private const int HexPasses = 8;

    private readonly Matrix4x4 _toWorld;
    private readonly Matrix4x4 _toLocal;

    // A hexagon's corners from its middle in the world, clockwise from the top left one, and each side's normal out
    // of it: side i runs from corner i to corner i + 1 and is shared with neighbor i (see Tilemap.Neighbors).
    private readonly Vector2[] _corners = new Vector2[6];
    private readonly Vector2[] _normals = new Vector2[6];
    private readonly float _size;

    private HexWalls(Tilemap map, Matrix4x4 toWorld, Matrix4x4 toLocal) : base(map)
    {
        _toWorld = toWorld;
        _toLocal = toLocal;
        _size = map.TileSize;

        var middle = map.CellCenter(0, 0);
        var outline = map.OutlineOf(0, 0, 0, 0);
        for (var i = 0; i < 6; i++)
            _corners[i] = Vector2.TransformNormal(outline[i] - middle, toWorld);
        for (var i = 0; i < 6; i++)
        {
            var side = _corners[(i + 1) % 6] - _corners[i];
            var normal = Vector2.Normalize(new Vector2(-side.Y, side.X));
            _normals[i] = Vector2.Dot(normal, _corners[i] + _corners[(i + 1) % 6]) < 0 ? -normal : normal;
        }

        var (min, max) = map.Bounds();
        var (lo, hi) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in (ReadOnlySpan<Vector2>)[min, max, new Vector2(min.X, max.Y), new Vector2(max.X, min.Y)])
        {
            var p = Vector2.Transform(corner, toWorld);
            (lo, hi) = (Vector2.Min(lo, p), Vector2.Max(hi, p));
        }

        Bounds = new Box((lo + hi) / 2, (hi - lo) / 2);
        var scale = MathF.Min(Vector2.TransformNormal(Vector2.UnitX, toWorld).Length(), Vector2.TransformNormal(Vector2.UnitY, toWorld).Length());
        StepLength = Math.Max(1e-4f, HexStep * _size * scale);
    }

    public override Box Bounds { get; }

    /// <summary>The longest step a move or a ray is looked along in, in world units.</summary>
    public float StepLength { get; }

    /// <summary>The walls of a hex map as it's placed now, or null if it has no size or its place can't be turned round.</summary>
    public static HexWalls? Of(Tilemap map)
    {
        if (map.TileSize <= 0)
            return null;
        var world = map.Entity!.Transform.WorldMatrix;
        if (!Matrix4x4.Invert(world, out var toLocal))
            return null;
        return new HexWalls(map, world, toLocal);
    }

    private (int X, int Y) CellAt(Vector2 world) => Map.CellAt(Vector2.Transform(world, _toLocal));

    public override bool BlockedAt(Vector2 world)
    {
        var (x, y) = CellAt(world);
        return Blocked(x, y);
    }

    // Moves the collider from where its middle was (`from`) to where it is now, a little at a time, pushing it back out of
    // any solid hexagon it goes into across the side it went in by: so walking into a wall slides along it. Velocity into a
    // side stops (or bounces back off it); along it carries on. A body that hasn't moved is left alone, as on a square map
    // (a grid mover places itself).
    public override WallMove? KeepOff(Vector2 from, Vector2 to, Vector2 half, float? radius, Vector2 velocity, float bounciness,
        float settleSpeed)
    {
        var move = to - from;
        if (move == Vector2.Zero)
            return null;
        if (!Union(new Box(from, half), new Box(to, half)).Overlaps(Bounds))
            return null;

        var bounce = Math.Clamp(bounciness, 0, 1);
        var (hit, grounded, wall) = (false, false, 0);
        var steps = Math.Clamp((int)MathF.Ceiling(move.Length() / StepLength), 1, HexReach);
        var at = from;
        for (var step = 1; step <= steps; step++)
        {
            at += move / steps;
            // Out of the hexagons it has gone into: the deepest, or the two deepest at once (in a notch between two,
            // pushed out of both together it settles in the middle, rather than rocking from side to side).
            for (var pass = 0; pass < HexPasses; pass++)
            {
                (float Depth, Vector2 Normal)? first = null, second = null;
                var (x0, y0, x1, y1) = CellsNear(new Box(at, half));
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        if (!Blocked(x, y) || PushOut(x, y, new Box(at, half), radius) is not { } push)
                            continue;
                        if (first is null || push.Depth > first.Value.Depth)
                            (first, second) = (push, first);
                        else if (second is null || push.Depth > second.Value.Depth)
                            second = push;
                    }
                }

                if (first is not var (depth, normal))
                    break;
                at += OutOf(depth, normal, second);
                hit = true;
                Meet(normal);
                if (second is var (_, other))
                    Meet(other);
            }
        }

        return hit ? new WallMove(at, velocity, grounded, wall) : null;

        // Into the side stops, or bounces back off it; along it carries on.
        void Meet(Vector2 side)
        {
            var into = Vector2.Dot(velocity, side);
            if (into < 0)
            {
                var back = -into * bounce;
                velocity += side * (-into + (back < settleSpeed ? 0 : back));
            }

            grounded |= side.Y > 0.5f;
            if (MathF.Abs(side.X) > 0.9f)
                wall = -MathF.Sign(side.X);
        }
    }

    // How far to move to get out of the deepest hexagon a body is in, and out of the next deepest at the same time when
    // that one's side runs another way (the move that takes it just out of both).
    private static Vector2 OutOf(float depth, Vector2 normal, (float Depth, Vector2 Normal)? second)
    {
        if (second is not var (otherDepth, other))
            return normal * depth;
        var across = normal.X * other.Y - normal.Y * other.X;
        if (MathF.Abs(across) < 0.2f)
            return normal * depth;

        // Solve d . normal = depth and d . other = otherDepth.
        var both = new Vector2(depth * other.Y - otherDepth * normal.Y, otherDepth * normal.X - depth * other.X) / across;
        return both.Length() <= 4 * (depth + otherDepth) ? both : normal * depth;
    }

    // The middle of hexagon x, y in the world.
    private Vector2 Middle(int x, int y) => Vector2.Transform(Map.CellCenter(x, y), _toWorld);

    // Whether the hexagon beside x, y across side i is open (not a wall).
    private bool Open(int x, int y, int i)
    {
        var (nx, ny) = Tilemap.NeighborOf(x, y, i);
        return !Blocked(nx, ny);
    }

    // A hexagon's six corners in the world, clockwise from its top left one.
    private Vector2[] Hexagon(int x, int y)
    {
        var middle = Middle(x, y);
        var corners = new Vector2[6];
        for (var i = 0; i < 6; i++)
            corners[i] = middle + _corners[i];
        return corners;
    }

    /// <summary>A hexagon in the world, clockwise from its top left corner.</summary>
    public override int Cell(int x, int y, Span<Vector2> corners)
    {
        var middle = Middle(x, y);
        for (var i = 0; i < 6; i++)
            corners[i] = middle + _corners[i];
        return 6;
    }

    public override bool SharedSide(int x, int y, Vector2 normal)
    {
        for (var i = 0; i < 6; i++)
        {
            if (Vector2.Dot(_normals[i], normal) > 0.999f)
                return !Open(x, y, i);
        }

        return false;
    }

    public override (int X0, int Y0, int X1, int Y1) CellsNear(Box box)
    {
        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in (ReadOnlySpan<Vector2>)[box.Min, box.Max, new Vector2(box.Min.X, box.Max.Y), new Vector2(box.Max.X, box.Min.Y)])
        {
            var local = Vector2.Transform(corner, _toLocal) / _size;
            (min, max) = (Vector2.Min(min, local), Vector2.Max(max, local));
        }

        // Row y's hexagons reach from 3/4 y to 3/4 y + 1 tiles down, and column x's from x to x + 1.5 across.
        var (y0, y1) = ((int)MathF.Ceiling((-max.Y - 1) / 0.75f), (int)MathF.Floor(-min.Y / 0.75f));
        var (x0, x1) = ((int)MathF.Floor(min.X) - 1, (int)MathF.Floor(max.X));
        return (Math.Max(0, x0), Math.Max(0, y0), Math.Min(Width - 1, x1), Math.Min(Height - 1, y1));
    }

    // How far, and which way, to push a box (or the circle of `radius` round its middle) to get it out of hexagon x, y: the
    // shortest way out across one of its open sides (or straight along the box's own sides, where those lead into the
    // open), or null if they don't overlap (just touching doesn't count).
    private (float Depth, Vector2 Normal)? PushOut(int x, int y, Box box, float? radius)
    {
        var middle = Middle(x, y);
        var skin = Skin * _size;
        return radius is { } r ? PushCircleOut(x, y, middle, new Circle(box.Center, r), skin) : PushBoxOut(x, y, middle, box, skin);
    }

    private (float Depth, Vector2 Normal)? PushBoxOut(int x, int y, Vector2 middle, Box box, float skin)
    {
        // Apart along any of the hexagon's normals or the box's sides: not overlapping.
        for (var i = 0; i < 3; i++)
        {
            if (Overlap(middle, box, _normals[i]) is var (low, high) && (low <= skin || high <= skin))
                return null;
        }

        if (Overlap(middle, box, Vector2.UnitX) is var (left, right) && (left <= skin || right <= skin))
            return null;
        if (Overlap(middle, box, Vector2.UnitY) is var (down, up) && (down <= skin || up <= skin))
            return null;

        // Out across an open side, or along the box's own axes towards one, whichever is shortest.
        (float Depth, Vector2 Normal)? best = null;
        for (var all = 0; all < 2 && best is null; all++)
        {
            for (var i = 0; i < 6; i++)
            {
                if (all == 0 && !Open(x, y, i))
                    continue;
                Consider(Overlap(middle, box, _normals[i]).Forward, _normals[i]);
            }

            ConsiderAxis(Vector2.UnitX, right);
            ConsiderAxis(-Vector2.UnitX, left);
            ConsiderAxis(Vector2.UnitY, up);
            ConsiderAxis(-Vector2.UnitY, down);

            void ConsiderAxis(Vector2 way, float depth)
            {
                if (all == 1 || OpensTowards(x, y, way))
                    Consider(depth, way);
            }
        }

        return best;

        void Consider(float depth, Vector2 way)
        {
            if (best is null || depth < best.Value.Depth)
                best = (depth, way);
        }
    }

    private (float Depth, Vector2 Normal)? PushCircleOut(int x, int y, Vector2 middle, Circle circle, float skin)
    {
        var center = circle.Center;
        var inside = true;
        var (nearest, closest) = (float.PositiveInfinity, Vector2.Zero);
        for (var i = 0; i < 6; i++)
        {
            var (a, b) = (middle + _corners[i], middle + _corners[(i + 1) % 6]);
            if (Vector2.Dot(_normals[i], center - a) > 0)
                inside = false;
            var side = b - a;
            var t = Math.Clamp(Vector2.Dot(center - a, side) / Math.Max(1e-12f, side.LengthSquared()), 0, 1);
            var point = a + side * t;
            var distance = Vector2.DistanceSquared(point, center);
            if (distance < nearest)
                (nearest, closest) = (distance, point);
        }

        if (!inside)
        {
            var distance = MathF.Sqrt(nearest);
            if (distance >= circle.Radius - skin || distance < 1e-9f)
                return null;
            return (circle.Radius - distance, (center - closest) / distance);
        }

        // Its middle is inside: out across the nearest open side.
        (float Depth, Vector2 Normal)? best = null;
        for (var all = 0; all < 2 && best is null; all++)
        {
            for (var i = 0; i < 6; i++)
            {
                if (all == 0 && !Open(x, y, i))
                    continue;
                var depth = Vector2.Dot(_normals[i], middle + _corners[i] - center) + circle.Radius;
                if (best is null || depth < best.Value.Depth)
                    best = (depth, _normals[i]);
            }
        }

        return best;
    }

    // Whether one of hexagon x, y's open sides faces (mostly) the given way, so pushing a box that way leads out.
    private bool OpensTowards(int x, int y, Vector2 way)
    {
        for (var i = 0; i < 6; i++)
        {
            if (Vector2.Dot(_normals[i], way) > 0.5f && Open(x, y, i))
                return true;
        }

        return false;
    }

    // How much a box and the hexagon round `middle` overlap along `axis` (a unit vector): as how far the box would go
    // back against it to be clear (Back) and how far forward along it (Forward).
    private (float Back, float Forward) Overlap(Vector2 middle, Box box, Vector2 axis)
    {
        var (low, high) = (float.PositiveInfinity, float.NegativeInfinity);
        for (var i = 0; i < 6; i++)
        {
            var p = Vector2.Dot(middle + _corners[i], axis);
            (low, high) = (Math.Min(low, p), Math.Max(high, p));
        }

        var center = Vector2.Dot(box.Center, axis);
        var reach = MathF.Abs(axis.X) * box.HalfSize.X + MathF.Abs(axis.Y) * box.HalfSize.Y;
        return (center + reach - low, high - (center - reach));
    }

    public override bool Overlaps(Box box)
    {
        var (x0, y0, x1, y1) = CellsNear(box);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (Blocked(x, y) && PolygonOverlapsBox(Hexagon(x, y), box))
                    return true;
            }
        }

        return false;
    }

    public override bool Overlaps(Circle circle)
    {
        var (x0, y0, x1, y1) = CellsNear(circle.Bounds);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (Blocked(x, y) && PolygonOverlapsCircle(Hexagon(x, y), circle))
                    return true;
            }
        }

        return false;
    }

    public override (float Distance, Vector2 Normal)? Raycast(Vector2 origin, Vector2 direction, float maxDistance)
    {
        // Only the stretch of the ray over the map.
        var (near, far) = (0f, maxDistance);
        for (var axis = 0; axis < 2; axis++)
        {
            var (s, d, low, high) = axis == 0
                ? (origin.X, direction.X, Bounds.Min.X, Bounds.Max.X)
                : (origin.Y, direction.Y, Bounds.Min.Y, Bounds.Max.Y);
            if (MathF.Abs(d) < 1e-12f)
            {
                if (s < low || s > high)
                    return null;
                continue;
            }

            var (t0, t1) = ((low - s) / d, (high - s) / d);
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            (near, far) = (Math.Max(near, t0), Math.Min(far, t1));
        }

        if (near > far)
            return null;

        // A step at a time along it, looking at the hexagon there and the six round it: every hexagon the ray crosses
        // is one of those at the step nearest where it goes in, so the first hit is found by a step past it.
        (float Distance, Vector2 Normal)? best = null;
        var step = StepLength;
        for (var t = near; ; t += step)
        {
            var (x, y) = CellAt(origin + direction * Math.Min(t, far));
            Look(x, y);
            for (var i = 0; i < 6; i++)
            {
                var (nx, ny) = Tilemap.NeighborOf(x, y, i);
                Look(nx, ny);
            }

            if ((best is { } found && found.Distance <= t - step) || t >= far)
                break;
        }

        return best;

        void Look(int x, int y)
        {
            if (!Blocked(x, y) || RayInto(x, y, origin, direction) is not var (distance, normal) || distance > maxDistance)
                return;
            if (best is null || distance < best.Value.Distance)
                best = (distance, normal);
        }
    }

    // Where a ray goes into hexagon x, y through one of its sides, and that side's normal; null if it misses, or starts
    // inside it.
    private (float Distance, Vector2 Normal)? RayInto(int x, int y, Vector2 origin, Vector2 direction)
    {
        var middle = Middle(x, y);
        var (enter, leave, side) = (float.NegativeInfinity, float.PositiveInfinity, -1);
        for (var i = 0; i < 6; i++)
        {
            var normal = _normals[i];
            var facing = Vector2.Dot(normal, direction);
            var away = Vector2.Dot(normal, middle + _corners[i] - origin);
            if (MathF.Abs(facing) < 1e-12f)
            {
                if (away < 0)
                    return null;
                continue;
            }

            var t = away / facing;
            if (facing < 0)
            {
                if (t > enter)
                    (enter, side) = (t, i);
            }
            else
            {
                leave = Math.Min(leave, t);
            }
        }

        if (side < 0 || enter > leave || leave <= 0 || enter < -1e-5f * _size)
            return null;
        return (Math.Max(0, enter), _normals[side]);
    }
}
