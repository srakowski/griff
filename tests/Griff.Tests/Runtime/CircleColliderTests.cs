using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class CircleColliderTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public CircleColliderTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        _scene.Add(entity);
        return entity;
    }

    private Entity AddCircle(string name, Vector2 position, float radius, bool body = false, bool trigger = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<CircleCollider>();
        collider.Radius = radius;
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
    public void FallingCircle_LandsOnABox()
    {
        AddBox("Ground", new Vector2(0, -1), new Vector2(20, 1));
        var ball = AddCircle("Ball", new Vector2(0, 5), 0.5f, body: true);

        Run(2);

        // Ground top is at -0.5; the ball's radius is 0.5.
        Assert.Equal(0f, ball.Transform.Position.Y, 3);
        Assert.True(ball.GetComponent<Body>()!.IsGrounded);
    }

    [Fact]
    public void FallingCircle_RestsOnABoxCorner_AtTheRightHeight()
    {
        // Box top-right corner at (0.5, 0.5). Ball center 0.3 to the right of it.
        AddBox("Ledge", Vector2.Zero, Vector2.One);
        var ball = AddCircle("Ball", new Vector2(0.8f, 3), 0.5f, body: true);

        Run(2);

        // Touching the corner: center is 0.5 from it, so 0.4 above it.
        Assert.Equal(0.8f, ball.Transform.Position.X, 3);
        Assert.Equal(0.9f, ball.Transform.Position.Y, 3);
    }

    [Fact]
    public void FallingBox_LandsOnACircle()
    {
        AddCircle("Boulder", Vector2.Zero, 1);
        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(0, 4, 0);
        crate.AddComponent<BoxCollider>();
        crate.AddComponent<Body>();
        _scene.Add(crate);

        Run(2);

        // Circle top is at 1; the crate's half height is 0.5.
        Assert.Equal(1.5f, crate.Transform.Position.Y, 3);
    }

    [Fact]
    public void Circles_CollideWithEachOther()
    {
        AddCircle("Post", new Vector2(3, 0), 1);
        var ball = AddCircle("Ball", Vector2.Zero, 0.5f, body: true);
        var body = ball.GetComponent<Body>()!;
        body.GravityScale = 0;
        body.Velocity = new Vector2(5, 0);

        Run(1);

        Assert.Equal(1.5f, ball.Transform.Position.X, 3);
        Assert.Equal(0f, body.Velocity.X);
    }

    [Fact]
    public void CircleRollingAlongTouchingBoxes_DoesNotSnagOnTheSeam()
    {
        AddBox("Left", new Vector2(-1, -0.5f), new Vector2(2, 1));
        AddBox("Right", new Vector2(1, -0.5f), new Vector2(2, 1));
        var ball = AddCircle("Ball", new Vector2(-1.5f, 0.5f), 0.5f, body: true);
        var body = ball.GetComponent<Body>()!;

        for (var i = 0; i < 60; i++)
        {
            body.Velocity = body.Velocity with { X = 2 };
            _game.Tick(Step);
        }

        Assert.True(ball.Transform.Position.X > 0.4f, $"Ball stopped at {ball.Transform.Position.X}.");
    }

    [Fact]
    public void BouncyBody_BouncesBack_ThenSettles()
    {
        AddBox("Ground", new Vector2(0, -1), new Vector2(20, 1));
        var ball = AddCircle("Ball", new Vector2(0, 3), 0.5f, body: true);
        var body = ball.GetComponent<Body>()!;
        body.Bounciness = 0.8f;

        var bouncedUp = false;
        for (var i = 0; i < 60; i++)
        {
            _game.Tick(Step);
            bouncedUp |= body.Velocity.Y > 5;
        }

        Assert.True(bouncedUp);

        Run(10);
        Assert.Equal(0f, body.Velocity.Y);
        Assert.Equal(0f, ball.Transform.Position.Y, 3);
    }

    [Fact]
    public void BouncyBody_BouncesOffWalls()
    {
        AddBox("Wall", new Vector2(3, 0), new Vector2(1, 10));
        var ball = AddCircle("Ball", Vector2.Zero, 0.5f, body: true);
        var body = ball.GetComponent<Body>()!;
        body.GravityScale = 0;
        body.Bounciness = 1;
        body.Velocity = new Vector2(10, 0);

        Run(0.5f);

        Assert.Equal(-10f, body.Velocity.X, 3);
    }

    [Fact]
    public void CircleTrigger_ReportsEnterAndExit()
    {
        var coin = AddCircle("Coin", new Vector2(2, 0), 0.5f, trigger: true);
        var player = AddBox("Player", Vector2.Zero, Vector2.One);
        var events = new List<string>();
        coin.AddComponent<TriggerLog>().Events = events;

        _game.Tick(Step);
        player.Transform.Position = new Vector3(1.2f, 0, 0);
        _game.Tick(Step);
        player.Transform.Position = Vector3.Zero;
        _game.Tick(Step);

        Assert.Equal(["enter Player", "exit Player"], events);
    }

    [Fact]
    public void Queries_SeeCircles()
    {
        var ball = AddCircle("Ball", new Vector2(5, 0), 1);

        Assert.Equal([ball], _game.Physics.OverlapPoint(_scene, new Vector2(5.9f, 0)));
        // Inside the bounding box but outside the circle.
        Assert.Empty(_game.Physics.OverlapPoint(_scene, new Vector2(5.9f, 0.9f)));
        Assert.Equal([ball], _game.Physics.OverlapBox(_scene, new Vector2(3.6f, 0), Vector2.One));
        Assert.Equal([ball], _game.Physics.OverlapCircle(_scene, new Vector2(5, 1.5f), 0.6f));
    }

    [Fact]
    public void Raycast_HitsTheCircleSurface_WithAnOutwardNormal()
    {
        AddCircle("Ball", new Vector2(5, 0), 1);

        var hit = _game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX);

        Assert.NotNull(hit);
        Assert.Equal(4f, hit.Value.Distance, 4);
        Assert.Equal(new Vector2(4, 0), hit.Value.Point);
        Assert.Equal(-Vector2.UnitX, hit.Value.Normal);
    }

    [Fact]
    public void Raycast_MissesACircleItPassesBeside_AndOneBehindIt()
    {
        AddCircle("Beside", new Vector2(5, 1.1f), 1);
        AddCircle("Behind", new Vector2(-5, 0), 1);

        Assert.Null(_game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX));
    }

    [Fact]
    public void Radius_ScalesWithTheLargerScale()
    {
        var ball = AddCircle("Ball", Vector2.Zero, 0.5f);
        ball.Transform.Scale = new Vector3(2, 3, 1);

        Assert.Equal(1.5f, Physics.BoundsOf(ball.GetComponent<CircleCollider>()!).Radius);
    }

    [Fact]
    public void CircleCollider_RoundTripsThroughScenes_AndIsACollider()
    {
        var ball = AddCircle("Ball", Vector2.Zero, 0.75f);
        ball.GetComponent<CircleCollider>()!.Offset = new Vector2(0, 0.25f);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene));

        var collider = Assert.IsType<CircleCollider>(loaded.Entities.Single().GetComponent<Collider>());
        Assert.Equal(0.75f, collider.Radius);
        Assert.Equal(new Vector2(0, 0.25f), collider.Offset);
    }

    private sealed class TriggerLog : Behavior
    {
        [DoNotSerialize]
        public List<string> Events { get; set; } = [];

        protected override void OnTriggerEnter(Entity other) => Events.Add($"enter {other.Name}");

        protected override void OnTriggerExit(Entity other) => Events.Add($"exit {other.Name}");
    }
}
