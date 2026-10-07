using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class FollowTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly Entity _camera;
    private readonly Follow _follow;
    private readonly Entity _player;

    public FollowTests()
    {
        _camera = new Entity("Main Camera");
        _camera.Transform.Position = new Vector3(0, 0, -10);
        _camera.AddComponent<Camera>().Size = 5;
        _follow = _camera.AddComponent<Follow>();
        _scene.Add(_camera);

        _player = new Entity("Player");
        _player.Transform.Position = new Vector3(3, 2, 0);
        _scene.Add(_player);
    }

    private Game CreateGame() => new(_scene, new InputState()) { ViewAspect = 2 };

    private static void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += Frame)
            game.Tick(Frame);
    }

    private Vector2 CameraAt => new(_camera.Transform.Position.X, _camera.Transform.Position.Y);

    [Fact]
    public void SnapsToTheTarget_OnTheFirstFrame_KeepingItsZ()
    {
        CreateGame().Tick(Frame);

        Assert.Equal(new Vector3(3, 2, -10), _camera.Transform.Position);
    }

    [Fact]
    public void CatchesUpSmoothly_AfterTheTargetMoves()
    {
        var game = CreateGame();
        game.Tick(Frame);

        _player.Transform.Position = new Vector3(8, 2, 0);
        game.Tick(Frame);
        Assert.InRange(CameraAt.X, 3.01f, 4f);

        Run(game, 2);
        Assert.Equal(8, CameraAt.X, 2);
    }

    [Fact]
    public void ZeroSmoothing_SticksExactly_WithTheOffset()
    {
        _follow.Smoothing = 0;
        _follow.Offset = new Vector2(0, 1.5f);
        var game = CreateGame();
        game.Tick(Frame);

        _player.Transform.Position = new Vector3(-4, 0, 0);
        game.Tick(Frame);

        Assert.Equal(new Vector2(-4, 1.5f), CameraAt);
    }

    [Fact]
    public void DeadZone_IgnoresSmallMoves_ThenTrailsAtItsEdge()
    {
        _follow.Smoothing = 0;
        _follow.DeadZone = new Vector2(2, 2);
        var game = CreateGame();
        game.Tick(Frame);

        _player.Transform.Position = new Vector3(3.8f, 2, 0);
        game.Tick(Frame);
        Assert.Equal(3, CameraAt.X, 4);

        _player.Transform.Position = new Vector3(6, 2, 0);
        game.Tick(Frame);
        Assert.Equal(5, CameraAt.X, 4);
    }

    [Fact]
    public void StayInside_KeepsTheWholeViewInsideABox()
    {
        var level = new Entity("Level");
        level.AddComponent<BoxCollider>().Size = new Vector2(40, 20);
        _scene.Add(level);
        _follow.StayInside = "Level";
        _follow.Smoothing = 0;
        _player.Transform.Position = new Vector3(19, -9, 0);

        CreateGame().Tick(Frame);

        // Camera size 5 at aspect 2 sees 10 either side and 5 up and down; the level spans x ±20, y ±10.
        Assert.Equal(new Vector2(10, -5), CameraAt);
    }

    [Fact]
    public void StayInside_UsesATilemapsGrid()
    {
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(-2, 8, 0);
        level.AddComponent<Tilemap>().Tiles = [new string('0', 30), "", "", "", "", "", "", "", "", "", "", "", "0"];
        _scene.Add(level);
        _follow.StayInside = "Level";
        _player.Transform.Position = new Vector3(-50, 50, 0);

        CreateGame().Tick(Frame);

        // The grid runs from (-2, 8) right 30 and down 13: x -2..28, y -5..8.
        Assert.Equal(new Vector2(8, 3), CameraAt);
    }

    [Fact]
    public void StayInside_CentersAViewBiggerThanTheArea()
    {
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(1, 1, 0);
        level.AddComponent<BoxCollider>().Size = new Vector2(4, 4);
        _scene.Add(level);
        _follow.StayInside = "Level";

        CreateGame().Tick(Frame);

        Assert.Equal(new Vector2(1, 1), CameraAt);
    }

    [Fact]
    public void LookAhead_LeadsTheWayTheTargetMoves()
    {
        _follow.Smoothing = 0;
        _follow.LookAhead = 0.5f;
        _player.AddComponent<Body>().GravityScale = 0;
        var game = CreateGame();
        game.Tick(Frame);

        _player.GetComponent<Body>()!.Velocity = new Vector2(4, 0);
        Run(game, 2);

        // 4 units/s, half a second ahead: 2 units in front of the player.
        Assert.Equal(_player.Transform.Position.X + 2, CameraAt.X, 1);
    }

    [Fact]
    public void FindsTheTargetAgain_AfterItIsReplaced()
    {
        _follow.Smoothing = 0;
        var game = CreateGame();
        game.Tick(Frame);

        game.Destroy(_player);
        game.Tick(Frame);
        var respawned = new Entity("Player");
        respawned.Transform.Position = new Vector3(-7, 0, 0);
        game.Spawn(respawned);
        game.Tick(Frame);

        Assert.Equal(new Vector2(-7, 0), CameraAt);
    }

    [Fact]
    public void ScreenAnchors_PinToTheCameraWhereFollowLeftIt()
    {
        _follow.Smoothing = 0;
        var hud = new Entity("Score");
        hud.AddComponent<ScreenAnchor>().Anchor = Anchor.TopLeft;
        _scene.Add(hud);

        CreateGame().Tick(Frame);

        Assert.Equal(new Vector3(3 - 10, 2 + 5, 0), hud.Transform.Position);
    }

    [Fact]
    public void FollowsInWorldSpace_WhenParented()
    {
        var rig = new Entity("Rig");
        rig.Transform.Position = new Vector3(10, 0, 0);
        _scene.Add(rig);
        _scene.Remove(_camera);
        _scene.Add(_camera, rig);
        _follow.Smoothing = 0;

        CreateGame().Tick(Frame);

        Assert.Equal(new Vector3(3, 2, -10), _camera.Transform.WorldPosition);
    }
}
