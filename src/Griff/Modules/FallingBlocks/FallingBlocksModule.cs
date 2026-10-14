using Griff.Runtime;

namespace Griff.Modules.FallingBlocks;

/// <summary>
/// Boards that pieces fall into, to be slid, turned and dropped so full rows clear
/// (<see cref="FallingBoard"/>), the Falling Blocks template, and Create Falling Board in the Entity menu.
/// </summary>
public sealed class FallingBlocksModule : GameModule
{
    public override string Name => "Falling Blocks";

    protected internal override void AddTo(GameSetup game)
    {
        // After scripts, so a move a script asks for is made this frame: with the other boards, before attacks.
        var boards = game.AddSystem(new FallingBlocksSystem());
        game.StepBefore("Attacks", "Falling Boards", boards, boards.RunBoards);
    }
}
