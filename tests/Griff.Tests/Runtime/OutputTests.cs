using System.Numerics;
using Griff.Input;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Contraptions;
using Griff.Modules.Dungeons;
using Griff.Modules.FallingBlocks;
using Griff.Modules.Match3;
using Griff.Modules.Puzzles;
using Griff.Modules.Strategy;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Anything reading anything: components' outputs, shown by counters, bars and texts and carried by wires.</summary>
public sealed class OutputTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public OutputTests()
    {
        _game = new Game(_scene, _input)
        {
            PrefabLoader = name => new Entity(name),
            SceneLoader = _ => new Scene(),
            SceneName = "Level",
        };
        _game.Physics.Gravity = Vector2.Zero;
    }

    private void Run(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity Add(string name, Entity? parent = null)
    {
        var entity = new Entity(name);
        _scene.Add(entity, parent);
        return entity;
    }

    // ----- Outputs -----

    public static TheoryData<Type, string> NamedOutputs => new()
    {
        { typeof(MatchBoard), "Score" }, { typeof(MatchBoard), "Swaps" }, { typeof(MatchBoard), "MovesLeft" }, { typeof(MatchBoard), "Chain" },
        { typeof(FallingBoard), "Score" }, { typeof(FallingBoard), "Lines" }, { typeof(FallingBoard), "Level" },
        { typeof(Goal), "Covered" }, { typeof(Goal), "GoalsCovered" }, { typeof(MoveHistory), "Moves" },
        { typeof(DungeonGenerator), "Floor" }, { typeof(TurnOrder), "Round" }, { typeof(TurnOrder), "Turn" }, { typeof(TurnOrder), "Left" },
        { typeof(BeatClock), "Beat" }, { typeof(Faction), "Amount" }, { typeof(Faction), "Units" }, { typeof(Hotbar), "Blocks" },
        { typeof(Hotbar), "SelectedCount" }, { typeof(Inventory), "Count" }, { typeof(Counter), "Count" }, { typeof(Counter), "Reached" },
        { typeof(Health), "Health" }, { typeof(Switch), "On" }, { typeof(Body), "Speed" }, { typeof(Body), "Height" },
        { typeof(WireGate), "Out" }, { typeof(WireButton), "On" }, { typeof(WireSensor), "Out" }, { typeof(WireSensor), "Count" },
    };

    [Theory]
    [MemberData(nameof(NamedOutputs))]
    public void Components_NameTheirOutputs(Type component, string output)
    {
        var outputs = ComponentOutputs.Of(component);

        Assert.Contains(outputs, o => o.Name == output);
        Assert.Same(ComponentRegistry.CreateDefault().Get(component).Outputs, outputs);
    }

    [Fact]
    public void Outputs_AreFoundByTheirAttribute_OnAnyComponent_WithWhatTheyGive()
    {
        var outputs = ComponentOutputs.Of(typeof(Gauge));

        Assert.Equal(["Fuel", "Lives", "Ready", "Pilot", "Cargo"], outputs.Select(o => o.Name));
        Assert.Equal([typeof(float), typeof(int), typeof(bool), typeof(string), typeof(int)], outputs.Select(o => o.ValueType));
        Assert.Equal("MaxFuel", outputs[0].OutOfName);
        Assert.Equal(("item", "Ore", "Cargo:Ore"), (outputs[4].KeyName, outputs[4].Example, outputs[4].Reference));
        Assert.Throws<NotSupportedException>(() => ComponentOutputs.Of(typeof(BadGauge)));

        var gauge = Add("Gauge").AddComponent<Gauge>();
        (gauge.Fuel, gauge.MaxFuel, gauge.Lives, gauge.Ready, gauge.Pilot) = (3.5f, 10, 2, true, "Ada");
        gauge.Hold["Ore"] = 7;

        Assert.Equal(3.5f, ComponentOutputs.Read(gauge.Entity!, "Fuel"));
        Assert.Equal(2, ComponentOutputs.Read(gauge.Entity!, "Gauge.Lives"));
        Assert.Equal(1, ComponentOutputs.Read(gauge.Entity!, "ready"));
        Assert.Equal(7, ComponentOutputs.Read(gauge.Entity!, "Cargo: Ore"));
        Assert.Equal(0, ComponentOutputs.Read(gauge.Entity!, "Nothing"));
        Assert.Equal(10, outputs[0].ReadOutOf(gauge));
        Assert.Equal("Ada", outputs[3].ReadText(gauge));
    }

    [Fact]
    public void WirePorts_AreTheOutputsThatGiveNumbers_TheOldOnesStill()
    {
        var parts = Add("Parts");
        parts.AddComponent<WireGate>();
        parts.AddComponent<WireSensor>();
        parts.AddComponent<Counter>();
        parts.AddComponent<Health>();
        parts.AddComponent<Body>();
        parts.AddComponent<Switch>();
        var turns = Add("Turns").AddComponent<TurnOrder>();

        var ports = WirePorts.Outputs(parts);

        foreach (var port in new[] { "Out", "Count", "Health", "Speed", "Height", "On", "Reached" })
            Assert.Contains(port, ports);
        Assert.Contains("Left:Enemy", WirePorts.Outputs(turns.Entity!));
        Assert.DoesNotContain("Turn", WirePorts.Outputs(turns.Entity!));
    }

    [Fact]
    public void AWire_CarriesAnyOutput_OldWiresAsBefore()
    {
        var button = Add("Button").AddComponent<WireButton>();
        var lamp = Add("Lamp").AddComponent<WireLamp>();
        var wire = lamp.Entity!.AddComponent<Wire>();
        (wire.From, wire.Output, wire.Input) = ("Button", "On", "On");

        var faction = Add("Red").AddComponent<Faction>();
        faction.Resources = "Gold 40";
        var display = Add("Display").AddComponent<WireDisplay>();
        var gold = display.Entity!.AddComponent<Wire>();
        (gold.From, gold.Output, gold.Input) = ("Red", "Amount:Gold", "Value");

        button.IsOn = true;
        Run(2);
        Assert.Equal(1, lamp.Brightness);
        Assert.Equal(40, display.Value);

        faction.Add("Gold", 15);
        Run();
        Assert.Equal(55, display.Value);
    }

    // ----- Counters -----

    [Fact]
    public void ACounter_CountsAnOutput_OutOfWhatItSays()
    {
        var board = Add("Board");
        board.AddComponent<Tilemap>();
        var match = board.AddComponent<MatchBoard>();
        (match.Columns, match.Rows, match.Seed, match.TargetScore) = (6, 6, 3, 500);
        var hud = Add("Score Label");
        hud.AddComponent<Text>();
        var counter = hud.AddComponent<Counter>();
        (counter.Track, counter.Output, counter.Format, counter.Pop) = ("Board", "Score", "{0} / {2}", 1);
        Run(2);

        match.Score = 120;
        Run();

        Assert.Equal((120, 500), (counter.Value, counter.Total));
        Assert.Equal("120 / 500", hud.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void ACounter_WithoutATrack_CountsWhatItsInside()
    {
        var dungeon = Add("Dungeon").AddComponent<DungeonGenerator>();
        dungeon.GenerateOnPlay = false;
        var counter = Add("Floor Label", dungeon.Entity).AddComponent<Counter>();
        counter.Output = "Floor";
        dungeon.Floor = 3;
        Run();

        Assert.Equal(3, counter.Value);
    }

    [Fact]
    public void ACounter_CountsOnePerMessage_WhateverItSends()
    {
        var beats = Add("Beats").AddComponent<Counter>();
        (beats.Message, beats.OnePerMessage) = ("Beat", true);
        var summed = Add("Summed").AddComponent<Counter>();
        summed.Message = "Beat";

        foreach (var beat in new[] { 0, 1, 2, 3 })
            _game.Send("Beat", beat);

        Assert.Equal((4, 6), (beats.Value, summed.Value));
    }

    [Fact]
    public void ACounter_SendsItsMessage_AsItReachesItsValue_AndATriggerActsOnIt()
    {
        var coins = Add("Coins").AddComponent<Counter>();
        (coins.Message, coins.Reaches, coins.Reach, coins.ReachMessage) = ("Coin", CounterReach.AtLeast, 3, "Rich");
        var door = Add("Vault").AddComponent<Door>();
        door.Locked = true;
        var trigger = door.Entity!.AddComponent<Trigger>();
        (trigger.Message, trigger.Do) = ("Rich", "Open");
        var heard = new List<object?>();
        _game.On("Rich", heard.Add);
        Run();

        _game.Send("Coin");
        _game.Send("Coin");
        Run();
        Assert.True(door.Locked);
        Assert.False(coins.Reached);

        _game.Send("Coin");
        Run();
        _game.Send("Coin");
        Run(3);

        Assert.False(door.Locked);
        Assert.Equal([3], heard);
        Assert.True(coins.Reached);

        // Leaving it and coming back reaches it again.
        coins.Add(-3);
        Run();
        coins.Add(2);
        Run();
        Assert.Equal(2, heard.Count);
    }

    [Fact]
    public void ACounter_CountingDown_ReachesItsValue_AndItsReachedTurnsOnAWire()
    {
        var moves = Add("Moves").AddComponent<Counter>();
        (moves.Message, moves.Value, moves.Reaches, moves.Reach) = ("Moved", 3, CounterReach.AtMost, 0);
        var gate = Add("Gate");
        var spawner = gate.AddComponent<Spawner>();
        (spawner.Prefab, spawner.Running, spawner.Interval) = ("Slime", false, 100);
        var wire = gate.AddComponent<Wire>();
        (wire.From, wire.Output, wire.Input) = ("Moves", "Reached", "Power");
        Run(2);
        Assert.False(spawner.Running);

        for (var i = 0; i < 3; i++)
            _game.Send("Moved", -1);
        Run(2);

        Assert.True(moves.Reached);
        Assert.True(spawner.Running);
    }

    [Fact]
    public void ACounter_StartingAtItsValue_HasntReachedIt_AsAfterARewind()
    {
        var counter = Add("Counter").AddComponent<Counter>();
        (counter.Message, counter.Value, counter.Reaches, counter.Reach, counter.ReachMessage) = ("Point", 5, CounterReach.AtLeast, 5, "Done");
        var heard = 0;
        _game.On("Done", _ => heard++);

        Run(5);
        _game.Send("Point");
        Run(2);

        Assert.Equal(0, heard);
        Assert.True(counter.Reached);
    }

    [Fact]
    public void ACounter_MultipliedByAnother_AddsTimesItsCount_AndStopsAtItsMax()
    {
        var multiplier = Add("Multiplier").AddComponent<Counter>();
        (multiplier.Message, multiplier.OnePerMessage, multiplier.Value, multiplier.Max) = ("Lit", true, 1, 3);
        var score = Add("Score").AddComponent<Counter>();
        (score.Message, score.MultiplyBy) = ("Score", "Multiplier");
        var bonus = score.Entity!.AddComponent<Trigger>();
        (bonus.Message, bonus.Do, bonus.UseMessageNumber) = ("Bonus", "Add", true);
        var back = multiplier.Entity!.AddComponent<Trigger>();
        (back.Message, back.Do, back.With) = ("Lost", "Set", "1");
        Run();

        _game.Send("Score", 100);
        Assert.Equal(100, score.Value);
        _game.Send("Lit");
        _game.Send("Score", 100);
        Assert.Equal(300, score.Value);

        // The multiplier stops at its Max; the Add action (here from a trigger) is multiplied too.
        for (var i = 0; i < 4; i++)
            _game.Send("Lit");
        Assert.Equal(3, multiplier.Value);
        _game.Send("Bonus", 1000);
        Run();
        Assert.Equal(3300, score.Value);

        // A trigger's Set puts it back.
        _game.Send("Lost");
        Run();
        Assert.Equal(1, multiplier.Value);
        _game.Send("Score", 10);
        Assert.Equal(3310, score.Value);

        // Taking away isn't stopped by its Max; a counter can't be multiplied by itself.
        multiplier.Add(-5);
        Assert.Equal(-4, multiplier.Value);
        multiplier.MultiplyBy = "Multiplier";
        multiplier.Set(2);
        multiplier.Add(1);
        Assert.Equal(3, multiplier.Value);
    }

    [Fact]
    public void ACounter_SaysWhenItBeatsTheBestKept_ANewBest_OnceAndNotWithoutOne()
    {
        var score = Add("Score").AddComponent<Counter>();
        (score.Message, score.SaveBestAs, score.NewBestMessage) = ("Score", "best", "New Best");
        var said = new List<object?>();
        _game.On("New Best", said.Add);
        _game.SaveData.Set("best", 50);
        var label = Add("New Best Label").AddComponent<Text>();
        (label.Track, label.Output, label.Value) = ("Score", "New Best", "{0:'New best!';;''}");
        Run();
        Assert.False(score.NewBest);
        Assert.Equal(50, score.BestToBeat);
        Assert.Equal("", label.Shown);

        _game.Send("Score", 40);
        Run();
        _game.Send("Score", 20);
        Run();
        _game.Send("Score", 30);
        Run();
        Assert.True(score.NewBest);
        Assert.Equal([60], said);
        Assert.Equal("New best!", label.Shown);
        Assert.Equal(90, _game.SaveData.GetInt("best"));

        // With nothing kept yet, the first points are a new best, but not one to say.
        var fresh = Add("Fresh").AddComponent<Counter>();
        (fresh.Message, fresh.SaveBestAs, fresh.NewBestMessage) = ("Points", "fresh", "New Best");
        Run();
        _game.Send("Points", 5);
        Run();
        Assert.True(fresh.NewBest);
        Assert.Single(said);

        // A true-or-false output without a format still says On or Off.
        var plain = Add("Plain").AddComponent<Text>();
        (plain.Track, plain.Output, plain.Value) = ("Score", "New Best", "");
        Run();
        Assert.Equal("On", plain.Shown);
    }

    // ----- Bars and texts -----

    [Fact]
    public void ABar_ShowsAnOutput_OutOfWhatItSays_OrItsOwnMax()
    {
        var board = Add("Board");
        board.AddComponent<Tilemap>();
        var match = board.AddComponent<MatchBoard>();
        (match.Columns, match.Rows, match.Seed, match.TargetScore, match.Score) = (6, 6, 3, 400, 100);
        var bar = Add("Progress").AddComponent<Bar>();
        (bar.Track, bar.Output) = ("Board", "Score");
        var faction = Add("Blue").AddComponent<Faction>();
        faction.Resources = "Wood 30";
        var wood = Add("Wood").AddComponent<Bar>();
        (wood.Track, wood.Output, wood.Max) = ("Blue", "Amount:Wood", 60);
        Run();

        Assert.Equal((100, 400, 0.25f), (bar.Value, bar.Max, bar.Fraction));
        Assert.Equal((30, 60), (wood.Value, wood.Max));
    }

    [Fact]
    public void ATextShowsAnOutput_FilledIntoWhatItSays_NamesToo()
    {
        var turns = Add("Turns").AddComponent<TurnOrder>();
        turns.Sides = "Knights, Orcs";
        var label = Add("Turn Label").AddComponent<Text>();
        (label.Track, label.Output, label.Value) = ("Turns", "Turn", "{0}'s turn, round {1}");
        var round = Add("Round Label").AddComponent<Text>();
        (round.Track, round.Output, round.Value) = ("Turns", "Round", "Text");
        var board = Add("Board");
        board.AddComponent<Tilemap>();
        var match = board.AddComponent<MatchBoard>();
        (match.Columns, match.Rows, match.Seed, match.TargetScore, match.Score) = (6, 6, 3, 900, 42);
        var score = Add("Score", board).AddComponent<Text>();
        (score.Output, score.Value) = ("Score", "{0:0000} of {1} - {Jump}");
        Run(2);

        Assert.Equal("Knights's turn, round 0", label.Shown);
        Assert.Equal("1", round.Shown);
        Assert.Equal("{0}'s turn, round {1}", label.Value);
        Assert.StartsWith("0042 of 900 - ", score.Shown);
        Assert.DoesNotContain("{Jump}", score.Shown);

        turns.Current = "Orcs";
        Run();
        Assert.StartsWith("Orcs's turn", label.Shown);
    }

    // ----- Network games -----

    [Fact]
    public void InANetworkGame_EveryMachineShowsTheOutput_AndHearsAReachOnce()
    {
        static Scene Arena()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var points = new Entity("Points");
            points.AddComponent<Counter>().Message = "Point";
            scene.Add(points);

            // One the host runs, and one on each player's own HUD.
            var goal = new Entity("Goal");
            var counter = goal.AddComponent<Counter>();
            (counter.Track, counter.Output, counter.Reaches, counter.Reach, counter.ReachMessage) = ("Points", "Count", CounterReach.AtLeast, 3, "Won");
            scene.Add(goal);
            var hud = new Entity("Hud");
            hud.AddComponent<ScreenAnchor>();
            var mine = hud.AddComponent<Counter>();
            (mine.Track, mine.Output, mine.Reaches, mine.Reach, mine.ReachMessage) = ("Points", "Count", CounterReach.AtLeast, 3, "Cheer");
            var label = new Entity("Label");
            var text = label.AddComponent<Text>();
            (text.Track, text.Output, text.Value) = ("Points", "Count", "Points: {0}");
            scene.Add(hud);
            scene.Add(label, hud);
            return scene;
        }

        var world = new Networking.NetWorld(Arena);
        var (won, cheered) = (new Dictionary<Game, int>(), new Dictionary<Game, int>());
        foreach (var game in new[] { world.Host, world.Client })
        {
            (won[game], cheered[game]) = (0, 0);
            game.On("Won", _ => won[game]++);
            game.On("Cheer", _ => cheered[game]++);
        }

        for (var i = 0; i < 3; i++)
            world.Host.Send("Point");
        world.Run(0.5);

        foreach (var game in new[] { world.Host, world.Client })
        {
            Assert.Equal("Points: 3", game.Scene.Entities.Single(e => e.Name == "Label").GetComponent<Text>()!.Shown);
            Assert.Equal((1, 1), (won[game], cheered[game]));
        }
    }

    // ----- Steady play -----

    [Fact]
    public void ReadingOutputs_AddsNoGarbage_WhileNothingChanges()
    {
        var gauge = Add("Gauge").AddComponent<Gauge>();
        (gauge.Lives, gauge.Pilot) = (3, "Ada");
        var faction = Add("Red").AddComponent<Faction>();
        faction.Resources = "Gold 40";
        for (var i = 0; i < 5; i++)
        {
            var counter = Add($"Counter {i}").AddComponent<Counter>();
            (counter.Track, counter.Output, counter.Reaches, counter.Reach) = ("Gauge", "Lives", CounterReach.AtLeast, 100);
            counter.Entity!.AddComponent<Text>();
            var bar = Add($"Bar {i}").AddComponent<Bar>();
            (bar.Track, bar.Output) = ("Red", "Amount:Gold");
            var text = Add($"Text {i}").AddComponent<Text>();
            (text.Track, text.Output, text.Value) = ("Gauge", "Pilot", "Pilot: {0}");
            var round = Add($"Round {i}").AddComponent<Text>();
            (round.Track, round.Output, round.Value) = ("Gauge", "Lives", "Lives {0}");
            var display = Add($"Display {i}").AddComponent<WireDisplay>();
            var wire = display.Entity!.AddComponent<Wire>();
            (wire.From, wire.Output, wire.Input) = ("Red", "Amount:Gold", "Value");
            var spawner = Add($"Spawner {i}").AddComponent<Spawner>();
            (spawner.Prefab, spawner.NeedsFlag) = ("Slime", "Alarm");
        }

        Run(30);

        Assert.Equal(0, Allocations.PerCall(() => _game.Tick(Frame), 10));
    }

    [Fact]
    public void OutputFields_SaveAndLoad_AndOldScenesKeepCountingMessages()
    {
        var registry = ComponentRegistry.CreateDefault();
        var serializer = new SceneSerializer(registry);
        var hud = Add("Hud");
        var counter = hud.AddComponent<Counter>();
        (counter.Track, counter.Output, counter.OnePerMessage, counter.Reaches, counter.Reach, counter.ReachMessage) =
            ("Board", "Score", true, CounterReach.AtMost, 2, "Low");
        var text = hud.AddComponent<Text>();
        (text.Track, text.Output) = ("Turns", "Turn");

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Hud");
        var back = loaded.GetComponent<Counter>()!;
        Assert.Equal(("Board", "Score", true, CounterReach.AtMost, 2, "Low"),
            (back.Track, back.Output, back.OnePerMessage, back.Reaches, back.Reach, back.ReachMessage));
        Assert.Equal(("Turns", "Turn"), (loaded.GetComponent<Text>()!.Track, loaded.GetComponent<Text>()!.Output));

        // A counter from before outputs: it adds up its message as it always did.
        var old = Add("Old").AddComponent<Counter>();
        old.Message = "Gem";
        _game.Send("Gem", 5);
        Assert.Equal(5, old.Value);
    }

    // ----- Test components -----

    public sealed class Gauge : Component
    {
        [Output(OutOf = nameof(MaxFuel))]
        public float Fuel { get; set; }

        public float MaxFuel { get; set; }

        [Output]
        public int Lives { get; set; }

        [Output]
        public bool Ready { get; set; }

        [Output]
        public string? Pilot { get; set; }

        [DoNotSerialize]
        public Dictionary<string, int> Hold { get; } = [];

        [Output("Cargo", Example = "Ore")]
        public int Carried(string item) => Hold.GetValueOrDefault(item);
    }

    public sealed class BadGauge : Component
    {
        [Output]
        public Vector2 Where { get; set; }
    }
}
