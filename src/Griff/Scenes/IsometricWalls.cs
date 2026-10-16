using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// An isometric map's walls: its solid diamonds. A body stands on the diamond under its collider (as wide as the collider,
/// half as tall, round its middle), which in the map's grid is a square, as the map's diamonds are: so a body is kept off
/// the solid diamonds in the grid (columns and rows from the map's start, in which each diamond is a square), a grid axis
/// at a time, and slides along walls the way they run on the screen.
/// </summary>
internal sealed class IsometricWalls : TileWalls
{
    // Touching isn't overlapping: an edge this close to a cell's counts as on it.
    private const float GridSkin = 1e-4f;

    // The most cells a move or a ray looks along (a body moving so fast it would cross more goes the rest unchecked).
    private const int GridReach = 4096;

    private IsometricWalls(Tilemap map, Matrix3x2 toGrid, Matrix3x2 fromGrid) : base(map)
    {
        ToGrid = toGrid;
        FromGrid = fromGrid;
    }

    public Matrix3x2 ToGrid { get; }

    public Matrix3x2 FromGrid { get; }

    public override Box Bounds
    {
        get
        {
            var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
            foreach (var corner in (ReadOnlySpan<Vector2>)[Vector2.Zero, new Vector2(Width, 0), new Vector2(Width, Height), new Vector2(0, Height)])
            {
                var world = Vector2.Transform(corner, FromGrid);
                (min, max) = (Vector2.Min(min, world), Vector2.Max(max, world));
            }

            return new Box((min + max) / 2, (max - min) / 2);
        }
    }

    /// <summary>The walls of an isometric map as it's placed now, or null if its place can't be turned round.</summary>
    public static IsometricWalls? Of(Tilemap map)
    {
        var w = map.Entity!.Transform.WorldMatrix;
        var toWorld = new Matrix3x2(w.M11, w.M12, w.M21, w.M22, w.M41, w.M42);
        if (!Matrix3x2.Invert(toWorld, out var toLocal))
            return null;
        var (across, down) = (map.GridAt(Vector2.UnitX), map.GridAt(Vector2.UnitY));
        var toGrid = toLocal * new Matrix3x2(across.X, across.Y, down.X, down.Y, 0, 0);
        if (!Matrix3x2.Invert(toGrid, out var fromGrid))
            return null;
        return new IsometricWalls(map, toGrid, fromGrid);
    }

    // Half a footprint's side in cells, for a collider half a world width wide.
    private float HalfOf(float halfWidth) => Math.Max(1e-3f, halfWidth * MathF.Max(MathF.Abs(ToGrid.M11), MathF.Abs(ToGrid.M12)));

    public override bool BlockedAt(Vector2 world)
    {
        var grid = Vector2.Transform(world, ToGrid);
        return Blocked((int)MathF.Floor(grid.X), (int)MathF.Floor(grid.Y));
    }

    // Moves the footprint from where the collider's middle was (`from`) to where it is (`to`), a grid axis at a time,
    // stopping at the first solid diamond in the way: so walking into a wall slides along it. Velocity into a wall (on the
    // grid) stops, or bounces back; along it carries on.
    public override WallMove? KeepOff(Vector2 from, Vector2 to, Vector2 half, float? radius, Vector2 velocity, float bounciness,
        float settleSpeed)
    {
        var start = Vector2.Transform(from, ToGrid);
        var end = Vector2.Transform(to, ToGrid);
        var side = HalfOf(half.X);
        if (!Near(start, end, side))
            return null;

        var (reached, acrossBlocked, downBlocked) = Sweep(start, end, side);
        if (!acrossBlocked && !downBlocked)
            return null;

        var onGrid = Vector2.TransformNormal(velocity, ToGrid);
        var moved = end - start;
        var bounce = -Math.Clamp(bounciness, 0, 1);
        if (acrossBlocked && onGrid.X * moved.X > 0)
            onGrid.X = Settle(onGrid.X * bounce, settleSpeed);
        if (downBlocked && onGrid.Y * moved.Y > 0)
            onGrid.Y = Settle(onGrid.Y * bounce, settleSpeed);
        return new WallMove(Vector2.Transform(reached, FromGrid), Vector2.TransformNormal(onGrid, FromGrid), false, 0);
    }

    // A bounce slower than the settling speed (in world units) settles to a stop, as it does against anything else.
    private float Settle(float gridSpeed, float settleSpeed)
    {
        var perWorldUnit = MathF.Max(MathF.Abs(ToGrid.M11), MathF.Abs(ToGrid.M12));
        return MathF.Abs(gridSpeed) < settleSpeed * perWorldUnit ? 0 : gridSpeed;
    }

    // Whether a footprint going from start to end could touch any of the map's cells.
    private bool Near(Vector2 start, Vector2 end, float half)
    {
        var min = Vector2.Min(start, end) - new Vector2(half);
        var max = Vector2.Max(start, end) + new Vector2(half);
        return max.X > 0 && max.Y > 0 && min.X < Width && min.Y < Height;
    }

    // A square footprint (half a side `half`, in cells) moved from start to end along the grid's columns, then its rows,
    // stopping at solid cells it didn't already overlap: where it gets to, and which axes were blocked.
    private (Vector2 At, bool AcrossBlocked, bool DownBlocked) Sweep(Vector2 start, Vector2 end, float half)
    {
        var x = Along(start.X, end.X - start.X, start.Y, half, across: true, out var acrossBlocked);
        var y = Along(start.Y, end.Y - start.Y, x, half, across: false, out var downBlocked);
        return (new Vector2(x, y), acrossBlocked, downBlocked);
    }

    // Moves a footprint's middle `at` by `delta` along one axis, the other axis's middle at `side`.
    private float Along(float at, float delta, float side, float half, bool across, out bool blocked)
    {
        blocked = false;
        if (delta == 0)
            return at;

        // The rows (or columns) beside it: those it overlaps, not just touches.
        var low = (int)MathF.Floor(side - half + GridSkin);
        var high = Math.Min(low + GridReach, (int)MathF.Ceiling(side + half - GridSkin) - 1);
        if (delta > 0)
        {
            var edge = at + half;
            var first = (int)MathF.Ceiling(edge - GridSkin);
            var last = Math.Min(first + GridReach, (int)MathF.Ceiling(edge + delta) - 1);
            for (var i = first; i <= last; i++)
            {
                if (AnyBlocked(i, low, high, across))
                {
                    blocked = true;
                    return i - half;
                }
            }
        }
        else
        {
            var edge = at - half;
            var first = (int)MathF.Floor(edge + GridSkin) - 1;
            var last = Math.Max(first - GridReach, (int)MathF.Floor(edge + delta));
            for (var i = first; i >= last; i--)
            {
                if (AnyBlocked(i, low, high, across))
                {
                    blocked = true;
                    return i + 1 + half;
                }
            }
        }

        return at + delta;
    }

    private bool AnyBlocked(int line, int low, int high, bool across)
    {
        if (line < 0 || line >= (across ? Width : Height))
            return false;
        for (var j = Math.Max(0, low); j <= Math.Min(high, (across ? Height : Width) - 1); j++)
        {
            if (across ? Blocked(line, j) : Blocked(j, line))
                return true;
        }

        return false;
    }

    // The solid cells a world box covers on the grid (clamped to the map).
    private IEnumerable<(int X, int Y)> CellsUnder(Box box)
    {
        var (x0, y0, x1, y1) = CellsNear(box);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (Blocked(x, y))
                    yield return (x, y);
            }
        }
    }

    /// <summary>The columns and rows of the cells a world box covers on the grid, clamped to the map (none when the ends cross).</summary>
    public override (int X0, int Y0, int X1, int Y1) CellsNear(Box box)
    {
        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in (ReadOnlySpan<Vector2>)[box.Min, box.Max, new Vector2(box.Min.X, box.Max.Y), new Vector2(box.Max.X, box.Min.Y)])
        {
            var grid = Vector2.Transform(corner, ToGrid);
            (min, max) = (Vector2.Min(min, grid), Vector2.Max(max, grid));
        }

        var (x0, y0) = (Math.Max(0, (int)MathF.Floor(min.X)), Math.Max(0, (int)MathF.Floor(min.Y)));
        var (x1, y1) = (Math.Min(Width - 1, (int)MathF.Floor(max.X)), Math.Min(Height - 1, (int)MathF.Floor(max.Y)));
        return (x0, y0, x1, y1);
    }

    /// <summary>A cell's diamond in the world, round from its top.</summary>
    public override int Cell(int x, int y, Span<Vector2> corners)
    {
        corners[0] = Vector2.Transform(new Vector2(x, y), FromGrid);
        corners[1] = Vector2.Transform(new Vector2(x + 1, y), FromGrid);
        corners[2] = Vector2.Transform(new Vector2(x + 1, y + 1), FromGrid);
        corners[3] = Vector2.Transform(new Vector2(x, y + 1), FromGrid);
        return 4;
    }

    public override bool MetByFootprints => true;

    /// <summary>
    /// The diamond under a collider's middle, <paramref name="halfWidth"/> half as wide as it in the world: a square on the
    /// grid, what a body meets the walls with.
    /// </summary>
    public override int Footprint(Vector2 center, float halfWidth, Span<Vector2> corners)
    {
        var middle = Vector2.Transform(center, ToGrid);
        var half = HalfOf(halfWidth);
        corners[0] = Vector2.Transform(middle + new Vector2(-half, -half), FromGrid);
        corners[1] = Vector2.Transform(middle + new Vector2(half, -half), FromGrid);
        corners[2] = Vector2.Transform(middle + new Vector2(half, half), FromGrid);
        corners[3] = Vector2.Transform(middle + new Vector2(-half, half), FromGrid);
        return 4;
    }

    // A cell's diamond in the world: its four corners, round from its top.
    private Vector2[] Diamond(int x, int y) =>
    [
        Vector2.Transform(new Vector2(x, y), FromGrid), Vector2.Transform(new Vector2(x + 1, y), FromGrid),
        Vector2.Transform(new Vector2(x + 1, y + 1), FromGrid), Vector2.Transform(new Vector2(x, y + 1), FromGrid),
    ];

    public override bool Overlaps(Box box)
    {
        foreach (var (x, y) in CellsUnder(box))
        {
            if (PolygonOverlapsBox(Diamond(x, y), box))
                return true;
        }

        return false;
    }

    public override bool Overlaps(Circle circle)
    {
        foreach (var (x, y) in CellsUnder(circle.Bounds))
        {
            if (PolygonOverlapsCircle(Diamond(x, y), circle))
                return true;
        }

        return false;
    }

    public override (float Distance, Vector2 Normal)? Raycast(Vector2 origin, Vector2 direction, float maxDistance)
    {
        var start = Vector2.Transform(origin, ToGrid);
        var way = Vector2.TransformNormal(direction, ToGrid);

        // Only the stretch of the ray over the map.
        var (near, far, nearAxis) = (0f, maxDistance, -1);
        for (var axis = 0; axis < 2; axis++)
        {
            var (s, d, size) = axis == 0 ? (start.X, way.X, Width) : (start.Y, way.Y, Height);
            if (MathF.Abs(d) < 1e-12f)
            {
                if (s < 0 || s > size)
                    return null;
                continue;
            }

            var (t0, t1) = ((0 - s) / d, (size - s) / d);
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            if (t0 > near)
                (near, nearAxis) = (t0, axis);
            far = Math.Min(far, t1);
        }

        if (near > far)
            return null;

        var point = start + way * near;
        var (x, y) = ((int)MathF.Floor(point.X + way.X * 1e-5f), (int)MathF.Floor(point.Y + way.Y * 1e-5f));
        (x, y) = (Math.Clamp(x, 0, Width - 1), Math.Clamp(y, 0, Height - 1));

        // Coming in from off the map, the first cell is entered through a face; starting inside one, it's left alone.
        if (near > 0 && Blocked(x, y))
            return (near, NormalOf(nearAxis, direction));

        var (stepX, stepY) = (Math.Sign(way.X), Math.Sign(way.Y));
        var nextX = stepX == 0 ? float.PositiveInfinity : ((stepX > 0 ? x + 1 : x) - start.X) / way.X;
        var nextY = stepY == 0 ? float.PositiveInfinity : ((stepY > 0 ? y + 1 : y) - start.Y) / way.Y;
        var (dx, dy) = (stepX == 0 ? float.PositiveInfinity : MathF.Abs(1 / way.X), stepY == 0 ? float.PositiveInfinity : MathF.Abs(1 / way.Y));
        for (var steps = 0; steps < 2 * GridReach; steps++)
        {
            int axis;
            float t;
            if (nextX < nextY)
                (axis, t, x, nextX) = (0, nextX, x + stepX, nextX + dx);
            else
                (axis, t, y, nextY) = (1, nextY, y + stepY, nextY + dy);
            if (t > far || x < 0 || y < 0 || x >= Width || y >= Height)
                return null;
            if (Blocked(x, y))
                return (t, NormalOf(axis, direction));
        }

        return null;
    }

    // The world normal of a face across a grid axis (a cell's side along the other axis), facing back up the ray.
    private Vector2 NormalOf(int axis, Vector2 direction)
    {
        var side = Vector2.TransformNormal(axis == 0 ? Vector2.UnitY : Vector2.UnitX, FromGrid);
        var normal = Vector2.Normalize(new Vector2(side.Y, -side.X));
        return Vector2.Dot(normal, direction) > 0 ? -normal : normal;
    }
}
