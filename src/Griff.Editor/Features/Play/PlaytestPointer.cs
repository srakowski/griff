using System.Numerics;
using Griff.Hosting;
using Griff.Input;
using Griff.Runtime;

namespace Griff.Editor.Features.Play;

/// <summary>
/// Where an agent playtesting a game in the editor points its mouse (see <see cref="PlayMode.PointAt"/>), added to the
/// Game tab's loop: while <see cref="At"/> is set, the game sees the mouse there, in world units, and over the view,
/// rather than wherever the real one is; the real mouse moving over the game takes it back. Put on a button or anything
/// else of the HUD's, it rests on the screen as a real mouse does: as the camera moves, it stays on its spot on the
/// screen (the game seeing it at the world point now under it), so a panel that opens there again, pinned to the screen
/// while the camera moved, opens under it, and clicking where it rests clicks that. Moved with a button held (a drag), it
/// moves across the screen as far as the point it was put at moved, so dragging a camera with CameraControls from one
/// point to another moves the view by that much rather than chasing the point. An exported game's loop has none.
/// </summary>
internal sealed class PlaytestPointer : IGameLoopExtension
{
    private Vector2? _at;

    // Where it was put on the screen (-1 to 1 each way, through the view as last drawn), and the step it was put there
    // at, until the game has taken a step with it there; then whether that was on the HUD, so it rests on the screen.
    private Vector2? _onScreen;
    private long? _placedAt;
    private bool? _onHud;

    // Where it is on the screen for what moves the camera by the mouse (IInput.MouseOnScreen), and the point it was at before.
    private Vector2? _spot;
    private Vector2? _from;

    /// <summary>
    /// The world point it was put at, or null for the real mouse. Set again to the point it was put at, it stays where it
    /// rests: on the HUD, its spot on the screen, however the camera has moved since.
    /// </summary>
    public Vector2? At
    {
        get => _at;
        set
        {
            if (value == _at)
                return;
            (_from, _at) = (_at, value);
            (_onScreen, _placedAt, _onHud) = (null, null, null);
        }
    }

    /// <summary>True once the game has found it on the HUD, so it rests on its spot on the screen rather than in the world.</summary>
    public bool RestsOnScreen => _onHud == true && _onScreen is not null;

    public void BeforeSteps(GameLoop loop)
    {
        if (_at is not { } at)
            return;
        var drawn = loop.LastFrame?.ViewProjection;
        var frame = loop.Game.Time.Frame;
        if (_placedAt is null)
        {
            _placedAt = frame;
            if (drawn is { } view)
            {
                _onScreen = ToScreen(at, view);
                // Dragged on from where it was, it goes as far across the screen as the point did, as a hand moves a mouse.
                _spot = _from is { } from && _spot is { } spot && Held(loop.Input)
                    ? spot + _onScreen.Value - ToScreen(from, view)
                    : _onScreen;
            }
        }
        else if (_onHud is null && frame > _placedAt)
        {
            // The game has taken a step with it there: over a button or what a system shows (MouseOnUi), it's on the HUD.
            _onHud = loop.Game.MouseOnUi;
        }

        var world = at;
        if (RestsOnScreen && drawn is { } now && Matrix4x4.Invert(now, out var back))
            world = ToWorld(_onScreen!.Value, back);
        (loop.Input.MouseWorld, loop.Input.IsMouseInView) = (world, true);
        loop.Input.MouseOnScreen = RestsOnScreen ? _onScreen : _spot;
    }

    private static bool Held(IInput input) =>
        input.IsDown(MouseButton.Left) || input.IsDown(MouseButton.Middle) || input.IsDown(MouseButton.Right);

    public void PointerMoved(GameLoop loop) => At = null;

    private static Vector2 ToScreen(Vector2 world, Matrix4x4 viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(world, 0, 1), viewProjection);
        return new Vector2(clip.X, clip.Y) / clip.W;
    }

    private static Vector2 ToWorld(Vector2 screen, Matrix4x4 inverse)
    {
        var world = Vector4.Transform(new Vector4(screen, 0, 1), inverse);
        return new Vector2(world.X, world.Y) / world.W;
    }
}
