using System.Numerics;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Slingshots: shots loaded from inside them (and then from a prefab) and held in the pouch and a row behind, pulled back
/// with the mouse or the keys and let go to fly off the other way as fast as they were pulled, the path shown as it'll be
/// flown, the next loaded once the last has come to rest, out of shots said; its actions, the camera following, focus,
/// turns, rewinds, network games and garbage.
/// </summary>
public sealed class SlingshotTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _heard = [];

    public SlingshotTests()
    {
        _game = new Game(_scene, _input) { PrefabLoader = Shot };
        _game.On("Launched", _ => _heard.Add("Launched"));
        _game.On("OutOfShots", _ => _heard.Add("OutOfShots"));
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Step()
    {
        _game.Tick(Frame);
        _input.EndFrame();
    }

    private static Entity Shot(string name)
    {
        var shot = new Entity(name);
        shot.AddComponent<Sprite>().Size = new Vector2(0.6f);
        shot.AddComponent<CircleCollider>().Radius = 0.3f;
        shot.AddComponent<Body>().Rotates = true;
        return shot;
    }

    private static Entity Floor(Scene scene)
    {
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(200, 1);
        scene.Add(floor);
        return floor;
    }

    private static Slingshot AddSlingshot(Scene scene, int shots = 3, string? prefab = null)
    {
        var entity = new Entity("Slingshot");
        entity.Transform.Position = new Vector3(-8, 0, 0);
        var slingshot = entity.AddComponent<Slingshot>();
        (slingshot.Shot, slingshot.LaunchMessage, slingshot.OutOfShotsMessage) = (prefab, "Launched", "OutOfShots");
        scene.Add(entity);
        for (var i = 0; i < shots; i++)
            scene.Add(Shot($"Shot {i + 1}"), entity);
        return slingshot;
    }

    private Slingshot AddSlingshot(int shots = 3, string? prefab = null) => AddSlingshot(_scene, shots, prefab);

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // Presses on the pouch, drags it back `pull`, and lets go.
    private void Drag(Slingshot slingshot, Vector2 pull)
    {
        var pouch = slingshot.PouchInWorld;
        _input.MouseWorld = pouch + new Vector2(0.1f, 0);
        Step();
        _input.SetMouseButton(MouseButton.Left, true);
        Step();
        for (var i = 1; i <= 5; i++)
        {
            _input.MouseWorld = pouch + pull * i / 5;
            Step();
        }

        _input.SetMouseButton(MouseButton.Left, false);
        Step();
    }

    [Fact]
    public void Its_first_shot_is_loaded_in_the_pouch_and_the_rest_wait_in_a_row_behind_it()
    {
        Floor(_scene);
        var slingshot = AddSlingshot();
        Run(0.5f);
        var shots = slingshot.Entity!.Children.ToList();
        Assert.True(slingshot.Loaded);
        Assert.Same(shots[0], slingshot.LoadedShot);
        Assert.Equal(slingshot.PouchInWorld, At(shots[0]));
        Assert.All(shots, s => Assert.True(s.GetComponent<Body>()!.Frozen));
        Assert.True(Vector2.Distance(new Vector2(-8 - 0.6f - 0.3f, 0.3f), At(shots[1])) < 1e-4f);
        Assert.True(Vector2.Distance(new Vector2(-8 - 0.6f - 0.3f - 0.8f, 0.3f), At(shots[2])) < 1e-4f);
        Assert.Equal(3, slingshot.ShotsLeft);
    }

    [Fact]
    public void Pulled_back_and_let_go_a_shot_flies_the_other_way_as_fast_as_it_was_pulled()
    {
        Floor(_scene);
        var slingshot = AddSlingshot();
        Run(0.2f);
        var shot = slingshot.LoadedShot!;
        Drag(slingshot, new Vector2(-1, -0.5f));

        // Half its Max Pull back (1.12 of 2): out of the slingshot, flying up and right at that share of its Power.
        Assert.Null(shot.Parent);
        Assert.False(slingshot.Loaded);
        Assert.Equal(2, slingshot.ShotsLeft);
        Assert.Equal(["Launched"], _heard);
        var body = shot.GetComponent<Body>()!;
        Assert.False(body.Frozen);
        Assert.True(body.Velocity.X > 8 && body.Velocity.Y > 0, $"It flies at {body.Velocity}.");
        Assert.Equal(shot.Id.ToString(), slingshot.Flying);

        Run(2);
        Assert.True(At(shot).X > -4, $"It's at {At(shot)}.");
    }

    [Fact]
    public void The_dotted_line_is_where_the_shot_goes()
    {
        var slingshot = AddSlingshot();
        Run(0.2f);
        var pull = new Vector2(-1.6f, -1.2f);
        slingshot.Pull = pull;
        Step();
        Assert.True(slingshot.DotCount > 10);
        var dots = slingshot.Dots.Take(slingshot.DotCount).ToArray();
        Step();
        Assert.Equal(dots, slingshot.Dots.Take(slingshot.DotCount));

        // Launched (nothing in its way), it passes through each dot in turn: the second dot is 4 steps on (two a dot).
        var shot = slingshot.LoadedShot!;
        _game.Do(slingshot.Entity!, "Launch");
        var path = new List<Vector2>();
        for (var i = 0; i < 40; i++)
        {
            Step();
            path.Add(At(shot));
        }

        for (var i = 0; i < 10; i++)
            Assert.True(Vector2.Distance(dots[i], path[(i + 1) * 2 - 1]) < 1e-3f, $"Dot {i} at {dots[i]}, the shot at {path[(i + 1) * 2 - 1]}.");
    }

    [Fact]
    public void It_draws_its_frame_and_bands_with_nothing_else_on_it_and_the_dotted_line_while_its_pulled()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        _scene.Add(camera);
        var slingshot = AddSlingshot();
        Run(0.2f);
        var resting = Griff.Rendering.SceneRenderer.Build(_game, 16 / 9f).Sprites.Count(d => d.Entity == slingshot.Entity);
        Assert.Equal(6, resting);
        slingshot.Pull = new Vector2(-1.5f, -0.5f);
        Step();
        var pulled = Griff.Rendering.SceneRenderer.Build(_game, 16 / 9f).Sprites.Count(d => d.Entity == slingshot.Entity);
        Assert.Equal(6 + slingshot.DotCount, pulled);
        Assert.True(slingshot.DotCount > 10);
    }

    [Fact]
    public void A_short_pull_lets_it_back_without_launching()
    {
        var slingshot = AddSlingshot();
        Run(0.2f);
        Drag(slingshot, new Vector2(-0.15f, 0));
        Assert.True(slingshot.Loaded);
        Assert.Equal(Vector2.Zero, slingshot.Pull);
        Assert.Empty(_heard);
    }

    [Fact]
    public void The_keys_pull_it_about_and_jump_lets_go_or_dash_lets_it_back()
    {
        var slingshot = AddSlingshot();
        Run(0.2f);
        _input.SetKey(Key.Left, true);
        _input.SetKey(Key.Down, true);
        Run(0.3f);
        _input.SetKey(Key.Left, false);
        _input.SetKey(Key.Down, false);
        Step();
        Assert.True(slingshot.Pull.X < -0.5f && slingshot.Pull.Y < -0.5f, $"Pulled {slingshot.Pull}.");
        _input.SetKey(Key.LeftShift, true);
        Step();
        _input.SetKey(Key.LeftShift, false);
        Assert.Equal(Vector2.Zero, slingshot.Pull);
        Assert.True(slingshot.Loaded);

        _input.SetKey(Key.Left, true);
        Run(0.5f);
        _input.SetKey(Key.Left, false);
        _input.SetKey(Key.Space, true);
        Step();
        _input.SetKey(Key.Space, false);
        Assert.False(slingshot.Loaded);
        Assert.Equal(["Launched"], _heard);
    }

    [Fact]
    public void It_loads_the_next_once_the_last_has_come_to_rest_and_says_when_its_out()
    {
        Floor(_scene);
        var slingshot = AddSlingshot(shots: 2);
        slingshot.LoadDelay = 0.5f;
        Run(0.2f);
        for (var i = 0; i < 2; i++)
        {
            _game.Do(slingshot.Entity!, "Launch");
            Run(1);
            Assert.False(slingshot.Loaded);
            Run(11);
            Assert.Equal(i == 0, slingshot.Loaded);
        }

        Assert.Equal(0, slingshot.ShotsLeft);
        Assert.True(slingshot.OutOfShots);
        Assert.Equal(["Launched", "Launched", "OutOfShots"], _heard);
    }

    [Fact]
    public void After_the_shots_inside_it_it_launches_copies_of_its_prefab()
    {
        var slingshot = AddSlingshot(shots: 1, prefab: "Bird");
        slingshot.Shots = 2;
        Run(0.2f);
        Assert.Equal(3, slingshot.ShotsLeft);
        var names = new List<string>();
        slingshot.Launched += (_, shot) => names.Add(shot.Name);
        for (var i = 0; i < 4; i++)
        {
            _game.Do(slingshot.Entity!, "Launch");
            Run(12);
        }

        Assert.Equal(["Shot 1", "Bird", "Bird"], names);
        Assert.Equal((0, 2), (slingshot.ShotsLeft, slingshot.Made));
        Assert.True(slingshot.OutOfShots);
    }

    [Fact]
    public void Without_auto_load_it_loads_on_its_load_action()
    {
        var slingshot = AddSlingshot();
        slingshot.AutoLoad = false;
        Run(0.5f);
        Assert.False(slingshot.Loaded);
        _game.Do(slingshot.Entity!, "Load");
        Step();
        Assert.True(slingshot.Loaded);
    }

    [Fact]
    public void The_camera_follows_the_shot_in_flight_and_comes_back()
    {
        Floor(_scene);
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        var follow = camera.AddComponent<Follow>();
        follow.Target = "Slingshot";
        _scene.Add(camera);
        var slingshot = AddSlingshot();
        slingshot.LoadDelay = 0.2f;
        Run(0.2f);
        var shot = slingshot.LoadedShot!;
        _game.Do(slingshot.Entity!, "Launch");
        Run(0.5f);
        Assert.Same(shot, follow.TargetEntity);
        Run(10);
        Assert.Same(slingshot.Entity, follow.FindTarget());
    }

    [Fact]
    public void Without_focus_or_off_its_turn_it_ignores_the_mouse_and_a_launch_uses_an_action()
    {
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Sides, order.ComputerSides, order.Between) = ("Blue, Red", "", 0);
        _scene.Add(turns);
        var blue = new Entity("Blue Slingshot");
        blue.AddComponent<TurnTaker>().Side = "Blue";
        _scene.Add(blue);
        var slingshot = AddSlingshot();
        var taker = slingshot.Entity!.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = ("Red", 1);
        Run(0.3f);

        // Blue's turn: it won't be pulled.
        Drag(slingshot, new Vector2(-1.5f, -1));
        Assert.True(slingshot.Loaded);
        _game.Do(turns, "EndTurn");
        Run(0.3f);

        // Not Controllable: it won't either.
        slingshot.Controllable = false;
        Drag(slingshot, new Vector2(-1.5f, -1));
        Assert.True(slingshot.Loaded);
        _game.Do(slingshot.Entity!, "Focus");
        Step();
        Assert.True(slingshot.Controllable);

        // Red's turn and in focus: pulled and launched, which uses Red's only action and passes the turn.
        Drag(slingshot, new Vector2(-1.5f, -1));
        Assert.False(slingshot.Loaded);
        Assert.Equal(1, taker.ActionsUsed);
    }

    private void Create(string id)
    {
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == id);
        var entity = new Entity(preset.EntityName(_scene));
        preset.Configure(entity, _scene);
        var children = preset.CreateChildren(_scene).ToList();
        var edits = preset.EditScene(_scene).ToList();
        _scene.Add(entity);
        foreach (var child in children)
            _scene.Add(child, entity);
        foreach (var edit in edits)
            edit.Apply();
    }

    [Fact]
    public void Create_Slingshot_and_Create_Tower_make_a_level_to_play_straight_away()
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        _scene.Add(camera);
        Create("Entity.CreateSlingshot");
        Create("Entity.CreateTower");
        Assert.Equal(["Main Camera", "Slingshot", "Ground", "Tower"], _scene.Roots.Select(e => e.Name));
        Assert.Equal("Slingshot", camera.GetComponent<Follow>()!.Target);
        var score = camera.Children.Single(e => e.Name == "Score").GetComponent<Counter>()!;
        var tower = _scene.Roots.Single(e => e.Name == "Tower");
        Assert.Equal(8, tower.Children.Count(e => e.GetComponent<Breakable>() is not null));
        Assert.All(tower.Children, e => Assert.True(e.GetComponent<Body>()!.Asleep));

        // The tower stands until it's hit.
        Run(1);
        var slingshot = _scene.Roots.Single(e => e.Name == "Slingshot").GetComponent<Slingshot>()!;
        Assert.True(slingshot.Loaded);
        Assert.Equal(3, slingshot.ShotsLeft);
        Assert.All(tower.Children, e => Assert.Equal(0, e.GetComponent<Breakable>()!.Damage));

        // Pulled all the way back, a little down, the shot flies into the tower and knocks it about: something breaks.
        Drag(slingshot, new Vector2(-1.8f, -0.9f));
        Assert.False(slingshot.Loaded);
        Run(4);
        Assert.True(score.Value > 0, "Nothing broke.");
    }

    // ----- To the bit, rewinds, network games and garbage -----

    private static Scene Range()
    {
        var scene = new Scene();
        Floor(scene);
        AddSlingshot(scene);
        for (var i = 0; i < 4; i++)
        {
            var crate = new Entity("Crate");
            crate.Transform.Position = new Vector3(4, 0.5f + i * 1.01f, 0);
            crate.AddComponent<BoxCollider>().Size = Vector2.One;
            crate.AddComponent<Body>().Rotates = true;
            scene.Add(crate);
        }

        return scene;
    }

    private static Vector3[] Bodies(Scene scene) => scene.Entities.Where(e => e.GetComponent<Body>() is not null).Select(e => e.Transform.Position).ToArray();

    [Fact]
    public void A_launch_plays_out_the_same_to_the_last_bit_twice_and_across_a_rewind()
    {
        static Vector3[] Play(Scene scene)
        {
            var game = new Game(scene, new InputState());
            for (var i = 0; i < 200; i++)
            {
                if (i == 20)
                    scene.Entities.Single(e => e.Name == "Slingshot").GetComponent<Slingshot>()!.Pull = new Vector2(-2, -0.4f);
                if (i == 21)
                    game.Do(scene.Entities.Single(e => e.Name == "Slingshot"), "Launch");
                game.Tick(Frame);
            }

            return Bodies(scene);
        }

        Assert.Equal(Play(Range()), Play(Range()));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Range();
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        var slingshot = scene.Entities.Single(e => e.Name == "Slingshot");
        for (var frame = 0; frame < 60; frame++)
        {
            if (frame == 20)
                slingshot.GetComponent<Slingshot>()!.Pull = new Vector2(-2, -0.4f);
            if (frame == 21)
                game.Do(slingshot, "Launch");
            game.Tick(Frame);
            timeline.Record(scene, game.Time.Total);
        }

        for (var frame = 0; frame < 140; frame++)
            game.Tick(Frame);
        // Snapshot 4 is the scene as frame 50 ended, the shot in flight: played on to frame 200 as the first was.
        var restored = timeline.Restore(4);
        var replay = new Game(restored, new InputState());
        for (var frame = 0; frame < 150; frame++)
            replay.Tick(Frame);
        Assert.Equal(Bodies(scene), Bodies(restored));
        Assert.Equal(scene.Entities.Single(e => e.Name == "Slingshot").GetComponent<Slingshot>()!.ShotsLeft,
            restored.Entities.Single(e => e.Name == "Slingshot").GetComponent<Slingshot>()!.ShotsLeft);
    }

    [Fact]
    public void In_a_network_game_a_player_pulls_and_launches_through_the_host()
    {
        static Scene Networked()
        {
            var scene = Range();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>().Player = "";
            scene.Add(settings);
            return scene;
        }

        var world = new NetWorld(Networked);
        world.Run(1);
        Entity On(Game game, string name) => game.Scene.Entities.Single(e => e.Name == name);
        var (host, client) = (On(world.Host, "Slingshot").GetComponent<Slingshot>()!, On(world.Client, "Slingshot").GetComponent<Slingshot>()!);
        Assert.True(client.Loaded);

        var keys = NetWorld.Keys(world.Client);
        var pouch = client.PouchInWorld;
        keys.MouseWorld = pouch;
        world.Run(NetWorld.Frame);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(NetWorld.Frame);
        for (var i = 1; i <= 6; i++)
        {
            keys.MouseWorld = pouch + new Vector2(-1.8f, -0.6f) * i / 6;
            world.Run(NetWorld.Frame);
        }

        world.Run(0.2);
        Assert.True(host.Pull.X < -1.5f, $"The host has it pulled {host.Pull}.");
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(0.5);
        Assert.False(host.Loaded);
        Assert.Equal(2, host.ShotsLeft);
        Assert.Equal(2, client.ShotsLeft);
        var flying = world.Client.Scene.Entities.Single(e => e.Name == "Shot 1");
        Assert.True(At(flying).X > -6, $"The player sees it at {At(flying)}.");
    }

    [Fact]
    public void Pulling_and_flying_make_no_garbage()
    {
        Floor(_scene);
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        camera.AddComponent<Follow>().Target = "Slingshot";
        _scene.Add(camera);
        var slingshot = AddSlingshot();
        Run(0.3f);
        _input.SetKey(Key.Left, true);
        Run(0.2f);
        var pulling = Allocations.PerCall(() => _game.Tick(Frame), 20);
        _input.SetKey(Key.Left, false);
        _input.SetKey(Key.Space, true);
        Step();
        _input.SetKey(Key.Space, false);
        Run(0.2f);
        Assert.False(slingshot.Loaded);
        var flying = Allocations.PerCall(() => _game.Tick(Frame), 20);
        Assert.True(pulling == 0 && flying == 0, $"A step allocated {pulling:N0} bytes pulling, {flying:N0} flying.");
    }
}
