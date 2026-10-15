using System.Numerics;
using Griff.Input;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Contraptions;
using Griff.Modules.FallingBlocks;
using Griff.Modules.FirstPerson;
using Griff.Modules.Match3;
using Griff.Modules.Strategy;
using Griff.Modules.Tactics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Genres sharing a scene: whatever reads the player's input says so with [Focus] (a movement's Controllable, a board's,
/// the strategy commander's), and the Focus action hands the input from one to another.
/// </summary>
public sealed class FocusTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public FocusTests()
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

    private void Click(Vector2 at)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    private Entity Add(string name, Entity? parent = null)
    {
        var entity = new Entity(name);
        _scene.Add(entity, parent);
        return entity;
    }

    // A top-down player in the middle of the scene.
    private TopDownMovement Player()
    {
        var player = Add("Player");
        player.AddComponent<BoxCollider>();
        player.AddComponent<Body>();
        return player.AddComponent<TopDownMovement>();
    }

    // No lines yet; swapping the last two gems of the bottom row lines up three 0s. Its top left is at (20, 0).
    private MatchBoard Board()
    {
        var entity = Add("Board");
        entity.Transform.Position = new Vector3(20, 0, 0);
        var map = entity.AddComponent<Tilemap>();
        (map.Solid, map.Tiles) = (false, ["1234", "2345", "3451", "0010"]);
        var board = entity.AddComponent<MatchBoard>();
        (board.Columns, board.Rows, board.Deal, board.Seed, board.ShuffleWhenStuck, board.Refill) = (4, 4, false, 7, false, false);
        return board;
    }

    private static Vector2 Cell(int x, int y) => new(20 + x + 0.5f, -(y + 0.5f));

    [Fact]
    public void WhatTakesInput_IsFound_ByItsFocusProperty_AndGetsAFocusAction()
    {
        foreach (var type in new[]
                 {
                     typeof(PlatformerMovement), typeof(TopDownMovement), typeof(GridMovement), typeof(FirstPersonMovement), typeof(MatchBoard),
                     typeof(FallingBoard), typeof(Commander), typeof(TacticsControls), typeof(SandboxTools), typeof(BlockBuilder),
                 })
        {
            Assert.True(ComponentFocus.Has(type), type.Name);
            Assert.Equal("Controllable", Assert.Single(ComponentFocus.PropertiesOf(type)).Name);
            Assert.Contains(ComponentActions.Of(type), a => a.Name == "Focus" && a.ValueType is null);
        }

        // A script's own counts the same way; one that isn't true or false is refused.
        Assert.True(ComponentFocus.Has(typeof(Steering)));
        Assert.Contains(ComponentActions.Of(typeof(Steering)), a => a.Name == "Focus");
        Assert.False(ComponentFocus.Has(typeof(Health)));
        Assert.DoesNotContain(ComponentActions.Of(typeof(Health)), a => a.Name == "Focus");
        Assert.Throws<NotSupportedException>(() => ComponentFocus.Has(typeof(BadFocus)));
    }

    [Fact]
    public void ATrigger_FocusesABoard_ThePlayerStopsOnTheArrows_AndFocusingThePlayerGivesThemBack()
    {
        var player = Player();
        var board = Board();
        var arcade = Add("Arcade");
        var play = arcade.AddComponent<Trigger>();
        (play.Message, play.Do, play.Targets) = ("Play", "Focus", "Board");
        var quit = arcade.AddComponent<Trigger>();
        (quit.Message, quit.Do, quit.Targets) = ("Quit", "Focus", "Player");
        Run(Frame);

        _game.Send("Play");
        Assert.True(board.Controllable);
        Assert.False(player.Controllable);
        Assert.False(Game.HasFocus(player.Entity!));

        _input.SetKey(Key.Right, true);
        Run(0.5f);
        _input.SetKey(Key.Right, false);
        Assert.Equal(0, player.Entity!.Transform.Position.X, 3);

        _game.Send("Quit");
        Assert.True(player.Controllable);
        Assert.False(board.Controllable);
        _input.SetKey(Key.Right, true);
        Run(0.5f);
        _input.SetKey(Key.Right, false);
        Assert.True(player.Entity!.Transform.Position.X > 1);
    }

    [Fact]
    public void ABoard_WithoutFocus_IgnoresTheMouse_AndPutsDownWhatItHeld()
    {
        var board = Board();
        Run(Frame);
        Click(Cell(2, 3));
        Assert.Equal((2, 3), board.Picked);

        board.Controllable = false;
        Run(Frame);
        Assert.Null(board.Picked);
        Click(Cell(2, 3));
        Click(Cell(3, 3));
        Run(1.5f);
        Assert.Equal(0, board.Swaps);

        _game.Do(board.Entity!, "Focus");
        Click(Cell(2, 3));
        Click(Cell(3, 3));
        Run(1.5f);
        Assert.Equal(1, board.Swaps);
    }

    [Fact]
    public void AFallingBoard_WithoutFocus_LetsItsPieceFall_ButTakesNoKeys()
    {
        var entity = Add("Well");
        entity.AddComponent<Tilemap>().Solid = false;
        var board = entity.AddComponent<FallingBoard>();
        (board.Columns, board.Rows, board.Seed, board.FallTime) = (10, 20, 3, 0.2f);
        board.Controllable = false;
        Run(0.1f);
        var (x, y) = board.PieceAt!.Value;

        for (var i = 0; i < 3; i++)
        {
            _input.SetKey(Key.Left, true);
            Run(Frame);
            _input.SetKey(Key.Left, false);
            Run(Frame);
        }

        Run(0.5f);
        Assert.Equal(x, board.PieceAt!.Value.X);
        Assert.NotEqual(y, board.PieceAt!.Value.Y);
    }

    [Fact]
    public void FocusOnAGroup_GoesToWhatsInside_AndOnlyTakesItFromTheSamePlayers()
    {
        var mine = Player();
        var cabinet = Add("Cabinet");
        var inside = Add("Board", cabinet).AddComponent<Steering>();
        inside.Controllable = false;
        var other = Add("Other Player");
        var theirs = other.AddComponent<TopDownMovement>();
        Run(Frame);

        _game.Focus(cabinet);
        Assert.True(inside.Controllable);
        Assert.False(mine.Controllable);
        Assert.False(theirs.Controllable);

        // Online, a player's own character only gives up focus to what that player (or everyone) plays.
        _game.Focus(mine.Entity!);
        mine.Entity!.AddComponent<Networked>().Owner = 1;
        other.AddComponent<Networked>().Owner = 2;
        theirs.Controllable = true;
        _game.Focus(mine.Entity!);
        Assert.True(theirs.Controllable);
        Assert.False(inside.Controllable);
    }

    [Fact]
    public void TheTacticsControls_AndTheStrategyCommander_WithoutFocus_IgnoreTheirInput()
    {
        var camera = Add("Camera");
        camera.AddComponent<Camera>().Size = 8;
        var commander = camera.AddComponent<Commander>();
        (commander.EdgeScroll, commander.MapSize, commander.Controllable) = (false, new Vector2(80, 30), false);
        Run(0.1f);
        _input.SetKey(Key.Right, true);
        Run(0.5f);
        Assert.Equal(0, camera.Transform.Position.X, 3);

        _game.Do(camera, "Focus");
        Run(0.5f);
        _input.SetKey(Key.Right, false);
        Assert.True(camera.Transform.Position.X > 1);
    }

    [Fact]
    public void TheSandboxTools_InUse_AreTheOnesWithFocus()
    {
        var a = Add("A").AddComponent<SandboxTools>();
        var b = Add("B").AddComponent<SandboxTools>();
        Run(Frame);
        Assert.Same(a, _game.Tools);

        _game.Focus(b.Entity!);
        Run(Frame);
        Assert.Same(b, _game.Tools);
        Assert.False(a.Controllable);

        b.Controllable = false;
        Run(Frame);
        Assert.Null(_game.Tools);
        Assert.Empty(_game.HudButtons);
    }

    [Fact]
    public void InANetworkGame_TheHostSaysWhoHasFocus_EvenOnAPlayersOwnCharacter()
    {
        var player = Player();
        var slots = NetSchema.Build(player.Entity!, ComponentRegistry.CreateDefault(), ownerMoved: true);
        var focus = slots.Single(s => s.Component == player && s.Property?.Name == "Controllable");
        Assert.False(focus.OwnerOwned);
        Assert.Contains(slots, s => s.Component == player && s.Property?.Name == "Speed" && s.OwnerOwned);
    }

    /// <summary>A script's own component that takes input.</summary>
    private sealed class Steering : Component
    {
        [Focus]
        public bool Controllable { get; set; } = true;
    }

    private sealed class BadFocus : Component
    {
        [Focus]
        public int Controllable { get; set; }
    }
}
