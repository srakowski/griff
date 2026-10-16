using Griff.Runtime;

namespace Griff.Modules.Strategy;

/// <summary>
/// Real-time strategy: factions, units that walk around obstacles and fight, workers that gather and build, buildings that
/// train units, fog of war, the computer playing a side, the player's controls and HUD, playing online team against team,
/// and the Strategy template.
/// </summary>
public sealed class StrategyModule : GameModule
{
    public override string Name => "Strategy";

    protected override void AddTo(GameSetup game)
    {
        // Before physics, after scripts: a script's orders take effect this frame.
        var strategy = game.AddSystem(new StrategySystem());
        game.StepBefore(Steps.Attacks, "Strategy", strategy, strategy.Run);
        game.OnNetCommand(StrategySystem.StrategyCommand, strategy.RunStrategyOrder);
    }
}
