using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Match3;

/// <summary>
/// Creates a match-3 board: an 8 by 8 <see cref="Tilemap"/> of six kinds of gem with a <see cref="MatchBoard"/>, centered
/// on the scene's middle and dealt already so it shows; each game deals it afresh. Swap gems to line up three of a kind.
/// </summary>
[Category("Match-3")]
public sealed class MatchBoardPreset : EntityPreset
{
    public override string Id => "Entity.CreateMatchBoard";

    public override string Text => "Create Match Board";

    public override int Order => 19;

    public override string? Icon => "Star";

    public override string EntityName(Scene scene) => "Board";

    public override void Configure(Entity entity, Scene scene)
    {
        var map = entity.AddComponent<Tilemap>();
        map.Solid = false;
        var board = entity.AddComponent<MatchBoard>();
        entity.Transform.Position = new Vector3(-board.Columns / 2f, board.Rows / 2f, 0);
        board.DealInto(map, new Random());
    }
}
