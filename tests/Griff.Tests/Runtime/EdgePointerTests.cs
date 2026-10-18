using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class EdgePointerTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Entity _camera;
    private readonly Camera _lens;
    private readonly CameraControls _controls;
    private readonly Entity _worker;
    private readonly Entity _arrow;
    private readonly EdgePointer _pointer;

    // A camera on the right of the floor (its view x 0 to 20 at 2:1), a worker far off on the left, and an arrow on the
    // HUD pointing to it that glides the camera there when it's clicked.
    public EdgePointerTests()
    {
        _camera = new Entity("Main Camera");
        _camera.Transform.Position = new Vector3(10, 0, -10);
        _lens = _camera.AddComponent<Camera>();
        _lens.Size = 5;
        _controls = _camera.AddComponent<CameraControls>();
        _controls.FocusSeconds = 0.25f;
        _scene.Add(_camera);

        _worker = new Entity("Worker");
        _worker.Transform.Position = new Vector3(-20, 0, 0);
        _scene.Add(_worker);

        _arrow = new Entity("Goal Arrow");
        _pointer = _arrow.AddComponent<EdgePointer>();
        _pointer.Target = "Worker";
        _arrow.AddComponent<Sprite>().Size = new Vector2(1, 1);
        var button = _arrow.AddComponent<Button>();
        (button.Size, button.Action, button.Do) = (new Vector2(1, 1), ButtonAction.Do, "Glide");
        _scene.Add(_arrow);
    }

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

    private Vector2 Arrow => Flat(_arrow.Transform.WorldPosition);

    private Vector2 CameraAt => Flat(_camera.Transform.Position);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private void Tick(Game game)
    {
        game.Tick(Frame);
        _input.EndFrame();
    }

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void EdgePoint_IsWhereTheLineToTheTargetCrossesTheEdge_OrNoneInside()
    {
        var reach = new Vector2(8, 4);
        Assert.Null(EdgePointer.EdgePoint(Vector2.Zero, reach, new Vector2(7, -3)));
        Near(new Vector2(-8, 0), EdgePointer.EdgePoint(Vector2.Zero, reach, new Vector2(-30, 0))!.Value);
        Near(new Vector2(4, 4), EdgePointer.EdgePoint(Vector2.Zero, reach, new Vector2(10, 10))!.Value);
        Near(new Vector2(10.5f, 5), EdgePointer.EdgePoint(new Vector2(10, 1), reach, new Vector2(11, 9))!.Value);
    }

    [Fact]
    public void ATargetOffToTheLeft_PutsItOnTheLeftEdgePointingLeft_SlidingAsTheCameraPans_HiddenOnceInView()
    {
        var game = CreateGame();
        Tick(game);
        Assert.True(_arrow.IsEnabled);
        Assert.True(_pointer.IsOnEdge);
        Near(new Vector2(0.75f, 0), Arrow);
        Assert.Equal(180, _arrow.Transform.Rotation.Z, 3);
        Assert.Equal(30, _pointer.Distance, 3);
        Assert.True(Views.IsOnScreen(_arrow, _camera));

        // The camera pans up: it slides down the left edge, still on the line to the worker.
        _camera.Transform.Position = new Vector3(10, 3, -10);
        Tick(game);
        var reach = new Vector2(9.25f, 4.25f);
        Near(new Vector2(10, 3) + new Vector2(-30, -3) * (reach.X / 30), Arrow);
        Assert.Equal(MathF.Atan2(-3, -30) * 180 / MathF.PI, _arrow.Transform.Rotation.Z, 2);

        // With the worker in view it hides, and comes back once the worker's off the screen again.
        _camera.Transform.Position = new Vector3(-15, 0, -10);
        Tick(game);
        Assert.False(_arrow.IsEnabled);
        Assert.False(_pointer.IsOnEdge);
        _camera.Transform.Position = new Vector3(10, 0, -10);
        Tick(game);
        Assert.True(_arrow.IsEnabled);
    }

    [Fact]
    public void ClickingIt_GlidesTheCameraToTheTarget_WhereItHides()
    {
        var game = CreateGame();
        Tick(game);
        Tick(game);
        _input.MouseWorld = Arrow;
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.True(_controls.IsGliding);

        for (var i = 0; i < 30; i++)
            Tick(game);
        Assert.False(_controls.IsFocused);
        Near(new Vector2(-20, 0), CameraAt);
        Assert.Equal(5, _lens.Size);
        Assert.False(_arrow.IsEnabled);
    }

    [Fact]
    public void ItKeepsItsSizeOnTheScreen_AndItsMarginWithIt_AsTheCameraZooms()
    {
        _lens.HudSize = 5;
        _lens.Size = 10;
        var game = CreateGame();
        Tick(game);
        Assert.Equal(2, _arrow.Transform.ScreenScale);
        // The view reaches 20 across from the middle; the margin is twice as wide in the world.
        Near(new Vector2(10 - 20 + 1.5f, 0), Arrow);
    }

    [Fact]
    public void OverTarget_SitsOverItPointingDown_WhileItsInView_AndTheTextInsideStaysUpright()
    {
        _pointer.InView = EdgePointerInView.OverTarget;
        _pointer.OverTarget = new Vector2(0, 1.5f);
        var label = new Entity("Distance");
        label.AddComponent<Text>().Output = "Distance";
        _scene.Add(label, _arrow);
        _camera.Transform.Position = new Vector3(-18, 0, -10);
        var game = CreateGame();
        Tick(game);
        Assert.True(_arrow.IsEnabled);
        Assert.False(_pointer.IsOnEdge);
        Near(new Vector2(-20, 1.5f), Arrow);
        Assert.Equal(-90, _arrow.Transform.Rotation.Z, 3);
        Assert.Equal(90, label.Transform.Rotation.Z, 3);

        _camera.Transform.Position = new Vector3(10, 0, -10);
        Tick(game);
        Assert.True(_pointer.IsOnEdge);
        Assert.Equal(180, _arrow.Transform.Rotation.Z, 1);
        Assert.Equal(-180, label.Transform.Rotation.Z, 1);
    }

    [Fact]
    public void PointAt_MovesItOn_AndNoTargetHidesIt()
    {
        var dock = new Entity("Dock");
        dock.Transform.Position = new Vector3(10, 30, 0);
        _scene.Add(dock);
        var game = CreateGame();
        Tick(game);

        _pointer.PointAt("Dock");
        Tick(game);
        Near(new Vector2(10, 4.25f), Arrow);
        Assert.Equal(90, _arrow.Transform.Rotation.Z, 3);

        // A script's own entity holds until the Target's name changes.
        _pointer.TargetEntity = _worker;
        Tick(game);
        Near(new Vector2(0.75f, 0), Arrow);

        _pointer.PointAt("");
        Tick(game);
        Assert.False(_arrow.IsEnabled);
        _pointer.PointAt("Nobody");
        Tick(game);
        Assert.False(_arrow.IsEnabled);
    }

    [Fact]
    public void TheEntityMenusEdgePointer_PointsGlidesAndSaysHowFarOff()
    {
        _scene.Remove(_arrow);
        var preset = new Griff.Editing.Presets.EdgePointerPreset();
        Assert.Contains(Griff.Editing.Presets.EdgePointerPreset.Arrow, preset.Files().Keys);
        var made = new Entity(preset.EntityName(_scene));
        preset.Configure(made, _scene);
        made.GetComponent<EdgePointer>()!.Target = "Worker";
        _scene.Add(made);
        foreach (var child in preset.CreateChildren(_scene))
            _scene.Add(child, made);
        var game = CreateGame();
        Tick(game);
        Tick(game);
        Assert.Equal("30", made.Children[0].GetComponent<Text>()!.Shown);

        _input.MouseWorld = Flat(made.Transform.WorldPosition);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.True(_controls.IsGliding);
    }
}
