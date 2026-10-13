using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Physics in big scenes, where each body only tests the shapes near it (a grid): they still land on, stop at and pass
/// through what they would if they tested everything.
/// </summary>
public class BroadphaseTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public BroadphaseTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private sealed class Toucher : Behavior
    {
        public HashSet<string> Touched { get; } = [];

        public HashSet<string> Left { get; } = [];

        protected override void OnTriggerEnter(Entity other) => Touched.Add(other.Name);

        protected override void OnTriggerExit(Entity other) => Left.Add(other.Name);
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool body = false, bool trigger = false, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        if (body)
            entity.AddComponent<Body>();
        _scene.Add(entity, parent);
        return entity;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
            _game.Tick(Step);
    }

    [Fact]
    public void Hundreds_of_bodies_land_on_a_long_floor_and_the_steps_scattered_over_it()
    {
        // A floor far longer than the grid's squares (so it's tested by everything), and steps under some of the crates.
        AddBox("Floor", new Vector2(0, -0.5f), new Vector2(2000, 1));
        var crates = new List<(Entity Crate, float RestsAt)>();
        for (var i = 0; i < 400; i++)
        {
            var x = -800 + i * 4f;
            var onStep = i % 3 == 0;
            if (onStep)
                AddBox($"Step {i}", new Vector2(x, 0.5f), new Vector2(2, 1));
            crates.Add((AddBox($"Crate {i}", new Vector2(x, 6 + i % 7), new Vector2(1, 1), body: true), onStep ? 1.5f : 0.5f));
        }

        Run(3);

        foreach (var (crate, restsAt) in crates)
        {
            Assert.Equal(restsAt, crate.Transform.Position.Y, 3);
            Assert.True(crate.GetComponent<Body>()!.IsGrounded, crate.Name);
        }
    }

    [Fact]
    public void A_fast_body_stops_at_a_wall_many_squares_away()
    {
        _game.Physics.Gravity = Vector2.Zero;
        for (var i = 0; i < 200; i++)
            AddBox($"Pebble {i}", new Vector2(i * 3, 50), new Vector2(0.5f, 0.5f));
        AddBox("Wall", new Vector2(30, 0), new Vector2(4, 4));
        var bullet = AddBox("Bullet", Vector2.Zero, new Vector2(0.5f, 0.5f), body: true);
        bullet.GetComponent<Body>()!.Velocity = new Vector2(1800, 0);

        // Thirty units in one step, into the wall's middle: pushed back out to its near side.
        _game.Tick(Step);

        Assert.Equal(27.75f, bullet.Transform.Position.X, 3);
        Assert.Equal(1, bullet.GetComponent<Body>()!.WallSide);
    }

    [Fact]
    public void A_moving_parent_carries_its_childs_collider_into_what_it_hits()
    {
        _game.Physics.Gravity = Vector2.Zero;
        for (var i = 0; i < 100; i++)
            AddBox($"Pebble {i}", new Vector2(i * 3 - 150, 50), new Vector2(0.5f, 0.5f));
        var cart = AddBox("Cart", Vector2.Zero, new Vector2(1, 1), body: true);
        cart.GetComponent<Body>()!.Velocity = new Vector2(6, 0);
        var flag = AddBox("Flag", new Vector2(0, 3), new Vector2(0.5f, 0.5f), trigger: true, parent: cart);
        var toucher = flag.AddComponent<Toucher>();
        AddBox("Bell", new Vector2(8, 3), new Vector2(1, 1), trigger: true);

        Run(1.5f);

        Assert.Contains("Bell", toucher.Touched);
    }

    [Fact]
    public void A_trigger_among_hundreds_of_colliders_hears_just_those_it_overlaps()
    {
        _game.Physics.Gravity = Vector2.Zero;
        for (var i = 0; i < 300; i++)
            AddBox($"Coin {i}", new Vector2(i * 2, 0), new Vector2(0.5f, 0.5f), trigger: true);
        var player = AddBox("Player", new Vector2(-2, 0), new Vector2(1, 1), body: true, trigger: true);
        var toucher = player.AddComponent<Toucher>();
        player.GetComponent<Body>()!.Velocity = new Vector2(10, 0);

        // Along to x = 18 or so: onto coin 9, past the ones before it.
        Run(2);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Coin {i}").ToHashSet(), toucher.Touched);
        Assert.Equal(Enumerable.Range(0, 9).Select(i => $"Coin {i}").ToHashSet(), toucher.Left);
    }
}
