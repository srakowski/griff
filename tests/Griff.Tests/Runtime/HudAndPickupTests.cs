using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Bars, counters and pickups: HUDs and collectibles with no code.</summary>
public class HudAndPickupTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly Dictionary<string, Func<Entity>> _prefabs = [];

    public HudAndPickupTests()
    {
        _game = new Game(_scene, new InputState()) { PrefabLoader = name => _prefabs[name]() };
        _game.Physics.Gravity = Vector2.Zero;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
            _game.Tick(Step);
    }

    private Entity AddPlayer(Vector2 position, string? layer = "Player")
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(position, 0);
        player.AddComponent<BoxCollider>().Layer = layer;
        player.AddComponent<Body>();
        _scene.Add(player);
        return player;
    }

    private Pickup AddCoin(Vector2 position, string? layers = "Player")
    {
        var coin = new Entity("Coin");
        coin.Transform.Position = new Vector3(position, 0);
        coin.AddComponent<Sprite>().Tint = Color.FromBytes(0xE3, 0xB3, 0x41);
        var collider = coin.AddComponent<CircleCollider>();
        collider.Radius = 0.3f;
        collider.IsTrigger = true;
        var pickup = coin.AddComponent<Pickup>();
        pickup.Layers = layers;
        _scene.Add(coin);
        return pickup;
    }

    private Counter AddCounter(string? message = "Score", string? format = "Score: {0}")
    {
        var score = new Entity("Score");
        score.AddComponent<Text>();
        var counter = score.AddComponent<Counter>();
        counter.Message = message;
        counter.Format = format;
        _scene.Add(score);
        return counter;
    }

    [Fact]
    public void Pickup_TouchedByThePlayer_SendsItsAmount_AndDisappears()
    {
        var counter = AddCounter();
        var coin = AddCoin(new Vector2(0, 0));
        coin.Amount = 5;
        Entity? collectedBy = null;
        coin.Collected += by => collectedBy = by;
        var player = AddPlayer(new Vector2(0, 0));

        Run(0.05f);

        Assert.True(coin.IsCollected);
        Assert.Null(coin.Entity!.Scene);
        Assert.Same(player, collectedBy);
        Assert.Equal(5, counter.Value);
        Assert.Equal("Score: 5", counter.Entity!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void Pickup_IgnoresLayersItDoesntAccept_AndThingsWithoutABody()
    {
        var counter = AddCounter();
        AddCoin(Vector2.Zero);
        var enemy = AddPlayer(Vector2.Zero, layer: "Enemy");
        enemy.Name = "Enemy";
        var wall = new Entity("Wall");
        wall.AddComponent<BoxCollider>().Size = new Vector2(4, 4);
        _scene.Add(wall);

        Run(0.1f);
        Assert.Equal(0, counter.Value);
        Assert.NotNull(_scene.Entities.FirstOrDefault(e => e.Name == "Coin"));

        // Empty Layers: anything with a body collects it.
        _scene.Entities.First(e => e.Name == "Coin").GetComponent<Pickup>()!.Layers = null;
        Run(0.05f);
        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public void Pickup_CollectedByTwoAtOnce_CountsOnce()
    {
        var counter = AddCounter();
        AddCoin(Vector2.Zero, layers: null);
        AddPlayer(new Vector2(0.1f, 0));
        AddPlayer(new Vector2(-0.1f, 0));

        Run(0.1f);

        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public void Pickup_SpawnsItsEffect_InItsColor_AndTellsTheCollectorsBehaviors()
    {
        _prefabs["Sparkle"] = () =>
        {
            var sparkle = new Entity("Sparkle");
            sparkle.AddComponent<ParticleEmitter>().Emitting = false;
            return sparkle;
        };
        var coin = AddCoin(new Vector2(1, 2));
        coin.Effect = "Sparkle";
        var tint = coin.Entity!.GetComponent<Sprite>()!.Tint;
        var player = AddPlayer(new Vector2(1, 2));
        var collector = new PickupCollector();
        player.AddComponent(collector);

        Run(0.05f);

        var sparkle = _game.Find("Sparkle")!;
        Assert.Equal(new Vector3(1, 2, 0), sparkle.Transform.Position);
        Assert.Equal(tint, sparkle.GetComponent<ParticleEmitter>()!.StartColor);
        Assert.Equal(["Coin"], collector.Collected);
    }

    [Fact]
    public void Pickup_Spins()
    {
        var coin = AddCoin(Vector2.Zero);
        coin.Spin = 90;

        Run(0.5f);

        Assert.Equal(45, coin.Entity!.Transform.Rotation.Z, 1);
    }

    [Fact]
    public void Counter_AddsUpAnyNumberSent_OrOneForEachMessageWithout()
    {
        var kills = AddCounter("EnemyDied", "Kills: {0}");
        var score = AddCounter();
        _game.Tick(Step);

        Assert.Equal(1, _game.Send("Score", 10));
        _game.Send("Score", 2.6f);
        _game.Send("EnemyDied", new Entity("Enemy"));
        _game.Send("EnemyDied");
        _game.Tick(Step);

        Assert.Equal(13, score.Value);
        Assert.Equal(2, kills.Value);
        Assert.Equal("Kills: 2", kills.Entity!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void Counter_PopsWhenItChanges_AndSpringsBack()
    {
        var counter = AddCounter();
        counter.Entity!.Transform.Scale = new Vector3(2, 2, 1);
        _game.Tick(Step);

        counter.Add(1);
        _game.Tick(Step);
        Assert.True(counter.Entity.Transform.Scale.X > 2.2f);

        Run(0.5f);
        Assert.Equal(2, counter.Entity.Transform.Scale.X, 3);
    }

    [Fact]
    public void Counter_SaveBestAs_KeepsTheBestBetweenRuns()
    {
        var counter = AddCounter(format: "{0} (best {1})");
        counter.SaveBestAs = "best";
        _game.SaveData.Set("best", 7);
        _game.Tick(Step);
        Assert.Equal("0 (best 7)", counter.Entity!.GetComponent<Text>()!.Value);

        counter.Add(5);
        _game.Tick(Step);
        Assert.Equal(7, _game.SaveData.GetInt("best"));

        counter.Add(4);
        _game.Tick(Step);
        Assert.Equal(9, _game.SaveData.GetInt("best"));
        Assert.Equal("9 (best 9)", counter.Entity.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void Counter_WithABrokenFormat_ShowsTheNumber()
    {
        var counter = new Counter { Format = "Score: {", Value = 4 };
        Assert.Equal("4", counter.Display());
        counter.Format = "{0:000}";
        Assert.Equal("004", counter.Display());
        counter.Format = "";
        Assert.Equal("4", counter.Display());
    }

    [Fact]
    public void Bar_TracksHealth_AndShowsEmptyOnceItsGone()
    {
        var boss = AddPlayer(Vector2.Zero);
        boss.Name = "Boss";
        var health = boss.AddComponent<Health>();
        health.Max = 4;
        health.InvincibleTime = 0;
        var hud = new Entity("Boss Bar");
        var bar = hud.AddComponent<Bar>();
        bar.Track = "Boss";
        _scene.Add(hud);

        _game.Tick(Step);
        Assert.Equal(1, bar.Fraction);

        health.Damage(1);
        _game.Tick(Step);
        Assert.Equal(0.75f, bar.Fraction, 4);

        health.Kill();
        Run(0.05f);
        Assert.Null(boss.Scene);
        Assert.Equal(0, bar.Fraction);
    }

    [Fact]
    public void Bar_TrailLingersAfterALoss_ThenDrainsAway()
    {
        var entity = new Entity("Bar");
        var bar = entity.AddComponent<Bar>();
        bar.Max = 10;
        bar.Value = 10;
        _scene.Add(entity);
        _game.Tick(Step);

        bar.Value = 6;
        Run(0.2f);
        Assert.Equal(0.6f, bar.Fraction, 4);
        Assert.Equal(1, bar.TrailFraction, 4);

        Run(1.5f);
        Assert.Equal(0.6f, bar.TrailFraction, 4);

        // Going up shows straight away.
        bar.Value = 9;
        _game.Tick(Step);
        Assert.Equal(0.9f, bar.TrailFraction, 4);
    }

    [Fact]
    public void Bar_Fraction_StaysBetweenZeroAndOne()
    {
        Assert.Equal(1, new Bar { Value = 12, Max = 10 }.Fraction);
        Assert.Equal(0, new Bar { Value = -1, Max = 10 }.Fraction);
        Assert.Equal(0, new Bar { Value = 1, Max = 0 }.Fraction);
        Assert.Equal(0, new Bar { Value = float.NaN }.Fraction);
    }

    private sealed class PickupCollector : Behavior
    {
        public List<string> Collected { get; } = [];

        protected override void OnPickup(Entity pickup) => Collected.Add(pickup.Name);
    }
}
