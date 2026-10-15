using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Walls on isometric maps. A body stands on the diamond under its collider (as wide as the collider, half as tall, round
// its middle), which in the map's grid is a square, as the map's diamonds are: so a body is kept off a map's solid
// diamonds in the grid, a grid axis at a time, and slides along walls the way they run on the screen.
public sealed partial class Physics
{
    // Touching isn't overlapping: an edge this close to a cell's counts as on it.
    private const float GridSkin = 1e-4f;

    // The most cells a move or a ray looks along (a body moving so fast it would cross more goes the rest unchecked).
    private const int GridReach = 4096;

    private readonly List<IsometricWalls> _isometric = [];

    /// <summary>
    /// World-space outlines of a tilemap's solid diamonds, one per run along a column's row (the four corners of each run,
    /// round from its top): what bodies meet on an isometric map. Empty unless the map is <see cref="Tilemap.Solid"/> and
    /// isometric; background, one-way, ladder and slope tiles are left out (see <see cref="Tilemap.IsBlocked"/>).
    /// </summary>
    public static IEnumerable<Vector2[]> IsometricShapes(Tilemap tilemap)
    {
        if (!tilemap.Solid || tilemap.Shape != TileShape.Isometric || tilemap.Entity is null)
            yield break;

        var world = tilemap.Entity.Transform.WorldMatrix;
        foreach (var (x, y, length, oneWay) in tilemap.CollisionRuns())
        {
            if (oneWay)
                continue;
            var outline = tilemap.OutlineOf(x, y, x + length - 1, y);
            for (var i = 0; i < outline.Length; i++)
                outline[i] = Vector2.Transform(outline[i], world);
            yield return outline;
        }
    }

    // The solid isometric maps of a scene, each with its grid's place in the world. With layers, only maps on those layers.
    private static void CollectIsometric(Scene scene, string? layers, List<IsometricWalls> into)
    {
        var tilemaps = scene.Active<Tilemap>();
        if (tilemaps.Count == 0)
            return;
        var only = layers is null ? null : CollisionLayers.Parse(layers);
        foreach (var tilemap in tilemaps)
        {
            if (!tilemap.Solid || tilemap.Shape != TileShape.Isometric || (only is not null && !CollisionLayers.Contains(only, tilemap.Layer)))
                continue;
            if (IsometricWalls.Of(tilemap) is { } walls)
                into.Add(walls);
        }
    }

    private static List<IsometricWalls> Isometric(Scene scene, string? layers)
    {
        var list = new List<IsometricWalls>();
        CollectIsometric(scene, layers, list);
        return list;
    }

    // Moves a body's footprint from where its collider's middle was (`from`) to where it is now, on each solid isometric
    // map, a grid axis at a time, stopping at the first solid diamond in the way: so walking into a wall slides along it.
    // Velocity into a wall stops (or bounces), and the wall's map is reported as touched, unless it's being carried.
    private void KeepOffIsometricWalls(Body body, Collider collider, Solids solids, Vector2 from, Action<Entity, Entity> onCollision,
        Entity? carrier, Entity? pusher)
    {
        var entity = body.Entity!;
        var filter = LayerFilter.Of(collider);
        var bounds = Area.Of(collider).Bounds;
        var to = bounds.Center;
        foreach (var walls in _isometric)
        {
            var map = walls.Map.Entity!;
            if (filter.IgnoresEachOther(walls.Filter) || map.IsSelfOrDescendantOf(entity) || map == carrier || map == pusher)
                continue;

            var start = Vector2.Transform(from, walls.ToGrid);
            var end = Vector2.Transform(to, walls.ToGrid);
            var half = walls.HalfOf(bounds.HalfSize.X);
            if (!walls.Near(start, end, half))
                continue;

            var (reached, acrossBlocked, downBlocked) = walls.Sweep(start, end, half);
            if (!acrossBlocked && !downBlocked)
                continue;

            var at = Vector2.Transform(reached, walls.FromGrid);
            Translate(solids, entity, at - to);
            to = at;
            if (carrier is not null)
                continue;

            // Into the wall (on the grid) stops, or bounces back; along it carries on.
            var velocity = Vector2.TransformNormal(body.Velocity, walls.ToGrid);
            var moved = end - start;
            var bounce = -Math.Clamp(body.Bounciness, 0, 1);
            if (acrossBlocked && velocity.X * moved.X > 0)
                velocity.X = Settle(velocity.X * bounce, walls.ToGrid);
            if (downBlocked && velocity.Y * moved.Y > 0)
                velocity.Y = Settle(velocity.Y * bounce, walls.ToGrid);
            body.Velocity = Vector2.TransformNormal(velocity, walls.FromGrid);
            onCollision(entity, map);
        }
    }

    // A bounce slower than SettleSpeed (in world units) settles to a stop, as it does against anything else.
    private static float Settle(float gridSpeed, Matrix3x2 toGrid)
    {
        var perWorldUnit = MathF.Max(MathF.Abs(toGrid.M11), MathF.Abs(toGrid.M12));
        return MathF.Abs(gridSpeed) < SettleSpeed * perWorldUnit ? 0 : gridSpeed;
    }

    // An isometric map's solid cells, and the way between the world and its grid (columns and rows from its start, in
    // which each diamond is a square).
    private sealed class IsometricWalls
    {
        private IsometricWalls(Tilemap map, Matrix3x2 toGrid, Matrix3x2 fromGrid, LayerFilter filter)
        {
            Map = map;
            ToGrid = toGrid;
            FromGrid = fromGrid;
            Filter = filter;
            Width = map.Width;
            Height = map.Height;
        }

        public Tilemap Map { get; }

        public Matrix3x2 ToGrid { get; }

        public Matrix3x2 FromGrid { get; }

        public LayerFilter Filter { get; }

        public int Width { get; }

        public int Height { get; }

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
            return new IsometricWalls(map, toGrid, fromGrid, new LayerFilter(map.Layer, CollisionLayers.ParseKept(map.IgnoreLayers)));
        }

        // Half a footprint's side in cells, for a collider half a world width wide.
        public float HalfOf(float halfWidth) => Math.Max(1e-3f, halfWidth * MathF.Max(MathF.Abs(ToGrid.M11), MathF.Abs(ToGrid.M12)));

        public bool Blocked(int x, int y) => Map.IsBlocked(x, y);

        public bool BlockedAt(Vector2 world)
        {
            var grid = Vector2.Transform(world, ToGrid);
            return Blocked((int)MathF.Floor(grid.X), (int)MathF.Floor(grid.Y));
        }

        // Whether a footprint going from start to end could touch any of the map's cells.
        public bool Near(Vector2 start, Vector2 end, float half)
        {
            var min = Vector2.Min(start, end) - new Vector2(half);
            var max = Vector2.Max(start, end) + new Vector2(half);
            return max.X > 0 && max.Y > 0 && min.X < Width && min.Y < Height;
        }

        /// <summary>
        /// A square footprint (half a side <paramref name="half"/>, in cells) moved from start to end along the grid's
        /// columns, then its rows, stopping at solid cells it didn't already overlap: where it gets to, and which axes
        /// were blocked.
        /// </summary>
        public (Vector2 At, bool AcrossBlocked, bool DownBlocked) Sweep(Vector2 start, Vector2 end, float half)
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

        // The cells a world box covers on the grid (clamped to the map), for looking at the solid ones in it.
        public IEnumerable<(int X, int Y)> CellsUnder(Box box)
        {
            var (x0, y0, x1, y1) = CellRange(box);
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
        public (int X0, int Y0, int X1, int Y1) CellRange(Box box)
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

        /// <summary>A cell's diamond in the world, as a turning body's shapes meet it.</summary>
        public void DiamondHull(ref Hull hull, int x, int y)
        {
            hull.Count = 4;
            hull.Points[0] = Vector2.Transform(new Vector2(x, y), FromGrid);
            hull.Points[1] = Vector2.Transform(new Vector2(x + 1, y), FromGrid);
            hull.Points[2] = Vector2.Transform(new Vector2(x + 1, y + 1), FromGrid);
            hull.Points[3] = Vector2.Transform(new Vector2(x, y + 1), FromGrid);
            FinishPolygon(ref hull);
        }

        /// <summary>
        /// The diamond under a collider's middle, <paramref name="halfWidth"/> half as wide as it in the world: a square on
        /// the grid, what a body meets the walls with.
        /// </summary>
        public void FootprintHull(ref Hull hull, Vector2 center, float halfWidth)
        {
            var middle = Vector2.Transform(center, ToGrid);
            var half = HalfOf(halfWidth);
            hull.Count = 4;
            hull.Points[0] = Vector2.Transform(middle + new Vector2(-half, -half), FromGrid);
            hull.Points[1] = Vector2.Transform(middle + new Vector2(half, -half), FromGrid);
            hull.Points[2] = Vector2.Transform(middle + new Vector2(half, half), FromGrid);
            hull.Points[3] = Vector2.Transform(middle + new Vector2(-half, half), FromGrid);
            FinishPolygon(ref hull);
            hull.Center = center;
        }

        // A cell's diamond in the world: its four corners, round from its top.
        public Vector2[] Diamond(int x, int y) =>
        [
            Vector2.Transform(new Vector2(x, y), FromGrid), Vector2.Transform(new Vector2(x + 1, y), FromGrid),
            Vector2.Transform(new Vector2(x + 1, y + 1), FromGrid), Vector2.Transform(new Vector2(x, y + 1), FromGrid),
        ];

        public bool Overlaps(Box box)
        {
            foreach (var (x, y) in CellsUnder(box))
            {
                if (PolygonOverlapsBox(Diamond(x, y), box))
                    return true;
            }

            return false;
        }

        public bool Overlaps(Circle circle)
        {
            foreach (var (x, y) in CellsUnder(circle.Bounds))
            {
                if (PolygonOverlapsCircle(Diamond(x, y), circle))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Where a ray (a unit <paramref name="direction"/>) first goes into a solid cell within
        /// <paramref name="maxDistance"/>, and the face's normal in the world; cells it starts in are passed through.
        /// </summary>
        public (float Distance, Vector2 Normal)? Raycast(Vector2 origin, Vector2 direction, float maxDistance)
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

    // A convex polygon against a box, by separating axes: the box's two and the polygon's edges' normals.
    private static bool PolygonOverlapsBox(Vector2[] polygon, Box box)
    {
        Span<Vector2> corners = [box.Min, new Vector2(box.Max.X, box.Min.Y), box.Max, new Vector2(box.Min.X, box.Max.Y)];
        if (Separated(polygon, corners, Vector2.UnitX) || Separated(polygon, corners, Vector2.UnitY))
            return false;
        for (var i = 0; i < polygon.Length; i++)
        {
            var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
            if (Separated(polygon, corners, new Vector2(-edge.Y, edge.X)))
                return false;
        }

        return true;
    }

    private static bool Separated(Vector2[] a, ReadOnlySpan<Vector2> b, Vector2 axis)
    {
        var (minA, maxA) = (float.PositiveInfinity, float.NegativeInfinity);
        foreach (var p in a)
            (minA, maxA) = (Math.Min(minA, Vector2.Dot(p, axis)), Math.Max(maxA, Vector2.Dot(p, axis)));
        var (minB, maxB) = (float.PositiveInfinity, float.NegativeInfinity);
        foreach (var p in b)
            (minB, maxB) = (Math.Min(minB, Vector2.Dot(p, axis)), Math.Max(maxB, Vector2.Dot(p, axis)));
        return maxA <= minB || maxB <= minA;
    }

    // A convex polygon (corners in order) against a circle: its middle inside, or an edge within its radius.
    private static bool PolygonOverlapsCircle(Vector2[] polygon, Circle circle)
    {
        var inside = true;
        var sign = 0f;
        for (var i = 0; i < polygon.Length; i++)
        {
            var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Length]);
            var edge = b - a;
            var cross = edge.X * (circle.Center.Y - a.Y) - edge.Y * (circle.Center.X - a.X);
            if (sign == 0)
                sign = MathF.Sign(cross);
            else if (MathF.Sign(cross) != sign && cross != 0)
                inside = false;

            var t = Math.Clamp(Vector2.Dot(circle.Center - a, edge) / Math.Max(1e-12f, edge.LengthSquared()), 0, 1);
            if (Vector2.DistanceSquared(a + edge * t, circle.Center) < circle.Radius * circle.Radius)
                return true;
        }

        return inside;
    }
}
