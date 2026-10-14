using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Networking;

/// <summary>A component of a game's own (as a script's would be) that says what it sends.</summary>
public sealed class Charger : Component, INetState
{
    private readonly List<string> _log = [];

    /// <summary>Saved, so sent.</summary>
    public float Rate { get; set; } = 1;

    /// <summary>Saved, but each machine works it out for itself.</summary>
    [NotSent]
    public float Wobble { get; set; }

    /// <summary>Kept only while playing, and sent.</summary>
    [Sent]
    public float Charge { get; private set; }

    /// <summary>Kept only while playing, and not sent.</summary>
    public float Secret { get; private set; }

    public IReadOnlyList<string> Log => _log;

    public void Bump()
    {
        Charge++;
        Secret++;
        _log.Add($"bump {Charge}");
    }

    void INetState.Network(NetState state) => state.Add<IReadOnlyList<string>>(() => _log.ToArray(), lines =>
    {
        _log.Clear();
        _log.AddRange(lines ?? []);
    });
}

/// <summary>A component of a game's own that each machine keeps for itself, like a HUD.</summary>
[NetLocal]
public sealed class Gauge : Component
{
    public float Shown { get; set; }
}

public sealed class NetStateTests
{
    private static Scene Lab()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        settings.AddComponent<Multiplayer>().Player = "";
        scene.Add(settings);

        var battery = new Entity("Battery");
        battery.AddComponent<Charger>();
        scene.Add(battery);

        var dial = new Entity("Dial");
        dial.AddComponent<Gauge>();
        scene.Add(dial);
        return scene;
    }

    private static void Register(Game game)
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Charger>();
        registry.Register<Gauge>();
        game.Components = registry;
    }

    [Fact]
    public void A_games_own_component_sends_what_it_says()
    {
        var world = new NetWorld(Lab, setUp: Register);
        var charger = world.Host.Find("Battery")!.GetComponent<Charger>()!;
        charger.Rate = 3;
        charger.Wobble = 5;
        charger.Bump();
        charger.Bump();

        world.Run(0.5);

        var copy = world.Client.Find("Battery")!.GetComponent<Charger>()!;
        Assert.Equal(3, copy.Rate);
        Assert.Equal(2, copy.Charge);
        Assert.Equal(["bump 1", "bump 2"], copy.Log);
        Assert.Equal(0, copy.Wobble);
        Assert.Equal(0, copy.Secret);
    }

    [Fact]
    public void A_component_marked_local_keeps_its_entity_on_each_machine()
    {
        var world = new NetWorld(Lab, setUp: Register);
        world.Host.Find("Dial")!.GetComponent<Gauge>()!.Shown = 9;
        world.Client.Find("Dial")!.GetComponent<Gauge>()!.Shown = 4;

        world.Run(0.5);

        Assert.Equal(9, world.Host.Find("Dial")!.GetComponent<Gauge>()!.Shown);
        Assert.Equal(4, world.Client.Find("Dial")!.GetComponent<Gauge>()!.Shown);
        Assert.Single(world.Client.Scene.Entities, e => e.Name == "Dial");
    }
}
