using Griff.Runtime;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Physics puzzlers: a <see cref="Slingshot"/> to pull back and launch shots from, and <see cref="Breakable"/> things (planks,
/// panes, blocks, pigs) that crack and break on hard knocks; and a Slingshot and a Tower to make from the Entity menu (Genres, Physics Games).
/// </summary>
public sealed class PhysicsGamesModule : GameModule
{
    public override string Name => "Physics Games";

    protected internal override void AddTo(GameSetup game)
    {
        // Slingshots after scripts and dragging (so a launch a script asks for flies this step), before physics; breaking
        // straight after physics, from the knocks it found.
        var slingshots = game.AddSystem(new SlingshotSystem());
        game.StepAfter("Dragging", "Slingshots", slingshots, slingshots.Run);
        var breakables = game.AddSystem(new BreakablesSystem());
        game.StepAfter("Physics", "Breakables", breakables, breakables.Run);
        game.OnNetCommand(SlingshotSystem.Command, slingshots.RunCommand);
    }
}
