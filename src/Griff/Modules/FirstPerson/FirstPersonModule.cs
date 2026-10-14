using Griff.Runtime;

namespace Griff.Modules.FirstPerson;

/// <summary>
/// First-person games: the world seen through the player's eyes from a flat map (<see cref="FirstPersonView"/>), walking
/// and turning (<see cref="FirstPersonMovement"/>), weapons, billboards and solid blocks, and the Shooter template.
/// </summary>
public sealed class FirstPersonModule : GameModule
{
    public override string Name => "First Person";

    protected internal override void AddTo(GameSetup game)
    {
        // After the other no-code players move, before scripts, so a script can still take over a body this frame.
        var firstPerson = game.AddSystem(new FirstPersonSystem());
        game.StepAfter("Movement", "First Person", firstPerson, firstPerson.Run);
    }
}
