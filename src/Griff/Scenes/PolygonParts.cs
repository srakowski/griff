using System.Globalization;
using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// The shape a <see cref="PolygonCollider"/>'s points make, as physics uses it: fixed up where the points don't make a
/// proper shape (too few, all in a line, sides crossing), going round anticlockwise without repeated points or points
/// along a straight side, and split into convex pieces of at most <see cref="MaxCorners"/> corners each, so a concave
/// shape (an L, a cup, a crescent of rock) collides as the pieces it's made of. Worked out once when the points change;
/// the same points always make the same pieces.
/// </summary>
public sealed class PolygonParts
{
    /// <summary>The most points a polygon uses: those after it are left out.</summary>
    public const int MaxPoints = 64;

    /// <summary>The most corners one convex piece has (what physics works with at once).</summary>
    public const int MaxCorners = 8;

    /// <summary>How thick a shape with no area (its points all in a line) is made.</summary>
    public const float FlatThickness = 0.1f;

    // Points closer than this are the same point.
    private const float Same = 1e-5f;

    private PolygonParts(Vector2[] outline, int[][] pieces, string? problem)
    {
        Outline = outline;
        Pieces = pieces;
        Problem = problem;
        Inner = new uint[pieces.Length];
        InnerReversed = new uint[pieces.Length];
        for (var p = 0; p < pieces.Length; p++)
        {
            var piece = pieces[p];
            var n = piece.Length;
            for (var i = 0; i < n; i++)
            {
                // A side from one corner of the outline to the next is its edge; any other runs through the inside.
                if (piece[(i + 1) % n] == (piece[i] + 1) % outline.Length)
                    continue;
                Inner[p] |= 1u << i;
                // Gone round the other way (mirrored), side i runs between what were corners n-1-i and n-2-i: old side n-2-i.
                InnerReversed[p] |= 1u << ((n - 2 - i + n) % n);
            }
        }

        var (area, centroid) = (0f, Vector2.Zero);
        for (var i = 0; i < outline.Length; i++)
        {
            var (a, b) = (outline[i], outline[(i + 1) % outline.Length]);
            var cross = a.X * b.Y - a.Y * b.X;
            area += cross;
            centroid += (a + b) * cross;
        }

        Area = area / 2;
        Centroid = MathF.Abs(area) > 1e-12f ? centroid / (3 * area) : Average(outline);
        var (min, max) = (outline[0], outline[0]);
        foreach (var point in outline)
            (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
        (Min, Max) = (min, max);
    }

    /// <summary>The shape's corners, anticlockwise, in the collider's own units round its offset.</summary>
    public Vector2[] Outline { get; }

    /// <summary>Its convex pieces, each the corners of <see cref="Outline"/> it's made of (by number), anticlockwise.</summary>
    public int[][] Pieces { get; }

    /// <summary>For each piece, the sides (bit i: from its corner i to the next) inside the shape, where pieces meet.</summary>
    public uint[] Inner { get; }

    /// <summary>The same, for each piece with its corners in the opposite order (when the shape is mirrored).</summary>
    public uint[] InnerReversed { get; }

    /// <summary>What was wrong with the points and how it was fixed, or null when they make a proper shape.</summary>
    public string? Problem { get; }

    /// <summary>Its area, in its own units.</summary>
    public float Area { get; }

    /// <summary>Its middle (where it balances), in its own units.</summary>
    public Vector2 Centroid { get; }

    /// <summary>The corners of the box round it, in its own units.</summary>
    public Vector2 Min { get; }

    public Vector2 Max { get; }

    /// <summary>Reads "x y; x y; x y" (commas work between the numbers too); anything that isn't a pair of numbers is skipped.</summary>
    public static List<Vector2> Parse(string? text) => [.. Formats.Points(text)];

    /// <summary>"x y; x y; x y", on hundredths, as <see cref="Parse"/> reads it.</summary>
    public static string Format(IEnumerable<Vector2> points) =>
        string.Join("; ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{Round(p.X):0.##} {Round(p.Y):0.##}")));

    // Rounded to hundredths, -0 as 0.
    private static float Round(float value) => MathF.Round(value * 100) / 100 + 0f;

    /// <summary>The shape <paramref name="given"/> make, fixed up where they don't make a proper one.</summary>
    public static PolygonParts Of(IReadOnlyList<Vector2> given)
    {
        var problems = new List<string>();
        var points = given.Take(MaxPoints).ToList();
        if (given.Count > MaxPoints)
            problems.Add($"It has {given.Count} points, and only the first {MaxPoints} are used.");

        // The same point twice in a row is one point; so is the last and the first.
        for (var i = points.Count - 1; i >= 0 && points.Count > 1; i--)
        {
            if (Vector2.DistanceSquared(points[i], points[(i + 1) % points.Count]) <= Same * Same)
                points.RemoveAt(i);
        }

        if (points.Count < 3 || InALine(points))
            return Degenerate(points, problems);

        // Points along a straight side add nothing, and one that doubles straight back (a spike) has no area to it.
        if (RemoveStraight(points))
            problems.Add("A point doubles back on itself along a side, so it's left out.");
        if (points.Count < 3)
            return Degenerate(points, problems);

        if (!IsSimple(points))
        {
            problems.Add("Its sides cross each other, so it's taken as the shape round all its points.");
            points = ConvexHull(points);
        }

        if (points.Count < 3 || MathF.Abs(TwiceArea(points)) <= 1e-6f * Extent(points) * Extent(points))
            return Degenerate(points, problems);

        if (TwiceArea(points) < 0)
            points.Reverse();
        var outline = points.ToArray();
        if (IsConvex(outline))
            return Convex(outline, problems);

        if (Split(outline) is not { } pieces)
        {
            // A shape too fine to split cleanly (it shouldn't happen to one whose sides don't cross): the shape round it.
            problems.Add("It couldn't be split into convex pieces, so it's taken as the shape round all its points.");
            return Convex(ConvexHull(points).ToArray(), problems);
        }

        return new PolygonParts(outline, pieces, Joined(problems));
    }

    private static string? Joined(List<string> problems) => problems.Count == 0 ? null : string.Join(" ", problems);

    // A convex outline, in pieces of at most MaxCorners corners: fanned out from its first corner.
    private static PolygonParts Convex(Vector2[] outline, List<string> problems)
    {
        var n = outline.Length;
        var pieces = new List<int[]>();
        if (n <= MaxCorners)
            pieces.Add(Enumerable.Range(0, n).ToArray());
        else
        {
            // Each piece takes the first corner and a run of the others, sharing its last with the next piece's first.
            for (var start = 1; start < n - 1; start += MaxCorners - 2)
            {
                var end = Math.Min(n - 1, start + MaxCorners - 2);
                pieces.Add([0, .. Enumerable.Range(start, end - start + 1)]);
            }
        }

        return new PolygonParts(outline, pieces.ToArray(), Joined(problems));
    }

    // Too few points, or all in a line: a box round them, or a thin bar along the line.
    private static PolygonParts Degenerate(List<Vector2> points, List<string> problems)
    {
        if (points.Count == 0)
        {
            problems.Add("It has no points, so it's a 1 × 1 square until it has three.");
            return Convex(Square(Vector2.Zero), problems);
        }

        // The two points furthest apart.
        var (a, b, far) = (points[0], points[0], 0f);
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var distance = Vector2.DistanceSquared(points[i], points[j]);
                if (distance > far)
                    (a, b, far) = (points[i], points[j], distance);
            }
        }

        if (far <= Same * Same)
        {
            problems.Add(points.Count == 1
                ? "It has only one point, so it's a 1 × 1 square round it until it has three."
                : "Its points are all in one place, so it's a 1 × 1 square round them until they spread out.");
            return Convex(Square(points[0]), problems);
        }

        problems.Add(points.Count == 2
            ? $"It has only two points, so it's a bar {FlatThickness} thick between them until it has three."
            : $"Its points are all in a line, so it has no area: it's a bar {FlatThickness} thick along them.");
        var along = Vector2.Normalize(b - a);
        var side = new Vector2(-along.Y, along.X) * (FlatThickness / 2);
        return Convex([a - side, b - side, b + side, a + side], problems);
    }

    // True if the points all lie along one line (or in one place).
    private static bool InALine(List<Vector2> points)
    {
        var (a, b, far) = (points[0], points[0], 0f);
        foreach (var p in points)
        {
            foreach (var q in points)
            {
                var distance = Vector2.DistanceSquared(p, q);
                if (distance > far)
                    (a, b, far) = (p, q, distance);
            }
        }

        if (far <= Same * Same)
            return true;
        var length = MathF.Sqrt(far);
        foreach (var p in points)
        {
            if (MathF.Abs(Turn(a, b, p)) / length > 1e-5f * length)
                return false;
        }

        return true;
    }

    private static Vector2[] Square(Vector2 center) =>
        [center + new Vector2(-0.5f, -0.5f), center + new Vector2(0.5f, -0.5f), center + new Vector2(0.5f, 0.5f), center + new Vector2(-0.5f, 0.5f)];

    // Takes out points along a straight side and points that double back; true if one doubled back.
    private static bool RemoveStraight(List<Vector2> points)
    {
        var spiked = false;
        for (var changed = true; changed && points.Count >= 3;)
        {
            changed = false;
            for (var i = 0; i < points.Count && points.Count >= 3; i++)
            {
                var (before, here, after) = (points[(i + points.Count - 1) % points.Count], points[i], points[(i + 1) % points.Count]);
                var (into, outOf) = (here - before, after - here);
                var cross = Cross(into, outOf);
                if (MathF.Abs(cross) > 1e-6f * into.Length() * outOf.Length() && Vector2.DistanceSquared(after, before) > Same * Same)
                    continue;
                spiked |= Vector2.Dot(into, outOf) < 0;
                points.RemoveAt(i);
                // Taking out a spike can leave the same point twice in a row.
                if (points.Count >= 2 && Vector2.DistanceSquared(points[i % points.Count], points[(i + points.Count - 1) % points.Count]) <= Same * Same)
                    points.RemoveAt(i % points.Count);
                changed = true;
                i--;
            }
        }

        return spiked;
    }

    private static float TwiceArea(List<Vector2> points)
    {
        var sum = 0f;
        for (var i = 0; i < points.Count; i++)
            sum += Cross(points[i], points[(i + 1) % points.Count]);
        return sum;
    }

    // How big it is: the larger side of the box round it (at least a little).
    private static float Extent(List<Vector2> points)
    {
        var (min, max) = (points[0], points[0]);
        foreach (var point in points)
            (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
        return MathF.Max(1e-3f, MathF.Max(max.X - min.X, max.Y - min.Y));
    }

    private static Vector2 Average(Vector2[] points)
    {
        var sum = Vector2.Zero;
        foreach (var point in points)
            sum += point;
        return sum / points.Length;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // Which side of the line a to b the point c is: positive on the left.
    private static float Turn(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - a);

    // True if no two sides cross or touch, other than neighbours at the corner they share.
    private static bool IsSimple(List<Vector2> points)
    {
        var n = points.Count;
        var tolerance = 1e-6f * Extent(points);
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (points[i], points[(i + 1) % n]);
            for (var j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1))
                {
                    // Neighbours: only the corner they share in common, so neither runs back along the other.
                    continue;
                }

                if (SegmentsMeet(a, b, points[j], points[(j + 1) % n], tolerance))
                    return false;
            }
        }

        return true;
    }

    // True if segments a-b and c-d share a point (within the tolerance).
    private static bool SegmentsMeet(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float tolerance)
    {
        var (d1, d2) = (Turn(c, d, a), Turn(c, d, b));
        var (d3, d4) = (Turn(a, b, c), Turn(a, b, d));
        var (cd, ab) = ((d - c).Length(), (b - a).Length());
        var (t1, t2) = (tolerance * cd, tolerance * ab);
        if (((d1 > t1 && d2 < -t1) || (d1 < -t1 && d2 > t1)) && ((d3 > t2 && d4 < -t2) || (d3 < -t2 && d4 > t2)))
            return true;
        // An end on (or all but on) the other segment.
        return OnSegment(c, d, a, d1, t1) || OnSegment(c, d, b, d2, t1) || OnSegment(a, b, c, d3, t2) || OnSegment(a, b, d, d4, t2);
    }

    private static bool OnSegment(Vector2 from, Vector2 to, Vector2 point, float turn, float tolerance)
    {
        if (MathF.Abs(turn) > tolerance)
            return false;
        var along = to - from;
        var t = Vector2.Dot(point - from, along);
        return t >= -tolerance && t <= along.LengthSquared() + tolerance;
    }

    private static bool IsConvex(Vector2[] outline)
    {
        for (var i = 0; i < outline.Length; i++)
        {
            if (Turn(outline[i], outline[(i + 1) % outline.Length], outline[(i + 2) % outline.Length]) <= 0)
                return false;
        }

        return true;
    }

    // The smallest convex shape round the points, anticlockwise (Andrew's monotone chain), without points along its sides.
    internal static List<Vector2> ConvexHull(IEnumerable<Vector2> given)
    {
        var points = given.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (points.Count < 3)
            return points;
        var hull = new List<Vector2>();
        foreach (var pass in (ReadOnlySpan<int>)[0, 1])
        {
            var start = hull.Count;
            for (var k = 0; k < points.Count; k++)
            {
                var point = points[pass == 0 ? k : points.Count - 1 - k];
                while (hull.Count >= start + 2 && Turn(hull[^2], hull[^1], point) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    // A simple, anticlockwise, concave outline in convex pieces: cut into triangles by clipping ears (the fattest first, so
    // no needle-thin ones), then neighbouring pieces joined wherever what they make is still convex and small enough
    // (Hertel and Mehlhorn). Null if it can't be done.
    private static int[][]? Split(Vector2[] outline)
    {
        var n = outline.Length;
        var left = Enumerable.Range(0, n).ToList();
        var pieces = new List<List<int>>();
        while (left.Count > 3)
        {
            var (best, bestAngle) = (-1, -1f);
            for (var i = 0; i < left.Count; i++)
            {
                var (a, b, c) = (outline[left[(i + left.Count - 1) % left.Count]], outline[left[i]], outline[left[(i + 1) % left.Count]]);
                if (Turn(a, b, c) <= 0 || !IsEar(outline, left, i, a, b, c))
                    continue;
                var angle = SmallestAngle(a, b, c);
                if (angle > bestAngle)
                    (best, bestAngle) = (i, angle);
            }

            if (best < 0)
                return null;
            pieces.Add([left[(best + left.Count - 1) % left.Count], left[best], left[(best + 1) % left.Count]]);
            left.RemoveAt(best);
        }

        pieces.Add(left);

        // Join neighbours across the cuts, as long as the joined piece stays convex and small enough.
        for (var joined = true; joined;)
        {
            joined = false;
            for (var p = 0; p < pieces.Count && !joined; p++)
            {
                for (var q = p + 1; q < pieces.Count && !joined; q++)
                {
                    if (Join(outline, pieces[p], pieces[q]) is not { } merged)
                        continue;
                    pieces[p] = merged;
                    pieces.RemoveAt(q);
                    joined = true;
                }
            }
        }

        return pieces.Select(p => p.ToArray()).ToArray();
    }

    // True if no other corner still left is inside (or on) the triangle a, b, c cut off at left[i].
    private static bool IsEar(Vector2[] outline, List<int> left, int i, Vector2 a, Vector2 b, Vector2 c)
    {
        var (ia, ib, ic) = (left[(i + left.Count - 1) % left.Count], left[i], left[(i + 1) % left.Count]);
        foreach (var index in left)
        {
            if (index == ia || index == ib || index == ic)
                continue;
            var p = outline[index];
            if (p == a || p == b || p == c)
                continue;
            if (Turn(a, b, p) >= 0 && Turn(b, c, p) >= 0 && Turn(c, a, p) >= 0)
                return false;
        }

        return true;
    }

    private static float SmallestAngle(Vector2 a, Vector2 b, Vector2 c)
    {
        static float Angle(Vector2 at, Vector2 p, Vector2 q)
        {
            var (u, v) = (p - at, q - at);
            return MathF.Atan2(MathF.Abs(Cross(u, v)), Vector2.Dot(u, v));
        }

        return MathF.Min(Angle(a, b, c), MathF.Min(Angle(b, c, a), Angle(c, a, b)));
    }

    // Two pieces sharing a side (one goes x to y round it, the other y to x), joined into one if that's convex and has no
    // more than MaxCorners corners; null otherwise.
    private static List<int>? Join(Vector2[] outline, List<int> p, List<int> q)
    {
        if (p.Count + q.Count - 2 > MaxCorners)
            return null;
        for (var i = 0; i < p.Count; i++)
        {
            var (x, y) = (p[i], p[(i + 1) % p.Count]);
            var j = q.IndexOf(y);
            if (j < 0 || q[(j + 1) % q.Count] != x)
                continue;

            // Round p from y back to x, then on round q from x (left out) to y (left out).
            var merged = new List<int>();
            for (var k = 0; k < p.Count; k++)
                merged.Add(p[(i + 1 + k) % p.Count]);
            for (var k = 2; k < q.Count; k++)
                merged.Add(q[(j + k) % q.Count]);
            for (var k = 0; k < merged.Count; k++)
            {
                var (a, b, c) = (outline[merged[k]], outline[merged[(k + 1) % merged.Count]], outline[merged[(k + 2) % merged.Count]]);
                if (Turn(a, b, c) <= 1e-7f * (b - a).Length() * (c - b).Length())
                    return null;
            }

            return merged;
        }

        return null;
    }
}
