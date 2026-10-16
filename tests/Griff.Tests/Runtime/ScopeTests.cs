using System.Numerics;
using Griff.Input;
using Griff.Modules.Contraptions;
using Griff.Modules.Puzzles;
using Griff.Modules.Strategy;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Genres sharing a scene: what once worked on the whole scene (a move history, a turn order, the sandbox tools) works
/// within a group or an area instead, named by its Within.
/// </summary>
public sealed class ScopeTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public ScopeTests()
    {
        _game = new Game(_scene, _input);
        _game.Physics.Gravity = Vector2.Zero;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(0.3f);
    }

    private Entity Add(string name, Vector2 at = default, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        _scene.Add(entity, parent);
        return entity;
    }

    private Entity Room(string name, Vector2 at, Vector2 size)
    {
        var room = Add(name, at);
        room.AddComponent<Room>().Size = size;
        return room;
    }

    // ----- Groups and areas -----

    [Fact]
    public void AScope_CoversWhatsInsideItsEntity_AndWhatStandsInItsArea()
    {
        var room = Room("Arena", new Vector2(10, 0), new Vector2(4, 4));
        var inside = Add("Inside", new Vector2(-50, 0), room);
        var standing = Add("Standing", new Vector2(11, 1));
        var away = Add("Away", new Vector2(20, 0));
        var group = Add("Group");
        var member = Add("Member", new Vector2(99, 99), group);

        var arena = Scope.Of(away, "Arena");
        Assert.True(arena.Covers(inside));
        Assert.True(arena.Covers(standing));
        Assert.False(arena.Covers(away));
        Assert.True(arena.Covers(new Vector2(9, -1)));
        Assert.Equal(new Box(new Vector2(10, 0), new Vector2(2, 2)), arena.Area);

        // An entity with no area is a group: only what's inside it.
        var grouped = Scope.Of(away, "Group");
        Assert.True(grouped.Covers(member));
        Assert.False(grouped.Covers(standing));
        Assert.Null(grouped.Area);

        Assert.True(Scope.Of(away, " ").IsWholeScene);
        Assert.True(Scope.Of(away, null).Covers(away));
        Assert.False(Scope.Of(away, "Nowhere").Covers(away));
    }

    [Fact]
    public void Areas_AreFound_ByTheirAttribute()
    {
        var map = Add("Map", new Vector2(2, 0)).AddComponent<Tilemap>();
        map.Tiles = ["...", "..."];
        Assert.Equal(new Box(new Vector2(3.5f, -1), new Vector2(1.5f, 1)), ComponentAreas.Of(map.Entity!));

        var crate = Add("Crate", new Vector2(5, 5));
        crate.AddComponent<BoxCollider>().Size = new Vector2(2, 2);
        Assert.Equal(new Box(new Vector2(5, 5), new Vector2(1, 1)), ComponentAreas.Of(crate));

        var camera = Add("Camera");
        var commander = camera.AddComponent<Commander>();
        (commander.MapCenter, commander.MapSize) = (new Vector2(1, 2), new Vector2(10, 6));
        Assert.Equal(new Box(new Vector2(1, 2), new Vector2(5, 3)), ComponentAreas.Of(camera));

        Assert.Null(ComponentAreas.Of(Add("Nothing")));
        Assert.Throws<NotSupportedException>(() => ComponentAreas.Has(typeof(BadArea)));
    }

    private sealed class BadArea : Component
    {
        [Area]
        public Vector2 Size { get; set; }
    }

    // ----- Move history -----

    // A level of two puzzle rooms side by side, walled off from each other: cells (1..3, 1) and (6..8, 1).
    private (GridMovement Player, Entity CrateA, Entity CrateB, MoveHistory A, MoveHistory B) TwoPuzzles()
    {
        var level = Add("Level");
        level.AddComponent<Tilemap>().Tiles = ["1111111111", "1...11...1", "1111111111"];
        var left = Room("Left Room", new Vector2(2.5f, -1.5f), new Vector2(4, 3));
        var right = Room("Right Room", new Vector2(7.5f, -1.5f), new Vector2(4, 3));
        var a = left.AddComponent<MoveHistory>();
        a.Within = "Left Room";
        var b = right.AddComponent<MoveHistory>();
        b.Within = "Right Room";

        var player = Add("Player", new Vector2(1.5f, -1.5f));
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>();
        var movement = player.AddComponent<GridMovement>();
        return (movement, Crate("Crate A", 2, left), Crate("Crate B", 7, right), a, b);

        Entity Crate(string name, int x, Entity room)
        {
            var crate = Add(name, new Vector2(x + 0.5f - room.Transform.Position.X, -1.5f - room.Transform.Position.Y), room);
            crate.AddComponent<BoxCollider>().Size = new Vector2(0.9f);
            crate.AddComponent<Body>();
            crate.AddComponent<GridMovement>();
            crate.AddComponent<Pushable>();
            return crate;
        }
    }

    [Fact]
    public void AMoveHistoryWithin_ARoom_CountsAndTakesBackOnlyWhatsThere()
    {
        var (player, crateA, crateB, a, b) = TwoPuzzles();
        Run(0.1f);

        Tap(Key.Right);
        Assert.Equal(new Vector2(3.5f, -1.5f), new Vector2(crateA.Transform.WorldPosition.X, crateA.Transform.WorldPosition.Y));
        Assert.Equal((1, 0), (a.Moves, b.Moves));

        // The other room's crate, moved by a script, isn't this history's to put back.
        crateB.GetComponent<GridMovement>()!.Step(1, 0);
        Run(0.3f);
        Tap(Key.Z);
        Assert.Equal(2.5f, crateA.Transform.WorldPosition.X, 3);
        Assert.Equal(1.5f, player.Entity!.Transform.WorldPosition.X, 3);
        Assert.Equal(8.5f, crateB.Transform.WorldPosition.X, 3);
        Assert.Equal((0, 0), (a.Moves, b.Moves));
    }

    [Fact]
    public void AMoveHistoryWithin_ARoom_HearsUndo_OnlyWhileAPlayerWithTheInputIsThere()
    {
        var (player, crateA, _, a, b) = TwoPuzzles();
        Run(0.1f);
        Tap(Key.Right);
        var undone = new List<MoveHistory>();
        a.Undone += undone.Add;
        b.Undone += undone.Add;

        // Taken out of the room (by a door, say), its Z does nothing there.
        player.Entity!.Transform.Teleport(new Vector3(7.5f, -1.5f, 0));
        player.Started = false;
        Run(0.1f);
        Tap(Key.Z);
        Assert.Empty(undone);
        Assert.Equal(3.5f, crateA.Transform.WorldPosition.X, 3);
    }

    // ----- Turns -----

    private TurnOrder Turns(string name, string within, Vector2 at, Vector2 size)
    {
        var room = Room(name, at, size);
        var order = room.AddComponent<TurnOrder>();
        (order.Within, order.Between, order.ComputerTeams, order.Teams) = (within, 0, "", "Red, Blue");
        return order;
    }

    private TurnTaker Taker(string name, string side, Vector2 at)
    {
        var taker = Add(name, at).AddComponent<TurnTaker>();
        (taker.Team, taker.Actions) = (side, 1);
        return taker;
    }

    [Fact]
    public void TurnOrdersWithin_TheirOwnAreas_EachTakeTheirOwnTurns()
    {
        var west = Turns("West", "West", new Vector2(-10, 0), new Vector2(8, 8));
        var east = Turns("East", "East", new Vector2(10, 0), new Vector2(8, 8));
        var westRed = Taker("West Red", "Red", new Vector2(-10, 0));
        var westBlue = Taker("West Blue", "Blue", new Vector2(-9, 0));
        var eastRed = Taker("East Red", "Red", new Vector2(10, 0));
        var eastBlue = Taker("East Blue", "Blue", new Vector2(11, 0));
        var free = Taker("Free", "Blue", new Vector2(0, 0));
        Run(Frame);

        Assert.Equal(("Red", "Red"), (west.Current, east.Current));
        Assert.True(westRed.IsTurn && eastRed.IsTurn);
        Assert.False(westBlue.IsTurn || eastBlue.IsTurn);
        Assert.False(_game.Waiting(free.Entity));
        Assert.Equal(1, west.Left("Blue"));

        // Red's go in the west ends the west's turn only.
        westRed.Act();
        Run(Frame * 2);
        Assert.Equal(("Blue", "Red"), (west.Current, east.Current));

        // Walking into the east, the free one joins its turns.
        free.Entity!.Transform.Position = new Vector3(9, 1, 0);
        Run(Frame);
        Assert.Same(east, _game.TurnOrderOf(free.Entity));
        Assert.Equal(2, east.Left("Blue"));
        Assert.True(_game.Waiting(free.Entity));
    }

    [Fact]
    public void EndTurn_EndsTheTurnsOfTheOrdersAPersonMayEnd_AndAButtonEndsTheOneItsIn()
    {
        var west = Turns("West", "West", new Vector2(-10, 0), new Vector2(8, 8));
        var east = Turns("East", "East", new Vector2(10, 0), new Vector2(8, 8));
        west.Teams = east.Teams = "Red, Blue";
        Taker("West Red", "Red", new Vector2(-10, 0)).Actions = 0;
        Taker("West Blue", "Blue", new Vector2(-9, 0)).Actions = 0;
        Taker("East Red", "Red", new Vector2(10, 0)).Actions = 0;
        Taker("East Blue", "Blue", new Vector2(11, 0)).Actions = 0;
        var button = Add("End", new Vector2(10, -3)).AddComponent<Button>();
        button.Action = ButtonAction.EndTurn;
        Run(Frame);

        Click(new Vector2(10, -3));
        Run(Frame * 2);
        Assert.Equal(("Red", "Blue"), (west.Current, east.Current));

        Tap(Key.Tab);
        Assert.Equal(("Blue", "Red"), (west.Current, east.Current));
    }

    [Fact]
    public void TakingTurns_AddsNoGarbage_FromFrameToFrame()
    {
        var west = Turns("West", "West", new Vector2(-10, 0), new Vector2(8, 8));
        west.Entity!.AddComponent<Text>();
        (west.TurnTime, west.Format) = (60, "{0}'s turn, round {1} ({2})");
        Turns("East", "East", new Vector2(10, 0), new Vector2(8, 8));
        var whole = Add("Whole").AddComponent<TurnOrder>();
        (whole.Teams, whole.ComputerTeams) = ("Player, Enemy", "Enemy");
        for (var i = 0; i < 4; i++)
        {
            Taker($"West {i}", i % 2 == 0 ? "Red" : "Blue", new Vector2(-10 + i * 0.5f, 0)).Actions = 0;
            Taker($"East {i}", i % 2 == 0 ? "Red" : "Blue", new Vector2(10 + i * 0.5f, 0)).Actions = 0;
            Taker($"Player {i}", i % 2 == 0 ? "Player" : "Enemy", new Vector2(0, i)).Actions = 0;
        }

        var counter = Add("Left").AddComponent<Counter>();
        (counter.Track, counter.Output) = ("West", "Left:Blue");
        Run(1);

        Assert.Equal(0, Allocations.PerCall(() => _game.Tick(Frame), 10));
    }

    // ----- Sandbox tools -----

    [Fact]
    public void SandboxToolsWithin_AWorkshop_WorkOnlyOnWhatsThere_AndSpawnOnlyThere()
    {
        var camera = Add("Camera");
        camera.AddComponent<Camera>().Size = 8;
        Room("Workshop", new Vector2(-5, 0), new Vector2(6, 6));
        var user = Add("User", new Vector2(-5, 0));
        var tools = user.AddComponent<SandboxTools>();
        (tools.Tools, tools.Props, tools.Within) = ("Remove, Spawn", "Crate", "Workshop");
        var inside = Add("Inside", new Vector2(-6, 1));
        inside.AddComponent<BoxCollider>();
        inside.AddComponent<Body>();
        var outside = Add("Outside", new Vector2(5, 1));
        outside.AddComponent<BoxCollider>();
        outside.AddComponent<Body>();
        _game.PrefabLoader = name =>
        {
            var crate = new Entity(name);
            crate.AddComponent<BoxCollider>();
            return crate;
        };
        Run(Frame);

        Click(new Vector2(5, 1));
        Run(Frame);
        Assert.NotNull(outside.Scene);
        Click(new Vector2(-6, 1));
        Run(Frame);
        Assert.Null(inside.Scene);

        Pick("Spawn");
        Click(new Vector2(5, -2));
        Assert.Null(_game.Find("Crate"));
        Click(new Vector2(-4, -2));
        Assert.NotNull(_game.Find("Crate"));
    }

    private void Click(Vector2 at)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    private void Pick(string tool) => Click(_game.HudButtons.Single(b => b.Kind == ToolHud.Tool && b.Label == tool).Rect.Center);

    // ----- Flags -----

    [Fact]
    public void APanelThatNeedsAFlag_OpensOnTheSameMessage_WhoseTriggerSetsIt()
    {
        var panel = Add("Secret");
        panel.IsEnabled = false;
        var shown = panel.AddComponent<Panel>();
        (shown.OpenMessage, shown.NeedsFlag, shown.PauseGame) = ("Found", "Treasure", false);
        var trigger = Add("Chest").AddComponent<Trigger>();
        (trigger.Message, trigger.Do, trigger.With) = ("Found", "Set Flag", "Treasure");
        Run(Frame);

        _game.Send("Found");
        Run(Frame);

        Assert.True(_game.HasFlag("Treasure"));
        Assert.True(panel.IsEnabled);
    }
}
