using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Crates to push, bouncers, crumbling platforms and autotiles: level pieces with no code.</summary>
public class LevelPieceTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public LevelPieceTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
        }
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool trigger = false, string? layer = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        collider.Layer = layer;
        _scene.Add(entity);
        return entity;
    }

    // A floor whose top is at y = 0.
    private void AddFloor() => AddBox("Floor", new Vector2(0, -0.5f), new Vector2(60, 1));

    private (Entity Entity, Body Body) AddPlayer(Vector2 position, bool topDown = false)
    {
        var player = AddBox("Player", position, new Vector2(0.8f, 1), layer: "Player");
        var body = player.AddComponent<Body>();
        if (topDown)
            player.AddComponent<TopDownMovement>().Acceleration = 0;
        else
            player.AddComponent<PlatformerMovement>().Acceleration = 0;
        return (player, body);
    }

    private (Entity Entity, Pushable Pushable) AddCrate(Vector2 position, bool topDown = false)
    {
        var crate = AddBox("Crate", position, new Vector2(1, 1), layer: "Crate");
        crate.AddComponent<Body>().GravityScale = topDown ? 0 : 1;
        return (crate, crate.AddComponent<Pushable>());
    }

    // ---- Pushing ----

    [Fact]
    public void Pushable_IsPushedAlong_AtItsShareOfThePushersSpeed_UntilAWallStopsIt()
    {
        AddFloor();
        var (player, body) = AddPlayer(new Vector2(0, 0.5f));
        var (crate, pushable) = AddCrate(new Vector2(2, 0.5f));
        AddBox("Wall", new Vector2(8, 2), new Vector2(1, 4));
        Run(0.2f);

        _input.SetKey(Key.D, true);
        Run(0.4f);
        var gap = crate.Transform.Position.X - player.Transform.Position.X;
        Assert.Equal(0.9f, gap, 2); // touching
        var from = crate.Transform.Position.X;
        Run(0.5f);
        var moved = crate.Transform.Position.X - from;
        Assert.Equal(7 * pushable.Speed * 0.5f, moved, 0.2f);
        Assert.Equal(0, body.WallSide); // a crate that gives way isn't a wall

        // Against the wall it won't budge, and the player is stopped.
        Run(3);
        Assert.Equal(7, crate.Transform.Position.X, 2);
        Assert.Equal(6.1f, player.Transform.Position.X, 2);
        Assert.Equal(1, body.WallSide);
    }

    [Fact]
    public void Pushable_OnlyMovesForItsLayers()
    {
        AddFloor();
        var (player, _) = AddPlayer(new Vector2(0, 0.5f));
        var (crate, pushable) = AddCrate(new Vector2(2, 0.5f));
        pushable.Layers = "Robot";
        Run(0.2f);

        _input.SetKey(Key.D, true);
        Run(1);
        Assert.Equal(2, crate.Transform.Position.X, 3);
        Assert.Equal(1.1f, player.Transform.Position.X, 2);
    }

    [Fact]
    public void Pushable_PushesTheCrateInFrontOfIt()
    {
        AddFloor();
        AddPlayer(new Vector2(0, 0.5f));
        var (first, _) = AddCrate(new Vector2(2, 0.5f));
        var (second, _) = AddCrate(new Vector2(3, 0.5f));
        Run(0.2f);

        _input.SetKey(Key.D, true);
        Run(1);
        Assert.True(second.Transform.Position.X > 3.5f);
        Assert.Equal(1, second.Transform.Position.X - first.Transform.Position.X, 2);
    }

    [Fact]
    public void Pushable_InAPlatformer_CanBeStoodOn_NotPushedDown()
    {
        AddFloor();
        var (crate, _) = AddCrate(new Vector2(0, 0.5f));
        var (player, body) = AddPlayer(new Vector2(0, 3));
        Run(1);
        Assert.True(body.IsGrounded);
        Assert.Equal(crate, body.Ground);
        Assert.Equal(1.5f, player.Transform.Position.Y, 2);
        Assert.Equal(0.5f, crate.Transform.Position.Y, 2);
    }

    [Fact]
    public void Pushable_InATopDownGame_IsPushedUpAndDownToo_OntoAPlate()
    {
        _game.Physics.Gravity = Vector2.Zero;
        var (_, _) = AddPlayer(new Vector2(0, 0), topDown: true);
        var (crate, _) = AddCrate(new Vector2(0, 1.5f), topDown: true);
        var plate = AddBox("Plate", new Vector2(0, 5), new Vector2(1, 1), trigger: true);
        var @switch = plate.AddComponent<Switch>();
        @switch.Mode = SwitchMode.Plate;
        @switch.Layers = "Crate";

        _input.SetKey(Key.W, true);
        Run(1);
        _input.SetKey(Key.W, false);
        Assert.InRange(crate.Transform.Position.Y, 4.5f, 5.5f);
        Run(0.1f);
        Assert.True(@switch.IsOn);
    }

    // ---- Bouncers ----

    [Fact]
    public void Bouncer_LaunchesWhatLandsOnIt_HighAsItsStrength()
    {
        AddFloor();
        var spring = AddBox("Spring", new Vector2(0, 0.25f), new Vector2(1, 0.5f));
        var bouncer = spring.AddComponent<Bouncer>();
        bouncer.Strength = 20;
        var bounced = 0;
        bouncer.Bounced += _ => bounced++;
        var (player, body) = AddPlayer(new Vector2(0, 2));

        var peak = float.NegativeInfinity;
        for (var t = 0f; t < 2; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
            peak = Math.Max(peak, player.Transform.Position.Y);
        }

        // v² / 2g = 400 / 50 = 8 above where it bounced, give or take a frame's worth.
        Assert.InRange(peak - 1, 7.2f, 8.2f);
        Assert.True(bounced >= 1);
        Assert.Equal(new Vector3(1, 1, 1), spring.Transform.Scale); // squashed, and back again
    }

    [Fact]
    public void Bouncer_GivesBackAirJumps_InMidAir()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        var pad = AddBox("Pad", new Vector2(0, 5), new Vector2(2, 1), trigger: true);
        pad.AddComponent<Bouncer>().Strength = 15;
        var (player, body) = AddPlayer(new Vector2(0, 3));
        var movement = player.GetComponent<PlatformerMovement>()!;
        movement.AirJumps = 1;
        movement.ShortHop = false;
        body.Velocity = new Vector2(0, 12);

        // Spend the air jump on the way up into the pad.
        _input.SetKey(Key.Space, true);
        Run(Step);
        Assert.Equal(0, movement.JumpsLeft);
        _input.SetKey(Key.Space, false);
        var launched = false;
        for (var t = 0f; t < 0.6f && !launched; t += Step)
        {
            Run(Step);
            launched = movement.JumpsLeft == 1;
        }

        Assert.True(launched, "passing up through the pad should give the air jump back");
        Assert.True(body.Velocity.Y > 14);
    }

    [Fact]
    public void Bouncer_OnlyLaunchesFromItsSide()
    {
        AddFloor();
        var spring = AddBox("Spring", new Vector2(2, 0.25f), new Vector2(1, 0.5f));
        spring.AddComponent<Bouncer>();
        var (_, body) = AddPlayer(new Vector2(0, 0.5f));
        Run(0.2f);

        // Walking into its side: it's just in the way.
        _input.SetKey(Key.D, true);
        Run(0.5f);
        Assert.True(body.Velocity.Y < 15);
    }

    [Fact]
    public void Bouncer_Sideways_PushesAlongItsDirection()
    {
        _game.Physics.Gravity = Vector2.Zero;
        var pad = AddBox("Pad", Vector2.Zero, new Vector2(1, 1), trigger: true);
        var bouncer = pad.AddComponent<Bouncer>();
        bouncer.Direction = new Vector2(1, 0);
        bouncer.Strength = 10;
        var ball = AddBox("Ball", new Vector2(0.4f, 3), new Vector2(0.5f, 0.5f));
        var body = ball.AddComponent<Body>();
        body.Velocity = new Vector2(0, -5);

        Run(0.6f);
        Assert.Equal(10, body.Velocity.X, 3);
        Assert.Equal(-5, body.Velocity.Y, 3);
    }

    // ---- Crumbling platforms ----

    [Fact]
    public void Crumble_ShakesWhenStoodOn_FallsAway_AndComesBackOnceClear()
    {
        var ledge = AddBox("Ledge", new Vector2(0, -0.25f), new Vector2(2, 0.5f));
        ledge.AddComponent<Sprite>();
        var crumble = ledge.AddComponent<Crumble>();
        crumble.Delay = 0.5f;
        crumble.Respawn = 1;
        var (fell, returned) = (0, 0);
        crumble.Fell += _ => fell++;
        crumble.Returned += _ => returned++;
        AddBox("Floor", new Vector2(0, -6), new Vector2(10, 1));
        var (player, body) = AddPlayer(new Vector2(0, 1));

        Run(0.3f);
        Assert.True(body.IsGrounded);
        Assert.True(crumble.IsShaking);
        Assert.NotEqual(Vector2.Zero, crumble.ShakeOffset);
        Assert.Equal(new Vector3(0, -0.25f, 0), ledge.Transform.Position); // only drawn shaken

        // The shake shows in what's drawn, not where it is.
        var drawn = SceneRenderer.Build(_scene, 1).Sprites.First(s => s.Entity == ledge);
        Assert.Equal(crumble.ShakeOffset.X, drawn.World.M41, 3);

        Run(0.4f);
        Assert.True(crumble.Crumbled);
        Assert.False(ledge.IsEnabled);
        Assert.Equal(1, fell);
        Run(0.5f);
        Assert.True(player.Transform.Position.Y < -2, "the player should fall once it's gone");

        // It waits for the player to be out of the way: put them back where it was.
        player.Transform.Position = new Vector3(0, -0.25f, 0);
        body.Velocity = Vector2.Zero;
        player.GetComponent<PlatformerMovement>()!.Controllable = false;
        body.GravityScale = 0;
        Run(1.5f);
        Assert.True(crumble.Crumbled);
        player.Transform.Position = new Vector3(5, 3, 0);
        Run(Step * 2);
        Assert.False(crumble.Crumbled);
        Assert.True(ledge.IsEnabled);
        Assert.Equal(1, returned);
    }

    [Fact]
    public void Crumble_StaysGone_AcrossASaveAndLoad()
    {
        var ledge = new Entity("Ledge") { IsEnabled = false };
        ledge.AddComponent<BoxCollider>();
        ledge.AddComponent<Crumble>().Crumbled = true;
        _scene.Add(ledge);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene));
        Assert.True(loaded.Entities.Single().GetComponent<Crumble>()!.Crumbled);
    }

    [Fact]
    public void Crumble_Respawn0_NeverComesBack()
    {
        var ledge = AddBox("Ledge", new Vector2(0, -0.25f), new Vector2(2, 0.5f));
        var crumble = ledge.AddComponent<Crumble>();
        crumble.Delay = 0;
        crumble.Respawn = 0;
        AddPlayer(new Vector2(0, 0.5f));
        Run(0.2f);
        Assert.True(crumble.Crumbled);
        Run(5);
        Assert.True(crumble.Crumbled);
        Assert.False(ledge.IsEnabled);
    }

    // ---- Autotiles ----

    [Fact]
    public void AutoTiles_ShowThePieceThatJoinsTheirNeighbors()
    {
        var map = new Tilemap { AutoTiles = "16-31" };
        map.Tiles = [
            ".g.",
            "ggg",
            ".g.",
            "...",
            "g2.",
        ];
        var g = Tilemap.TileOf('g');
        Assert.Equal(16, g);

        Assert.Equal(16 + 1 + 2 + 4 + 8, map.ShownTile(1, 1, g)); // the middle joins every side
        Assert.Equal(16 + 4, map.ShownTile(1, 0, g));             // the top joins down
        Assert.Equal(16 + 1, map.ShownTile(1, 2, g));             // the bottom joins up
        Assert.Equal(16 + 2, map.ShownTile(0, 1, g));             // the left joins right
        Assert.Equal(16 + 8, map.ShownTile(2, 1, g));             // the right joins left
        Assert.Equal(16, map.ShownTile(0, 4, g));                 // a tile from outside the run doesn't join
        Assert.Equal(2, map.ShownTile(1, 4, 2));                  // ordinary tiles are themselves
    }

    [Fact]
    public void AutoTiles_AreDrawnByTheRenderer_AndAnimateToo()
    {
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        map.Image = "tiles.png";
        map.Columns = 8;
        map.Rows = 8;
        map.AutoTiles = "16";
        map.Tiles = ["gh"];
        _scene.Add(entity);

        var sources = SceneRenderer.TileDraws(entity, map).Select(d => d.Source).ToList();
        Assert.Equal(map.TileRect(16 + 2), sources[0]);
        Assert.Equal(map.TileRect(16 + 8), sources[1]);
    }

    [Theory]
    [InlineData("16-31", new[] { 16 })]
    [InlineData("0, 16", new[] { 0, 16 })]
    [InlineData("16-30", new int[0])]   // not 16 long
    [InlineData("21440", new int[0])]  // runs past the highest tile number
    [InlineData("x, 46-61", new[] { 46 })]
    public void AutoTiles_Parse(string text, int[] firsts) => Assert.Equal(firsts, Tilemap.ParseAutoTiles(text));
}
