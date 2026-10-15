using System.Numerics;
using Griff.Agent;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Web;

namespace Griff.Tests.Runtime;

/// <summary>
/// Several people at one machine: the controllers kept apart, the keyboard split in two, the keys and controllers shared
/// out by how many play, each player's choice kept, and things a Played By gives to a player answering to theirs alone.
/// </summary>
public sealed class LocalPlayersTests
{
    private const float Frame = 1 / 60f;
    private readonly InputState _input = new();

    // A scene with a character for each player: Player 1's and Player 2's, each a Played By.
    private Game TwoPlayers(out Entity first, out Entity second)
    {
        var scene = new Scene();
        first = new Entity("One");
        first.AddComponent<PlayedBy>().Player = 1;
        second = new Entity("Two");
        second.AddComponent<PlayedBy>().Player = 2;
        scene.Add(first);
        scene.Add(second);
        var game = new Game(scene, _input);
        game.Tick(Frame);
        _input.EndFrame();
        return game;
    }

    [Fact]
    public void The_game_by_itself_reads_every_controller_and_each_is_kept_apart_too()
    {
        _input.SetGamepadConnected(0, true);
        _input.SetGamepadConnected(1, true);
        _input.SetGamepad(1, [GamepadButton.A], new Vector2(0, -1), Vector2.Zero);

        Assert.Equal(2, _input.GamepadCount);
        Assert.True(_input.IsGamepadConnected);
        Assert.True(_input.IsDown(GamepadButton.A));
        Assert.True(_input.WasPressed("Jump"));
        Assert.Equal(-1, _input.Vertical);
        Assert.True(_input.IsDown(1, GamepadButton.A));
        Assert.False(_input.IsDown(0, GamepadButton.A));
        Assert.Equal(Vector2.Zero, _input.LeftStickOf(0));

        // Held on both, let go on one: still held.
        _input.SetGamepad(0, [GamepadButton.A], Vector2.Zero, Vector2.Zero);
        _input.EndFrame();
        _input.SetGamepad(1, [], Vector2.Zero, Vector2.Zero);
        Assert.True(_input.IsDown(GamepadButton.A));
        Assert.False(_input.WasReleased(GamepadButton.A));
        Assert.True(_input.WasReleased(1, GamepadButton.A));
    }

    [Fact]
    public void Two_at_one_keyboard_have_its_two_sides()
    {
        var game = TwoPlayers(out var first, out var second);
        var (one, two) = (game.InputOf(first), game.InputOf(second));
        Assert.Equal((KeySet.Left, KeySet.Right), (((LocalPlayerInput)one).Keys, ((LocalPlayerInput)two).Keys));

        // WASD and F/G are the left player's; the arrows and K/L the right's.
        _input.SetKey(Key.D, true);
        _input.SetKey(Key.F, true);
        Assert.Equal((1f, 0f), (one.Horizontal, two.Horizontal));
        Assert.True(one.WasPressed("Punch"));
        Assert.False(two.WasPressed("Punch"));
        _input.EndFrame();
        _input.SetKey(Key.Left, true);
        _input.SetKey(Key.K, true);
        Assert.Equal((1f, -1f), (one.Horizontal, two.Horizontal));
        Assert.True(two.WasPressed("Punch"));
        Assert.False(one.WasPressed("Kick"));
        _input.SetKey(Key.L, true);
        Assert.True(two.WasPressed("Kick"));

        // An action without keys of its own on each side keeps its keys on that side: Undo is Z on the left, U on the right.
        _input.SetKey(Key.U, true);
        Assert.True(two.IsDown("Undo"));
        Assert.False(one.IsDown("Undo"));
        Assert.Equal([Key.Z], ((LocalPlayerInput)one).KeysOf("Undo", KeySet.Left));

        // The mouse is nobody's when the keyboard is split; the game's own input still reads everything.
        _input.SetMouseButton(MouseButton.Left, true);
        Assert.False(one.IsDown(MouseButton.Left));
        Assert.True(game.Input.IsDown("Punch"));
    }

    [Fact]
    public void The_keys_and_controllers_are_shared_out_by_how_many_play()
    {
        var game = TwoPlayers(out _, out _);
        var players = game.LocalPlayers;
        Assert.Equal(2, players.Playing);
        Assert.Equal(("Left keys (WASD)", "Right keys (arrows)"), (players.Describe(1, 2), players.Describe(2, 2)));

        // One controller: one at the keyboard, one on it.
        _input.SetGamepadConnected(2, true);
        Assert.Equal((KeySet.All, -1), players.DeviceOf(1, 2));
        Assert.Equal((KeySet.None, 2), players.DeviceOf(2, 2));
        Assert.Equal(("Keyboard", "Gamepad 1"), (players.Describe(1, 2), players.Describe(2, 2)));

        // Two: one each, the first with the keyboard too.
        _input.SetGamepadConnected(0, true);
        Assert.Equal((KeySet.All, 0), players.DeviceOf(1, 2));
        Assert.Equal((KeySet.None, 2), players.DeviceOf(2, 2));
        Assert.Equal(("Keyboard and Gamepad 1", "Gamepad 2"), (players.Describe(1, 2), players.Describe(2, 2)));

        // Three playing on two controllers: the first at the keyboard, a controller each for the others.
        Assert.Equal((KeySet.All, -1), players.DeviceOf(1, 3));
        Assert.Equal((KeySet.None, 0), players.DeviceOf(2, 3));
        Assert.Equal((KeySet.None, 2), players.DeviceOf(3, 3));

        // Four on two: the keyboard split, and a controller each for the last two.
        Assert.Equal((KeySet.Left, -1), players.DeviceOf(1, 4));
        Assert.Equal((KeySet.Right, -1), players.DeviceOf(2, 4));
        Assert.Equal((KeySet.None, 2), players.DeviceOf(4, 4));

        // One playing has everything.
        Assert.Same(game.Input, players.Input(1, 1));
        Assert.Equal("Keyboard and mouse", players.Describe(1, 1));
    }

    [Fact]
    public void Each_player_reads_their_own_controller_with_buffered_presses()
    {
        _input.SetGamepadConnected(0, true);
        _input.SetGamepadConnected(1, true);
        var game = TwoPlayers(out var first, out var second);
        var (one, two) = (game.InputOf(first), game.InputOf(second));

        _input.SetGamepad(1, [GamepadButton.X], new Vector2(-1, 0), Vector2.Zero);
        Assert.True(two.WasPressed("Punch"));
        Assert.False(one.WasPressed("Punch"));
        Assert.Equal((0f, -1f), (one.Horizontal, two.Horizontal));
        Assert.True(two.IsGamepadConnected);

        // A press a moment ago still counts once for the one who made it.
        game.Tick(Frame);
        _input.EndFrame();
        game.Tick(Frame);
        Assert.True(two.WasPressed("Punch", buffer: 0.1f));
        Assert.False(two.WasPressed("Punch", buffer: 0.1f));
        Assert.False(one.WasPressed("Punch", buffer: 0.1f));

        // Each shakes its own.
        two.Rumble(0.8f, 0.5f);
        Assert.Equal((0f, 0.8f), (_input.RumbleStrengthOf(0), _input.RumbleStrengthOf(1)));
    }

    [Fact]
    public void Choosing_and_swapping_are_kept_with_the_players_settings()
    {
        _input.SetGamepadConnected(0, true);
        var saves = new SaveData();
        var game = TwoPlayers(out var first, out var second);
        game.SaveData = saves;
        var players = game.LocalPlayers;
        Assert.Equal(("Keyboard", "Gamepad 1"), (players.Describe(1, 2), players.Describe(2, 2)));

        players.Swap(1, 2, 2);
        Assert.Equal(("Gamepad 1", "Keyboard"), (players.Describe(1, 2), players.Describe(2, 2)));
        Assert.Equal(KeySet.All, ((LocalPlayerInput)game.InputOf(second)).Keys);

        // Read back from the save data next time.
        var next = new Game(new Scene(), new InputState()) { SaveData = saves };
        Assert.Equal((PlayerDevice.Gamepad1, PlayerDevice.Keyboard), (next.LocalPlayers.Chosen(1), next.LocalPlayers.Chosen(2)));
        players.ResetChoices();
        Assert.Null(saves.GetString(LocalPlayers.SaveKey));
        Assert.Equal(PlayerDevice.Auto, players.Chosen(1));
    }

    [Fact]
    public void Played_By_matters_only_with_two_playing_and_focus_stays_with_each_player()
    {
        var scene = new Scene();
        var solo = new Entity("Solo");
        solo.AddComponent<PlayedBy>().Player = 1;
        scene.Add(solo);
        var game = new Game(scene, _input);
        game.Tick(Frame);
        Assert.Same(game.Input, game.InputOf(solo));

        // A second player arrives: each answers to their own.
        var second = new Entity("Second");
        second.AddComponent<PlayedBy>().Player = 2;
        second.AddComponent<PlatformerMovement>();
        var board = new Entity("Board");
        board.AddComponent<PlayedBy>().Player = 1;
        board.AddComponent<TopDownMovement>();
        solo.AddComponent<PlatformerMovement>();
        scene.Add(second);
        scene.Add(board);
        game.Tick(Frame);
        Assert.Equal(2, game.LocalPlayers.Playing);
        Assert.NotSame(game.InputOf(solo), game.InputOf(second));
        Assert.Same(game.InputOf(solo), game.InputOf(board));

        // Player 1 focusing their board leaves Player 2's character alone.
        game.Focus(board);
        Assert.False(solo.GetComponent<PlatformerMovement>()!.Controllable);
        Assert.True(board.GetComponent<TopDownMovement>()!.Controllable);
        Assert.True(second.GetComponent<PlatformerMovement>()!.Controllable);
    }

    [Fact]
    public void A_controller_can_be_pretended_for_agents_and_demos_until_unplugged()
    {
        _input.HoldGamepadButton(1, GamepadButton.X, true);
        Assert.True(_input.IsGamepadConnectedAt(1));
        Assert.True(_input.WasPressed(1, GamepadButton.X));
        _input.HoldGamepadButton(1, GamepadButton.X, false);
        Assert.True(_input.IsGamepadConnectedAt(1));
        Assert.True(_input.WasReleased(1, GamepadButton.X));
        Assert.Equal(1, _input.ConnectedGamepad(0));

        _input.SetGamepadConnected(1, false);
        Assert.False(_input.IsGamepadConnected);

        Assert.Equal((0, GamepadButton.A), Playtesting.PadButtonNamed("Gamepad A"));
        Assert.Equal((1, GamepadButton.DPadLeft), Playtesting.PadButtonNamed("Gamepad2 Left"));
        Assert.Equal((1, GamepadButton.Start), Playtesting.PadButtonNamed("pad2 start"));
        Assert.Null(Playtesting.PadButtonNamed("Gamepad9 A"));
        Assert.Null(Playtesting.PadButtonNamed("Space"));
    }

    [Fact]
    public void The_browser_hands_each_controller_over_by_its_number()
    {
        WebGamepad.Apply(_input, 1, true, 1 << 2, 0, 0, 0, 0, 0, 0);
        WebGamepad.Apply(_input, 0, true, 1 << 0, 1, 0, 0, 0, 0, 0);
        Assert.Equal(2, _input.GamepadCount);
        Assert.True(_input.IsDown(1, GamepadButton.X));
        Assert.True(_input.IsDown(0, GamepadButton.A));
        Assert.Equal(new Vector2(1, 0), _input.LeftStickOf(0));
        WebGamepad.Apply(_input, 1, false, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(1, _input.GamepadCount);
        Assert.False(_input.IsDown(GamepadButton.X));
    }
}
