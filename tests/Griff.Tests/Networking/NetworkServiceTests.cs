using System.Numerics;
using Griff.Input;
using Griff.Networking;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Networking;

/// <summary>
/// Networking is a service of the game (INetwork, Game.Network): a game has a network session only while it's networked,
/// everything the service says has an answer offline too, and what runs for each other player on the host is handed that
/// player and their controls, while the game's own controls stay this machine's.
/// </summary>
public sealed class NetworkServiceTests
{
    private static Scene Arena()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        settings.AddComponent<Multiplayer>().Teams = "Red, Blue";
        scene.Add(settings);
        var player = new Entity("Player");
        player.AddComponent<Sprite>();
        player.AddComponent<Body>().GravityScale = 0;
        scene.Add(player);
        return scene;
    }

    [Fact]
    public void Offline_there_is_no_session_and_everything_still_answers()
    {
        var game = new Game(Arena(), new InputState());
        var heard = new List<object?>();
        game.On("Chat", heard.Add);
        object? ran = "not run";
        game.NetworkSystem.OnNetCommand("Test.Command", (player, args) => ran = args);

        Assert.Null(game.Network.Session);
        Assert.Same(game.Network, game.Services.Get<INetwork>());
        Assert.Equal(NetRole.Offline, game.Network.Role);
        Assert.False(game.Network.IsOnline || game.Network.IsHost || game.Network.IsClient || game.Network.IsConnecting);
        Assert.Empty(game.Network.Players);
        Assert.Empty(game.Network.RemotePlayers);
        Assert.Null(game.Network.LocalPlayer);
        Assert.True(game.Network.IsMine(game.Find("Player")));
        Assert.Equal(["Red", "Blue"], game.Network.TeamNames());

        game.Network.Send("Chat", "gg");
        game.Network.SendCommand("Test.Command", 5f);
        game.Tick(1 / 60.0);
        Assert.Equal(["gg"], heard);
        Assert.Equal(5f, ran);
    }

    [Fact]
    public void A_session_is_made_by_hosting_and_let_go_on_leaving_and_the_status_stays()
    {
        var network = new MemoryNetwork();
        var game = new Game(Arena(), new InputState()) { SceneName = "Arena" };
        game.Network.FakeLag = new NetConditions { Latency = 0.05 };

        game.Network.Host(network.Open(7777));
        var session = game.Network.Session;
        Assert.NotNull(session);
        Assert.True(game.Network.IsHost);
        Assert.Same(session.LocalPlayer, game.Network.LocalPlayer);
        Assert.Single(game.Network.Players);

        game.Network.Leave("Done for today.");
        Assert.Null(game.Network.Session);
        Assert.False(session.IsOnline);
        Assert.Equal("Done for today.", game.Network.Status);
        Assert.Empty(game.Network.Players);

        // Hosting again makes a new one.
        game.Network.Host(network.Open(7778));
        Assert.NotNull(game.Network.Session);
        Assert.NotSame(session, game.Network.Session);
    }

    [Fact]
    public void Hosting_and_joining_open_their_sockets_with_the_transport_the_host_hands()
    {
        // A platform with its own way to reach other machines hands its transport; here, a network inside the process.
        var network = new MemoryNetwork();
        var loaded = new LoadedGame(null, Griff.Scenes.Serialization.ComponentRegistry.CreateDefault(), Arena(), new ProblemLog()) { SceneName = "Arena" };
        var host = loaded.NewGame(new GameHostOptions { Transport = network });
        Assert.Same(network, host.Network.Transport);
        Assert.Same(UdpTransport.Instance, new Game(Arena(), new InputState()).Network.Transport);

        Assert.True(host.Network.Host(7777));
        var guest = (loaded with { Scene = Arena() }).NewGame(new GameHostOptions { Transport = network });
        guest.Network.Join("my-pc:7777");
        for (var i = 0; i < 120 && !guest.Network.IsClient; i++)
        {
            host.Tick(1 / 60.0);
            guest.Tick(1 / 60.0);
        }

        Assert.True(guest.Network.IsClient, guest.Network.Status);
        Assert.Equal(2, host.Network.Players.Count);

        // A port the transport can't open is said as a socket's would be.
        var third = (loaded with { Scene = Arena() }).NewGame(new GameHostOptions { Transport = network });
        Assert.False(third.Network.Host(7777));
        Assert.Null(third.Network.Session);
        Assert.StartsWith("Couldn't host", third.Network.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Hosting_or_joining_that_cant_start_leaves_no_session()
    {
        var game = new Game(Arena(), new InputState()) { SceneName = "Arena" };
        game.Network.Unavailable = "No network here.";
        var ended = new List<string>();
        game.Network.Ended += ended.Add;

        Assert.False(game.Network.Host());
        Assert.Null(game.Network.Session);
        Assert.Equal("No network here.", game.Network.Status);

        game.Network.Join("192.168.1.5");
        Assert.Null(game.Network.Session);
        Assert.Equal(["No network here."], ended);
    }

    [Fact]
    public void Each_players_tools_are_run_with_their_own_input_and_the_games_stays_this_machines()
    {
        Tools? tools = null;
        var world = new NetWorld(Arena, setUp: game =>
        {
            if (game.Network.Session is null && tools is null)
                tools = game.AddSystem(new Tools());
        });
        var hostInput = world.Host.Input;
        var guest = world.Host.Network.Players.Single(p => !p.IsLocal);

        NetWorld.Keys(world.Client).SetKey(Key.Space, true);
        world.Run(0.2);

        Assert.NotNull(tools);
        Assert.NotEmpty(tools.Runs);
        Assert.All(tools.Runs, run =>
        {
            Assert.Same(guest, run.Player);
            Assert.Same(guest.Input, run.Input);
            Assert.Same(hostInput, run.GameInput);
        });
        Assert.Contains(tools.Runs, run => run.SpaceDown);
        Assert.Equal([guest], world.Host.Network.RemotePlayers);
        Assert.Empty(world.Client.Network.RemotePlayers);
    }

    private sealed class Tools : GameSystem, IPlayerTools
    {
        public override string Name => "Test Tools";

        public List<(NetPlayer Player, IInput Input, IInput GameInput, bool SpaceDown)> Runs { get; } = [];

        public void RunForPlayer(NetPlayer player, IInput input) => Runs.Add((player, input, Game.Input, input.IsDown(Key.Space)));
    }
}
