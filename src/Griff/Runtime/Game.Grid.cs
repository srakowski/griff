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
    public const int GridSearchMargin = GridWalls.SearchMargin;
    /// <summary>The most cells a grid search looks at before it gives up.</summary>
    public const int GridSearchLimit = GridWalls.SearchLimit;

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

    /// <summary>
    /// Puts it at a world position, whatever its parent.
    /// </summary>
    public static void MoveTo(Entity entity, Vector2 world) =>
        GridSystem.MoveTo(entity, world);
}
