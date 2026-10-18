using System.Numerics;
using Griff.Data;
using Griff.Hosting;
using Griff.Input;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// A camera with CameraControls dragged and zoomed by a player's mouse: a spot on the view the loop puts in the world through
/// the frame's camera as it was drawn (between its last two steps), with real frames of a 60 Hz screen, a slow one's two
/// steps, a fast-forwarded game's four and a 144 Hz screen's frames that sometimes take none. The view goes where the mouse
/// takes it and stays there, however the frames fall, rather than chasing its own last move.
/// </summary>
public sealed class CameraControlsMouseTests
{
    private sealed class View : IGameView
    {
        public float Aspect => 2;

        public void Render(RenderFrame frame)
        {
        }

        public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame) => null;
    }

    // A screen's frames: how long each is, and how fast the game is fast-forwarded.
    public enum Screen
    {
        Hz60,
        Hz30,
        FastForward4,
        Hz144,
    }

    private static (GameLoop Loop, Entity Camera, Action Frame) NewGame(Screen screen, Action<Scene>? add = null)
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(0, 0, -10);
        camera.AddComponent<Camera>().Size = 5;
        camera.AddComponent<CameraControls>();
        scene.Add(camera);
        add?.Invoke(scene);

        var game = new LoadedGame(null, ComponentRegistry.CreateDefault(), scene, new ProblemLog()).NewGame();
        var loop = new GameLoop(game, new View());
        loop.Start();
        if (screen == Screen.FastForward4)
            game.Time.FastForward = 4;
        var seconds = screen switch
        {
            Screen.Hz30 => 1 / 30.0,
            Screen.Hz144 => 1 / 144.0,
            _ => 1 / 60.0,
        };
        loop.Frame(1 / 60.0);
        return (loop, game.Find("Main Camera")!, () => loop.Frame(seconds));
    }

    private static Vector2 At(Entity camera) => new(camera.Transform.Position.X, camera.Transform.Position.Y);

    private static void Near(Vector2 expected, Vector2 actual, string when) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"{when}: expected {expected}, got {actual}.");

    [Theory]
    [InlineData(Screen.Hz60)]
    [InlineData(Screen.Hz30)]
    [InlineData(Screen.FastForward4)]
    [InlineData(Screen.Hz144)]
    public void ADrag_TakesTheViewWhereTheMouseGoes_AndItStaysThere(Screen screen)
    {
        var (loop, camera, frame) = NewGame(screen);
        loop.PointAt(Vector2.Zero);
        frame();
        loop.Input.SetMouseButton(MouseButton.Middle, true);
        for (var i = 0; i < 4; i++)
            frame();
        Assert.True(camera.GetComponent<CameraControls>()!.IsDragging);

        // The hand moves the mouse a little each frame, a fifth of the view across and a tenth up; the view (20 by 10)
        // goes the other way by as much of it.
        for (var i = 1; i <= 20; i++)
        {
            loop.PointAt(new Vector2(0.01f, 0.005f) * i);
            frame();
        }

        frame();
        frame();
        var target = new Vector2(-2, -0.5f);
        Near(target, At(camera), "as the mouse stops");

        // Held still, it stays: frame after frame, never swinging back and forth.
        for (var i = 0; i < 120; i++)
        {
            frame();
            Near(target, At(camera), $"{i + 1} frames after");
        }

        // A jump of the mouse lands it there at once.
        loop.PointAt(new Vector2(-0.3f, 0.2f));
        frame();
        frame();
        frame();
        for (var i = 0; i < 30; i++)
        {
            frame();
            Near(new Vector2(3, -1), At(camera), $"{i + 1} frames after the jump");
        }
    }

    [Theory]
    [InlineData(Screen.Hz60)]
    [InlineData(Screen.Hz30)]
    [InlineData(Screen.Hz144)]
    public void TheWheel_KeepsThePointUnderTheMouse_NotchAfterNotch(Screen screen)
    {
        var (loop, camera, frame) = NewGame(screen);
        var lens = camera.GetComponent<Camera>()!;
        var spot = new Vector2(0.5f, 0.5f);
        loop.PointAt(spot);
        frame();
        var under = new Vector2(5, 2.5f);

        // A notch each frame, in and then out: the world point under the mouse, through the camera, stays put.
        foreach (var notch in new[] { 1, 1, 1, -1, -1, -1, -1 })
        {
            loop.Input.AddScroll(notch);
            frame();
            Near(under, At(camera) + spot * new Vector2(lens.Size * 2, lens.Size), $"at size {lens.Size}");
        }

        frame();
        frame();
        frame();
        Assert.Equal(5 / 0.85f, lens.Size, 3);
    }

    [Fact]
    public void DraggedPastTheLevelsEdge_DraggingBackMovesTheViewAtOnce()
    {
        var (loop, camera, frame) = NewGame(Screen.Hz60, scene =>
        {
            var level = new Entity("Level");
            level.AddComponent<BoxCollider>().Size = new Vector2(30, 10);
            scene.Add(level);
        });
        camera.GetComponent<CameraControls>()!.StayInside = "Level";
        loop.PointAt(Vector2.Zero);
        frame();
        loop.Input.SetMouseButton(MouseButton.Middle, true);
        frame();
        frame();

        // Dragged nine units right, the view stops at the level's left edge, five units over.
        loop.PointAt(new Vector2(0.9f, 0));
        for (var i = 0; i < 5; i++)
            frame();
        Near(new Vector2(-5, 0), At(camera), "at the edge");

        // A unit back, and it comes a unit back.
        loop.PointAt(new Vector2(0.8f, 0));
        frame();
        frame();
        Near(new Vector2(-4, 0), At(camera), "dragged back");
    }
}
