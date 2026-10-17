using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class CameraControlsTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Entity _camera;
    private readonly Camera _lens;
    private readonly CameraControls _controls;

    public CameraControlsTests()
    {
        _camera = new Entity("Main Camera");
        _camera.Transform.Position = new Vector3(0, 0, -10);
        _lens = _camera.AddComponent<Camera>();
        _lens.Size = 5;
        _controls = _camera.AddComponent<CameraControls>();
        _scene.Add(_camera);
    }

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

    private Vector2 At => new(_camera.Transform.Position.X, _camera.Transform.Position.Y);

    // The mouse resting on a point of the screen (-1 to 1 across it), put in the world through the camera as it is now, as
    // the platform does each frame.
    private void MouseOnScreen(Vector2 across) => _input.MouseWorld = At + across * new Vector2(_lens.Size * 2, _lens.Size);

    private void Tick(Game game, Vector2? mouseOnScreen = null)
    {
        if (mouseOnScreen is { } across)
            MouseOnScreen(across);
        game.Tick(Frame);
        _input.EndFrame();
    }

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void TheKeys_PanAScreensHeightASecond_WhateverTheZoom()
    {
        var game = CreateGame();
        _input.SetKey(Key.D, true);
        for (var i = 0; i < 30; i++)
            Tick(game);
        Near(new Vector2(5, 0), At);

        _lens.Size = 10;
        _input.SetKey(Key.D, false);
        _input.SetKey(Key.Up, true);
        for (var i = 0; i < 30; i++)
            Tick(game);
        Near(new Vector2(5, 10), At);
        Assert.Equal(-10, _camera.Transform.Position.Z);
    }

    [Fact]
    public void PanUp_RebindToI_PansWithIAndNotW()
    {
        var game = CreateGame();
        _input.Rebind("PanUp", Key.I);

        _input.SetKey(Key.W, true);
        for (var i = 0; i < 30; i++)
            Tick(game);
        Near(Vector2.Zero, At);

        _input.SetKey(Key.W, false);
        _input.SetKey(Key.I, true);
        for (var i = 0; i < 30; i++)
            Tick(game);
        Near(new Vector2(0, 5), At);
    }

    [Fact]
    public void TheProjectsControls_TakingWAndSOffThePanActions_FreesThem()
    {
        var game = CreateGame();
        _input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>>
        {
            ["PanUp"] = [Key.Up, GamepadButton.DPadUp],
            ["PanDown"] = [Key.Down, GamepadButton.DPadDown],
        });

        _input.SetKey(Key.W, true);
        _input.SetKey(Key.D, true);
        for (var i = 0; i < 30; i++)
            Tick(game);

        // D still pans right; W does nothing.
        Near(new Vector2(5, 0), At);
    }

    [Fact]
    public void TheLeftStick_StillPans()
    {
        var game = CreateGame();
        for (var i = 0; i < 30; i++)
        {
            _input.SetGamepad([], new Vector2(-1, 0));
            Tick(game);
        }
        Near(new Vector2(-5, 0), At);
    }

    [Fact]
    public void TheWheel_ZoomsTowardTheMouse_BetweenItsLimits()
    {
        var game = CreateGame();
        MouseOnScreen(new Vector2(0.5f, 0.5f));
        var under = _input.MouseWorld;

        _input.AddScroll(1);
        Tick(game);

        Assert.Equal(5 * 0.85f, _lens.Size, 4);
        // The point under the mouse is still under it.
        MouseOnScreen(new Vector2(0.5f, 0.5f));
        Near(under, _input.MouseWorld);

        for (var i = 0; i < 20; i++)
        {
            _input.AddScroll(-1);
            Tick(game);
        }

        Assert.Equal(16, _lens.Size, 4);
        _controls.ZoomStep = 0;
        _input.AddScroll(1);
        Tick(game);
        Assert.Equal(16, _lens.Size, 4);
    }

    [Fact]
    public void DraggingWithTheMiddleOrRightButton_KeepsTheWorldUnderTheMouse()
    {
        var game = CreateGame();
        MouseOnScreen(Vector2.Zero);
        _input.SetMouseButton(MouseButton.Right, true);
        Tick(game);
        Assert.True(_controls.IsDragging);

        // The mouse moves a quarter of the screen right and up: the world goes with it, so the camera goes the other way.
        Tick(game, new Vector2(0.25f, 0.25f));
        Tick(game, new Vector2(0.25f, 0.25f));
        Near(new Vector2(-2.5f, -1.25f), At);

        _input.SetMouseButton(MouseButton.Right, false);
        Tick(game, new Vector2(0.25f, 0.25f));
        Tick(game, new Vector2(-0.5f, 0));
        Assert.False(_controls.IsDragging);
        Near(new Vector2(-2.5f, -1.25f), At);

        _controls.DragButton = CameraDrag.None;
        _input.SetMouseButton(MouseButton.Middle, true);
        Tick(game);
        Assert.False(_controls.IsDragging);
    }

    [Fact]
    public void EdgeScroll_PansTowardTheEdgeTheMouseRestsAt()
    {
        var game = CreateGame();
        Tick(game, new Vector2(0.99f, 0));
        Assert.Equal(Vector2.Zero, At);

        _controls.EdgeScroll = true;
        for (var i = 0; i < 60; i++)
            Tick(game, new Vector2(0.99f, -0.99f));

        Near(new Vector2(10, -10) / MathF.Sqrt(2), At);
    }

    [Fact]
    public void StayInside_KeepsTheViewInsideTheLevel_AsItGrows()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Tiles = Enumerable.Repeat(new string('1', 30), 20).ToList();
        _scene.Add(level);
        _controls.StayInside = "Level";
        var (min, max) = map.Bounds();
        var game = CreateGame();

        _input.SetKey(Key.Left, true);
        _input.SetKey(Key.Down, true);
        for (var i = 0; i < 120; i++)
            Tick(game);
        Near(min + new Vector2(10, 5), At);

        // The level grows: the view can go further.
        map.Tiles = Enumerable.Repeat(new string('1', 30), 30).ToList();
        for (var i = 0; i < 120; i++)
            Tick(game);
        Near(map.Bounds().Min + new Vector2(10, 5), At);
        Assert.Equal(max.X, map.Bounds().Max.X, 4);
    }

    [Fact]
    public void OverTheHud_TheWheelAndDragsAreItsOwn_AndTheHudKeepsItsPlaceAsItZooms()
    {
        _lens.HudSize = 5;
        var bar = new Entity("Top Bar");
        bar.AddComponent<Sprite>().Size = new Vector2(20, 1);
        var pin = bar.AddComponent<ScreenAnchor>();
        pin.Anchor = Anchor.Top;
        pin.Offset = new Vector2(0, -0.5f);
        _scene.Add(bar);
        var game = CreateGame();
        Tick(game);

        // On the bar, at the top of the screen.
        MouseOnScreen(new Vector2(0, 0.95f));
        _input.AddScroll(1);
        _input.SetMouseButton(MouseButton.Middle, true);
        Tick(game);
        Assert.Equal(5, _lens.Size);
        Assert.False(_controls.IsDragging);
        _input.SetMouseButton(MouseButton.Middle, false);
        Tick(game);

        // Off it, the wheel zooms, and the bar stays at the top of the screen at the same size.
        MouseOnScreen(new Vector2(0, -0.5f));
        _input.AddScroll(-3);
        Tick(game);
        Assert.True(_lens.Size > 5);
        var onScreen = (new Vector2(bar.Transform.WorldPosition.X, bar.Transform.WorldPosition.Y) - At) / _lens.Size;
        Near(new Vector2(0, 1 - 0.5f / 5), onScreen);
        Assert.Equal(_lens.Size / 5, bar.Transform.WorldMatrix.M11, 4);
    }

    [Fact]
    public void FocusOn_FollowsAWalkingCharacterAtItsSize_TheKeysAndWheelWaiting_ThenHandsBackAtTheOldSize()
    {
        _lens.Size = 9;
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(10, 0, 0);
        _scene.Add(walker);
        var game = CreateGame();
        Tick(game);

        _controls.FocusSeconds = 0.5f;
        _controls.FocusOn(walker, 6);
        Assert.True(_controls.IsFocused);
        Assert.Same(walker, _controls.FocusTarget);

        // Halfway through the glide it's on its way, zooming in; WASD and the wheel do nothing meanwhile.
        _input.SetKey(Key.W, true);
        for (var i = 0; i < 15; i++)
        {
            walker.Transform.Position += new Vector3(0.05f, 0, 0);
            _input.AddScroll(-1);
            Tick(game, new Vector2(0.5f, 0.5f));
        }

        Assert.InRange(_lens.Size, 6.01f, 8.99f);
        Assert.InRange(At.X, 0.01f, 10.7f);

        for (var i = 0; i < 60; i++)
        {
            walker.Transform.Position += new Vector3(0.05f, 0, 0);
            _input.AddScroll(-1);
            Tick(game, new Vector2(0.5f, 0.5f));
        }

        Assert.Equal(6, _lens.Size, 4);
        Near(new Vector2(walker.Transform.Position.X, 0), At);
        Assert.Equal(-10, _camera.Transform.Position.Z);

        // Released, it zooms back to the Size it had, staying where it is, and then the keys and the wheel work again.
        _input.SetKey(Key.W, false);
        _controls.Release();
        Assert.True(_controls.IsFocused);
        Assert.Null(_controls.FocusTarget);
        var there = At;
        for (var i = 0; i < 31; i++)
            Tick(game);
        Assert.False(_controls.IsFocused);
        Assert.Equal(9, _lens.Size, 4);
        Near(there, At);

        _input.SetKey(Key.D, true);
        for (var i = 0; i < 30; i++)
            Tick(game);
        Near(there + new Vector2(9, 0), At);
        _input.SetKey(Key.D, false);
        _input.AddScroll(1);
        Tick(game, Vector2.Zero);
        Assert.Equal(9 * 0.85f, _lens.Size, 4);
    }

    [Fact]
    public void FocusOn_APoint_StaysInsideTheLevel_AndWithNoSizeOrTime_JumpsThereAtItsOwnSize()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Tiles = Enumerable.Repeat(new string('1', 30), 20).ToList();
        _scene.Add(level);
        _controls.StayInside = "Level";
        var (min, _) = map.Bounds();
        var game = CreateGame();
        Tick(game);

        _controls.FocusSeconds = 0;
        _controls.FocusOn(min - new Vector2(50, 50));
        Tick(game);

        Assert.Equal(5, _lens.Size, 4);
        Near(min + new Vector2(10, 5), At);
    }

    [Fact]
    public void FocusOn_AndRelease_AreActions_ATriggerOrButtonCanDo_ByName()
    {
        var names = string.Join(", ", ComponentActions.Of(typeof(CameraControls)).Select(a => a.Name));
        Assert.Equal("FocusOn, Release", names);

        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(3, 1, 0);
        _scene.Add(walker);
        _controls.FocusSize = 4;
        _controls.FocusSeconds = 0;
        var game = CreateGame();

        _controls.FocusOn("Nobody");
        Assert.False(_controls.IsFocused);
        _controls.FocusOn("Walker");
        Tick(game);
        Assert.Equal(4, _lens.Size, 4);
        Near(new Vector2(3, 1), At);

        _controls.Release();
        Tick(game);
        Assert.False(_controls.IsFocused);
        Assert.Equal(5, _lens.Size, 4);
    }

    [Fact]
    public void APauseMenu_HoldsTheViewStill()
    {
        var menu = new Entity("Pause Menu");
        menu.AddComponent<Panel>().Open = true;
        _scene.Add(menu);
        var game = CreateGame();
        Tick(game);
        Assert.True(game.LocalInputBlocked);

        _input.SetKey(Key.D, true);
        Tick(game);

        Assert.Equal(Vector2.Zero, At);
    }
}
