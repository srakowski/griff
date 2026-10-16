#if DEBUG
using Griff.Editor.Diagnostics.Demo;
using Griff.Modules.FirstPerson;
using Griff.Runtime;

namespace Griff.Editor.Genres.FirstPerson;

/// <summary>The First Person genre's part of the demo player: mouselook turns the viewer when the game didn't take the mouse.</summary>
internal sealed class FirstPersonDemo : DemoGenre
{
    public override bool Look(Game game, double x)
    {
        if (game.FirstPersonViewer is not { } viewer || viewer.GetComponent<FirstPersonMovement>() is not { } movement)
            return false;
        viewer.Transform.Rotation -= new System.Numerics.Vector3(0, 0, (float)(x * movement.MouseSensitivity));
        return true;
    }
}
#endif
