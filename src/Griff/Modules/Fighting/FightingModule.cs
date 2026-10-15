using Griff.Runtime;

namespace Griff.Modules.Fighting;

/// <summary>
/// Fighting games: <see cref="Hitboxes"/>, hitboxes and hurtboxes for each frame of an animation's clips (drawn in the Clip
/// Editor), that land hits with damage, knockback and hitstun; fighters (<see cref="FightingCharacter"/>) that walk, jump, crouch, block and do
/// moves written in numpad notation (quarter circles, dragon punches, charges) with an input buffer, cancels and combos;
/// and a sparring pair and a training pair to make from the Entity menu (Genres, Fighting).
/// </summary>
public sealed class FightingModule : GameModule
{
    public override string Name => "Fighting";

    protected internal override void AddTo(GameSetup game)
    {
        // After physics (where everything is this step), before touch damage and health (which hear the damage).
        // Fighters read their players' input with the other player movements, before scripts.
        var fighters = game.AddSystem(new FightersSystem());
        game.StepAfter("Movement", "Fighters", fighters, fighters.Run);
        game.StepAfter("Physics", "Fighters Landing", fighters, fighters.Land);
        var hitboxes = game.AddSystem(new HitboxesSystem());
        game.StepBefore("Touch Damage", "Hitboxes", hitboxes, hitboxes.Run);
    }
}
