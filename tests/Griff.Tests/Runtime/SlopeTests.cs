using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>45 degree slope tiles: walked up and down, landed on, and seen by raycasts.</summary>
public class SlopeTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly Physics _physics = new();

    public SlopeTests() => _game = new Game(_scene, new InputState());

    // One-unit tiles from the origin: row y spans -y-1 to -y, so the ground row 3 has its top at -3.
    private Tilemap AddLevel(params string[] rows)
    {
        var entity = new Entity("Level");
        _scene.Add(entity);
        var tilemap = entity.AddComponent<Tilemap>();
        tilemap.Tiles = rows;
        tilemap.SlopeUpTiles = "1";
        tilemap.SlopeDownTiles = "2";
        return tilemap;
    }

    private Body AddPlayer(Vector2 feet)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(feet.X, feet.Y + 0.5f, 0);
        _scene.Add(player);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        return player.AddComponent<Body>();
    }

    private static float Feet(Body body) => body.Entity!.Transform.Position.Y - 0.5f;

    private static float X(Body body) => body.Entity!.Transform.Position.X;

    // Walks at a steady speed, the way movement keeps setting the body's speed every frame.
    private List<(float X, float Feet, bool Grounded)> Walk(Body body, float speed, float seconds)
    {
        var path = new List<(float, float, bool)>();
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            body.Velocity = body.Velocity with { X = speed };
            _game.Tick(Step);
            path.Add((X(body), Feet(body), body.IsGrounded));
        }

        return path;
    }

    [Fact]
    public void WalkingUpASlope_ClimbsItAndCarriesOnAlongTheTop()
    {
        AddLevel(
            "........",
            "........",
            "...10000",
            "00000000");
        var player = AddPlayer(new Vector2(1, -3));
        _game.Tick(Step);

        var path = Walk(player, 3, 1.6f);

        Assert.True(X(player) > 5.5f, $"stopped at x = {X(player)}");
        Assert.Equal(-2, Feet(player), 2);
        Assert.All(path.Zip(path.Skip(1)), p => Assert.True(p.Second.X > p.First.X, $"stuck at x = {p.First.X}"));

        // Halfway up the slope, the feet are halfway up the tile.
        var middle = path.First(p => p.X >= 3.1f);
        Assert.InRange(middle.Feet, -2.75f, -2.4f);
    }

    [Fact]
    public void WalkingDownASlope_StaysOnTheGround_AllTheWay()
    {
        AddLevel(
            "........",
            "........",
            "00002...",
            "00000000");
        var player = AddPlayer(new Vector2(2, -2));
        _game.Tick(Step);

        var path = Walk(player, 4, 1.2f);

        Assert.True(X(player) > 6.5f);
        Assert.Equal(-3, Feet(player), 2);
        Assert.All(path, p => Assert.True(p.Grounded, $"in the air at x = {p.X}, feet {p.Feet}"));
    }

    [Fact]
    public void FallingOntoASlope_LandsOnItsSurface()
    {
        AddLevel(
            "........",
            "........",
            "....1...",
            "00000000");
        var player = AddPlayer(new Vector2(4.5f, 2));

        for (var i = 0; i < 120; i++)
            _game.Tick(Step);

        Assert.True(player.IsGrounded);
        Assert.Equal("Level", player.Ground?.Name);

        // The box's right edge is 0.4 past its middle, at 4.9, where the slope is 0.9 up the tile.
        Assert.Equal(-3 + 0.9f, Feet(player), 2);
    }

    [Fact]
    public void TheTallSideOfASlope_IsAWall()
    {
        AddLevel(
            "........",
            "........",
            "....1...",
            "00000000");
        var player = AddPlayer(new Vector2(7, -3));
        _game.Tick(Step);

        Walk(player, -3, 1.5f);

        Assert.Equal(5 + 0.4f, X(player), 2);
        Assert.Equal(-3, Feet(player), 2);
    }

    [Fact]
    public void JumpingIntoASlopeFromBelow_HitsItsUnderside()
    {
        AddLevel(
            "........",
            "....1...",
            "........",
            "00000000");
        var player = AddPlayer(new Vector2(4.5f, -3));
        player.Velocity = new Vector2(0, 12);

        for (var i = 0; i < 10; i++)
            _game.Tick(Step);

        // The slope tile's bottom is at -2; the player's head stops there.
        Assert.True(Feet(player) <= -3 + 1e-3f || player.Entity!.Transform.Position.Y + 0.5f <= -2 + 1e-3f);
        Assert.True(player.Entity!.Transform.Position.Y + 0.5f <= -2 + 1e-3f);
    }

    [Fact]
    public void Raycast_HitsTheSlopesSurface()
    {
        AddLevel(
            "........",
            "........",
            "....1...",
            "00000000");

        var down = _physics.Raycast(_scene, new Vector2(4.25f, 0), -Vector2.UnitY);
        var side = _physics.Raycast(_scene, new Vector2(8, -2.5f), -Vector2.UnitX);

        Assert.NotNull(down);
        Assert.Equal(-2.75f, down.Value.Point.Y, 3);
        Assert.Equal(Vector2.Normalize(new Vector2(-1, 1)), down.Value.Normal);
        Assert.Equal(new Vector2(5, -2.5f), side!.Value.Point);
        Assert.Equal(Vector2.UnitX, side.Value.Normal);
    }

    [Fact]
    public void SlopeTiles_DontCountAsBoxes_AndAreSaved()
    {
        var tilemap = AddLevel("12", "00");

        Assert.Equal(1, tilemap.SlopeOf(1));
        Assert.Equal(-1, tilemap.SlopeOf(2));
        Assert.Equal(0, tilemap.SlopeOf(0));
        Assert.DoesNotContain(tilemap.CollisionRuns(), r => r.Y == 0);
        Assert.Equal([(0, 0, true), (1, 0, false)], tilemap.SlopeCells());

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Level").GetComponent<Tilemap>()!;
        Assert.Equal(("1", "2"), (copy.SlopeUpTiles, copy.SlopeDownTiles));
    }

    [Fact]
    public void APlatformerPlayer_RunsUpAHill_AndCanJumpFromIt()
    {
        AddLevel(
            "..........",
            "....10000.",
            "...100000.",
            "0000000000");
        var input = new InputState();
        var game = new Game(_scene, input);
        var body = AddPlayer(new Vector2(1, -3));
        body.Entity!.AddComponent<PlatformerMovement>();
        game.Tick(Step);

        input.SetKey(Key.D, true);
        var groundedOnTheWay = 0;
        for (var i = 0; i < 60; i++)
        {
            game.Tick(Step);
            input.EndFrame();
            if (body.IsGrounded)
                groundedOnTheWay++;
        }

        Assert.True(X(body) > 6, $"stopped at x = {X(body)}");
        Assert.Equal(-1, Feet(body), 2);
        Assert.True(groundedOnTheWay > 55, $"grounded {groundedOnTheWay} of 60 frames");

        // Back down onto the slope, then jump from halfway down it.
        input.SetKey(Key.D, false);
        input.SetKey(Key.A, true);
        while (X(body) > 4.6f)
        {
            game.Tick(Step);
            input.EndFrame();
        }

        Assert.True(body.IsGrounded);
        input.SetKey(Key.Space, true);
        game.Tick(Step);
        input.EndFrame();
        game.Tick(Step);
        Assert.True(body.Velocity.Y > 5);
    }

    [Fact]
    public void APatrol_WalksOverAHill_WithoutTurningAtItsTop()
    {
        AddLevel(
            "0........0",
            "0........0",
            "0...12...0",
            "0000000000");
        var slime = AddPlayer(new Vector2(2, -3));
        slime.Entity!.Name = "Slime";
        var patrol = slime.Entity.AddComponent<Patrol>();
        patrol.Speed = 2;
        patrol.TurnAtEdges = true;

        var (highest, furthest) = (float.MinValue, float.MinValue);
        for (var i = 0; i < 300; i++)
        {
            _game.Tick(Step);
            highest = Math.Max(highest, Feet(slime));
            furthest = Math.Max(furthest, X(slime));
        }

        Assert.True(highest > -2.3f, $"never got up the hill (feet reached {highest})");
        Assert.True(furthest > 8, $"turned back at x = {furthest}");
        Assert.True(Feet(slime) > -3.01f);
    }

    [Fact]
    public void PaintingASlope_UpdatesTheSlopes()
    {
        var tilemap = AddLevel("....", "0000");
        Assert.Empty(tilemap.SlopeCells());

        tilemap.SetTile(1, 0, 1);

        Assert.Equal([(1, 0, true)], tilemap.SlopeCells());
    }
}
