using Griff.Runtime;

namespace Griff.Modules.Fighting;

/// <summary>
/// Fighting games: <see cref="Hitboxes"/>, hitboxes and hurtboxes for each frame of an animation's clips (drawn in the Clip
/// Editor), that land hits with damage, knockback and hitstun; and a sparring pair to make from the Entity menu (Genres,
/// Fighting).
/// </summary>
public sealed class FightingModule : GameModule
{
    public override string Name => "Fighting";

    protected internal override void AddTo(GameSetup game)
    {
        // After physics (where everything is this step), before touch damage and health (which hear the damage).
        var hitboxes = game.AddSystem(new HitboxesSystem());
        game.StepBefore("Touch Damage", "Hitboxes", hitboxes, hitboxes.Run);
    }
}
