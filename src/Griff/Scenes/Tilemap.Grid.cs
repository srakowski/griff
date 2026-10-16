using System.Numerics;

namespace Griff.Scenes;

// One grid: a map whose cells line up with another's. A match-3 or falling-blocks board keeps its pieces in a tilemap of
// its own; lined up with a level's map, its cells are the level's cells, so a falling-blocks stack is a wall a platformer
// climbs, its blocks on the same squares as the level's.
public sealed partial class Tilemap
{
    private string? _linesUpWith;

    /// <summary>
    /// Another tilemap, by its entity's name, whose cells this one's line up with, e.g. "Level": it takes that map's
    /// <see cref="TileSize"/> and <see cref="Shape"/>, and its top left stays on a corner of that map's cells (moved to the
    /// nearest as it's set, as it's dragged in the Scene view, and while the game runs), so a match-3 or falling-blocks
    /// board in a platformer level sits on the level's squares. Its own tiles stay its own: with <see cref="Solid"/>
    /// ticked, a board's settled pieces are walls and floors the level's bodies stand on. Empty lines up with nothing.
    /// </summary>
    [Group("Grid")]
    [EntityName]
    public string? LinesUpWith
    {
        get => _linesUpWith;
        set
        {
            _linesUpWith = value;
            LineUp();
        }
    }

    /// <summary>The map <see cref="LinesUpWith"/> names, if it's in the scene (and isn't this one).</summary>
    public Tilemap? GridMap =>
        string.IsNullOrWhiteSpace(_linesUpWith) || Entity?.Scene is not { } scene ||
        scene.FindNamed(_linesUpWith.Trim())?.GetComponent<Tilemap>() is not { } map || map == this
            ? null
            : map.BaseMap;

    /// <summary>
    /// Lines its cells up with <see cref="GridMap"/>'s: takes its tile size and shape, and moves its entity to the nearest
    /// place whose top left is a corner of that map's cells. Returns whether anything changed. Allocates nothing.
    /// </summary>
    public bool LineUp()
    {
        if (GridMap is not { } grid || Entity is not { } entity || LayerOf is not null)
            return false;
        var changed = false;
        if (_tileSize != grid.TileSize || _shape != grid.Shape)
            (_tileSize, _shape, changed) = (grid.TileSize, grid.Shape, true);
        _settingsVersion += changed ? 1 : 0;
        var position = LinedUp(entity.Transform.Position);
        if (Vector3.DistanceSquared(position, entity.Transform.Position) > 1e-10f)
            (entity.Transform.Position, changed) = (position, true);
        return changed;
    }

    /// <summary>
    /// The position (in its entity's parent's space) nearest <paramref name="position"/> that puts its top left on a
    /// corner of <see cref="GridMap"/>'s cells; <paramref name="position"/> itself when it lines up with nothing.
    /// </summary>
    public Vector3 LinedUp(Vector3 position)
    {
        if (GridMap is not { } grid || Entity is not { } entity)
            return position;
        var parentWorld = entity.Parent?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        if (!Matrix4x4.Invert(parentWorld, out var toParent))
            return position;
        var corner = grid.CornerNearest(Vector3.Transform(position, parentWorld));
        var local = Vector3.Transform(corner, toParent);
        return new Vector3(local.X, local.Y, position.Z);
    }

    /// <summary>
    /// The corner of its cells nearest <paramref name="world"/>, a point in the world: where a map lined up with this one
    /// (see <see cref="LinesUpWith"/>) can start.
    /// </summary>
    public Vector3 CornerNearest(Vector3 world)
    {
        var gridWorld = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        if (!Matrix4x4.Invert(gridWorld, out var toGrid))
            return world;
        var along = GridAt(Flat(Vector3.Transform(world, toGrid)));
        var (x, y) = (MathF.Round(along.X), MathF.Round(along.Y));

        // Hex rows shift every other row, so a lined-up map starts on an even row.
        if (Shape == TileShape.Hex)
            y = MathF.Round(along.Y / 2) * 2;
        var corner = Vector3.Transform(new Vector3(PointAt(x, y), 0), gridWorld);
        return new Vector3(corner.X, corner.Y, world.Z);
    }

    /// <summary>
    /// Lines a new board's map (on <paramref name="entity"/>, not yet in <paramref name="scene"/>) up with the scene's level:
    /// its first square tilemap that's no layer and no board, if it has one (boards draw square cells). Its tile size becomes the level's, and
    /// <paramref name="at"/>, the world point it would start at, moves to the nearest corner of the level's cells.
    /// </summary>
    public static void LineUpWithLevel(Entity entity, Scene scene, Vector3 at)
    {
        entity.Transform.Position = at;
        if (entity.GetComponent<Tilemap>() is not { } map)
            return;
        foreach (var other in scene.Entities)
        {
            if (other == entity || other.GetComponent<Tilemap>() is not { LayerOf: null, Shape: TileShape.Square } level ||
                other.Components.Any(c => c is IDrawsTilemap))
                continue;
            map.TileSize = level.TileSize;
            map._linesUpWith = other.Name;
            entity.Transform.Position = level.CornerNearest(at);
            return;
        }
    }

    /// <summary>Lines up every tilemap in <paramref name="scene"/> that lines up with another (see <see cref="LineUp"/>).</summary>
    public static void LineUpAll(Scene scene)
    {
        foreach (var map in scene.All<Tilemap>())
        {
            if (map._linesUpWith is not null)
                map.LineUp();
        }
    }

    private static Vector2 Flat(Vector3 point) => new(point.X, point.Y);
}
