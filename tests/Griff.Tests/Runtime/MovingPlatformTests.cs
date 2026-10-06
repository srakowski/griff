using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class MovingPlatformTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public MovingPlatformTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool body = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        if (body)
            entity.AddComponent<Body>();
        _scene.Add(entity);
        return entity;
    }

    private void Run(float seconds, Action? eachStep = null)
    {
        for (var t = 0f; t < seconds; t += Step)
        {
            eachStep?.Invoke();
            _game.Tick(Step);
        }
    }

    [Fact]
    public void Ground_IsWhatTheBodyStandsOn()
    {
        var floor = AddBox("Floor", new Vector2(0, -1), new Vector2(20, 1));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        var body = player.GetComponent<Body>()!;

        Run(1);
        Assert.Same(floor, body.Ground);

        body.Velocity = new Vector2(0, 10);
        _game.Tick(Step);
        Assert.Null(body.Ground);
    }

    [Fact]
    public void Platform_MovingSideways_CarriesTheBodyOnIt()
    {
        var platform = AddBox("Platform", new Vector2(0, 0), new Vector2(4, 0.5f));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        Run(0.5f);
        var startX = player.Transform.Position.X;

        Run(1, () => platform.Transform.Position += new Vector3(3 * Step, 0, 0));

        Assert.Equal(startX + 3, player.Transform.Position.X, 1);
        Assert.True(player.GetComponent<Body>()!.IsGrounded);
    }

    [Fact]
    public void Platform_MovingUpAndDown_KeepsTheBodyOnTop()
    {
        var platform = AddBox("Lift", new Vector2(0, 0), new Vector2(4, 0.5f));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        var body = player.GetComponent<Body>()!;
        Run(0.5f);
        float Gap() => player.Transform.Position.Y - 0.5f - (platform.Transform.Position.Y + 0.25f);

        Run(1, () => platform.Transform.Position += new Vector3(0, 4 * Step, 0));
        Assert.Equal(0, Gap(), 2);
        Assert.True(body.IsGrounded, "riding up");

        Run(1, () => platform.Transform.Position -= new Vector3(0, 4 * Step, 0));
        Assert.Equal(0, Gap(), 2);
        Assert.True(body.IsGrounded, "riding down");
    }

    [Fact]
    public void Platform_CarryingIntoAWall_StopsAtTheWall_WithoutKillingVelocity()
    {
        var platform = AddBox("Platform", new Vector2(0, 0), new Vector2(6, 0.5f));
        AddBox("Wall", new Vector2(2.5f, 2), new Vector2(1, 3));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        var body = player.GetComponent<Body>()!;
        Run(0.5f);

        Run(1, () => platform.Transform.Position += new Vector3(2 * Step, 0, 0));

        // The wall's left face is at x = 2; the player is 1 wide.
        Assert.Equal(1.5f, player.Transform.Position.X, 2);
        Assert.Equal(0, body.Velocity.X);
    }

    [Fact]
    public void JumpingOff_StopsTheCarry()
    {
        var platform = AddBox("Platform", new Vector2(0, 0), new Vector2(4, 0.5f));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        var body = player.GetComponent<Body>()!;
        body.GravityScale = 0;
        Run(0.1f);
        body.GravityScale = 1;
        Run(0.5f);
        body.Velocity = new Vector2(0, 20);
        _game.Tick(Step);
        var x = player.Transform.Position.X;

        Run(0.2f, () => platform.Transform.Position += new Vector3(5 * Step, 0, 0));

        Assert.Equal(x, player.Transform.Position.X, 4);
    }

    [Fact]
    public void Tween_MovingAPlatform_CarriesTheRider()
    {
        var platform = AddBox("Platform", new Vector2(0, 0), new Vector2(4, 0.5f));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        Run(0.5f);

        _game.MoveTo(platform, new Vector2(5, 0), 1, Ease.Linear);
        Run(1.2f);

        Assert.Equal(5, player.Transform.Position.X, 1);
    }
}
