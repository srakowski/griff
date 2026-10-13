using System.Numerics;
using Griff.Input;
using Griff.Networking;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Networking;

/// <summary>Multiplayer for each kind of game: what each needs from the network, working across machines.</summary>
public sealed class MultiplayerGenreTests
{
    private static List<Entity> Named(Game game, string name) => game.Scene.Entities.Where(e => e.Name == name).ToList();

    private static Entity Settings(Scene scene, Action<Multiplayer>? set = null)
    {
        var entity = new Entity("Game");
        var settings = entity.AddComponent<Multiplayer>();
        set?.Invoke(settings);
        scene.Add(entity);
        return entity;
    }

    // ----- Arcade and co-op platformers -----

    private static Scene Platformer()
    {
        var scene = new Scene();
        Settings(scene);
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -1, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
        scene.Add(floor);

        var player = new Entity("Player");
        player.Transform.Position = new Vector3(0, 0.1f, 0);
        player.AddComponent<Sprite>();
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<PlatformerMovement>();
        var health = player.AddComponent<Health>();
        health.Max = 3;
        health.Current = 3;
        health.Respawn = true;
        health.RespawnDelay = 0.2f;
        health.InvincibleTime = 0;
        scene.Add(player);

        var spikes = new Entity("Spikes");
        spikes.Transform.Position = new Vector3(20, 0, 0);
        spikes.AddComponent<Sprite>();
        spikes.AddComponent<BoxCollider>().IsTrigger = true;
        spikes.AddComponent<DamageOnTouch>().Kill = true;
        scene.Add(spikes);
        return scene;
    }

    [Fact]
    public void A_player_jumps_on_their_own_machine_at_once_and_the_host_sees_them_land()
    {
        var world = new NetWorld(Platformer);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        world.Run(0.5);
        var ground = mine.Transform.Position.Y;

        NetWorld.Keys(world.Client).SetKey(Key.Space, true);
        world.Client.Tick(NetWorld.Frame);
        ((InputState)world.Client.Input).EndFrame();
        world.Client.Tick(NetWorld.Frame);
        Assert.True(mine.Transform.Position.Y > ground, "the jump starts on the player's own machine straight away");
        NetWorld.Keys(world.Client).SetKey(Key.Space, false);

        var highest = 0f;
        var hostCopy = world.Host.Scene.Find(mine.Id)!;
        for (var i = 0; i < 60; i++)
        {
            world.Run(NetWorld.Frame);
            highest = Math.Max(highest, hostCopy.Transform.Position.Y - ground);
        }

        world.Run(1);
        Assert.True(highest > 1, "the host saw the jump");
        Assert.Equal(ground, hostCopy.Transform.Position.Y, 2);
    }

    [Fact]
    public void Spikes_on_the_host_kill_a_player_and_they_respawn_where_they_started_on_their_own_machine()
    {
        var world = new NetWorld(Platformer);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        var home = mine.Transform.Position;

        // Walk into the spikes: their machine moves them, the host's spikes kill them.
        var hostCopy = world.Host.Scene.Find(mine.Id)!;
        var died = false;
        hostCopy.GetComponent<Health>()!.Died += () => died = true;
        mine.Transform.Position = new Vector3(20, home.Y, 0);
        world.Run(0.3);
        Assert.True(died);

        world.Run(1);
        Assert.True(hostCopy.IsEnabled);
        Assert.Equal(3, hostCopy.GetComponent<Health>()!.Current);
        Assert.InRange(Vector2.Distance(new Vector2(mine.Transform.Position.X, mine.Transform.Position.Y), new Vector2(home.X, home.Y)), 0, 0.5f);
        Assert.Equal(3, mine.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void The_host_can_still_push_a_player_around()
    {
        var world = new NetWorld(Platformer);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        var hostCopy = world.Host.Scene.Find(mine.Id)!;
        hostCopy.Transform.Position = new Vector3(-10, 3, 0);
        world.Run(0.4);
        Assert.Equal(-10, mine.Transform.Position.X, 1);
    }

    // ----- Shooters: versus, teams, scores -----

    private static Entity Bullet()
    {
        var bullet = new Entity("Bullet");
        bullet.AddComponent<Sprite>();
        bullet.AddComponent<CircleCollider>().IsTrigger = true;
        var body = bullet.AddComponent<Body>();
        body.GravityScale = 0;
        var damage = bullet.AddComponent<DamageOnTouch>();
        damage.Layers = "Player";
        damage.DestroyOnHit = true;
        bullet.AddComponent<Lifetime>().Seconds = 2;
        return bullet;
    }

    private static Func<Scene> Arena(string? teams = null) => () =>
    {
        var scene = new Scene();
        Settings(scene, m => m.Teams = teams);
        var player = new Entity("Player");
        player.AddComponent<Sprite>();
        player.AddComponent<CircleCollider>().Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<TopDownMovement>();
        var health = player.AddComponent<Health>();
        health.Max = 1;
        health.Current = 1;
        var gun = player.AddComponent<Shooter>();
        gun.Action = "Fire";
        gun.Prefab = "Bullet";
        gun.Speed = 20;
        gun.Interval = 0.2f;
        scene.Add(player);
        return scene;
    };

    private static void Shoot(NetWorld world, Game shooter, Entity at, double seconds = 0.6)
    {
        var keys = NetWorld.Keys(shooter);
        keys.MouseWorld = new Vector2(at.Transform.Position.X, at.Transform.Position.Y);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(seconds);
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(0.3);
    }

    [Fact]
    public void A_players_shot_runs_on_the_host_hurts_another_player_and_scores_for_them()
    {
        var world = new NetWorld(Arena(), setUp: g => g.PrefabLoader = _ => Bullet());
        var hostAvatar = world.Host.Net.LocalPlayer!.Avatar!;
        var guest = world.Host.Net.Players.Single(p => !p.IsLocal);
        Shoot(world, world.Client, world.Client.Scene.Find(hostAvatar.Id)!);

        Assert.True(hostAvatar.Scene is null || hostAvatar.GetComponent<Health>()!.IsDead);
        Assert.Equal(1, guest.Score);
        Assert.Equal(1, world.Client.Net.LocalPlayer!.Score);
        // Their own shots never hurt them.
        Assert.False(world.Client.Net.LocalPlayer!.Avatar!.GetComponent<Health>()!.IsDead);
    }

    [Fact]
    public void Teammates_cant_hurt_each_other()
    {
        var world = new NetWorld(Arena("Blue, Red"), players: 2, setUp: g => g.PrefabLoader = _ => Bullet());
        var players = world.Host.Net.Players;
        Assert.Equal(["Blue", "Red", "Blue"], players.Select(p => p.Team));
        var hostAvatar = world.Host.Net.LocalPlayer!.Avatar!;
        var blueGuest = world.Clients[1];

        // Out of the line of fire.
        world.Clients[0].Net.LocalPlayer!.Avatar!.Transform.Position = new Vector3(0, 6, 0);
        world.Run(0.3);
        Shoot(world, blueGuest, blueGuest.Scene.Find(hostAvatar.Id)!);
        Assert.False(hostAvatar.GetComponent<Health>()!.IsDead);

        Shoot(world, world.Clients[0], world.Clients[0].Scene.Find(hostAvatar.Id)!);
        Assert.True(hostAvatar.Scene is null || hostAvatar.GetComponent<Health>()!.IsDead);
        Assert.NotEqual(players[0].Color, players[1].Color);
        Assert.Equal(players[0].Color, players[2].Color);
    }

    // ----- First person -----

    private static Scene Corridor()
    {
        var scene = new Scene();
        Settings(scene);
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        camera.AddComponent<FirstPersonView>().Viewer = "Player";
        scene.Add(camera);

        var player = new Entity("Player");
        player.AddComponent<Billboard>();
        player.AddComponent<Sprite>();
        player.AddComponent<CircleCollider>().Radius = 0.3f;
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<FirstPersonMovement>().MouseLook = false;
        var gun = player.AddComponent<Weapon>();
        gun.Damage = 1;
        gun.Spread = 0;
        scene.Add(player);

        var dummy = new Entity("Dummy");
        dummy.Transform.Position = new Vector3(10, 5, 0);
        dummy.AddComponent<Sprite>();
        dummy.AddComponent<CircleCollider>().Radius = 0.5f;
        var health = dummy.AddComponent<Health>();
        health.Max = 5;
        health.Current = 5;
        health.InvincibleTime = 0;
        scene.Add(dummy);
        return scene;
    }

    [Fact]
    public void In_first_person_a_player_turns_on_their_own_machine_and_the_host_fires_where_they_look()
    {
        var world = new NetWorld(Corridor);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        var dummy = Named(world.Host, "Dummy")[0];
        var at = mine.Transform.Position;
        var angle = MathF.Atan2(5 - at.Y, 10 - at.X) * 180 / MathF.PI;
        mine.Transform.Rotation = new Vector3(0, 0, angle);
        world.Run(0.3);
        Assert.Equal(mine, world.Client.FirstPersonViewer);

        NetWorld.Keys(world.Client).SetMouseButton(MouseButton.Left, true);
        world.Run(0.1);
        NetWorld.Keys(world.Client).SetMouseButton(MouseButton.Left, false);
        world.Run(0.3);
        Assert.Equal(4, dummy.GetComponent<Health>()!.Current);
        Assert.Equal(4, Named(world.Client, "Dummy")[0].GetComponent<Health>()!.Current);
    }

    // ----- Sandbox: digging and building -----

    private static Scene Mine()
    {
        var scene = new Scene();
        Settings(scene);
        var world = new Entity("World");
        var map = world.AddComponent<Tilemap>();
        map.Tiles = ["", "", "1111111111", "1111111111", "9999999999"];
        var blocks = world.AddComponent<Blocks>();
        blocks.Items = ["Dirt 1"];
        blocks.Unbreakable = "9";
        blocks.MineTime = 0.2f;
        scene.Add(world);

        var player = new Entity("Player");
        player.Transform.Position = new Vector3(2, -1.2f, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1.6f);
        player.AddComponent<Body>();
        player.AddComponent<Inventory>();
        player.AddComponent<BlockBuilder>();
        scene.Add(player);
        return scene;
    }

    [Fact]
    public void A_player_digs_with_their_mouse_and_the_block_goes_for_everyone()
    {
        var world = new NetWorld(Mine);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        world.Run(0.5);
        var x = (int)MathF.Floor(mine.Transform.Position.X);
        var keys = NetWorld.Keys(world.Client);
        keys.MouseWorld = new Vector2(x + 0.5f, -2.5f);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(0.8);
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(0.3);

        var hostMap = Named(world.Host, "World")[0].GetComponent<Tilemap>()!;
        Assert.Equal(-1, hostMap.GetTile(x, 2));
        Assert.Equal(-1, Named(world.Client, "World")[0].GetComponent<Tilemap>()!.GetTile(x, 2));
        Assert.Equal(1, world.Host.Scene.Find(mine.Id)!.GetComponent<Inventory>()!.Count("Dirt"));
        Assert.Equal(1, mine.GetComponent<Inventory>()!.Count("Dirt"));
        // The host's own player didn't dig anything.
        Assert.Equal(0, world.Host.Net.LocalPlayer!.Avatar!.GetComponent<Inventory>()!.Count("Dirt"));
    }

    // ----- Strategy -----

    private static Scene Battlefield()
    {
        var scene = new Scene();
        Settings(scene, m => (m.Player, m.Teams) = ("", "Player, Enemy"));
        foreach (var team in new[] { "Player", "Enemy" })
        {
            var faction = new Entity(team + " Team");
            faction.AddComponent<Faction>().Team = team;
            faction.AddComponent<Opponent>();
            scene.Add(faction);
        }

        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 10;
        camera.AddComponent<Commander>().Hud = false;
        scene.Add(camera);

        foreach (var (team, x) in new[] { ("Player", -5f), ("Enemy", 5f) })
        {
            var hall = new Entity("Hall");
            hall.Transform.Position = new Vector3(x, 6, 0);
            hall.AddComponent<Sprite>();
            var building = hall.AddComponent<Building>();
            building.Team = team;
            building.Size = new Vector2(2, 2);
            scene.Add(hall);

            var soldier = new Entity("Soldier");
            soldier.Transform.Position = new Vector3(x, 0, 0);
            soldier.AddComponent<Sprite>();
            soldier.AddComponent<Unit>().Team = team;
            scene.Add(soldier);
        }

        return scene;
    }

    [Fact]
    public void In_a_strategy_game_each_player_commands_their_own_team_and_the_host_carries_out_their_orders()
    {
        var world = new NetWorld(Battlefield);
        var client = world.Client;
        Assert.Equal("Enemy", client.Net.LocalPlayer!.Team);
        Assert.Equal("Enemy", Named(client, "Main Camera")[0].GetComponent<Commander>()!.Team);

        var theirs = Named(client, "Soldier").Single(e => e.GetComponent<Unit>()!.Team == "Enemy");
        var mineOnHost = world.Host.Scene.Find(theirs.Id)!;
        client.Select([theirs]);
        var keys = NetWorld.Keys(client);
        keys.MouseWorld = new Vector2(5, -4);
        keys.SetMouseButton(MouseButton.Right, true);
        world.Run(NetWorld.Frame * 2);
        keys.SetMouseButton(MouseButton.Right, false);
        world.Run(4);

        Assert.InRange(Vector2.Distance(new Vector2(mineOnHost.Transform.Position.X, mineOnHost.Transform.Position.Y), new Vector2(5, -4)), 0, 0.8f);
        Assert.InRange(Vector2.Distance(new Vector2(theirs.Transform.Position.X, theirs.Transform.Position.Y), new Vector2(5, -4)), 0, 0.8f);

        // A player can't order the other team's units about.
        var hosts = Named(client, "Soldier").Single(e => e.GetComponent<Unit>()!.Team == "Player");
        var before = world.Host.Scene.Find(hosts.Id)!.Transform.Position;
        client.Select([hosts]);
        keys.MouseWorld = new Vector2(-5, -6);
        keys.SetMouseButton(MouseButton.Right, true);
        world.Run(NetWorld.Frame * 2);
        keys.SetMouseButton(MouseButton.Right, false);
        world.Run(2);
        Assert.Equal(before, world.Host.Scene.Find(hosts.Id)!.Transform.Position);
    }

    // ----- Big worlds (MMOs) -----

    private static Scene BigWorld()
    {
        var scene = new Scene();
        Settings(scene, m => m.ViewDistance = 15);
        var player = new Entity("Player");
        player.AddComponent<Sprite>();
        player.AddComponent<CircleCollider>();
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<TopDownMovement>();
        scene.Add(player);

        var tree = new Entity("Far Tree");
        tree.Transform.Position = new Vector3(100, 0, 0);
        tree.AddComponent<Sprite>();
        scene.Add(tree);

        var keeper = new Entity("Score Keeper");
        keeper.Transform.Position = new Vector3(500, 0, 0);
        keeper.AddComponent<Counter>();
        scene.Add(keeper);
        return scene;
    }

    [Fact]
    public void In_a_big_world_players_are_only_sent_what_is_near_them()
    {
        var world = new NetWorld(BigWorld);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        Assert.Empty(Named(world.Client, "Far Tree"));
        Assert.Single(Named(world.Client, "Score Keeper"));

        mine.Transform.Position = new Vector3(95, 0, 0);
        world.Run(0.5);
        Assert.Single(Named(world.Client, "Far Tree"));

        mine.Transform.Position = Vector3.Zero;
        world.Run(0.5);
        Assert.Empty(Named(world.Client, "Far Tree"));
    }

    [Fact]
    public void Remembered_players_come_back_as_they_left()
    {
        static Scene Remembering()
        {
            var scene = BigWorld();
            scene.Roots[0].GetComponent<Multiplayer>()!.RememberPlayers = true;
            return scene;
        }

        var world = new NetWorld(Remembering);
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        mine.Transform.Position = new Vector3(42, 7, 0);
        world.Run(0.5);
        world.Client.Net.Leave();
        world.Run(0.5);
        Assert.Single(Named(world.Host, "Player"));

        var again = world.AddClient(Remembering, name: "Guest1");
        world.Run(0.8);
        var back = again.Net.LocalPlayer!.Avatar!;
        Assert.Equal(new Vector3(42, 7, 0), back.Transform.Position);
    }

    // ----- Drag and drop -----

    private static Scene Table()
    {
        var scene = new Scene();
        Settings(scene);
        foreach (var (name, x) in new[] { ("Ace", -3f), ("King", 3f) })
        {
            var card = new Entity(name);
            card.Transform.Position = new Vector3(x, 0, 0);
            card.AddComponent<Sprite>();
            card.AddComponent<Draggable>().SlideTime = 0;
            scene.Add(card);
        }

        var slot = new Entity("Slot");
        slot.Transform.Position = new Vector3(0, 4, 0);
        slot.AddComponent<Sprite>().Size = new Vector2(1.5f);
        slot.AddComponent<DropTarget>();
        scene.Add(slot);
        return scene;
    }

    [Fact]
    public void A_player_drags_a_card_with_their_own_mouse_and_everyone_sees_it_land()
    {
        var world = new NetWorld(Table);
        var keys = NetWorld.Keys(world.Client);
        keys.MouseWorld = new Vector2(3, 0);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(0.2);
        foreach (var y in new[] { 1f, 2f, 3f, 4f })
        {
            keys.MouseWorld = new Vector2(3 - 0.75f * y, y);
            world.Run(NetWorld.Frame * 3);
        }

        Assert.True(Named(world.Host, "King").Single().GetComponent<Draggable>()!.IsHeld, "the host carries it for them");
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(0.5);

        var king = Named(world.Host, "King").Single();
        Assert.Equal(new Vector3(0, 4, 0), king.Transform.Position);
        Assert.Equal([king], Named(world.Host, "Slot").Single().GetComponent<DropTarget>()!.Contents);
        Assert.Equal(new Vector3(0, 4, 0), Named(world.Client, "King").Single().Transform.Position);
        Assert.Equal(new Vector3(-3, 0, 0), Named(world.Client, "Ace").Single().Transform.Position);
    }

    [Fact]
    public void A_player_carries_a_card_with_their_own_grabber_and_puts_it_on_a_slot()
    {
        var world = new NetWorld(() =>
        {
            var scene = Table();
            var player = new Entity("Player");
            player.Transform.Position = new Vector3(0, -4, 0);
            player.AddComponent<Grabber>().Reach = 0.5f;
            scene.Add(player);
            return scene;
        });
        world.Run(0.3);
        // Their character, as the host runs it (it has no movement of its own, so the host places it).
        var theirs = world.Host.Scene.Find(world.Client.Net.LocalPlayer!.Avatar!.Id)!.GetComponent<Grabber>()!;
        var keys = NetWorld.Keys(world.Client);
        void Tap()
        {
            keys.SetKey(Key.E, true);
            world.Run(NetWorld.Frame * 3);
            keys.SetKey(Key.E, false);
            world.Run(0.2);
        }

        theirs.Entity!.Transform.Position = new Vector3(3, 0, 0);
        Tap();
        Assert.Equal("King", theirs.Holding?.Name);
        Assert.Null(world.Host.Net.LocalPlayer!.Avatar!.GetComponent<Grabber>()!.Holding);

        theirs.Entity.Transform.Position = new Vector3(0, 4, 0);
        world.Run(0.1);
        Tap();
        var king = Named(world.Host, "King").Single();
        Assert.Null(theirs.Holding);
        Assert.Equal([king], Named(world.Host, "Slot").Single().GetComponent<DropTarget>()!.Contents);
        Assert.Equal(new Vector3(0, 4, 0), Named(world.Client, "King").Single().Transform.Position);
    }

    // ----- Every kind of game -----

    [Fact]
    public void When_the_host_changes_scene_everyone_follows()
    {
        static Scene Level(string name)
        {
            var scene = BigWorld();
            scene.Roots[0].Name = name;
            return scene;
        }

        var world = new NetWorld(() => Level("Level1"), setUp: g => g.SceneLoader = name => Level(name));
        world.Host.LoadScene("Level2");
        world.Run(1);
        Assert.Equal("Level2", world.Host.SceneName);
        Assert.Equal("Level2", world.Client.SceneName);
        Assert.Single(Named(world.Client, "Level2"));
        Assert.Equal(2, Named(world.Client, "Player").Count);
        Assert.NotNull(world.Client.Net.LocalPlayer!.Avatar);
    }

    [Fact]
    public void Messages_reach_every_machine_and_say_who_sent_them()
    {
        var world = new NetWorld(BigWorld, players: 2);
        var heard = new List<string>();
        foreach (var game in new[] { world.Host, world.Clients[0], world.Clients[1] })
            game.On("Chat", data => heard.Add($"{game.Net.LocalPlayer!.Name} heard {data} from {game.Net.Sender?.Name}"));
        world.Clients[0].Net.Send("Chat", "gg");
        world.Run(0.3);
        Assert.Equal(3, heard.Count);
        Assert.All(heard, h => Assert.EndsWith("heard gg from Guest1", h));

        // What the host's game says (a Counter's message, a death), everyone hears.
        var counted = 0;
        world.Clients[1].On("Goal", _ => counted++);
        world.Host.Send("Goal");
        world.Run(0.3);
        Assert.Equal(1, counted);
    }

    [Fact]
    public void A_dedicated_server_runs_the_game_without_playing_in_it()
    {
        var world = new NetWorld(BigWorld, players: 2, dedicated: true);
        Assert.Equal(NetRole.Server, world.Host.Net.Role);
        Assert.Null(world.Host.Net.LocalPlayer);
        Assert.Equal(["Guest1", "Guest2"], world.Host.Net.Players.Select(p => p.Name));
        Assert.Equal(2, Named(world.Host, "Player").Count);
        Assert.Equal(2, Named(world.Clients[1], "Player").Count);
    }

    [Fact]
    public void A_bad_connection_is_slower_but_everything_still_gets_there()
    {
        var world = new NetWorld(BigWorld);
        var bad = new NetConditions { Latency = 0.08, Jitter = 0.04, Loss = 0.2 };
        world.Host.Net.FakeLag = bad;
        world.Client.Net.FakeLag = bad;
        var mine = world.Client.Net.LocalPlayer!.Avatar!;
        NetWorld.Keys(world.Client).SetKey(Key.D, true);
        world.Run(1);
        NetWorld.Keys(world.Client).SetKey(Key.D, false);
        world.Run(2);
        Assert.Equal(mine.Transform.Position.X, world.Host.Scene.Find(mine.Id)!.Transform.Position.X, 2);

        var heard = 0;
        world.Client.On("Goal", _ => heard++);
        world.Host.Send("Goal");
        world.Run(2);
        Assert.Equal(1, heard);
    }

    [Fact]
    public void A_full_game_turns_players_away()
    {
        static Scene Small()
        {
            var scene = BigWorld();
            scene.Roots[0].GetComponent<Multiplayer>()!.MaxPlayers = 2;
            return scene;
        }

        var world = new NetWorld(Small);
        var late = world.AddClient(Small);
        string? why = null;
        late.Net.Ended += reason => why = reason;
        world.Run(0.5);
        Assert.Equal(NetRole.Offline, late.Net.Role);
        Assert.Contains("full", why);
        Assert.Contains("full", late.Net.Status);
    }
}
