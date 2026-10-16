using System.Numerics;
using Griff.Hosting;

namespace Griff.Editor.Features.Play;

/// <summary>
/// Where an agent playtesting a game in the editor points its mouse (see <see cref="PlayMode.PointAt"/>), added to the
/// Game tab's loop: while <see cref="At"/> is set, the game sees the mouse there, in world units, and over the view,
/// rather than wherever the real one is; the real mouse moving over the game takes it back. An exported game's loop has
/// none.
/// </summary>
internal sealed class PlaytestPointer : IGameLoopExtension
{
    /// <summary>Where the game sees the mouse, or null for the real one.</summary>
    public Vector2? At { get; set; }

    public void BeforeSteps(GameLoop loop)
    {
        if (At is { } at)
            (loop.Input.MouseWorld, loop.Input.IsMouseInView) = (at, true);
    }

    public void PointerMoved(GameLoop loop) => At = null;
}
