using Griff.Runtime;

namespace Griff.Modules.Adventures;

/// <summary>
/// Point-and-click adventures: walk areas a character finds its way round (<see cref="WalkArea"/>), the character that
/// walks where the player clicks (<see cref="Walker"/>), things to look at and use (<see cref="Hotspot"/>), and items to
/// pick up, use on things (<see cref="UseWith"/>) and put together (<see cref="InventoryBar"/>), with
/// Create Walk Area, Create Hotspot, Create Walker and Create Item in the Entity menu.
/// </summary>
public sealed class AdventuresModule : GameModule
{
    public override string Name => "Adventures";

    protected override void AddTo(GameSetup game)
    {
        // After the movements, so a click this frame sets off this frame; before the dialogues, so a talk a hotspot starts
        // doesn't hear the click that started it.
        var adventures = game.AddSystem(new AdventuresSystem());
        game.StepAfter(Steps.Movement, "Walkers", adventures, adventures.Run);
    }
}
