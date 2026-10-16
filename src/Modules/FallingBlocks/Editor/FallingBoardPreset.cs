using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.FallingBlocks;

/// <summary>
/// Creates a falling-block board: a <see cref="Tilemap"/> with a <see cref="FallingBoard"/> 10 cells across and 20 down,
/// centered on the scene's middle, its cells lined up with the scene's level if it has one (see
/// <see cref="Tilemap.LinesUpWith"/>; tick its tilemap's Solid for landed blocks a platformer climbs). Pieces fall into it as the game runs: slide them with left and right, turn them with
/// up, drop them with Space, and fill rows to clear them.
/// </summary>
[Category("Falling Blocks")]
public sealed class FallingBoardPreset : EntityPreset
{
    public override string Id => "Entity.CreateFallingBoard";

    public override string Text => "Create Falling Board";

    public override int Order => 20;

    public override string? Icon => "Blocks";

    public override string EntityName(Scene scene) => "Board";

    public override void Configure(Entity entity, Scene scene)
    {
        entity.AddComponent<Tilemap>().Solid = false;
        var board = entity.AddComponent<FallingBoard>();
        Tilemap.LineUpWithLevel(entity, scene, new Vector3(-board.Columns / 2f, board.Rows / 2f, 0));
    }
}
