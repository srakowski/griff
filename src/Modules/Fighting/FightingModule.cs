using Griff.Runtime;

namespace Griff.Modules.Fighting;

/// <summary>
/// Fighting games: <see cref="Hitboxes"/>, hitboxes and hurtboxes for each frame of an animation's clips (drawn in the Clip
/// Editor), that land hits with damage, knockback and hitstun; fighters (<see cref="FightingCharacter"/>) that walk, jump, crouch, block and do
/// moves written in numpad notation (quarter circles, dragon punches, charges) with an input buffer, cancels and combos;
/// a computer to fight (<see cref="ComputerFighter"/>); a <see cref="CharacterSelect"/> where players at one machine each
/// pick their fighter, and a <see cref="VersusMatch"/> of rounds against the clock; a sparring pair and a training pair to
/// make from the Entity menu (Genres, Fighting); and the Fighter template, a whole versus fighting game.
/// </summary>
public sealed class FightingModule : GameModule
{
    public override string Name => "Fighting";

    protected override void AddTo(GameSetup game)
    {
        // After physics (where everything is this step), before touch damage and health (which hear the damage).
        // Fighters read their players' input with the other player movements, before scripts.
        var fighters = game.AddSystem(new FightersSystem());
        var fighterStep = game.StepAfter(Steps.Movement, "Fighters", fighters, fighters.Run);
        game.StepAfter(Steps.Physics, "Fighters Landing", fighters, fighters.Land);
        var hitboxes = game.AddSystem(new HitboxesSystem());
        game.StepBefore(Steps.TouchDamage, "Hitboxes", hitboxes, hitboxes.Run);

        // Character selects and versus matches just before the fighters move: a round's start (or end) holds them this step.
        var versus = game.AddSystem(new VersusSystem());
        game.StepBefore(fighterStep, "Character Select", versus, versus.RunSelects);
        game.StepBefore(fighterStep, "Versus", versus, versus.RunMatches);
    }
}
