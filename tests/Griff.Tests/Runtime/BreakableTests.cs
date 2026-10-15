using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Things that break on hard knocks: physics says how hard each knock was; hits harder than a Breakable's Min Hit add up to
/// its Strength, cracking it in stages, and then it splits into pieces (or vanishes in dust, or into its Effect) with its
/// sound and messages; materials from a table, a Health to share the damage, its actions; a tower collapsing the same to the
/// bit twice and across a rewind, in a network game, and with no garbage while knocks don't break anything.
/// </summary>
public sealed class BreakableTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly List<object?> _heard = [];

    public BreakableTests()
    {
        _game = new Game(_scene, new InputState()) { PrefabLoader = name => new Entity(name) };
        _game.On("Score", data => _heard.Add(data));
    }

    private void Run(float seconds) => Run(_game, seconds);

    private static void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            game.Tick(Frame);
    }

    private static Entity Floor(Scene scene)
    {
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
        scene.Add(floor);
        return floor;
    }

    private static Entity Block(Scene scene, string name, Vector2 at, Vector2 size, float strength = 10, float minHit = 2, float mass = 1, bool rotates = true)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        entity.AddComponent<Sprite>().Size = size;
        entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        (body.Rotates, body.Mass) = (rotates, mass);
        var breakable = entity.AddComponent<Breakable>();
        (breakable.Strength, breakable.MinHit) = (strength, minHit);
        scene.Add(entity);
        return entity;
    }

    private Entity Block(string name, Vector2 at, Vector2 size, float strength = 10, float minHit = 2, float mass = 1, bool rotates = true) =>
        Block(_scene, name, at, size, strength, minHit, mass, rotates);

    // How fast something dropped from `height` lands, under the game's gravity.
    private static float Landing(float height) => MathF.Sqrt(2 * 25 * height);

    [Fact]
    public void Physics_says_how_hard_each_knock_was_both_ways_round()
    {
        Floor(_scene);
        var crate = Block("Crate", new Vector2(0, 0.5f + 1.25f), Vector2.One, strength: 0);
        var seen = new List<Impact>();
        for (var i = 0; i < 60 && seen.Count == 0; i++)
        {
            _game.Tick(Frame);
            seen.AddRange(_game.Physics.Impacts);
        }

        // Dropped from 1.25 up it lands at about 7.9 a second: a crate of Mass 1 hits about that hard, flat on the floor.
        Assert.Equal(2, seen.Count);
        Assert.Equal((crate, "Floor"), (seen[0].Struck, seen[0].By.Name));
        Assert.Equal(("Floor", crate), (seen[1].Struck.Name, seen[1].By));
        Assert.InRange(seen[0].Strength, Landing(1.25f) * 0.85f, Landing(1.25f) * 1.1f);
        Assert.Equal(seen[0].Strength, seen[1].Strength);

        // Resting there, it knocks nothing.
        Run(0.5f);
        Assert.Empty(_game.Physics.Impacts);
    }

    [Fact]
    public void Hits_add_up_cracking_it_in_stages_until_it_splits_into_pieces_that_fade()
    {
        Floor(_scene);
        var block = Block("Block", new Vector2(0, 0.5f + 0.8f), Vector2.One, strength: 14, minHit: 2);
        var breakable = block.GetComponent<Breakable>()!;
        breakable.BreakMessage = "Score";
        var hits = new List<float>();
        breakable.Hit += (_, damage, _) => hits.Add(damage);

        // A drop of 0.8 lands at about 6.3: cracked, not broken. Resting does nothing more.
        Run(1);
        Assert.Single(hits);
        Assert.InRange(breakable.Damage, 5f, 7f);
        Assert.Equal(1, breakable.Stage);
        Assert.False(breakable.Broken);
        Run(1);
        Assert.Single(hits);

        // Dropped again from higher (about 8.9): the two add up past its Strength, and it breaks into four pieces.
        block.Transform.Position = new Vector3(0, 0.5f + 1.6f, 0);
        block.Transform.Rotation = Vector3.Zero;
        Run(1);
        Assert.Equal(2, hits.Count);
        Assert.True(breakable.Broken);
        Assert.Null(block.Scene);
        var pieces = _scene.Entities.Where(e => e.Name == "Block Piece").ToList();
        Assert.Equal(4, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(new Vector2(0.5f), p.GetComponent<Sprite>()!.Size));
        Assert.All(pieces, p => Assert.True(p.GetComponent<Body>()!.Rotates));
        Assert.All(pieces, p => Assert.Equal(0.25f, p.GetComponent<Body>()!.Mass));
        Assert.Single(_heard);
        Assert.Same(block, _heard[0]);

        // They tumble about and fade out after Pieces Last.
        Run(2.5f);
        Assert.DoesNotContain(_scene.Entities, e => e.Name == "Block Piece");
    }

    [Fact]
    public void A_soft_landing_does_nothing_and_a_high_stack_resting_doesnt_wear_down_its_bottom()
    {
        Floor(_scene);
        var blocks = new List<Breakable>();
        for (var i = 0; i < 8; i++)
            blocks.Add(Block($"Block {i}", new Vector2(0, 0.5f + i * 1.01f), Vector2.One, strength: 10, minHit: 2).GetComponent<Breakable>()!);
        Run(4);
        Assert.All(blocks, b => Assert.Equal(0, b.Damage));
        Assert.All(blocks, b => Assert.False(b.Broken));
    }

    [Fact]
    public void A_picture_splits_into_its_parts_and_a_sheet_of_stages_shows_the_cracks()
    {
        Floor(_scene);
        var block = Block("Block", new Vector2(0, 3), new Vector2(2, 1), strength: 100, minHit: 1);
        var sprite = block.GetComponent<Sprite>()!;
        (sprite.Columns, sprite.Rows) = (4, 1);
        var breakable = block.GetComponent<Breakable>()!;
        breakable.Damage = 60;
        _game.Tick(Frame);
        Assert.Equal(2, sprite.Frame);

        // Split 3: nine pieces, each its part of the picture (frames counted from the top left).
        var pane = Block("Pane", new Vector2(5, 0.5f), new Vector2(3, 3), strength: 1, minHit: 0);
        var picture = pane.GetComponent<Sprite>()!;
        picture.Image = "Sprites/pane.png";
        var glass = pane.GetComponent<Breakable>()!;
        glass.Split = 3;
        _game.Do(pane, "Break");
        _game.Tick(Frame);
        var parts = _scene.Entities.Where(e => e.Name == "Pane Piece").ToList();
        Assert.Equal(9, parts.Count);
        Assert.All(parts, p => Assert.Equal((3, 3, "Sprites/pane.png"), (p.GetComponent<Sprite>()!.Columns, p.GetComponent<Sprite>()!.Rows, p.GetComponent<Sprite>()!.Image)));
        var bottomLeft = parts.OrderBy(p => p.Transform.Position.X + p.Transform.Position.Y * 10).First();
        Assert.Equal(6, bottomLeft.GetComponent<Sprite>()!.Frame);
        Assert.Equal(new Vector2(1), bottomLeft.GetComponent<BoxCollider>()!.Size / 0.96f, new Vector2Comparer(1e-4f));
    }

    [Fact]
    public void Without_pieces_it_vanishes_in_dust_or_its_effect_with_its_points()
    {
        Floor(_scene);
        var target = Block("Target", new Vector2(0, 0.5f), Vector2.One, strength: 3, minHit: 1);
        var breakable = target.GetComponent<Breakable>()!;
        (breakable.Split, breakable.Points, breakable.BreakMessage) = (0, 5000, "Score");
        var other = Block("Other", new Vector2(4, 0.5f), Vector2.One);
        var second = other.GetComponent<Breakable>()!;
        (second.Split, second.Effect) = (0, "Boom");

        _game.Do(target, "Damage", 5);
        _game.Do(other, "Break");
        _game.Tick(Frame);
        _game.Tick(Frame);
        Assert.Equal([5000], _heard);
        Assert.Null(target.Scene);
        Assert.DoesNotContain(_scene.Entities, e => e.Name == "Target Piece");
        var dust = _scene.Entities.Single(e => e.Name == "Target Dust").GetComponent<ParticleEmitter>()!;
        Assert.Equal((16, true), (dust.Particles.Count, dust.DestroyWhenFinished));
        Assert.Single(_scene.Entities, e => e.Name == "Boom");
        Assert.DoesNotContain(_scene.Entities, e => e.Name == "Other Dust");
    }

    [Fact]
    public void Left_in_place_it_lies_broken_until_repaired_and_says_so_on_its_output()
    {
        Floor(_scene);
        var wall = Block("Wall", new Vector2(0, 1), new Vector2(0.5f, 2), strength: 4, minHit: 1);
        var breakable = wall.GetComponent<Breakable>()!;
        (breakable.Remove, breakable.Split, breakable.Dust) = (false, 0, 0);
        _game.Do(wall, "Damage", 10);
        _game.Tick(Frame);
        Assert.True(breakable.Broken);
        Assert.False(wall.IsEnabled);
        Assert.Same(_scene, wall.Scene);
        var reader = new OutputReader();
        Assert.True(reader.Find(wall, null, "Broken"));
        Assert.Equal(1f, reader.Number);

        _game.Do(wall, "Repair");
        _game.Tick(Frame);
        Assert.Equal((false, 0f, true), (breakable.Broken, breakable.Damage, wall.IsEnabled));
    }

    [Fact]
    public void A_health_on_it_takes_the_damage_too_and_it_breaks_as_that_dies()
    {
        Floor(_scene);
        var pig = Block("Pig", new Vector2(0, 0.5f + 1), Vector2.One, strength: 0, minHit: 1);
        var health = pig.AddComponent<Health>();
        (health.Max, health.InvincibleTime) = (5, 0);
        var breakable = pig.GetComponent<Breakable>()!;
        breakable.Split = 0;

        // Dropped from 1, it hits about 7: past its health.
        for (var i = 0; i < 60 && !breakable.Broken; i++)
            _game.Tick(Frame);
        _game.Tick(Frame);
        Assert.True(health.IsDead);
        Assert.True(breakable.Broken);
        Assert.Null(pig.Scene);
        Assert.Single(_scene.Entities, e => e.Name == "Pig Dust");
    }

    [Fact]
    public void Materials_come_from_a_table()
    {
        var table = TableFile.Read("""
            { "columns": [ { "name": "Strength", "type": "number" }, { "name": "Min Hit", "type": "number" },
                           { "name": "Mass", "type": "number" }, { "name": "Tint", "type": "text" } ],
              "rows": { "Glass": { "Strength": 4, "Min Hit": 1.5, "Mass": 0.6, "Tint": "#A8DCF0C8" },
                        "Stone": { "Strength": 40, "Min Hit": 6, "Mass": 3, "Tint": "#8E929C" } } }
            """, "Materials");
        _game.Tables = new TableSet([table]);
        Floor(_scene);
        var glass = Block("Glass", new Vector2(-2, 0.5f), Vector2.One);
        var stone = Block("Stone", new Vector2(2, 0.5f), Vector2.One);
        foreach (var (entity, row) in new[] { (glass, "Glass"), (stone, "Stone") })
        {
            var values = entity.AddComponent<TableValues>();
            (values.Table, values.Row) = ("Materials", row);
        }

        _game.Tick(Frame);
        Assert.Equal((4f, 1.5f, 0.6f), (glass.GetComponent<Breakable>()!.Strength, glass.GetComponent<Breakable>()!.MinHit, glass.GetComponent<Body>()!.Mass));
        Assert.Equal((40f, 6f, 3f), (stone.GetComponent<Breakable>()!.Strength, stone.GetComponent<Breakable>()!.MinHit, stone.GetComponent<Body>()!.Mass));
        Assert.Equal(Color.FromBytes(0x8E, 0x92, 0x9C), stone.GetComponent<Sprite>()!.Tint);

        // The same ball, as hard at each: the glass breaks, the stone hardly notices.
        foreach (var x in new[] { -5f, -1f })
        {
            var ball = new Entity("Ball");
            ball.Transform.Position = new Vector3(x, 0.5f, 0);
            ball.AddComponent<CircleCollider>().Radius = 0.3f;
            var body = ball.AddComponent<Body>();
            (body.Rotates, body.Mass, body.Velocity) = (true, 2, new Vector2(8, 0));
            _scene.Add(ball);
        }

        Run(1);
        Assert.True(glass.GetComponent<Breakable>()!.Broken);
        Assert.False(stone.GetComponent<Breakable>()!.Broken);
    }

    [Fact]
    public void Bodies_moved_the_old_way_and_things_that_dont_move_break_too()
    {
        Floor(_scene);
        // An upright crate (no Rotates) dropped hard, and a pane that holds still, landed on by a heavy one.
        var crate = Block("Crate", new Vector2(-3, 0.5f + 2), Vector2.One, strength: 8, minHit: 2, rotates: false);
        var pane = new Entity("Pane");
        pane.Transform.Position = new Vector3(3, 0.25f, 0);
        pane.AddComponent<BoxCollider>().Size = new Vector2(2, 0.5f);
        pane.AddComponent<Breakable>().Strength = 5;
        _scene.Add(pane);
        Block("Anvil", new Vector2(3, 0.5f + 2.5f), Vector2.One, strength: 0, mass: 4);

        Run(1.5f);
        Assert.True(crate.GetComponent<Breakable>()!.Broken);
        Assert.True(pane.GetComponent<Breakable>()!.Broken);
    }

    // ----- To the bit, rewinds, network games and garbage -----

    // A tower of planks and posts with a heavy ball rolling into it.
    private static Scene Tower()
    {
        var scene = new Scene();
        Floor(scene);
        for (var storey = 0; storey < 3; storey++)
        {
            var y = storey * 2.35f;
            Block(scene, "Post", new Vector2(-1.1f, y + 1), new Vector2(0.35f, 2), strength: 8, minHit: 2).GetComponent<Body>()!.Asleep = true;
            Block(scene, "Post", new Vector2(1.1f, y + 1), new Vector2(0.35f, 2), strength: 8, minHit: 2).GetComponent<Body>()!.Asleep = true;
            Block(scene, "Plank", new Vector2(0, y + 2.175f), new Vector2(3, 0.35f), strength: 6, minHit: 2).GetComponent<Body>()!.Asleep = true;
        }

        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(-8, 4, 0);
        ball.AddComponent<CircleCollider>().Radius = 0.5f;
        var body = ball.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Velocity) = (true, 6, new Vector2(16, 3));
        scene.Add(ball);
        return scene;
    }

    private static (Vector3[] Bodies, string[] Names) Snapshot(Scene scene) =>
        (scene.Entities.Where(e => e.GetComponent<Body>() is not null).Select(e => e.Transform.Position).ToArray(),
         scene.Entities.Select(e => e.Name).ToArray());

    [Fact]
    public void A_tower_falls_the_same_to_the_last_bit_twice_and_across_a_rewind()
    {
        static (Vector3[], string[]) Play(Scene scene, int frames)
        {
            var game = new Game(scene, new InputState());
            for (var i = 0; i < frames; i++)
                game.Tick(Frame);
            return Snapshot(scene);
        }

        var (bodies, names) = Play(Tower(), 200);
        var (again, againNames) = Play(Tower(), 200);
        Assert.Contains("Plank Piece", names);
        Assert.Equal(names, againNames);
        Assert.Equal(bodies, again);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Tower();
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 60; frame++)
        {
            game.Tick(Frame);
            timeline.Record(scene, game.Time.Total);
        }

        for (var frame = 0; frame < 120; frame++)
            game.Tick(Frame);
        // Snapshot 4 is the scene as frame 50 ended, as it all comes down: played on to frame 180 as the first was.
        var restored = timeline.Restore(4);
        var replay = new Game(restored, new InputState());
        for (var frame = 0; frame < 130; frame++)
            replay.Tick(Frame);

        var (expected, actual) = (Snapshot(scene), Snapshot(restored));
        Assert.Equal(expected.Names, actual.Names);
        Assert.Equal(expected.Bodies, actual.Bodies);
    }

    [Fact]
    public void In_a_network_game_the_host_breaks_things_and_players_see_the_pieces()
    {
        static Scene Networked()
        {
            var scene = Tower();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>().Player = "";
            scene.Add(settings);
            return scene;
        }

        var world = new NetWorld(Networked);
        world.Run(3);
        var (host, client) = (world.Host.Scene, world.Client.Scene);
        Assert.Contains(host.Entities, e => e.Name == "Plank Piece");
        Assert.Equal(host.Entities.Count(e => e.GetComponent<Breakable>() is not null), client.Entities.Count(e => e.GetComponent<Breakable>() is not null));
        Assert.Equal(host.Entities.Count(e => e.Name == "Plank Piece"), client.Entities.Count(e => e.Name == "Plank Piece"));
        var damage = host.Entities.Select(e => e.GetComponent<Breakable>()).OfType<Breakable>().Sum(b => b.Damage);
        Assert.Equal(damage, client.Entities.Select(e => e.GetComponent<Breakable>()).OfType<Breakable>().Sum(b => b.Damage), 3);
    }

    [Fact]
    public void Knocks_that_dont_break_anything_make_no_garbage()
    {
        Floor(_scene);
        for (var i = 0; i < 6; i++)
        {
            var ball = new Entity("Ball");
            ball.Transform.Position = new Vector3(i * 1.5f - 4, 0.5f + i * 0.3f, 0);
            ball.AddComponent<CircleCollider>().Radius = 0.4f;
            var body = ball.AddComponent<Body>();
            (body.Rotates, body.Bounciness, body.RollingResistance) = (true, 0.9f, 0);
            var breakable = ball.AddComponent<Breakable>();
            (breakable.Strength, breakable.MinHit) = (1e9f, 0.5f);
            _scene.Add(ball);
        }

        Run(0.3f);
        var perStep = Allocations.PerCall(() => _game.Tick(Frame), 30);
        Assert.True(_scene.Entities.Sum(e => e.GetComponent<Breakable>()?.Damage ?? 0) > 0);
        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 obj) => 0;
    }
}
