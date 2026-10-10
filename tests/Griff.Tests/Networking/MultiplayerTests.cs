using System.Net;
using System.Numerics;
using Griff.Networking;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Networking;

/// <summary>A host and players in one process, on a pretend network, ticking together.</summary>
internal sealed class NetWorld
{
    public const double Frame = 1 / 60.0;
    private static readonly IPEndPoint HostAddress = new(IPAddress.Loopback, 7777);

    public MemoryNetwork Network { get; } = new();
    public Game Host { get; }
    public List<Game> Clients { get; } = [];

    private readonly string _sceneName;

    public NetWorld(Func<Scene> scene, int players = 1, Action<Game>? setUp = null, bool dedicated = false, string sceneName = "Arena")
    {
        _sceneName = sceneName;
        Host = new Game(scene(), new InputState()) { SceneName = sceneName };
        setUp?.Invoke(Host);
        Host.Net.PlayerName = "Hosty";
        Host.Net.Host(Network.Open(7777), dedicated);
        for (var i = 0; i < players; i++)
            AddClient(scene, setUp);
        Run(0.5);
    }

    public Game Client => Clients[0];

    public Game AddClient(Func<Scene> scene, Action<Game>? setUp = null, string? name = null)
    {
        var client = new Game(scene(), new InputState()) { SceneName = _sceneName };
        setUp?.Invoke(client);
        client.Net.PlayerName = name ?? $"Guest{Clients.Count + 1}";
        client.Net.Join(Network.Open(), HostAddress);
        Clients.Add(client);
        return client;
    }

    public void Run(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += Frame)
        {
            Tick(Host);
            foreach (var client in Clients)
                Tick(client);
        }
    }

    private static void Tick(Game game)
    {
        game.Tick(Frame);
        ((InputState)game.Input).EndFrame();
    }

    public static InputState Keys(Game game) => (InputState)game.Input;
}

public sealed class MultiplayerTests
{
    private static Scene Arena()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        settings.AddComponent<Multiplayer>();
        scene.Add(settings);

        var player = new Entity("Player");
        player.AddComponent<Sprite>();
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        var move = player.AddComponent<TopDownMovement>();
        move.Speed = 5;
        move.Acceleration = 0;
        move.Deceleration = 0;
        player.AddComponent<Health>().Max = 3;
        scene.Add(player);

        var coin = new Entity("Coin");
        coin.Transform.Position = new Vector3(8, 4, 0);
        coin.AddComponent<Sprite>();
        coin.AddComponent<CircleCollider>().IsTrigger = true;
        coin.AddComponent<Pickup>().Message = "Coin";
        scene.Add(coin);

        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        camera.AddComponent<Follow>().Smoothing = 0;
        scene.Add(camera);

        var hud = new Entity("Coins");
        hud.AddComponent<Text>();
        hud.AddComponent<ScreenAnchor>();
        hud.AddComponent<Counter>().Message = "Coin";
        scene.Add(hud);
        return scene;
    }

    private static List<Entity> Named(Game game, string name) => game.Scene.Entities.Where(e => e.Name == name).ToList();

    [Fact]
    public void A_player_joins_and_both_machines_see_both_characters()
    {
        var world = new NetWorld(Arena);
        var host = world.Host;
        var client = world.Client;

        Assert.Equal(NetRole.Host, host.Net.Role);
        Assert.Equal(NetRole.Client, client.Net.Role);
        Assert.Equal(["Hosty", "Guest1"], host.Net.Players.Select(p => p.Name));
        Assert.Equal(["Hosty", "Guest1"], client.Net.Players.Select(p => p.Name));
        Assert.Equal(2, Named(host, "Player").Count);
        Assert.Equal(2, Named(client, "Player").Count);
        Assert.Single(Named(client, "Coin"));
        Assert.Single(Named(client, "Main Camera"));

        // Each machine's camera follows its own player's character.
        var mine = client.Net.LocalPlayer!.Avatar;
        Assert.NotNull(mine);
        Assert.True(client.Net.IsMine(mine));
        Assert.Equal(mine, client.Find("Player"));
        Assert.Equal(host.Net.LocalPlayer!.Avatar, host.Find("Player"));
        Assert.NotEqual(Named(host, "Player")[0].Transform.Position, Named(host, "Player")[1].Transform.Position);
    }

    [Fact]
    public void A_player_moves_their_own_character_and_the_host_sees_it()
    {
        var world = new NetWorld(Arena);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var start = mine.Transform.Position.X;
        var hostCopy = world.Host.Scene.Find(mine.Id)!;
        var hostOwn = world.Host.Net.LocalPlayer!.Avatar!;
        var hostOwnStart = hostOwn.Transform.Position;

        NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(1);
        NetWorld.Keys(client).SetKey(Key.D, false);
        world.Run(0.3);

        Assert.InRange(mine.Transform.Position.X - start, 4, 6);
        Assert.Equal(mine.Transform.Position.X, hostCopy.Transform.Position.X, 3);
        Assert.Equal(hostOwnStart, hostOwn.Transform.Position);

        // And the host's own character, moved on the host, glides on the player's screen.
        NetWorld.Keys(world.Host).SetKey(Key.W, true);
        world.Run(1);
        NetWorld.Keys(world.Host).SetKey(Key.W, false);
        world.Run(0.5);
        var seen = client.Scene.Find(hostOwn.Id)!;
        Assert.InRange(hostOwn.Transform.Position.Y - hostOwnStart.Y, 4, 6);
        Assert.Equal(hostOwn.Transform.Position.Y, seen.Transform.Position.Y, 3);
    }

    [Fact]
    public void What_a_player_picks_up_counts_on_the_host_and_everyone_sees_it_go()
    {
        var world = new NetWorld(Arena);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        mine.Transform.Position = new Vector3(8, 4, 0);
        world.Run(0.5);

        Assert.Empty(Named(world.Host, "Coin"));
        Assert.Empty(Named(client, "Coin"));
        Assert.Equal(1, Named(world.Host, "Coins")[0].GetComponent<Counter>()!.Value);
        Assert.Equal(1, Named(client, "Coins")[0].GetComponent<Counter>()!.Value);
    }

    [Fact]
    public void Leaving_takes_the_character_away()
    {
        var world = new NetWorld(Arena);
        var left = new List<string>();
        world.Host.Net.PlayerLeft += p => left.Add(p.Name);
        world.Client.Net.Leave();
        world.Run(0.5);
        Assert.Equal(["Guest1"], left);
        Assert.Single(world.Host.Net.Players);
        Assert.Single(Named(world.Host, "Player"));
        Assert.Equal(NetRole.Offline, world.Client.Net.Role);
    }

    [Fact]
    public void Joining_a_game_already_running_with_uneven_frames_still_keeps_everyone_in_step()
    {
        // As in the editor: both machines' scenes are copies of one, with the same ids, and the host was already playing.
        var serializer = new Griff.Scenes.Serialization.SceneSerializer(Griff.Scenes.Serialization.ComponentRegistry.CreateDefault());
        var original = serializer.SaveToString(Arena());
        Scene Make() => serializer.LoadFromString(original);
        var failures = new List<string>();
        for (var run = 0; run < 60; run++)
        {
            var random = new Random(run);
            var network = new MemoryNetwork();
            var hostKeys = new InputState();
            var host = new Game(Make(), hostKeys) { SceneName = "Arena", SceneLoader = _ => Make() };
            void Tick(Game g) { g.Tick(random.NextDouble() * 0.05 + 0.002); ((InputState)g.Input).EndFrame(); }
            for (var i = 0; i < random.Next(1, 60); i++) Tick(host);
            host.Net.Host(network.Open(7777));
            var client = new Game(Make(), new InputState()) { SceneName = "Arena", SceneLoader = _ => Make() };
            client.Net.Join(network.Open(), new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
            void Both(int frames)
            {
                for (var i = 0; i < frames; i++)
                {
                    if (random.Next(2) == 0) { Tick(host); Tick(client); }
                    else { Tick(client); Tick(host); if (random.Next(4) == 0) Tick(host); }
                }
            }
            Both(random.Next(20, 120));
            hostKeys.SetKey(Key.W, true);
            Both(60);
            hostKeys.SetKey(Key.W, false);
            Both(60);
            var hostAvatar = host.Net.LocalPlayer!.Avatar!;
            var seen = client.Scene.Find(hostAvatar.Id);
            if (seen is null || Vector3.Distance(seen.Transform.Position, hostAvatar.Transform.Position) > 0.2f)
                failures.Add($"run {run}: host {hostAvatar.Transform.Position} seen {seen?.Transform.Position} :: {client.Net.Describe()}");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
