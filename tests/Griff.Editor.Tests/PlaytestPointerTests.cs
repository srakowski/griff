using System.Numerics;
using Griff.Editor.Features.Play;
using Griff.Hosting;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Tests;

/// <summary>
/// An agent's mouse in game_input (the editor's <see cref="PlaytestPointer"/>): put on the HUD, it rests on its spot on the
/// screen as a real mouse does, so clicking again where it rests clicks a panel that opened there again after the camera
/// moved; put on the world, it stays on its world point.
/// </summary>
public sealed class PlaytestPointerTests
{
    private sealed class View : IGameView
    {
        public float Aspect => 2;

        public void Render(RenderFrame frame)
        {
        }

        public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame) => null;
    }

    private readonly Scene _scene = new();
    private readonly Entity _camera = new("Main Camera");

    public PlaytestPointerTests()
    {
        _camera.AddComponent<Camera>().Size = 5;
        _scene.Add(_camera);
    }

    private (GameLoop Loop, PlaytestPointer Pointer) Start()
    {
        var game = new LoadedGame(null, ComponentRegistry.CreateDefault(), _scene, new ProblemLog()).NewGame();
        var loop = new GameLoop(game, new View());
        var pointer = new PlaytestPointer();
        loop.Add(pointer);
        loop.Start();
        Frames(loop, 2);
        return (loop, pointer);
    }

    private static void Frames(GameLoop loop, int frames)
    {
        for (var i = 0; i < frames; i++)
            loop.Frame(1.0 / loop.Game.Time.StepRate);
    }

    // As game_input's click: the mouse put there, a frame, pressed for two, let go for two.
    private static void Click(GameLoop loop, PlaytestPointer pointer, Vector2 at)
    {
        pointer.At = at;
        Frames(loop, 1);
        loop.Input.SetMouseButton(MouseButton.Left, true);
        Frames(loop, 2);
        loop.Input.SetMouseButton(MouseButton.Left, false);
        Frames(loop, 2);
    }

    [Fact]
    public void Clicking_again_where_it_rests_clicks_a_panel_that_opened_there_after_the_camera_moved()
    {
        var memo = new Entity("Memo");
        var panel = memo.AddComponent<Panel>();
        panel.Open = true;
        memo.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        _scene.Add(memo);
        var carryOn = new Entity("Memo Button");
        carryOn.Transform.Position = new Vector3(1, -2, 0);
        var button = carryOn.AddComponent<Button>();
        button.Size = new Vector2(4, 1);
        button.Action = ButtonAction.ClosePanel;
        _scene.Add(carryOn, memo);
        var (loop, pointer) = Start();
        var clicks = 0;
        button.Clicked += _ => clicks++;

        var at = Game.CenterOf(carryOn);
        Click(loop, pointer, at);
        Assert.Equal(1, clicks);
        Assert.False(memo.IsEnabled);
        Assert.True(pointer.RestsOnScreen);

        // The camera moves on, and the panel opens again where it was on the screen: somewhere else in the world.
        _camera.Transform.Position = new Vector3(6, 3, 0);
        Frames(loop, 2);
        loop.Game.OpenPanel(panel);
        Frames(loop, 2);
        Assert.True(Vector2.Distance(at, Game.CenterOf(carryOn)) > 1);

        // Clicking at the point the mouse was put at, where it still rests on the screen, clicks it.
        Click(loop, pointer, at);
        Assert.Equal(2, clicks);
        Assert.False(memo.IsEnabled);
    }

    [Fact]
    public void Put_on_the_world_it_stays_on_its_world_point_as_the_camera_moves()
    {
        var (loop, pointer) = Start();
        pointer.At = new Vector2(2, 2);
        Frames(loop, 2);

        _camera.Transform.Position = new Vector3(6, 3, 0);
        Frames(loop, 2);

        Assert.False(pointer.RestsOnScreen);
        Assert.Equal(new Vector2(2, 2), loop.Input.MouseWorld);
    }

    [Fact]
    public void Dragging_a_camera_with_CameraControls_moves_the_view_as_far_as_the_drag_and_it_stays()
    {
        _camera.AddComponent<CameraControls>();
        var (loop, pointer) = Start();

        // As game_input's drag with the middle button: put at the start, pressed, moved along in steps, two frames each.
        var (from, to) = (new Vector2(1, 1), new Vector2(4, 2));
        pointer.At = from;
        Frames(loop, 1);
        loop.Input.SetMouseButton(MouseButton.Middle, true);
        Frames(loop, 2);
        for (var i = 1; i <= 10; i++)
        {
            pointer.At = Vector2.Lerp(from, to, i / 10f);
            Frames(loop, 2);
        }

        // The point it took hold of is where the drag ended, on the screen: the view went back by as much, and stays.
        for (var i = 0; i < 30; i++)
        {
            Frames(loop, 1);
            var at = new Vector2(_camera.Transform.Position.X, _camera.Transform.Position.Y);
            Assert.True(Vector2.Distance(from - to, at) < 1e-3f, $"Expected {from - to}, got {at}.");
        }

        loop.Input.SetMouseButton(MouseButton.Middle, false);
    }
}
