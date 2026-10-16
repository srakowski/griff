using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Keeps tilemaps lined up with another's cells (Tilemap.LinesUpWith) and plays their animated tiles.
/// </summary>
internal sealed class TilemapSystem : GameSystem
{
    public override string Name => "Tilemaps";

    // Tilemaps that line up with another's cells (see Tilemap.LinesUpWith) stay on them: one spawned, or whose map moved.
    internal void LineUpGrids()
    {
        foreach (var map in Game.Scene.ActiveNow<Tilemap>())
        {
            if (map.LinesUpWith is not null)
                map.LineUp();
        }
    }

    internal void RunAnimatedTiles()
    {
        foreach (var tilemap in Game.Scene.RunningNow<Tilemap>())
        {
            if (!string.IsNullOrWhiteSpace(tilemap.AnimatedTiles))
                tilemap.AnimationTime += Game.Time.Delta;
        }
    }
}
