using Griff.Runtime;

namespace Griff.Modules.Battles;

/// <summary>
/// Turn-based battles, as in an RPG: fighters that attack, cast spells paid for in mana and use items on their turn, by
/// the numbers of a class's or an enemy's table row (<see cref="Battler"/>), the computer playing a team, and the RPG
/// Stats template, whose races, classes, items, spells and levels are tables and whose balance simulations show which
/// class is too strong at which level.
/// </summary>
public sealed class BattlesModule : GameModule
{
    public override string Name => "Battles";

    protected internal override void AddTo(GameSetup game)
    {
        // After the turns are taken, so a fighter whose turn has just come moves this frame.
        var battles = game.AddSystem(new BattlesSystem());
        game.StepAfter("Turns", "Battles", battles, battles.Run);
    }
}
