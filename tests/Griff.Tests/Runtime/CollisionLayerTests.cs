using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class CollisionLayerTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public CollisionLayerTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private Entity AddBox(string name, Vector2 position, string? layer = null, string? ignore = null, bool trigger = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Layer = layer;
        collider.IgnoreLayers = ignore;
        collider.IsTrigger = trigger;
        _scene.Add(entity);
        return entity;
    }

    private Entity AddFloor(string? layer = null, string? ignore = null)
    {
        var floor = AddBox("Floor", new Vector2(0, -3), layer, ignore);
        floor.GetComponent<BoxCollider>()!.Size = new Vector2(20, 1);
        return floor;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
            _game.Tick(Step);
    }

    [Fact]
    public void Body_FallsThroughALayerItIgnores()
    {
        AddFloor(layer: "Floor");
        var ghost = AddBox("Ghost", new Vector2(0, 0), ignore: "Floor");
        ghost.AddComponent<Body>();

        Run(1);

        Assert.True(ghost.Transform.Position.Y < -4);
    }

    [Fact]
    public void Ignoring_WorksFromEitherSide()
    {
        // Only the floor lists the ghost's layer.
        AddFloor(ignore: "ghost");
        var ghost = AddBox("Ghost", new Vector2(0, 0), layer: "Ghost");
        ghost.AddComponent<Body>();

        Run(1);

        Assert.True(ghost.Transform.Position.Y < -4);
    }

    [Fact]
    public void Body_StillLandsOnLayersItDoesNotIgnore()
    {
        AddFloor();
        var player = AddBox("Player", new Vector2(0, 0), layer: "Player", ignore: "Bullet, Enemy");
        var body = player.AddComponent<Body>();

        Run(1);

        Assert.True(body.IsGrounded);
        Assert.Equal(-2, player.Transform.Position.Y, 3);
    }

    [Fact]
    public void EmptyLayer_IsCalledDefault()
    {
        AddFloor();
        var ghost = AddBox("Ghost", new Vector2(0, 0), ignore: "Default");
        ghost.AddComponent<Body>();

        Run(1);

        Assert.True(ghost.Transform.Position.Y < -4);
    }

    [Fact]
    public void IgnoredLayers_DoNotReportCollisions()
    {
        AddFloor(layer: "Floor");
        var ghost = AddBox("Ghost", new Vector2(0, 0), ignore: "Floor");
        ghost.AddComponent<Body>();
        var log = ghost.AddComponent<CollisionLog>();

        Run(1);

        Assert.Empty(log.Hits);
    }

    [Fact]
    public void Triggers_SkipIgnoredLayers()
    {
        var pickup = AddBox("Coin", Vector2.Zero, trigger: true, ignore: "Enemy");
        var log = pickup.AddComponent<CollisionLog>();
        AddBox("Enemy", Vector2.Zero, layer: "Enemy");
        AddBox("Player", Vector2.Zero, layer: "Player");

        Run(Step);

        Assert.Equal(["Player"], log.Triggers);
    }

    [Fact]
    public void Tilemap_HasALayer()
    {
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(-5, -3, 0);
        var tilemap = level.AddComponent<Tilemap>();
        tilemap.Tiles = ["0000000000"];
        tilemap.Layer = "Walls";
        _scene.Add(level);
        var ghost = AddBox("Ghost", new Vector2(0, 0), ignore: "Walls");
        ghost.AddComponent<Body>();

        Run(1);

        Assert.True(ghost.Transform.Position.Y < -5);
    }

    [Fact]
    public void Tilemap_CanIgnoreLayers()
    {
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(-5, -3, 0);
        var tilemap = level.AddComponent<Tilemap>();
        tilemap.Tiles = ["0000000000"];
        tilemap.IgnoreLayers = "Ghost";
        _scene.Add(level);
        var ghost = AddBox("Ghost", new Vector2(0, 0), layer: "Ghost");
        ghost.AddComponent<Body>();

        Run(1);

        Assert.True(ghost.Transform.Position.Y < -5);
    }

    [Fact]
    public void Raycast_OnlyHitsTheGivenLayers()
    {
        AddBox("Crate", new Vector2(2, 0), layer: "Crate");
        AddBox("Enemy", new Vector2(4, 0), layer: "Enemy");

        var any = _game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX);
        var enemy = _game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX, layers: "Enemy");

        Assert.Equal("Crate", any?.Entity.Name);
        Assert.Equal("Enemy", enemy?.Entity.Name);
    }

    [Fact]
    public void Overlaps_FilterByLayer()
    {
        AddBox("Wall", Vector2.Zero);
        AddBox("Enemy", Vector2.Zero, layer: "Enemy");

        Assert.Equal(["Enemy"], _game.Physics.OverlapPoint(_scene, Vector2.Zero, layers: "enemy").Select(e => e.Name));
        Assert.Equal(["Wall"], _game.Physics.OverlapBox(_scene, Vector2.Zero, Vector2.One, layers: "Default").Select(e => e.Name));
        Assert.Equal(2, _game.Physics.OverlapCircle(_scene, Vector2.Zero, 1, layers: "Default; Enemy").Count());
    }

    [Fact]
    public void Layers_AreSavedWithTheScene()
    {
        var bullet = AddBox("Bullet", Vector2.Zero, layer: "Bullet", ignore: "Player, Bullet");

        var json = new SceneSerializer(ComponentRegistry.CreateDefault()).SaveToString(_scene);
        var loaded = new SceneSerializer(ComponentRegistry.CreateDefault()).LoadFromString(json);

        var collider = loaded.Find(bullet.Id)!.GetComponent<BoxCollider>()!;
        Assert.Equal("Bullet", collider.Layer);
        Assert.Equal("Player, Bullet", collider.IgnoreLayers);
    }

    private sealed class CollisionLog : Behavior
    {
        public List<string> Hits { get; } = [];

        public List<string> Triggers { get; } = [];

        protected override void OnCollision(Entity other) => Hits.Add(other.Name);

        protected override void OnTriggerEnter(Entity other) => Triggers.Add(other.Name);
    }
}
