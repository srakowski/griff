using Griff.Runtime;

namespace Griff.Modules.Contraptions;

/// <summary>
/// Contraptions, Garry's Mod and Wiremod style: building tools used while the game runs (<see cref="SandboxTools"/>),
/// welds, ropes and thrusters, wiring with buttons, sensors, gates, lamps and displays, and the Contraption template.
/// </summary>
public sealed class ContraptionsModule : GameModule
{
    public override string Name => "Contraptions";

    protected internal override void AddTo(GameSetup game)
    {
        var contraptions = game.AddSystem(new ContraptionsSystem());

        // Once the HUD is laid out, so a click on it isn't taken for one in the world.
        game.StepAfter(Steps.HUD, "Tools", contraptions, contraptions.RunTools);

        // Thrusters push and welded bodies are grouped before physics moves things.
        game.StepBefore(Steps.Physics, "Thrusters", contraptions, contraptions.RunThrusters);
        game.StepBefore(Steps.Physics, "Welds", contraptions, contraptions.GroupWelds);

        // Once what physics caused is known (pressure plates, sensors), before anything acts on it.
        game.StepBefore(Steps.Bouncers, "Wiring", contraptions, contraptions.RunWiring);
        game.OnHostCommand(ContraptionsSystem.UiCommand, contraptions.ShowUi);
    }
}
