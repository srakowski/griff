using Griff.Runtime;

namespace Griff.Modules.Match3;

/// <summary>
/// Boards of gems swapped into lines of three or more of a kind that clear (<see cref="MatchBoard"/>), levels with
/// moves and goals, the Match-3 template, and Create Match Board in the Entity menu.
/// </summary>
public sealed class Match3Module : GameModule
{
    public override string Name => "Match-3";

    protected internal override void AddTo(GameSetup game)
    {
        // After scripts, so a swap a script asks for is made this frame, and after dragging.
        var boards = game.AddSystem(new Match3System());
        game.StepAfter("Dragging", "Match Boards", boards, boards.RunBoards);
    }
}
