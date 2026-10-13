using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Switches, keys and inventories, trails and animated tiles: puzzles and dressing with no code.</summary>
public class PuzzleTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly Dictionary<string, Func<Entity>> _prefabs = [];

    public PuzzleTests()
    {
        _game = new Game(_scene, _input) { PrefabLoader = name => _prefabs[name]() };
        _game.Physics.Gravity = Vector2.Zero;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
        }
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        _game.Tick(Step);
        _input.EndFrame();
        _input.SetKey(key, false);
        _game.Tick(Step);
        _input.EndFrame();
    }

    private Entity Add(string name, Vector2 position, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity, parent);
        return entity;
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool trigger = false)
    {
        var entity = Add(name, position);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        return entity;
    }

    private Entity AddPlayer(Vector2 position)
    {
        var player = AddBox("Player", position, new Vector2(0.8f, 1));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<Sprite>();
        return player;
    }

    private Switch AddSwitch(string name, Vector2 position, SwitchMode mode)
    {
        var entity = AddBox(name, position, new Vector2(1, 1), trigger: true);
        entity.AddComponent<Sprite>();
        var component = entity.AddComponent<Switch>();
        component.Mode = mode;
        return component;
    }

    private static void MoveTo(Entity entity, Vector2 position) => entity.Transform.Position = new Vector3(position, 0);

    private static Vector2 Position(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // ---- Switches ----

    [Fact]
    public void Lever_Interact_FlipsItOnAndOff_AndItsTargetsWithIt()
    {
        var lever = AddSwitch("Lever", Vector2.Zero, SwitchMode.Lever);
        lever.Targets = "Gate, Bridge";
        var gate = Add("Gate", new Vector2(10, 0));
        var mover = gate.AddComponent<Mover>();
        mover.Points = "0 3";
        mover.Mode = MoverMode.Once;
        mover.Speed = 6;
        var bridge = Add("Bridge", new Vector2(-10, 0));
        AddPlayer(Vector2.Zero);

        // Off from the start: the gate stays shut and the bridge isn't there.
        Run(1);
        Assert.False(lever.IsOn);
        Assert.False(mover.Running);
        Assert.Equal(new Vector2(10, 0), Position(gate));
        Assert.False(bridge.IsEnabled);

        Press(Key.E);
        Assert.True(lever.IsOn);
        Assert.True(bridge.IsEnabled);
        Run(1);
        Assert.Equal(new Vector2(10, 3), Position(gate), new Vector2Comparer(0.01f));

        // Off again: a Once mover slides back.
        Press(Key.E);
        Assert.False(lever.IsOn);
        Assert.False(bridge.IsEnabled);
        Run(1);
        Assert.Equal(new Vector2(10, 0), Position(gate), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void Lever_ShowsItsPrompt_OnlyWhileSomeoneCanPullIt_AndShowsItsLook()
    {
        var lever = AddSwitch("Lever", Vector2.Zero, SwitchMode.Lever);
        var prompt = Add("Prompt", new Vector2(0, 1), lever.Entity);
        lever.Prompt = "Prompt";
        lever.OnFrame = 1;
        var sprite = lever.Entity!.GetComponent<Sprite>()!;
        sprite.Columns = 2;
        var player = AddPlayer(new Vector2(-5, 0));

        Run(3 * Step);
        Assert.False(prompt.IsEnabled);

        MoveTo(player, Vector2.Zero);
        Run(3 * Step);
        Assert.True(prompt.IsEnabled);

        Press(Key.E);
        Assert.Equal(1, sprite.Frame);
        Assert.Equal(lever.OnTint, sprite.Tint);
        Press(Key.E);
        Assert.Equal(0, sprite.Frame);
        Assert.Equal(Color.White, sprite.Tint);
    }

    [Fact]
    public void Plate_IsOnOnlyWhileStoodOn_AndSendsItsMessages()
    {
        var plate = AddSwitch("Plate", Vector2.Zero, SwitchMode.Plate);
        plate.Hides = "Barrier";
        plate.OnMessage = "PlateDown";
        plate.OffMessage = "PlateUp";
        var barrier = Add("Barrier", new Vector2(5, 0));
        var player = AddPlayer(new Vector2(-5, 0));
        var heard = new List<string>();
        _game.On("PlateDown", _ => heard.Add("down"));
        _game.On("PlateUp", _ => heard.Add("up"));

        Run(0.1f);
        Assert.True(barrier.IsEnabled);

        MoveTo(player, Vector2.Zero);
        Run(0.1f);
        Assert.True(plate.IsOn);
        Assert.False(barrier.IsEnabled);

        MoveTo(player, new Vector2(-5, 0));
        Run(0.1f);
        Assert.False(plate.IsOn);
        Assert.True(barrier.IsEnabled);
        Assert.Equal(["down", "up"], heard);
    }

    [Fact]
    public void Plate_StaysDown_WhileTheGameIsPaused()
    {
        var plate = AddSwitch("Plate", Vector2.Zero, SwitchMode.Plate);
        AddPlayer(Vector2.Zero);
        Run(0.1f);
        Assert.True(plate.IsOn);

        _game.Time.Scale = 0;
        Run(0.5f);

        Assert.True(plate.IsOn);
    }

    [Fact]
    public void Target_NamedByTwoPlates_IsOnOnlyWhileBothAreHeld()
    {
        var left = AddSwitch("Left Plate", new Vector2(-3, 0), SwitchMode.Plate);
        var right = AddSwitch("Right Plate", new Vector2(3, 0), SwitchMode.Plate);
        left.Layers = right.Layers = "Player, Crate";
        left.Targets = right.Targets = "Spawner";
        var spawner = Add("Spawner", new Vector2(0, 10)).AddComponent<Spawner>();
        spawner.Prefab = "Thing";
        spawner.Interval = 0.1f;
        _prefabs["Thing"] = () => new Entity("Thing");
        var player = AddPlayer(new Vector2(-3, 0));
        var crate = AddBox("Crate", new Vector2(0, -5), new Vector2(0.8f, 0.8f));
        crate.GetComponent<BoxCollider>()!.Layer = "Crate";
        crate.AddComponent<Body>();

        Run(0.5f);
        Assert.True(left.IsOn);
        Assert.False(spawner.Running);
        Assert.Equal(0, spawner.Spawned);

        MoveTo(crate, new Vector2(3, 0));
        Run(0.5f);
        Assert.True(spawner.Running);
        Assert.True(spawner.Spawned > 0);

        MoveTo(player, new Vector2(-8, 0));
        Run(0.1f);
        Assert.False(spawner.Running);
    }

    [Fact]
    public void Button_StaysOn_OrTurnsItselfOffAfterItsOnTime()
    {
        var button = AddSwitch("Button", Vector2.Zero, SwitchMode.Button);
        var timed = AddSwitch("Timed", new Vector2(0, 0), SwitchMode.Button);
        timed.OnTime = 0.5f;
        var player = AddPlayer(Vector2.Zero);
        var changes = new List<bool>();
        timed.Changed += changes.Add;

        Run(0.1f);
        MoveTo(player, new Vector2(10, 0));
        Run(0.3f);
        Assert.True(button.IsOn);
        Assert.True(timed.IsOn);

        Run(0.3f);
        Assert.True(button.IsOn);
        Assert.False(timed.IsOn);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void Switch_SetByAScript_SwitchesItsTargets_AndSoundsLikeAnyOther()
    {
        var @switch = AddSwitch("Switch", new Vector2(50, 0), SwitchMode.Lever);
        @switch.Targets = "Turret";
        @switch.Sound = "click.wav";
        var shooter = Add("Turret", Vector2.Zero).AddComponent<Shooter>();
        _prefabs["Bullet"] = () => new Entity("Bullet");
        var audio = new RecordingAudio();
        _game.Audio = audio;

        Run(0.1f);
        Assert.False(shooter.Running);
        Assert.Empty(_game.FindAll("Bullet"));
        Assert.Empty(audio.Played);

        @switch.IsOn = true;
        Run(Step);

        Assert.True(shooter.Running);
        Assert.Single(_game.FindAll("Bullet"));
        Assert.Equal(["click.wav"], audio.Played);
    }

    [Fact]
    public void Switch_Names_LookInsideItselfFirst_SoEachLockedBlockOpensOnlyItself()
    {
        var first = AddSwitch("Block", Vector2.Zero, SwitchMode.Button);
        first.Hides = "Block";
        var second = AddSwitch("Block", new Vector2(10, 0), SwitchMode.Button);
        second.Hides = "Block";
        AddPlayer(Vector2.Zero);

        Run(0.1f);

        Assert.False(first.Entity!.IsEnabled);
        Assert.True(second.Entity!.IsEnabled);
    }

    [Fact]
    public void Switch_WithAKey_NeedsOne_AndUsesItUp_EvenOnASolidBlock()
    {
        var block = AddBox("Locked Block", new Vector2(1, 0), new Vector2(1, 2));
        block.AddComponent<Sprite>();
        var lockComponent = block.AddComponent<Switch>();
        lockComponent.Mode = SwitchMode.Button;
        lockComponent.Key = "Key";
        lockComponent.Hides = "Locked Block";
        var player = AddPlayer(new Vector2(-1, 0));
        var body = player.GetComponent<Body>()!;

        // Walking into it without a key: it doesn't budge.
        for (var i = 0; i < 30; i++)
        {
            body.Velocity = new Vector2(4, 0);
            Run(Step);
        }

        Assert.True(block.IsEnabled);
        Assert.False(lockComponent.IsOn);
        Assert.True(Position(player).X < 0.5f);

        var inventory = player.AddComponent<Inventory>();
        inventory.Add("Key");
        for (var i = 0; i < 30; i++)
        {
            body.Velocity = new Vector2(4, 0);
            Run(Step);
        }

        Assert.False(block.IsEnabled);
        Assert.Equal(0, inventory.Count("Key"));
        Assert.Null(lockComponent.Key);
        Assert.True(Position(player).X > 0.5f, "the player walks on through where the block was");
    }

    // ---- Doors ----

    [Fact]
    public void Door_WithAKey_OpensOnlyForSomeoneCarryingOne_AndStaysOpen()
    {
        var door = AddBox("Door", Vector2.Zero, new Vector2(1, 2), trigger: true).AddComponent<Door>();
        door.Target = "Inside";
        door.Fade = 0;
        door.Key = "Key";
        Add("Inside", new Vector2(10, 0));
        var player = AddPlayer(Vector2.Zero);

        Run(0.1f);
        Assert.Equal(Vector2.Zero, Position(player));

        MoveTo(player, new Vector2(-5, 0));
        Run(0.1f);
        player.AddComponent<Inventory>().Items = ["Key"];
        MoveTo(player, Vector2.Zero);
        Run(0.1f);

        Assert.Equal(new Vector2(10, 0), Position(player), new Vector2Comparer(0.01f));
        Assert.False(player.GetComponent<Inventory>()!.Has("Key"));
        Assert.Null(door.Key);
    }

    [Fact]
    public void Door_SwitchedByALever_IsLockedUntilTheLeverIsPulled()
    {
        var lever = AddSwitch("Lever", new Vector2(-5, 0), SwitchMode.Lever);
        lever.Targets = "Door";
        var door = AddBox("Door", Vector2.Zero, new Vector2(1, 2), trigger: true).AddComponent<Door>();
        door.Target = "Inside";
        door.Fade = 0;
        Add("Inside", new Vector2(10, 0));
        var player = AddPlayer(new Vector2(-5, 0));

        Run(0.1f);
        Assert.True(door.Locked);
        MoveTo(player, Vector2.Zero);
        Run(0.1f);
        Assert.Equal(Vector2.Zero, Position(player));

        MoveTo(player, new Vector2(-5, 0));
        Run(0.1f);
        Press(Key.E);
        Assert.False(door.Locked);
        MoveTo(player, Vector2.Zero);
        Run(0.1f);
        Assert.Equal(new Vector2(10, 0), Position(player), new Vector2Comparer(0.01f));
    }

    // ---- Inventory ----

    [Fact]
    public void Pickup_WithAnItem_GoesInTheCollectorsInventory_AndAnItemCounterShowsIt()
    {
        var key = AddBox("Key", new Vector2(3, 0), new Vector2(0.5f, 0.5f), trigger: true).AddComponent<Pickup>();
        key.Item = "Key";
        key.Message = null;
        var keys = Add("Keys", new Vector2(0, 5));
        keys.AddComponent<Text>();
        var counter = keys.AddComponent<Counter>();
        counter.Item = "Key";
        counter.Format = "Keys: {0}";
        var player = AddPlayer(Vector2.Zero);

        Run(0.1f);
        Assert.Equal("Keys: 0", keys.GetComponent<Text>()!.Value);

        MoveTo(player, new Vector2(3, 0));
        Run(0.1f);

        Assert.Equal(1, player.GetComponent<Inventory>()!.Count("Key"));
        Assert.Equal("Keys: 1", keys.GetComponent<Text>()!.Value);

        player.GetComponent<Inventory>()!.Take("Key");
        Run(Step);
        Assert.Equal("Keys: 0", keys.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void Inventory_Items_ReadAndWriteAsNamesWithCounts()
    {
        var inventory = new Inventory { Items = ["Key 2", "Red Potion", "  ", "key"] };

        Assert.Equal(3, inventory.Count("KEY"));
        Assert.True(inventory.Has("Red Potion"));
        Assert.Equal(["Key 3", "Red Potion"], inventory.Items);

        Assert.False(inventory.Take("Key", 4));
        Assert.True(inventory.Take("Key", 3));
        Assert.Equal(["Red Potion"], inventory.Items);

        var changed = new List<string>();
        inventory.Changed += changed.Add;
        inventory.Add("Gem", 5);
        inventory.Clear();
        Assert.Equal(["Gem", "Red Potion", "Gem"], changed);
        Assert.Empty(inventory.Items);
    }

    // ---- Running ----

    [Fact]
    public void Mover_NotRunning_StopsWhereItIs()
    {
        var platform = Add("Platform", Vector2.Zero);
        var mover = platform.AddComponent<Mover>();
        mover.Points = "4 0";
        mover.Speed = 2;
        mover.Smooth = false;

        Run(1);
        mover.Running = false;
        Run(1);

        Assert.Equal(2, Position(platform).X, 0.05f);
    }

    [Fact]
    public void Power_WithNothingToRun_ShowsAndHidesTheEntity()
    {
        var entity = new Entity("Thing");
        var emitter = new Entity("Fountain");
        emitter.AddComponent<ParticleEmitter>();

        Game.Power(entity, false);
        Game.Power(emitter, false);

        Assert.False(entity.IsEnabled);
        Assert.True(emitter.IsEnabled);
        Assert.False(emitter.GetComponent<ParticleEmitter>()!.Emitting);
    }

    // ---- Trails ----

    [Fact]
    public void Trail_LeavesFadingCopies_OnlyWhileMovingFastEnough_BehindItsSprite()
    {
        var ship = Add("Ship", Vector2.Zero);
        ship.AddComponent<Sprite>().Tint = Color.Red;
        ship.AddComponent<Body>();
        ship.AddComponent<CircleCollider>();
        var trail = ship.AddComponent<Trail>();
        trail.Interval = 0.05f;
        trail.Fade = 0.2f;
        trail.MinSpeed = 2;

        ship.GetComponent<Body>()!.Velocity = new Vector2(1, 0);
        Run(0.5f);
        Assert.Empty(trail.Copies);

        ship.GetComponent<Body>()!.Velocity = new Vector2(10, 0);
        Run(0.3f);
        Assert.InRange(trail.Copies.Count, 3, 5);
        Assert.True(trail.Copies[0].World.M41 < Position(ship).X - 1, "the oldest copy is left behind");

        var draws = SceneRenderer.Build(_scene, 16f / 9f).Sprites.Where(d => d.Entity == ship).ToList();
        Assert.Equal(trail.Copies.Count + 1, draws.Count);
        Assert.All(draws.SkipLast(1), d => Assert.True(d.Tint.A < 0.5f));
        Assert.Equal(Color.Red, draws[^1].Tint);

        ship.GetComponent<Body>()!.Velocity = Vector2.Zero;
        Run(0.3f);
        Assert.Empty(trail.Copies);
    }

    // ---- Animated tiles ----

    [Fact]
    public void AnimatedTiles_StepThroughTheirRun_WithGameTime()
    {
        var level = Add("Level", Vector2.Zero);
        var tilemap = level.AddComponent<Tilemap>();
        tilemap.Image = "tiles.png";
        tilemap.Columns = 8;
        tilemap.Tiles = ["4560"];
        tilemap.AnimatedTiles = "4-7 @4";

        Assert.Equal([4, 5, 6, 0], Shown());

        Run(0.25f);
        Assert.Equal([5, 6, 7, 0], Shown());
        Run(0.55f);
        Assert.Equal([7, 4, 5, 0], Shown());

        // Paused, the water stands still.
        _game.Time.Scale = 0;
        Run(1);
        Assert.Equal([7, 4, 5, 0], Shown());

        List<int> Shown() => SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Entity == level)
            .Select(d => (int)MathF.Round(d.Source.X * 8)).ToList();
    }

    [Fact]
    public void AnimatedTiles_SkipWhatTheyCantRead()
    {
        var runs = Tilemap.ParseAnimations("4-7 @6, 10-8, nonsense, 3-3, 20-25 @0, 30-31");

        Assert.Equal([new TileAnimation(4, 7, 6), new TileAnimation(8, 10, 4), new TileAnimation(30, 31, 4)], runs);
    }

    // ---- Saving ----

    [Fact]
    public void PuzzleComponents_RoundTripThroughTheSceneFile()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var entity = new Entity("Everything");
        var @switch = entity.AddComponent<Switch>();
        @switch.Mode = SwitchMode.Plate;
        @switch.IsOn = true;
        @switch.Targets = "Gate";
        @switch.Key = "Key";
        entity.AddComponent<Inventory>().Items = ["Key 2", "Map"];
        entity.AddComponent<Trail>().Color = Color.Cyan;
        entity.AddComponent<Mover>().Running = false;
        var door = entity.AddComponent<Door>();
        door.Locked = true;
        door.Key = "Gold Key";
        entity.AddComponent<Pickup>().Item = "Key";
        entity.AddComponent<Counter>().Item = "Key";
        entity.AddComponent<Tilemap>().AnimatedTiles = "4-7 @6";
        scene.Add(entity);

        var loaded = serializer.LoadFromString(serializer.SaveToString(scene)).Roots.Single();

        var loadedSwitch = loaded.GetComponent<Switch>()!;
        Assert.Equal(SwitchMode.Plate, loadedSwitch.Mode);
        Assert.True(loadedSwitch.IsOn);
        Assert.Equal("Gate", loadedSwitch.Targets);
        Assert.Equal("Key", loadedSwitch.Key);
        Assert.Equal(["Key 2", "Map"], loaded.GetComponent<Inventory>()!.Items);
        Assert.Equal(Color.Cyan, loaded.GetComponent<Trail>()!.Color);
        Assert.False(loaded.GetComponent<Mover>()!.Running);
        Assert.True(loaded.GetComponent<Door>()!.Locked);
        Assert.Equal("Gold Key", loaded.GetComponent<Door>()!.Key);
        Assert.Equal("Key", loaded.GetComponent<Pickup>()!.Item);
        Assert.Equal("Key", loaded.GetComponent<Counter>()!.Item);
        Assert.Equal("4-7 @6", loaded.GetComponent<Tilemap>()!.AnimatedTiles);
    }

    [Fact]
    public void Switch_ResumedFromASavedScene_KeepsItsTargetsAsTheyWere()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var lever = AddSwitch("Lever", new Vector2(-5, 0), SwitchMode.Lever);
        lever.Targets = "Gate";
        var mover = Add("Gate", Vector2.Zero).AddComponent<Mover>();
        mover.Points = "0 3";
        mover.Mode = MoverMode.Once;
        lever.IsOn = true;
        Run(0.5f);
        var height = Position(mover.Entity!).Y;

        // As rewind does: a new game from a snapshot of the scene.
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var game = new Game(restored, new InputState());
        game.Tick(Step);

        var gate = restored.Entities.Single(e => e.Name == "Gate");
        Assert.True(gate.GetComponent<Mover>()!.Running);
        Assert.True(gate.Transform.Position.Y >= height);
    }

    private sealed class RecordingAudio : IAudio
    {
        public List<string> Played { get; } = [];

        public void Play(string sound, float volume = 1) => Played.Add(sound);

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) { }

        public void StopMusic() { }

        public void StopAll() { }

        public float Volume { get; set; } = 1;

        public float MusicVolume { get; set; } = 1;

        public float SoundVolume { get; set; } = 1;

        public bool Muted { get; set; }
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 v) => 0;
    }
}
