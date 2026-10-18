using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Where a grid mover can't go, gathered once so a search can ask about many cells: off its map (when it has to stay on),
/// solid tiles on any tilemap, solid colliders over a cell's middle, a solid conveyor's belts (not its crossings), and the
/// cells other grid movers are in or heading into. Layers either side passes through don't count. <see cref="FindWay"/>
/// searches it for the shortest way somewhere.
/// </summary>
public sealed class GridWalls
{
    /// <summary>How far past the ends a search for a way goes with no map to keep it in, in cells.</summary>
    public const int SearchMargin = 12;

    /// <summary>The most cells a search for a way looks at before it gives up.</summary>
    public const int SearchLimit = 250_000;

    private readonly GridCells _grid;
    private readonly bool _stayOnMap;
    private readonly List<(Tilemap Map, Matrix4x4 ToLocal)> _maps = [];
    private readonly List<Box> _boxes = [];
    private readonly List<Circle> _circles = [];
    private readonly List<PolygonCollider> _polygons = [];
    private readonly List<(Conveyor Conveyor, GridCells Grid)> _conveyors = [];
    private readonly Dictionary<(int X, int Y), Entity> _movers = [];

    private GridWalls(GridCells grid, bool stayOnMap)
    {
        _grid = grid;
        _stayOnMap = stayOnMap;
        if (grid.Map is { } map)
        {
            var layers = map.Layers();
            Size = (layers.Max(l => l.Width), layers.Max(l => l.Height));
        }
    }

    /// <summary>The width and height of its map in cells (its widest layer), or null with no map.</summary>
    public (int Width, int Height)? Size { get; }

    /// <summary>The cells it's about.</summary>
    public GridCells Grid => _grid;

    /// <summary>
    /// Gathers where <paramref name="m"/> can't go on <paramref name="grid"/> in <paramref name="scene"/> (only solid
    /// colliders on its <see cref="GridMovement.BlockingLayers"/>, if it has any), leaving out the mover
    /// <paramref name="except"/> (the one searching).
    /// </summary>
    public static GridWalls Gather(Scene scene, GridMovement m, GridCells grid, Entity? except = null)
    {
        var collider = m.Entity!.GetComponent<Collider>();
        var blocking = CollisionLayers.Parse(m.BlockingLayers);
        return Gather(scene, grid, m.StayOnMap, m.Entity, collider?.Layer, collider?.IgnoreLayers, blocking.Length > 0 ? blocking : null,
            moversByLayer: false, except);
    }

    /// <summary>
    /// Gathers what's in the way on <paramref name="grid"/> in <paramref name="scene"/> for something on
    /// <paramref name="layer"/> (ignoring <paramref name="ignoreLayers"/>): solid tiles, and solid colliders only on
    /// <paramref name="only"/> when it's given (and grid movers too, with <paramref name="moversByLayer"/>), leaving out
    /// <paramref name="self"/> and anything but grid movers in <paramref name="except"/>. A map with no scene is its own wall.
    /// </summary>
    internal static GridWalls Gather(Scene? scene, GridCells grid, bool stayOnMap, Entity? self, string? layer, string? ignoreLayers,
        string[]? only, bool moversByLayer, Entity? except)
    {
        var walls = new GridWalls(grid, stayOnMap);
        var ignores = CollisionLayers.Parse(ignoreLayers);
        bool PassesThrough(string? otherLayer, string? otherIgnores)
        {
            var theirs = CollisionLayers.Parse(otherIgnores);
            return (ignores.Length > 0 && CollisionLayers.Contains(ignores, otherLayer)) ||
                   (theirs.Length > 0 && CollisionLayers.Contains(theirs, layer));
        }

        bool Blocks(Collider c) => !c.IsTrigger && !PassesThrough(c.Layer, c.IgnoreLayers) && (only is null || CollisionLayers.Contains(only, c.Layer));

        foreach (var other in scene?.Entities ?? [])
        {
            if (!other.IsActive || (self is not null && other.IsSelfOrDescendantOf(self)))
                continue;

            // Another grid mover holds the cell it's in, or the one it's heading into.
            if (other.GetComponent<GridMovement>() is { } mover)
            {
                if (other.GetComponent<Collider>() is { IsTrigger: false } theirs && !PassesThrough(theirs.Layer, theirs.IgnoreLayers) &&
                    (!moversByLayer || Blocks(theirs)))
                {
                    var at = other.Transform.WorldPosition;
                    walls._movers.TryAdd(mover.Started ? mover.Cell : grid.CellAt(new Vector2(at.X, at.Y)), other);
                }

                continue;
            }

            if (except is not null && other.IsSelfOrDescendantOf(except))
                continue;
            foreach (var component in other.Components)
            {
                switch (component)
                {
                    case Tilemap { Solid: true } tilemap when !PassesThrough(tilemap.Layer, tilemap.IgnoreLayers):
                        if (Matrix4x4.Invert(other.Transform.WorldMatrix, out var toLocal))
                            walls._maps.Add((tilemap, toLocal));
                        break;
                    case BoxCollider { OneWay: false } box when Blocks(box):
                        walls._boxes.Add(box.WorldBox);
                        break;
                    case CircleCollider circle when Blocks(circle):
                        walls._circles.Add(circle.WorldCircle);
                        break;
                    case PolygonCollider polygon when Blocks(polygon):
                        walls._polygons.Add(polygon);
                        break;
                    // Its belts are solid on its layer, as a collider is; its crossings aren't.
                    case Conveyor { Solid: true } conveyor when !PassesThrough(conveyor.Layer, null) &&
                                                                (only is null || CollisionLayers.Contains(only, conveyor.Layer)):
                        walls._conveyors.Add((conveyor, conveyor.Grid));
                        break;
                }
            }
        }

        if (scene is null && grid.Map is { Solid: true } own)
            walls._maps.Add((own, Matrix4x4.Identity));
        return walls;
    }

    /// <summary>Off the map it has to stay on, a solid tile, or under a solid collider.</summary>
    public bool IsWall((int X, int Y) cell)
    {
        if (_stayOnMap && Size is var (width, height) && (cell.X < 0 || cell.Y < 0 || cell.X >= width || cell.Y >= height))
            return true;
        var point = _grid.Center(cell);
        foreach (var (map, toLocal) in _maps)
        {
            var (x, y) = map.CellAt(Vector2.Transform(point, toLocal));
            if (map.IsBlocked(x, y))
                return true;
        }

        foreach (var (conveyor, cells) in _conveyors)
        {
            if (conveyor.BlocksWalking(cells.CellAt(point)))
                return true;
        }

        return _boxes.Any(b => b.Contains(point)) || _circles.Any(c => c.Contains(point)) || _polygons.Any(p => p.Contains(point));
    }

    /// <summary>The grid mover in a cell, or heading into it.</summary>
    public Entity? MoverIn((int X, int Y) cell) => _movers.GetValueOrDefault(cell);

    /// <summary>True if nothing blocks a cell: no wall and no grid mover in it or heading into it.</summary>
    public bool IsFree((int X, int Y) cell) => !IsWall(cell) && MoverIn(cell) is null;

    /// <summary>
    /// A* over cells from <paramref name="start"/> to the nearest of <paramref name="goals"/>: the cells to step into after
    /// the start, ending at the goal it reaches; empty if the start is one; null if none can be reached. It never goes
    /// through walls or <paramref name="avoid"/>'s cells, nor, with <paramref name="avoidMovers"/>, other grid movers (but
    /// into a goal, whoever stands there). Four ways, or eight with <paramref name="diagonal"/> (never cutting a corner); six
    /// on a hex map.
    /// </summary>
    public List<(int X, int Y)>? FindWay((int X, int Y) start, (int X, int Y)[] goals, bool diagonal, bool avoidMovers,
        IReadOnlySet<(int X, int Y)>? avoid = null)
    {
        if (goals.Length == 0)
            return null;
        if (Array.IndexOf(goals, start) >= 0)
            return [];

        // The searched area: its map (and the cells round it, if it may step off), or a margin round the ends.
        var (lowX, lowY, highX, highY) = (start.X, start.Y, start.X, start.Y);
        foreach (var goal in goals)
            (lowX, lowY, highX, highY) = (Math.Min(lowX, goal.X), Math.Min(lowY, goal.Y), Math.Max(highX, goal.X), Math.Max(highY, goal.Y));
        int minX, minY, maxX, maxY;
        if (Size is { } size)
            (minX, minY, maxX, maxY) = _stayOnMap ? (0, 0, size.Width - 1, size.Height - 1) : (-1, -1, size.Width, size.Height);
        else
            (minX, minY, maxX, maxY) = (lowX - SearchMargin, lowY - SearchMargin, highX + SearchMargin, highY + SearchMargin);
        (minX, minY) = (Math.Min(minX, lowX), Math.Min(minY, lowY));
        (maxX, maxY) = (Math.Max(maxX, highX), Math.Max(maxY, highY));
        var width = maxX - minX + 1;
        var count = (long)width * (maxY - minY + 1);
        if (count > SearchLimit)
            return null;

        int IndexOf((int X, int Y) c) => (c.Y - minY) * width + (c.X - minX);
        (int X, int Y) CellOf(int index) => (index % width + minX, index / width + minY);
        bool Inside((int X, int Y) c) => c.X >= minX && c.Y >= minY && c.X <= maxX && c.Y <= maxY;
        bool IsGoal((int X, int Y) c) => Array.IndexOf(goals, c) >= 0;
        bool Open((int X, int Y) c) => !IsWall(c) && avoid?.Contains(c) != true && (!avoidMovers || IsGoal(c) || MoverIn(c) is null);

        // Every step takes as long, so the fewest steps win; a diagonal costs a hair more, so it walks straight when it can
        // (a hex map's six ways are all straight).
        var grid = _grid;
        float Estimate((int X, int Y) c)
        {
            var nearest = int.MaxValue;
            foreach (var goal in goals)
                nearest = Math.Min(nearest, grid.Steps(c, goal, diagonal));
            return nearest;
        }

        var cost = new float[count];
        Array.Fill(cost, float.PositiveInfinity);
        var cameFrom = new int[count];
        var open = new PriorityQueue<int, float>();
        var startIndex = IndexOf(start);
        cost[startIndex] = 0;
        open.Enqueue(startIndex, Estimate(start));
        while (open.TryDequeue(out var current, out var priority))
        {
            var cell = CellOf(current);
            if (IsGoal(cell))
            {
                // Back from the goal to the cell just after the start.
                var way = new List<(int X, int Y)>();
                for (; current != startIndex; current = cameFrom[current])
                    way.Add(CellOf(current));
                way.Reverse();
                return way;
            }

            if (priority > cost[current] + Estimate(cell) + 1e-4f)
                continue; // A stale entry: this cell was reached more cheaply since.

            foreach (var (dx, dy) in grid.Neighbours(cell, diagonal))
            {
                var next = (X: cell.X + dx, Y: cell.Y + dy);
                if (!Inside(next) || !Open(next) || (!grid.IsHex && dx != 0 && dy != 0 && !(Open((next.X, cell.Y)) && Open((cell.X, next.Y)))))
                    continue;
                var index = IndexOf(next);
                var nextCost = cost[current] + (!grid.IsHex && dx != 0 && dy != 0 ? 1.001f : 1);
                if (nextCost >= cost[index])
                    continue;
                cost[index] = nextCost;
                cameFrom[index] = current;
                open.Enqueue(index, nextCost + Estimate(next));
            }
        }

        return null;
    }
}
