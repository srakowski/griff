using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

/// <summary>
/// Where walkers can walk in a scene, worked out from its <see cref="WalkArea"/>s as they are now: the floor (every area
/// that doesn't block, joined where they overlap) less the holes (those that do). It finds the shortest way between two
/// points on it, bending only round the corners in the way (a corner of the floor that juts in, or one of a hole), so a
/// walker goes straight where it can and hugs corners where it can't. Made afresh for each walk, so an area that moves
/// (a hole inside a character) is where it is now. It's the same on every machine given the same areas.
/// </summary>
internal sealed class WalkMap
{
    // How far from a corner a way round it keeps, and how far into the floor a point off it is put, in world units.
    private const float Margin = 0.05f;

    private readonly List<Vector2[]> _floors = [];
    private readonly List<Vector2[]> _holes = [];

    private WalkMap()
    {
    }

    /// <summary>True if the scene has somewhere to walk; without, walkers walk straight anywhere.</summary>
    public bool HasFloor => _floors.Count > 0;

    /// <summary>The walk areas of <paramref name="scene"/> that are there now.</summary>
    public static WalkMap Of(Scene scene)
    {
        var map = new WalkMap();
        foreach (var area in scene.ActiveNow<WalkArea>())
        {
            if (area.GivenPoints.Count < 3)
                continue;
            var ring = area.GivenPoints.Select(area.ToWorld).ToArray();
            if (SignedArea(ring) < 0)
                Array.Reverse(ring);
            (area.Blocks ? map._holes : map._floors).Add(ring);
        }

        return map;
    }

    /// <summary>A map of these floors and holes, in the world, for tests.</summary>
    internal static WalkMap Of(IEnumerable<Vector2[]> floors, IEnumerable<Vector2[]>? holes = null)
    {
        var map = new WalkMap();
        foreach (var (rings, into) in new[] { (floors, map._floors), (holes ?? [], map._holes) })
        {
            foreach (var given in rings)
            {
                var ring = given.ToArray();
                if (SignedArea(ring) < 0)
                    Array.Reverse(ring);
                into.Add(ring);
            }
        }

        return map;
    }

    /// <summary>True if <paramref name="point"/> is on the floor and not in a hole (or anywhere, with no floor).</summary>
    public bool IsWalkable(Vector2 point)
    {
        if (_floors.Count == 0)
            return true;
        foreach (var hole in _holes)
        {
            if (Outlines.Inside(hole, point))
                return false;
        }

        foreach (var floor in _floors)
        {
            if (Outlines.Inside(floor, point))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The nearest point to <paramref name="point"/> a walker can stand on: itself when it's on the floor, otherwise the
    /// nearest spot just inside the floor's edge (or just outside a hole's). Itself with no floor.
    /// </summary>
    public Vector2 Nearest(Vector2 point)
    {
        if (IsWalkable(point))
            return point;
        var best = point;
        var bestDistance = float.MaxValue;
        foreach (var ring in _floors.Concat(_holes))
        {
            for (var i = 0; i < ring.Length; i++)
            {
                var (a, b) = (ring[i], ring[(i + 1) % ring.Length]);
                var on = ClosestOnSide(a, b, point);
                var distance = Vector2.DistanceSquared(on, point);
                if (distance >= bestDistance)
                    continue;
                // Stepped off the edge a little to one side or the other, whichever is walkable.
                var side = b - a;
                var normal = side.LengthSquared() > 0 ? Vector2.Normalize(new Vector2(-side.Y, side.X)) : Vector2.Zero;
                foreach (var candidate in (ReadOnlySpan<Vector2>)[on + normal * Margin, on - normal * Margin, Inward(ring, i, on)])
                {
                    if (IsWalkable(candidate))
                    {
                        (best, bestDistance) = (candidate, distance);
                        break;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The way from <paramref name="from"/> to <paramref name="to"/> (or as near it as can be stood on), as the points to
    /// walk through in turn, the last where it stops; empty when it's there already. A walker off the floor first goes to
    /// the nearest point on it. Where no way joins them (two floors that don't touch), it walks to the nearest point it
    /// can reach.
    /// </summary>
    public List<Vector2> Path(Vector2 from, Vector2 to)
    {
        var route = new List<Vector2>();
        var start = Nearest(from);
        var end = Nearest(to);
        if (Vector2.DistanceSquared(start, from) > 1e-8f)
            route.Add(start);
        if (Vector2.DistanceSquared(start, end) <= 1e-8f)
            return route;
        if (!HasFloor || Sees(start, end))
        {
            route.Add(end);
            return route;
        }

        // The corners a shortest way can bend round, pushed a little off them into the floor.
        var nodes = new List<Vector2> { start };
        foreach (var floor in _floors)
            AddCorners(floor, jutsIn: true, nodes);
        foreach (var hole in _holes)
            AddCorners(hole, jutsIn: false, nodes);
        AddJoins(nodes);
        nodes.Add(end);

        // Shortest ways from the start over the corners that see each other (A*, towards the end).
        var count = nodes.Count;
        var cost = new float[count];
        var came = new int[count];
        var done = new bool[count];
        Array.Fill(cost, float.MaxValue);
        Array.Fill(came, -1);
        cost[0] = 0;
        var seen = new Dictionary<(int, int), bool>();
        var goal = count - 1;
        while (true)
        {
            // The open node with the least cost plus the straight distance left (the first such, so ties go the same way).
            var current = -1;
            var bestGuess = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                if (done[i] || cost[i] == float.MaxValue)
                    continue;
                var guess = cost[i] + Vector2.Distance(nodes[i], nodes[goal]);
                if (guess < bestGuess)
                    (current, bestGuess) = (i, guess);
            }

            if (current < 0 || current == goal)
                break;
            done[current] = true;
            for (var next = 0; next < count; next++)
            {
                if (done[next] || next == current)
                    continue;
                var step = cost[current] + Vector2.Distance(nodes[current], nodes[next]);
                if (step >= cost[next])
                    continue;
                var key = current < next ? (current, next) : (next, current);
                if (!seen.TryGetValue(key, out var sees))
                    seen[key] = sees = Sees(nodes[current], nodes[next]);
                if (!sees)
                    continue;
                cost[next] = step;
                came[next] = current;
            }
        }

        var way = new List<Vector2>();
        if (cost[goal] < float.MaxValue)
        {
            for (var at = goal; at > 0; at = came[at])
                way.Add(nodes[at]);
            way.Reverse();
            route.AddRange(way);
            return route;
        }

        // No way there (a floor that doesn't join this one): to the spot it can reach nearest the end, along an edge of
        // the floor it's on.
        var spots = new List<Vector2>();
        foreach (var ring in _floors.Concat(_holes))
        {
            for (var i = 0; i < ring.Length; i++)
            {
                var (a, b) = (ring[i], ring[(i + 1) % ring.Length]);
                var on = ClosestOnSide(a, b, end);
                var side = b - a;
                var normal = side.LengthSquared() > 0 ? Vector2.Normalize(new Vector2(-side.Y, side.X)) : Vector2.Zero;
                foreach (var spot in (ReadOnlySpan<Vector2>)[on + normal * Margin, on - normal * Margin])
                {
                    if (IsWalkable(spot))
                        spots.Add(spot);
                }
            }
        }

        foreach (var spot in spots.OrderBy(s => Vector2.DistanceSquared(s, end)))
        {
            var via = -1;
            for (var i = 0; i < count - 1 && via < 0; i++)
            {
                if (cost[i] < float.MaxValue && Sees(nodes[i], spot))
                    via = i;
            }

            if (via < 0)
                continue;
            for (var at = via; at > 0; at = came[at])
                way.Add(nodes[at]);
            way.Reverse();
            route.AddRange(way);
            route.Add(spot);
            return route;
        }

        return route;
    }

    /// <summary>True if a walker can go straight from <paramref name="a"/> to <paramref name="b"/>, staying on the floor.</summary>
    public bool Sees(Vector2 a, Vector2 b)
    {
        if (!HasFloor)
            return true;

        // Where the line crosses any edge, in order along it: each stretch between two crossings is all in or all out, so
        // its middle says which.
        var crossings = new List<float> { 0, 1 };
        foreach (var ring in _floors.Concat(_holes))
        {
            for (var i = 0; i < ring.Length; i++)
            {
                if (Crossing(a, b, ring[i], ring[(i + 1) % ring.Length]) is { } t)
                    crossings.Add(t);
            }
        }

        crossings.Sort();
        for (var i = 1; i < crossings.Count; i++)
        {
            if (crossings[i] - crossings[i - 1] < 1e-6f)
                continue;
            if (!IsWalkable(Vector2.Lerp(a, b, (crossings[i - 1] + crossings[i]) / 2)))
                return false;
        }

        return true;
    }

    // The corners a way can bend round: on the floor, those that jut in (where its inside is more than half a turn); on a
    // hole, those that stick out. Each pushed off the corner into the floor along the middle of its two sides, kept if
    // that's walkable.
    private void AddCorners(Vector2[] ring, bool jutsIn, List<Vector2> into)
    {
        for (var i = 0; i < ring.Length; i++)
        {
            var (before, corner, after) = (ring[(i + ring.Length - 1) % ring.Length], ring[i], ring[(i + 1) % ring.Length]);
            var turn = Cross(corner - before, after - corner);
            if (jutsIn ? turn >= 0 : turn <= 0)
                continue;
            var spot = Inward(ring, i, corner);
            if (IsWalkable(spot))
                into.Add(spot);
        }
    }

    // Where the sides of two areas cross (two floors overlapping in an L, a hole over the floor's edge) is a corner too,
    // of the shape they make together: the spots round it that are walkable.
    private void AddJoins(List<Vector2> into)
    {
        var rings = _floors.Concat(_holes).ToList();
        for (var r = 0; r < rings.Count; r++)
        {
            for (var q = r + 1; q < rings.Count; q++)
            {
                var (one, other) = (rings[r], rings[q]);
                for (var i = 0; i < one.Length; i++)
                {
                    var (a, b) = (one[i], one[(i + 1) % one.Length]);
                    for (var j = 0; j < other.Length; j++)
                    {
                        if (Crossing(a, b, other[j], other[(j + 1) % other.Length]) is not { } t)
                            continue;
                        var at = Vector2.Lerp(a, b, t);
                        foreach (var way in Around)
                        {
                            var spot = at + way * Margin * 1.5f;
                            if (IsWalkable(spot))
                                into.Add(spot);
                        }
                    }
                }
            }
        }
    }

    // Eight ways round a point, a unit long.
    private static readonly Vector2[] Around =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        Vector2.Normalize(new(1, 1)), Vector2.Normalize(new(-1, 1)), Vector2.Normalize(new(1, -1)), Vector2.Normalize(new(-1, -1)),
    ];

    // A point just off corner i (or a point `on` its sides) on the floor's side: along the middle of the two sides' normals
    // that face the floor (a floor's inside is to the left of its sides, anticlockwise; a hole's floor to the right).
    private Vector2 Inward(Vector2[] ring, int i, Vector2 on)
    {
        var hole = _holes.Contains(ring);
        var (before, corner, after) = (ring[(i + ring.Length - 1) % ring.Length], ring[i], ring[(i + 1) % ring.Length]);
        var normal = LeftOf(corner - before) + LeftOf(after - corner);
        if (hole)
            normal = -normal;
        if (normal.LengthSquared() < 1e-12f)
            normal = hole ? -LeftOf(after - corner) : LeftOf(after - corner);
        return on + Vector2.Normalize(normal) * Margin * 1.5f;
    }

    private static Vector2 LeftOf(Vector2 side) => side.LengthSquared() > 0 ? Vector2.Normalize(new Vector2(-side.Y, side.X)) : Vector2.Zero;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float SignedArea(Vector2[] ring)
    {
        var area = 0f;
        for (var i = 0; i < ring.Length; i++)
            area += Cross(ring[i], ring[(i + 1) % ring.Length]);
        return area / 2;
    }

    // How far along a to b (0 to 1) it crosses the side c to d, or null when it doesn't (or runs along it).
    private static float? Crossing(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var (r, s) = (b - a, d - c);
        var denominator = Cross(r, s);
        if (MathF.Abs(denominator) < 1e-12f)
            return null;
        var t = Cross(c - a, s) / denominator;
        var u = Cross(c - a, r) / denominator;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? t : null;
    }

    private static Vector2 ClosestOnSide(Vector2 a, Vector2 b, Vector2 point)
    {
        var side = b - a;
        var length = side.LengthSquared();
        if (length <= 0)
            return a;
        var t = Math.Clamp(Vector2.Dot(point - a, side) / length, 0, 1);
        return a + side * t;
    }
}
