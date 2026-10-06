using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class PhysicsTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public PhysicsTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool body = false, bool trigger = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        if (body)
            entity.AddComponent<Body>();
        _scene.Add(entity);
        return entity;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
            _game.Tick(Step);
    }

    [Fact]
    public void FallingBody_LandsOnGround_AndIsGrounded()
    {
        AddBox("Ground", new Vector2(0, -1), new Vector2(20, 1));
        var player = AddBox("Player", new Vector2(0, 5), Vector2.One, body: true);

        Run(2);

        // Ground top is at -0.5; the player's half height is 0.5.
        Assert.Equal(0f, player.Transform.Position.Y, 3);
        Assert.True(player.GetComponent<Body>()!.IsGrounded);
        Assert.Equal(0f, player.GetComponent<Body>()!.Velocity.Y);
    }

    [Fact]
    public void Wall_StopsHorizontalMovement()
    {
        AddBox("Wall", new Vector2(3, 0), new Vector2(1, 10));
        var player = AddBox("Player", Vector2.Zero, Vector2.One, body: true);
        var body = player.GetComponent<Body>()!;
        body.GravityScale = 0;
        body.Velocity = new Vector2(10, 0);

        Run(1);

        // Wall's left face is at 2.5; the player's right face stops there.
        Assert.Equal(2f, player.Transform.Position.X, 3);
        Assert.Equal(0f, body.Velocity.X);
    }

    [Fact]
    public void ZeroGravityBody_KeepsMoving()
    {
        var ship = AddBox("Ship", Vector2.Zero, Vector2.One, body: true);
        var body = ship.GetComponent<Body>()!;
        body.GravityScale = 0;
        body.Velocity = new Vector2(0, 2);

        Run(1);

        Assert.Equal(2f, ship.Transform.Position.Y, 1);
    }

    [Fact]
    public void Trigger_ReportsEnterAndExit_ToBothSides()
    {
        var coin = AddBox("Coin", new Vector2(0, 0), Vector2.One, trigger: true);
        var coinLog = coin.AddComponent<Recorder>();
        var player = AddBox("Player", new Vector2(-3, 0), Vector2.One, body: true);
        var playerLog = player.AddComponent<Recorder>();
        var body = player.GetComponent<Body>()!;
        body.GravityScale = 0;
        body.Velocity = new Vector2(3, 0);

        Run(2.5f);

        Assert.Equal(["enter Player", "exit Player"], coinLog.Events);
        Assert.Equal(["enter Coin", "exit Coin"], playerLog.Events);
    }

    [Fact]
    public void Collision_IsReported()
    {
        AddBox("Ground", new Vector2(0, -1), new Vector2(20, 1));
        var player = AddBox("Player", new Vector2(0, 1), Vector2.One, body: true);
        var log = player.AddComponent<Recorder>();

        Run(1);

        Assert.Contains("hit Ground", log.Events);
    }

    [Fact]
    public void Overlap_Queries()
    {
        var crate = AddBox("Crate", new Vector2(5, 5), new Vector2(2, 2));

        Assert.Equal([crate], _game.Physics.OverlapPoint(_scene, new Vector2(5.5f, 4.5f)));
        Assert.Empty(_game.Physics.OverlapPoint(_scene, Vector2.Zero));
        Assert.Equal([crate], _game.Physics.OverlapBox(_scene, new Vector2(3.8f, 5), Vector2.One));
    }

    [Fact]
    public void Bounds_FollowScaleAndOffset()
    {
        var entity = AddBox("Big", new Vector2(1, 1), new Vector2(2, 1));
        entity.Transform.Scale = new Vector3(2, 3, 1);
        entity.GetComponent<BoxCollider>()!.Offset = new Vector2(0.5f, 0);

        var bounds = Physics.BoundsOf(entity.GetComponent<BoxCollider>()!);

        Assert.Equal(new Vector2(2, 1), bounds.Center);
        Assert.Equal(new Vector2(2, 1.5f), bounds.HalfSize);
    }

    public class Recorder : Behavior
    {
        [DoNotSerialize]
        public List<string> Events { get; } = [];

        protected override void OnTriggerEnter(Entity other) => Events.Add($"enter {other.Name}");

        protected override void OnTriggerExit(Entity other) => Events.Add($"exit {other.Name}");

        protected override void OnCollision(Entity other)
        {
            if (!Events.Contains($"hit {other.Name}"))
                Events.Add($"hit {other.Name}");
        }
    }
}
