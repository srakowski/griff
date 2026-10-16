using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for grid movement: each member forwards to GridSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// How far the stick has to lean before it counts as a way.
    /// </summary>
    public const float GridDeadZone = 0.4f;

    /// <summary>
    /// How far past the two ends a search for a way goes with no map to keep it in, in cells; and the most cells it looks at.
    /// </summary>
    public const int GridSearchMargin = 12;
    /// <summary>The most cells a grid search looks at before it gives up.</summary>
    public const int GridSearchLimit = 250_000;

    /// <summary>
    /// Stays in its cell, lunging part way toward the next and back over a step's time.
    /// </summary>
    public static void Lunge(GridMovement m, GridCells grid, (int X, int Y) toward, Vector2 direction, float progress) =>
        GridSystem.Lunge(m, grid, toward, direction, progress);

    /// <summary>
    /// Whether a step from a cell by a change of cell is open, as a step is: a diagonal can't cut a wall's corner (a hex
    /// map's neighbours all share a side). The goal is open whoever stands in it (it's where the target is).
    /// </summary>
    public static bool CanPass(GridWalls walls, (int X, int Y) from, (int X, int Y) by, bool avoidMovers, (int X, int Y) goal) =>
        GridSystem.CanPass(walls, from, by, avoidMovers, goal);

    // ----- What's in the way -----

    /// <summary>
    /// Where a grid mover can't go, gathered once so a search can ask about many cells: off its map (when it has to stay
    /// on), solid tiles on any tilemap, solid colliders over a cell's middle, and the cells other grid movers are in or
    /// heading into. Layers either side passes through don't count.
    /// </summary>
    public sealed class GridWalls
    {
        private readonly GridCells _grid;
        private readonly bool _stayOnMap;
        private readonly List<(Tilemap Map, Matrix4x4 ToLocal)> _maps = [];
        private readonly List<Box> _boxes = [];
        private readonly List<Circle> _circles = [];
        private readonly List<PolygonCollider> _polygons = [];
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

        /// <summary>Gathers where <paramref name="m"/> can't go on <paramref name="grid"/>, leaving out the mover <paramref name="except"/> (the one searching).</summary>
        public static GridWalls Gather(Game game, GridMovement m, GridCells grid, Entity? except = null)
        {
            var entity = m.Entity!;
            var walls = new GridWalls(grid, m.StayOnMap);
            var collider = entity.GetComponent<Collider>();
            var layer = collider?.Layer;
            var ignores = CollisionLayers.Parse(collider?.IgnoreLayers);
            bool PassesThrough(string? otherLayer, string? otherIgnores)
            {
                var theirs = CollisionLayers.Parse(otherIgnores);
                return (ignores.Length > 0 && CollisionLayers.Contains(ignores, otherLayer)) ||
                       (theirs.Length > 0 && CollisionLayers.Contains(theirs, layer));
            }

            foreach (var other in game.Scene.Entities.Where(e => e.IsActive))
            {
                if (other.IsSelfOrDescendantOf(entity))
                    continue;

                // Another grid mover holds the cell it's in, or the one it's heading into.
                if (other.GetComponent<GridMovement>() is { } mover)
                {
                    if (other.GetComponent<Collider>() is { IsTrigger: false } theirs && !PassesThrough(theirs.Layer, theirs.IgnoreLayers))
                        walls._movers.TryAdd(mover.Started ? mover.Cell : grid.CellAt(Flat(other.Transform.WorldPosition)), other);
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
                        case BoxCollider { IsTrigger: false, OneWay: false } box when !PassesThrough(box.Layer, box.IgnoreLayers):
                            walls._boxes.Add(Physics.BoundsOf(box));
                            break;
                        case CircleCollider { IsTrigger: false } circle when !PassesThrough(circle.Layer, circle.IgnoreLayers):
                            walls._circles.Add(Physics.BoundsOf(circle));
                            break;
                        case PolygonCollider { IsTrigger: false } polygon when !PassesThrough(polygon.Layer, polygon.IgnoreLayers):
                            walls._polygons.Add(polygon);
                            break;
                    }
                }
            }

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

            return _boxes.Any(b => b.Contains(point)) || _circles.Any(c => c.Contains(point)) || _polygons.Any(p => p.Contains(point));
        }

        /// <summary>The grid mover in a cell, or heading into it.</summary>
        public Entity? MoverIn((int X, int Y) cell) => _movers.GetValueOrDefault(cell);

        /// <summary>True if nothing blocks a cell: no wall and no grid mover in it or heading into it.</summary>
        public bool IsFree((int X, int Y) cell) => !IsWall(cell) && MoverIn(cell) is null;
    }

    /// <summary>
    /// Puts it at a world position, whatever its parent.
    /// </summary>
    public static void MoveTo(Entity entity, Vector2 world) =>
        GridSystem.MoveTo(entity, world);
}
