using Griff.Runtime;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Physics puzzlers: a <see cref="Slingshot"/> to pull back and launch shots from, and <see cref="Breakable"/> things (planks,
/// panes, blocks, pigs) that crack and break on hard knocks; a <see cref="LevelGoal"/> that wins a level once its targets
/// are down (with a bonus for the shots left, and one to three stars) or loses it once the shots run out; a Slingshot and a
/// Tower to make from the Entity menu (Genres, Physics Games); and the Physics template, levels of wood, glass and stone to
/// knock down. And pinball: <see cref="Flipper"/>s, <see cref="Bumper"/>s and kickers, a <see cref="Plunger"/>, a
/// <see cref="Tilt"/> to nudge, <see cref="Target"/>s and <see cref="Lane"/>s that score, and a <see cref="Drain"/> that loses,
/// saves and serves balls, with a Pinball Table and each piece to make from the Entity menu.
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
        var goals = game.AddSystem(new LevelGoalSystem());
        game.StepAfter("Breakables", "Level Goals", goals, goals.Run);
        game.OnNetCommand(SlingshotSystem.Command, slingshots.RunCommand);

        // Pinball: the tilt and the flippers before physics (so a flipper swings this step); bumpers, targets, lanes, the
        // plunger and the drain after it, from what it found touching.
        var pinball = game.AddSystem(new PinballSystem());
        game.StepAfter("Dragging", "Flippers", pinball, pinball.RunControls);
        game.StepAfter("Physics", "Pinball", pinball, pinball.RunHits);
    }
}
