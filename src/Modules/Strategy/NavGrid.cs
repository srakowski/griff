using System.Numerics;

namespace Griff.Modules.Strategy;

/// <summary>
/// A strategy map's ground as a grid of cells, each open or blocked (by a building, a resource, a wall or a solid tile),
/// for units to find their way around with A*. Paths are straightened, so units walk in lines rather than along cells.
/// </summary>
internal sealed class NavGrid
{
    private static readonly float Diagonal = MathF.Sqrt(2);
    private readonly bool[] _blocked;

    public NavGrid(Vector2 min, Vector2 size, float cellSize)
    {
        CellSize = cellSize;
        Min = min;
        Width = Math.Max(1, (int)MathF.Ceiling(size.X / cellSize));
        Height = Math.Max(1, (int)MathF.Ceiling(size.Y / cellSize));
        _blocked = new bool[Width * Height];
    }

    public Vector2 Min { get; }

    public float CellSize { get; }

    public int Width { get; }

    public int Height { get; }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Outside the map counts as blocked.</summary>
    public bool IsBlocked(int x, int y) => !InBounds(x, y) || _blocked[y * Width + x];

    public bool IsBlocked(Vector2 point) => IsBlocked(CellAt(point));

    public bool IsBlocked((int X, int Y) cell) => IsBlocked(cell.X, cell.Y);

    public (int X, int Y) CellAt(Vector2 point) =>
        ((int)MathF.Floor((point.X - Min.X) / CellSize), (int)MathF.Floor((point.Y - Min.Y) / CellSize));

    public Vector2 CenterOf((int X, int Y) cell) => Min + new Vector2((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);

    public void Clear() => Array.Clear(_blocked);

    /// <summary>Blocks every cell whose middle is inside the box, and at least the cell at its middle.</summary>
    public void Block(Box box)
    {
        var (fromX, fromY) = CellAt(box.Min + new Vector2(CellSize / 2));
        var (toX, toY) = CellAt(box.Max - new Vector2(CellSize / 2));
        for (var y = Math.Max(0, fromY); y <= Math.Min(Height - 1, toY); y++)
        {
            for (var x = Math.Max(0, fromX); x <= Math.Min(Width - 1, toX); x++)
                _blocked[y * Width + x] = true;
        }

        var (cx, cy) = CellAt(box.Center);
        if (InBounds(cx, cy))
            _blocked[cy * Width + cx] = true;
    }

    /// <summary>True if every cell a footprint covers is open.</summary>
    public bool IsOpen(Box box)
    {
        var (fromX, fromY) = CellAt(box.Min + new Vector2(CellSize / 2));
        var (toX, toY) = CellAt(box.Max - new Vector2(CellSize / 2));
        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                if (IsBlocked(x, y))
                    return false;
            }
        }

        return true;
    }

    /// <summary>The open cell nearest <paramref name="point"/>, searching outward ring by ring; null if none is close.</summary>
    public Vector2? NearestOpen(Vector2 point, int maxRings = 40)
    {
        var center = CellAt(point);
        if (!IsBlocked(center))
            return point;
        for (var ring = 1; ring <= maxRings; ring++)
        {
            Vector2? best = null;
            var bestDistance = float.PositiveInfinity;
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Abs(dx) != ring && Math.Abs(dy) != ring)
                        continue;
                    var cell = (center.X + dx, center.Y + dy);
                    if (IsBlocked(cell))
                        continue;
                    var at = CenterOf(cell);
                    var distance = Vector2.DistanceSquared(at, point);
                    if (distance < bestDistance)
                        (best, bestDistance) = (at, distance);
                }
            }

            if (best is not null)
                return best;
        }

        return null;
    }

    /// <summary>
    /// The way from <paramref name="from"/> to <paramref name="to"/> as world waypoints, ending exactly at
    /// <paramref name="to"/> (or the nearest open spot to it). Empty if there's no way.
    /// </summary>
    public List<Vector2> FindPath(Vector2 from, Vector2 to)
    {
        if (NearestOpen(to) is not { } goalPoint)
            return [];
        var start = CellAt(from);
        if (IsBlocked(start) && NearestOpen(from) is { } open)
            start = CellAt(open);
        var goal = CellAt(goalPoint);
        if (start == goal || HasLine(from, goalPoint))
            return [goalPoint];

        var cells = Search(start, goal);
        if (cells.Count == 0)
            return [];
        var points = cells.Select(CenterOf).ToList();
        points[^1] = goalPoint;
        return Straighten(from, points);
    }

    // A* over cells, eight ways, never cutting a blocked corner.
    private List<(int X, int Y)> Search((int X, int Y) start, (int X, int Y) goal)
    {
        var count = Width * Height;
        var cost = new float[count];
        Array.Fill(cost, float.PositiveInfinity);
        var cameFrom = new int[count];
        Array.Fill(cameFrom, -1);
        var open = new PriorityQueue<int, float>();
        var startIndex = start.Y * Width + start.X;
        cost[startIndex] = 0;
        open.Enqueue(startIndex, Estimate(start));

        // Too far to reach (say, a goal walled off): settle for the closest the search got.
        var closest = startIndex;
        var closestEstimate = Estimate(start);
        var expanded = 0;

        while (open.TryDequeue(out var current, out var priority))
        {
            var cell = (X: current % Width, Y: current / Width);
            if (cell == goal)
                return Trace(current);
            if (priority > cost[current] + Estimate(cell) + 1e-4f)
                continue;
            if (++expanded > count)
                break;
            var estimate = Estimate(cell);
            if (estimate < closestEstimate)
                (closest, closestEstimate) = (current, estimate);

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var nx = cell.X + dx;
                    var ny = cell.Y + dy;
                    if (IsBlocked(nx, ny) || (dx != 0 && dy != 0 && (IsBlocked(cell.X + dx, cell.Y) || IsBlocked(cell.X, cell.Y + dy))))
                        continue;
                    var next = ny * Width + nx;
                    var nextCost = cost[current] + (dx != 0 && dy != 0 ? Diagonal : 1);
                    if (nextCost >= cost[next])
                        continue;
                    cost[next] = nextCost;
                    cameFrom[next] = current;
                    open.Enqueue(next, nextCost + Estimate((nx, ny)));
                }
            }
        }

        return closest == startIndex ? [] : Trace(closest);

        float Estimate((int X, int Y) c)
        {
            var dx = Math.Abs(c.X - goal.X);
            var dy = Math.Abs(c.Y - goal.Y);
            return Math.Abs(dx - dy) + Diagonal * Math.Min(dx, dy);
        }

        List<(int X, int Y)> Trace(int end)
        {
            var path = new List<(int X, int Y)>();
            for (var at = end; at != startIndex && at >= 0; at = cameFrom[at])
                path.Add((at % Width, at / Width));
            path.Reverse();
            return path;
        }
    }

    // Skips every waypoint that can be walked past in a straight line.
    private List<Vector2> Straighten(Vector2 from, List<Vector2> points)
    {
        var result = new List<Vector2>();
        var here = from;
        var i = 0;
        while (i < points.Count)
        {
            var furthest = i;
            for (var j = points.Count - 1; j > i; j--)
            {
                if (HasLine(here, points[j]))
                {
                    furthest = j;
                    break;
                }
            }

            result.Add(points[furthest]);
            here = points[furthest];
            i = furthest + 1;
        }

        return result;
    }

    /// <summary>True if a unit could walk straight from one point to the other without crossing a blocked cell.</summary>
    public bool HasLine(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var steps = (int)MathF.Ceiling(delta.Length() / (CellSize * 0.25f));
        // A little to each side too, so a line doesn't graze a corner.
        var side = delta.LengthSquared() > 1e-8f ? Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * CellSize * 0.3f : Vector2.Zero;
        for (var s = 1; s <= steps; s++)
        {
            var point = from + delta * (s / (float)steps);
            if (IsBlocked(point) || IsBlocked(point + side) || IsBlocked(point - side))
                return false;
        }

        return true;
    }
}
