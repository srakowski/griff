using System.Numerics;
using Griff.Scenes;

namespace Griff.Art;

/// <summary>
/// Traces the outline of a picture's solid part as a few points, for a <see cref="PolygonCollider"/> that fits a sprite:
/// the biggest solid blob of the frame shown (its holes filled in), round its pixels' edges, then straightened out to no
/// more than a given number of points that still hug it, never crossing over themselves.
/// </summary>
public static class SpriteOutline
{
    /// <summary>
    /// The outline of what's solid (alpha at least <paramref name="alphaAtLeast"/>) in the part <paramref name="frame"/> (in
    /// pixels: left, top, width, height) of an RGBA picture, as polygon points in a sprite's own units: a sprite
    /// <paramref name="size"/> big centered on its entity, mirrored as <paramref name="flipX"/> and <paramref name="flipY"/>
    /// say. Anticlockwise, at most <paramref name="maxPoints"/> of them. Null when nothing in the frame is solid.
    /// </summary>
    public static IReadOnlyList<Vector2>? Trace(byte[] rgba, int width, int height, (int X, int Y, int Width, int Height) frame, Vector2 size,
        bool flipX = false, bool flipY = false, int maxPoints = 12, byte alphaAtLeast = 128)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        maxPoints = Math.Clamp(maxPoints, 3, PolygonParts.MaxPoints);
        var (left, top) = (Math.Clamp(frame.X, 0, width), Math.Clamp(frame.Y, 0, height));
        var (w, h) = (Math.Clamp(frame.Width, 0, width - left), Math.Clamp(frame.Height, 0, height - top));
        if (w == 0 || h == 0 || rgba.Length < width * height * 4)
            return null;

        var solid = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
                solid[y * w + x] = rgba[((top + y) * width + left + x) * 4 + 3] >= alphaAtLeast;
        }

        if (Biggest(solid, w, h) is not { } blob)
            return null;
        var edge = Edge(blob, w, h);
        var points = Straighten(edge, maxPoints);

        // From the frame's pixel corners (y down) to the sprite's own units (y up, round its middle).
        var outline = points.Select(p =>
        {
            var local = new Vector2((p.X / w - 0.5f) * size.X, (0.5f - p.Y / h) * size.Y);
            return new Vector2(flipX ? -local.X : local.X, flipY ? -local.Y : local.Y);
        }).ToList();
        var twiceArea = 0f;
        for (var i = 0; i < outline.Count; i++)
            twiceArea += outline[i].X * outline[(i + 1) % outline.Count].Y - outline[i].Y * outline[(i + 1) % outline.Count].X;
        if (twiceArea < 0)
            outline.Reverse();
        return outline;
    }

    // The biggest blob of solid pixels touching each other (corners count), with its holes filled in; null if none.
    private static bool[]? Biggest(bool[] solid, int w, int h)
    {
        var label = new int[w * h];
        var (best, bestSize, next) = (0, 0, 0);
        var queue = new Queue<int>();
        for (var start = 0; start < solid.Length; start++)
        {
            if (!solid[start] || label[start] != 0)
                continue;
            next++;
            var count = 0;
            label[start] = next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                count++;
                var (x, y) = (at % w, at / w);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (nx, ny) = (x + dx, y + dy);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        var n = ny * w + nx;
                        if (solid[n] && label[n] == 0)
                        {
                            label[n] = next;
                            queue.Enqueue(n);
                        }
                    }
                }
            }

            if (count > bestSize)
                (best, bestSize) = (next, count);
        }

        if (best == 0)
            return null;

        // Its holes: what's not of it and can't reach the frame's edge without crossing it (side to side only).
        var outside = new bool[w * h];
        for (var i = 0; i < label.Length; i++)
        {
            var (x, y) = (i % w, i / w);
            if (label[i] != best && (x == 0 || y == 0 || x == w - 1 || y == h - 1))
            {
                outside[i] = true;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            var (x, y) = (at % w, at / w);
            foreach (var (nx, ny) in (ReadOnlySpan<(int, int)>)[(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)])
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;
                var n = ny * w + nx;
                if (!outside[n] && label[n] != best)
                {
                    outside[n] = true;
                    queue.Enqueue(n);
                }
            }
        }

        var blob = new bool[w * h];
        for (var i = 0; i < blob.Length; i++)
            blob[i] = !outside[i];
        return blob;
    }

    // Round the blob's outer edge along its pixels' sides, from its top left corner, with the blob on the right going
    // round (in pixel corners, y down): a corner wherever it turns.
    private static List<Vector2> Edge(bool[] blob, int w, int h)
    {
        bool At(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && blob[y * w + x];

        var first = Array.IndexOf(blob, true);
        var start = (X: first % w, Y: first / w);
        // Heading right along the top of the topmost, leftmost pixel. Directions: right, down, left, up.
        ReadOnlySpan<(int X, int Y)> ways = [(1, 0), (0, 1), (-1, 0), (0, -1)];
        var (x, y, way) = (start.X, start.Y, 0);
        var corners = new List<Vector2>();
        var limit = 4 * (w + 1) * (h + 1);
        do
        {
            // The pixels ahead of the corner, on the left and on the right of the way it's going.
            var (ahead, side) = (ways[way], ways[(way + 1) % 4]);
            var leftPixel = PixelAt(x, y, ahead, side, onRight: false);
            var rightPixel = PixelAt(x, y, ahead, side, onRight: true);
            int turn;
            if (At(leftPixel.X, leftPixel.Y))
                turn = 3; // left
            else if (At(rightPixel.X, rightPixel.Y))
                turn = 0; // straight on
            else
                turn = 1; // right
            if (turn != 0 || corners.Count == 0)
                corners.Add(new Vector2(x, y));
            way = (way + turn) % 4;
            if (turn == 0)
                (x, y) = (x + ways[way].X, y + ways[way].Y);
        }
        while ((x != start.X || y != start.Y || way != 0) && --limit > 0);

        // Only the corners where it turned, each once.
        var result = new List<Vector2>();
        foreach (var corner in corners)
        {
            if (result.Count == 0 || result[^1] != corner)
                result.Add(corner);
        }

        if (result.Count > 1 && result[0] == result[^1])
            result.RemoveAt(result.Count - 1);
        return result;

        // The pixel whose corner (x, y) is, ahead of it going `ahead`, on the left or the right (towards `side`).
        static (int X, int Y) PixelAt(int x, int y, (int X, int Y) ahead, (int X, int Y) side, bool onRight)
        {
            // Pixel (px, py) covers corners (px, py) to (px + 1, py + 1). Ahead and to the right of a corner going right
            // (side is down) is pixel (x, y); ahead and to the left is (x, y - 1).
            var (fx, fy) = (ahead.X > 0 ? 0 : ahead.X < 0 ? -1 : 0, ahead.Y > 0 ? 0 : ahead.Y < 0 ? -1 : 0);
            (int X, int Y) lateral = onRight ? side : (-side.X, -side.Y);
            var (sx, sy) = (lateral.X > 0 ? 0 : lateral.X < 0 ? -1 : 0, lateral.Y > 0 ? 0 : lateral.Y < 0 ? -1 : 0);
            // Along the axis it goes, the ahead offset; across it, the side's.
            return ahead.X != 0 ? (x + fx, y + sy) : (x + sx, y + fy);
        }
    }

    // The outline cut down to at most `maxPoints` points that still hug it (Douglas and Peucker, the tolerance raised until
    // few enough), never crossing over itself; failing that, the shape round it.
    private static List<Vector2> Straighten(List<Vector2> edge, int maxPoints)
    {
        if (edge.Count <= maxPoints && !Crosses(edge))
            return edge;
        for (var tolerance = 0.5f; tolerance < 1e5f; tolerance *= 1.25f)
        {
            var kept = Simplify(edge, tolerance);
            if (kept.Count <= maxPoints && kept.Count >= 3 && !Crosses(kept))
                return kept;
            if (kept.Count < 3)
                break;
        }

        var hull = PolygonParts.ConvexHull(edge);
        return hull.Count <= maxPoints ? hull : Simplify(hull, Tolerance(hull, maxPoints));
    }

    // The smallest tolerance that cuts a convex outline down to `maxPoints` points.
    private static float Tolerance(List<Vector2> hull, int maxPoints)
    {
        var tolerance = 0.5f;
        while (Simplify(hull, tolerance).Count > maxPoints)
            tolerance *= 1.25f;
        return tolerance;
    }

    // A closed outline's points, keeping only those further than `tolerance` from the line their neighbours make: split at
    // the two points furthest apart, each half simplified.
    private static List<Vector2> Simplify(List<Vector2> outline, float tolerance)
    {
        var (a, b, far) = (0, 0, -1f);
        for (var i = 0; i < outline.Count; i++)
        {
            var distance = Vector2.DistanceSquared(outline[0], outline[i]);
            if (distance > far)
                (b, far) = (i, distance);
        }

        // From the point furthest from the first, the point furthest from it.
        (a, far) = (b, -1);
        for (var i = 0; i < outline.Count; i++)
        {
            var distance = Vector2.DistanceSquared(outline[a], outline[i]);
            if (distance > far)
                (b, far) = (i, distance);
        }

        if (a == b)
            return [outline[a]];
        var keep = new bool[outline.Count];
        (keep[a], keep[b]) = (true, true);
        Mark(outline, a, b, tolerance, keep);
        Mark(outline, b, a, tolerance, keep);
        var kept = new List<Vector2>();
        for (var i = 0; i < outline.Count; i++)
        {
            if (keep[i])
                kept.Add(outline[i]);
        }

        return kept;
    }

    // Douglas-Peucker on the stretch of a closed outline from `from` round to `to`.
    private static void Mark(List<Vector2> outline, int from, int to, float tolerance, bool[] keep)
    {
        var n = outline.Count;
        var count = (to - from + n) % n;
        if (count < 2)
            return;
        var (p, q) = (outline[from], outline[to]);
        var along = q - p;
        var length = along.Length();
        var (worst, worstAt) = (-1f, -1);
        for (var k = 1; k < count; k++)
        {
            var i = (from + k) % n;
            var offset = outline[i] - p;
            var distance = length > 1e-6f ? MathF.Abs(along.X * offset.Y - along.Y * offset.X) / length : offset.Length();
            if (distance > worst)
                (worst, worstAt) = (distance, i);
        }

        if (worst <= tolerance)
            return;
        keep[worstAt] = true;
        Mark(outline, from, worstAt, tolerance, keep);
        Mark(outline, worstAt, to, tolerance, keep);
    }

    // True if any two sides that aren't neighbours cross or touch.
    private static bool Crosses(List<Vector2> outline) => PolygonParts.Of(outline).Problem is not null;
}
