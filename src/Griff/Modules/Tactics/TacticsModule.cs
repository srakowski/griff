using Griff.Runtime;

namespace Griff.Modules.Tactics;

/// <summary>
/// Turn-based tactics: units on a grid, picked and sent to a cell in reach or set on a foe, side by side in turns
/// (<see cref="TacticsUnit"/>, <see cref="TacticsControls"/>), the computer playing a team, the Tactics template, and
/// Create Tactics Unit in the Entity menu.
/// </summary>
public sealed class TacticsModule : GameModule
{
    public override string Name => "Tactics";

    protected internal override void AddTo(GameSetup game)
    {
        // After turns are taken and before anything moves, so a unit sent off this frame takes its first step this frame.
        var tactics = game.AddSystem(new TacticsSystem());
        game.StepAfter("Turns", "Tactics", tactics, tactics.Run);
        game.OnNetCommand(TacticsSystem.TacticsCommand, tactics.RunTacticsOrder);
    }
}
