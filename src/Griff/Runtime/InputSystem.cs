
namespace Griff.Runtime;

/// <summary>
/// Reads each step's input as it starts: the keys, buttons and mouse since the last step, and which local player plays
/// what.
/// </summary>
internal sealed class InputSystem : GameSystem
{
    public override string Name => "Input";

    internal void ReadInput()
    {
        Game.HostInput.BeginFrame(Game.Time.UnscaledDelta);
        Game.LocalPlayers.Update(Game.Scene);
    }
}
