using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class TileCollisionTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public TileCollisionTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private Tilemap AddMap(params string[] rows)
    {
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        map.Tiles = rows;
        _scene.Add(entity);
        return map;
    }

    private Body AddPlayer(Vector2 position)
    {
        var entity = new Entity("Player");
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 0.8f);
        var body = entity.AddComponent<Body>();
        _scene.Add(entity);
        return body;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
            _game.Tick(Step);
    }

    [Theory]
    [InlineData("3", new[] { 3 })]
    [InlineData("7-9, 1", new[] { 1, 7, 8, 9 })]
    [InlineData(" 5 ;2-2  x 9-7 ", new[] { 2, 5, 7, 8, 9 })]
    [InlineData("21450-21499", new[] { 21450, 21451 })]
    [InlineData("", new int[0])]
    [InlineData(null, new int[0])]
    public void ParseTileSet_ReadsNumbersAndRanges(string? text, int[] expected)
    {
        var set = Tilemap.ParseTileSet(text);

        Assert.Equal(expected, Enumerable.Range(0, Tilemap.MaxTiles).Where(t => Tilemap.InSet(set, t)));
    }

    [Fact]
    public void CollisionRuns_LeaveOutBackgroundTiles_AndSplitOneWayRuns()
    {
        var map = AddMap("0011220");
        map.BackgroundTiles = "1";
        map.OneWayTiles = "2";

        Assert.Equal([(0, 0, 2, false), (4, 0, 2, true), (6, 0, 1, false)], map.CollisionRuns());
        // Drawing still sees every tile.
        Assert.Equal([(0, 0, 7)], map.Runs());
    }

    [Fact]
    public void ChangingTileSettings_RebuildsCollision()
    {
        var map = AddMap("000");
        Assert.Single(map.CollisionRuns());

        map.BackgroundTiles = "0";

        Assert.Empty(map.CollisionRuns());
        Assert.Empty(Physics.BoundsOf(map));
    }

    [Fact]
    public void BackgroundTiles_DontBlockBodies()
    {
        // Row 0 is decoration; row 2 is the floor.
        var map = AddMap("111", "...", "000");
        map.BackgroundTiles = "1";
        map.Entity!.Transform.Position = new Vector3(-1, 3, 0);
        var player = AddPlayer(new Vector2(0.5f, 5));

        Run(2);

        // Floor (row 2) spans y 0..1, so the player rests at 1.4.
        Assert.Equal(1.4f, player.Entity!.Transform.Position.Y, 3);
    }

    [Fact]
    public void OneWayTiles_CatchBodiesFromAbove_ButLetThemJumpThrough()
    {
        var map = AddMap("222");
        map.OneWayTiles = "2";
        map.Entity!.Transform.Position = new Vector3(-1, 1, 0);

        // Starts below the platform (y 0..1) moving up: passes through.
        var player = AddPlayer(new Vector2(0.5f, -1));
        player.Velocity = new Vector2(0, 15);
        Run(0.25f);
        Assert.True(player.Entity!.Transform.Position.Y > 1.4f);

        // Then falls back and lands on top.
        Run(2);
        Assert.Equal(1.4f, player.Entity.Transform.Position.Y, 3);
        Assert.True(player.IsGrounded);
    }

    [Fact]
    public void TileSettings_RoundTripThroughScenes()
    {
        var map = AddMap("01");
        map.BackgroundTiles = "1";
        map.OneWayTiles = "4-5";
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single().GetComponent<Tilemap>()!;

        Assert.Equal("1", loaded.BackgroundTiles);
        Assert.Equal("4-5", loaded.OneWayTiles);
        Assert.True(loaded.IsBackground(1));
        Assert.True(loaded.IsOneWay(5));
    }
}
