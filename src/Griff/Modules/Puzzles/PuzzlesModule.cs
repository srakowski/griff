using Griff.Runtime;

namespace Griff.Modules.Puzzles;

/// <summary>
/// Grid puzzles, Sokoban style: goals for crates (<see cref="Goal"/>), undo and restart (<see cref="MoveHistory"/>), the
/// Puzzle template, and Create Goal in the Entity menu.
/// </summary>
public sealed class PuzzlesModule : GameModule
{
    public override string Name => "Puzzles";

    protected internal override void AddTo(GameSetup game)
    {
        var puzzles = game.AddSystem(new PuzzlesSystem());

        // Before anything moves, so a move taken back this frame isn't stepped on from where it was.
        game.StepBefore(Steps.Enemies, "Move History", puzzles, puzzles.RunMoveHistory);

        // After switches, so a crate that covers a goal this frame counts this frame.
        game.StepAfter(Steps.Switches, "Goals", puzzles, puzzles.RunGoals);
    }
}
