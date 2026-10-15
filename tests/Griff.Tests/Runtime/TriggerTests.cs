using System.Numerics;
using Griff.Input;
using Griff.Modules.Contraptions;
using Griff.Modules.FallingBlocks;
using Griff.Modules.Match3;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Anything triggering anything: components' actions, triggers, wires that fire them, and messages kept to part of a scene.</summary>
public sealed class TriggerTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public TriggerTests()
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

    private static Trigger AddTrigger(Entity on, string message, string action, string? targets = null, string? with = null, string? from = null)
    {
        var trigger = on.AddComponent<Trigger>();
        (trigger.Message, trigger.Do, trigger.Targets, trigger.With, trigger.From) = (message, action, targets, with, from);
        return trigger;
    }

    // ----- Actions -----

    public static TheoryData<Type, string> NamedActions => new()
    {
        { typeof(Switch), "On" }, { typeof(Switch), "Off" }, { typeof(Switch), "Toggle" },
        { typeof(Door), "Open" }, { typeof(Door), "Lock" },
        { typeof(Spawner), "Spawn" }, { typeof(Shooter), "Fire" }, { typeof(Mover), "Step" },
        { typeof(Health), "Damage" }, { typeof(Health), "Heal" }, { typeof(Health), "Kill" },
        { typeof(TurnOrder), "EndTurn" }, { typeof(Panel), "Open" }, { typeof(Panel), "Close" },
        { typeof(MatchBoard), "Pause" }, { typeof(MatchBoard), "Restart" }, { typeof(MatchBoard), "Shuffle" },
        { typeof(FallingBoard), "Pause" }, { typeof(FallingBoard), "Restart" },
        { typeof(Dialogue), "Start" }, { typeof(SoundPlayer), "Play" }, { typeof(Trigger), "SetFlag" },
        { typeof(Counter), "Add" },
    };

    [Theory]
    [MemberData(nameof(NamedActions))]
    public void Components_NameTheirActions(Type component, string action)
    {
        var actions = ComponentActions.Of(component);

        Assert.Contains(actions, a => a.Name == action);
        Assert.Same(ComponentRegistry.CreateDefault().Get(component).Actions, actions);
    }

    [Fact]
    public void Actions_AreFoundByTheirAttribute_OnAnyComponent_WithWhatTheyTake()
    {
        var actions = ComponentActions.Of(typeof(Gadget));

        Assert.Equal(["Beep", "Charge", "Count", "Arm", "Label"], actions.Select(a => a.Name));
        Assert.Equal([null, typeof(float), typeof(int), typeof(bool), typeof(string)], actions.Select(a => a.ValueType));
        Assert.Equal("amount", actions[1].ValueName);
        Assert.Equal(5, actions[2].DefaultValue);
        Assert.True(actions[1].IsCalled("charge"));
        Assert.True(actions[1].IsCalled("Gadget.Charge"));
        Assert.False(actions[1].IsCalled("Health.Charge"));
        Assert.Equal("Gadget.Charge", actions[1].QualifiedName);
        Assert.Equal("End Turn", ComponentActions.Of(typeof(TurnOrder)).Single(a => a.Name == "EndTurn").DisplayName);
    }

    [Fact]
    public void AnActionThatCantBeCalled_SaysHowItShouldLook()
    {
        var error = Assert.Throws<NotSupportedException>(() => ComponentActions.Of(typeof(BadGadget)));

        Assert.Contains("BadGadget.Twice", error.Message);
        Assert.Contains("return nothing", error.Message);
    }

    [Fact]
    public void Do_DoesAnActionOnEveryComponentThatHasIt_WithItsValue()
    {
        var thing = Add("Thing");
        var gadget = thing.AddComponent<Gadget>();
        var health = thing.AddComponent<Health>();
        health.InvincibleTime = 0;
        Run();

        Assert.Equal(1, _game.Do(thing, "charge", "2.5"));
        Assert.Equal(1, _game.Do(thing, "Count"));
        Assert.Equal(1, _game.Do(thing, "Arm", "false"));
        Assert.Equal(1, _game.Do(thing, "Label", " Hello "));
        Assert.Equal(1, _game.Do(thing, "Health.Damage", 2));
        Assert.Equal(0, _game.Do(thing, "Gadget.Damage"));

        Assert.Equal(2.5f, gadget.Charged);
        Assert.Equal(5, gadget.Counted);
        Assert.False(gadget.Armed);
        Assert.Equal("Hello", gadget.Labelled);
        Assert.Equal(1, health.Current);
    }

    // ----- Triggers -----

    [Fact]
    public void ATrigger_DoesItsActionOnItsTargets_WhenItsMessageArrives()
    {
        var boss = Add("Boss");
        var health = boss.AddComponent<Health>();
        (health.Max, health.InvincibleTime) = (10, 0);
        var trigger = AddTrigger(Add("Rules"), "Score", "Damage", targets: "Boss", with: "2");
        Run();

        _game.Send("Score", 100);
        _game.Send("Other");

        Assert.Equal(8, health.Current);
        Assert.Equal(1, trigger.Fired);
    }

    [Fact]
    public void ATriggerWithNoTargets_DoesItOnItsOwnEntity_AndCanUseTheMessagesNumber()
    {
        var counter = Add("Score").AddComponent<Counter>();
        counter.Message = null;
        var trigger = AddTrigger(counter.Entity!, "Beat", "Add");
        trigger.UseMessageNumber = true;
        Run();

        _game.Send("Beat", 3);
        _game.Send("Beat", 4);

        Assert.Equal(7, counter.Value);
    }

    [Fact]
    public void ATriggerFromANamedEntity_HearsOnlyWhatItOrSomethingInsideItSent()
    {
        var boardA = Add("Board A");
        var goal = Add("Goal", boardA);
        var boardB = Add("Board B");
        var counter = Add("Score A").AddComponent<Counter>();
        counter.Message = null;
        AddTrigger(counter.Entity!, "Matched", "Add", from: "Board A");
        Run();

        _game.Send("Matched", null, from: boardB);
        _game.Send("Matched");
        _game.Send("Matched", null, from: boardA);
        _game.Send("Matched", null, from: goal);

        Assert.Equal(2, counter.Value);
    }

    [Fact]
    public void Triggers_SetOffSwitchesDoorsSpawnersShootersAndMore()
    {
        var lever = Add("Lever");
        var @switch = lever.AddComponent<Switch>();
        var gate = Add("Gate").AddComponent<Door>();
        (gate.Locked, gate.Key) = (true, "Key");
        var spawner = Add("Spawner").AddComponent<Spawner>();
        (spawner.Prefab, spawner.Running) = ("Bat", false);
        var cannon = Add("Cannon").AddComponent<Shooter>();
        (cannon.Prefab, cannon.Running, cannon.Speed) = ("Ball", false, 5);
        var rules = Add("Rules");
        AddTrigger(rules, "Go", "On", targets: "Lever");
        AddTrigger(rules, "Go", "Open", targets: "Gate");
        AddTrigger(rules, "Go", "Spawn", targets: "Spawner");
        AddTrigger(rules, "Go", "Fire", targets: "Cannon");
        AddTrigger(rules, "Go", "Set Flag", with: "Opened");
        Run();

        _game.Send("Go");
        Run();

        Assert.True(@switch.IsOn);
        Assert.False(gate.Locked);
        Assert.Null(gate.Key);
        Assert.Equal(1, spawner.Spawned);
        Assert.Contains(_scene.Entities, e => e.Name == "Ball" && e.GetComponent<Body>() is null);
        Assert.True(_game.HasFlag("Opened"));
    }

    [Fact]
    public void Triggers_OpenAndClosePanels_PlaySounds_AndStartDialogue()
    {
        var audio = new RecordingAudio();
        _game.Audio = audio;
        var panel = Add("Shop").AddComponent<Panel>();
        var sound = Add("Ding").AddComponent<SoundPlayer>();
        sound.Sound = "Sounds/ding.wav";
        var talk = Add("Guide");
        talk.AddComponent<BoxCollider>().IsTrigger = true;
        var dialogue = talk.AddComponent<Dialogue>();
        dialogue.Box = null;
        var rules = Add("Rules");
        AddTrigger(rules, "Open", "Open", targets: "Shop");
        AddTrigger(rules, "Close", "Close", targets: "Shop");
        AddTrigger(rules, "Open", "Play", targets: "Ding");
        AddTrigger(rules, "Talk", "Start", targets: "Guide");
        Run();
        Assert.False(panel.Entity!.IsEnabled);

        _game.Send("Open");
        Assert.True(panel.Entity.IsEnabled);
        Assert.Contains(audio.Played, p => p == "Sounds/ding.wav");
        _game.Send("Close");
        Assert.False(panel.Entity.IsEnabled);
        _game.Send("Talk");
        Assert.True(dialogue.IsTalking);
    }

    [Fact]
    public void ATrigger_EndsTheTurn()
    {
        var turns = Add("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Between, order.ComputerSides) = (0, null);
        AddTrigger(Add("Clock"), "Beat", "End Turn", targets: "Turns");
        Run(2);
        Assert.Equal("Player", order.Current);

        _game.Send("Beat", 1);
        Run(2);

        Assert.Equal("Enemy", order.Current);
    }

    [Fact]
    public void AMoverStepped_GoesToItsNextPoint_AndWaitsThere()
    {
        var platform = Add("Platform");
        var mover = platform.AddComponent<Mover>();
        (mover.Points, mover.Speed, mover.Wait, mover.Smooth) = ("4 0", 8, 0, false);
        AddTrigger(Add("Button"), "Press", "Step", targets: "Platform");
        mover.StepByStep = true;
        Run(30);
        Assert.Equal(0, platform.Transform.Position.X);

        _game.Send("Press");
        Run(60);
        Assert.Equal(4, platform.Transform.Position.X, 3);

        Run(60);
        Assert.Equal(4, platform.Transform.Position.X, 3);
        _game.Send("Press");
        Run(60);
        Assert.Equal(0, platform.Transform.Position.X, 3);
    }

    [Fact]
    public void TriggersSettingEachOtherOff_StopAfterAFewSteps()
    {
        var ping = Add("Ping");
        AddTrigger(ping, "Ping", "Echo").Targets = null;
        ping.AddComponent<Echo>().Game = _game;
        Run();

        _game.Send("Ping");

        Assert.InRange(ping.GetComponent<Echo>()!.Times, 2, 10);
    }

    [Fact]
    public void Triggers_SaveAndLoad_WithTheScene()
    {
        AddTrigger(Add("Rules"), "Score", "Damage", targets: "Boss", with: "2", from: "Board A").UseMessageNumber = true;
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene));

        var trigger = loaded.Entities.Single().GetComponent<Trigger>()!;
        Assert.Equal(("Score", "Damage", "Boss", "2", "Board A", true), (trigger.Message, trigger.Do, trigger.Targets, trigger.With, trigger.From, trigger.UseMessageNumber));
    }

    // ----- Messages kept to part of the scene -----

    [Fact]
    public void ScopedMessages_ReachOnlyTheBoardTheyCameFrom()
    {
        Counter Board(string name, out Entity board)
        {
            board = Add(name);
            board.AddComponent<MessageScope>();
            var counter = Add("Score", board).AddComponent<Counter>();
            counter.Message = "Score";
            return counter;
        }

        var a = Board("Board A", out var boardA);
        var b = Board("Board B", out var boardB);
        var everyone = Add("Total").AddComponent<Counter>();
        everyone.Message = "Score";
        var listened = new List<object?>();
        _game.On("Score", listened.Add);
        Run();

        _game.Send("Score", 10, from: boardA);
        _game.Send("Score", 5, from: boardB);
        _game.Send("Score", 1);

        Assert.Equal((11, 6, 1), (a.Value, b.Value, everyone.Value));
        Assert.Equal([1], listened);
    }

    [Fact]
    public void SendTo_ReachesANamedEntity_OrItsChildrenToo()
    {
        var parent = Add("Parent");
        var child = Add("Child", parent);
        var heard = new List<string>();
        _game.On("Hi", _ => heard.Add("parent"), parent);
        _game.On("Hi", _ => heard.Add("child"), child);
        _game.On("Hi", _ => heard.Add("nobody"));
        Run();

        _game.SendTo(parent, "Hi");
        _game.SendTo(parent, "Hi", children: true);

        Assert.Equal(["parent", "parent", "child"], heard);
    }

    [Fact]
    public void AScopeCanSendToAnotherEntity_OrToEveryone()
    {
        var board = Add("Board");
        var scope = board.AddComponent<MessageScope>();
        (scope.To, scope.Target) = (MessageAudience.Entity, "Hud");
        var hud = Add("Hud").AddComponent<Counter>();
        var inside = Add("Inside", board).AddComponent<Counter>();
        Run();

        _game.Send("Score", 2, from: board);
        scope.To = MessageAudience.Everyone;
        _game.Send("Score", 3, from: board);

        Assert.Equal((5, 3), (hud.Value, inside.Value));
    }

    [Fact]
    public void ScopedMessages_ReachTheSameBoard_OnEveryMachine()
    {
        static Scene Boards()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            foreach (var name in new[] { "Board A", "Board B" })
            {
                var board = new Entity(name);
                board.AddComponent<MessageScope>();
                scene.Add(board);
                var score = new Entity("Score");
                score.AddComponent<Counter>().Message = "Score";
                scene.Add(score, board);
            }

            return scene;
        }

        var world = new Networking.NetWorld(Boards);
        static int Count(Game game, string board) =>
            game.Scene.Entities.Single(e => e.Name == "Score" && e.Parent!.Name == board).GetComponent<Counter>()!.Value;

        world.Host.Send("Score", 7, from: world.Host.Scene.Entities.Single(e => e.Name == "Board A"));
        world.Run(0.3);

        Assert.Equal((7, 0), (Count(world.Host, "Board A"), Count(world.Host, "Board B")));
        Assert.Equal((7, 0), (Count(world.Client, "Board A"), Count(world.Client, "Board B")));
    }

    [Fact]
    public void WhatATriggerDid_RewindsWithTheScene()
    {
        var boss = Add("Boss");
        var health = boss.AddComponent<Health>();
        (health.Max, health.InvincibleTime) = (10, 0);
        AddTrigger(Add("Rules"), "Hit", "Damage", targets: "Boss", with: "3");
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1);
        Run();
        timeline.Record(_scene, _game.Time.Total);

        _game.Send("Hit");
        Run();
        timeline.Record(_scene, _game.Time.Total);
        Assert.Equal(7, health.Current);

        var before = timeline.Restore(0);
        Assert.Equal(10, before.Entities.Single(e => e.Name == "Boss").GetComponent<Health>()!.Current);
        Assert.Equal("Damage", before.Entities.Single(e => e.Name == "Rules").GetComponent<Trigger>()!.Do);
    }

    // ----- Wires and power -----

    [Fact]
    public void AWireIntoAnAction_DoesItOnceAsItsSignalComesOn()
    {
        var button = Add("Button").AddComponent<WireButton>();
        button.Toggle = true;
        var spawner = Add("Spawner").AddComponent<Spawner>();
        (spawner.Prefab, spawner.Running) = ("Crate", false);
        var wire = spawner.Entity!.AddComponent<Wire>();
        (wire.Input, wire.From, wire.Output) = ("Spawn", "Button", "On");
        Run(5);
        Assert.Equal(0, spawner.Spawned);

        button.IsOn = true;
        Run(5);
        Assert.Equal(1, spawner.Spawned);

        button.IsOn = false;
        Run(2);
        button.IsOn = true;
        Run(2);
        Assert.Equal(2, spawner.Spawned);
        Assert.Contains("Spawn", WirePorts.Inputs(spawner.Entity));
    }

    [Fact]
    public void Power_TurnsWhatComponentsMarkPoweredOnAndOff_ScriptsToo()
    {
        var thing = Add("Thing");
        var gadget = thing.AddComponent<Gadget>();
        var door = thing.AddComponent<Door>();

        Game.Power(thing, false);
        Assert.False(gadget.Armed);
        Assert.True(door.Locked);
        Assert.True(thing.IsEnabled);

        Game.Power(thing, true);
        Assert.True(gadget.Armed);
        Assert.False(door.Locked);

        var plain = Add("Plain");
        Game.Power(plain, false);
        Assert.False(plain.IsEnabled);
    }

    // ----- Boards -----

    [Fact]
    public void AMatchBoard_PausesResumesAndRestarts()
    {
        var entity = Add("Board");
        var map = entity.AddComponent<Tilemap>();
        var board = entity.AddComponent<MatchBoard>();
        (board.Columns, board.Rows, board.Seed) = (6, 6, 3);
        Run(2);
        (board.Score, board.Swaps) = (50, 4);

        _game.Do(entity, "Pause");
        Run(2);
        Assert.True(board.Paused);
        _game.Do(entity, "Resume");
        _game.Do(entity, "Restart");
        Run(2);

        Assert.False(board.Paused);
        Assert.Equal((0, 0), (board.Score, board.Swaps));
        Assert.True(map.GetTile(0, 0) >= 0);
    }

    [Fact]
    public void AFallingBoard_PausesAndRestarts()
    {
        var entity = Add("Board");
        var map = entity.AddComponent<Tilemap>();
        var board = entity.AddComponent<FallingBoard>();
        (board.Columns, board.Rows, board.Seed, board.Level) = (6, 10, 3, 2);
        Run(2);
        map.SetTile(0, 9, 0);
        (board.Score, board.Lines, board.Level) = (300, 3, 4);

        _game.Do(entity, "Pause");
        var at = board.PieceAt;
        Run(120);
        Assert.Equal(at, board.PieceAt);

        _game.Do(entity, "Resume");
        _game.Do(entity, "Restart");
        Run();

        Assert.Equal((0, 0, 2), (board.Score, board.Lines, board.Level));
        Assert.Equal(-1, map.GetTile(0, 9));
    }

    // ----- Steady play -----

    [Fact]
    public void Triggers_AddNoGarbage_WhileNothingSetsThemOff()
    {
        for (var i = 0; i < 20; i++)
            AddTrigger(Add($"Rules {i}"), "Score", "Damage", targets: "Boss");
        Add("Board").AddComponent<MessageScope>();
        Run(10);

        Assert.Equal(0, Allocations.PerCall(() => _game.Tick(Frame), 10));
    }

    // ----- Test components -----

    public sealed class Gadget : Component
    {
        public float Charged { get; set; }

        public int Counted { get; set; }

        [Powered]
        public bool Armed { get; set; } = true;

        public string? Labelled { get; set; }

        [Action]
        public void Beep()
        {
        }

        [Action]
        public void Charge(float amount) => Charged += amount;

        [Action]
        public void Count(int times = 5) => Counted += times;

        [Action]
        public void Arm(bool on) => Armed = on;

        [Action]
        public void Label(string? text) => Labelled = text;
    }

    public sealed class BadGadget : Component
    {
        [Action]
        public void Twice(float a, float b)
        {
        }
    }

    // Sends the message that sets itself off, each time it's set off.
    public sealed class Echo : Component
    {
        [DoNotSerialize]
        public Game? Game { get; set; }

        [DoNotSerialize]
        public int Times { get; set; }

        [Action("Echo")]
        public void Again()
        {
            Times++;
            Game!.Send("Ping");
        }
    }

    private sealed class RecordingAudio : IAudio
    {
        public List<string> Played { get; } = [];
        public float Volume { get; set; } = 1;
        public float MusicVolume { get; set; } = 1;
        public float SoundVolume { get; set; } = 1;
        public bool Muted { get; set; }

        public void Play(string sound, float volume = 1) => Played.Add(sound);

        public void Play(string sound, float volume, float pan) => Played.Add(sound);

        public void Play(string sound, float volume, float pan, float pitch) => Played.Add(sound);

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true)
        {
        }

        public void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true)
        {
        }

        public void StopMusic()
        {
        }

        public void StopAll()
        {
        }
    }
}
