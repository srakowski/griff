using System.Text.Json;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>What a player at this machine plays with (see <see cref="LocalPlayers"/>).</summary>
public enum PlayerDevice
{
    /// <summary>Shared out by how many play and how many controllers are plugged in (see <see cref="LocalPlayers"/>).</summary>
    Auto,

    /// <summary>The whole keyboard and the mouse.</summary>
    Keyboard,

    /// <summary>The left side of the keyboard: WASD to move, F and G for the first buttons, Space, Left Shift, E.</summary>
    LeftKeys,

    /// <summary>The right side of the keyboard: the arrows to move, K and L (or the number pad's 1 and 2) for the first buttons.</summary>
    RightKeys,

    /// <summary>The first controller plugged in.</summary>
    Gamepad1,

    /// <summary>The second controller plugged in.</summary>
    Gamepad2,

    /// <summary>The third controller plugged in.</summary>
    Gamepad3,

    /// <summary>The fourth controller plugged in.</summary>
    Gamepad4,
}

/// <summary>Which keys of the keyboard a player at this machine has.</summary>
public enum KeySet
{
    /// <summary>None: a controller only.</summary>
    None,

    /// <summary>All of it, and the mouse.</summary>
    All,

    /// <summary>The left side (see <see cref="PlayerDevice.LeftKeys"/>).</summary>
    Left,

    /// <summary>The right side (see <see cref="PlayerDevice.RightKeys"/>).</summary>
    Right,
}

/// <summary>
/// The people playing at this one machine: two at one keyboard (one on WASD, one on the arrows), on two controllers, or one
/// on the keyboard and one on a controller, each moving their own character. Something a <see cref="PlayedBy"/> names a
/// player for answers to that player's keys or controller (<see cref="Game.InputOf"/>); everything else, menus included,
/// answers to everyone.
/// </summary>
/// <remarks>
/// With <see cref="PlayerDevice.Auto"/>, how many play (the most a scene's Played Bys name) shares out what's there: one
/// player has everything; with no controller, two split the keyboard (Player 1 the left keys, Player 2 the right);
/// with a controller for every player but the first, Player 1 has the keyboard and the others a controller each; with one
/// for everyone, each has their own (and Player 1 the keyboard too). A player's choice (<see cref="Choose"/>, or
/// <see cref="Swap"/>) is kept with the player's settings.
/// </remarks>
public sealed class LocalPlayers
{
    /// <summary>The most people that can play at one machine.</summary>
    public const int Max = PlayedBy.MaxPlayers;

    /// <summary>Where the players' chosen devices are kept in <see cref="SaveData"/>.</summary>
    public const string SaveKey = "griff.players";

    private readonly Game _game;
    private readonly PlayerDevice[] _chosen = new PlayerDevice[Max];
    private readonly LocalPlayerInput?[] _inputs = new LocalPlayerInput?[Max];
    private SaveData? _readFrom;

    internal LocalPlayers(Game game) => _game = game;

    /// <summary>
    /// How many play in the running scene: the most a <see cref="PlayedBy"/> in it names (at least 1). Worked out as each
    /// frame starts.
    /// </summary>
    public int Playing { get; private set; } = 1;

    /// <summary>What player <paramref name="player"/> (1 to 4) has chosen to play with; <see cref="PlayerDevice.Auto"/> until they choose.</summary>
    public PlayerDevice Chosen(int player)
    {
        Read();
        return player is >= 1 and <= Max ? _chosen[player - 1] : PlayerDevice.Auto;
    }

    /// <summary>Has player <paramref name="player"/> (1 to 4) play with <paramref name="device"/> from now on, kept with the player's settings.</summary>
    public void Choose(int player, PlayerDevice device)
    {
        if (player is < 1 or > Max)
            return;
        Read();
        _chosen[player - 1] = device;
        Save();
    }

    /// <summary>
    /// Swaps what players <paramref name="a"/> and <paramref name="b"/> play with, as they are now with
    /// <paramref name="playing"/> people playing: the one at the keyboard on the left side and the one on the controller
    /// change places.
    /// </summary>
    public void Swap(int a, int b, int playing)
    {
        if (a is < 1 or > Max || b is < 1 or > Max || a == b)
            return;
        var (first, second) = (DeviceOf(a, playing), DeviceOf(b, playing));
        Read();
        (_chosen[a - 1], _chosen[b - 1]) = (Single(second), Single(first));
        Save();

        // A player with the keyboard and a controller together keeps both.
        static PlayerDevice Single((KeySet Keys, int Pad) device) => device.Keys switch
        {
            KeySet.All => PlayerDevice.Keyboard,
            KeySet.Left => PlayerDevice.LeftKeys,
            KeySet.Right => PlayerDevice.RightKeys,
            _ => device.Pad >= 0 ? PlayerDevice.Gamepad1 + device.Pad : PlayerDevice.Auto,
        };
    }

    /// <summary>Forgets every player's choice: everyone back to <see cref="PlayerDevice.Auto"/>.</summary>
    public void ResetChoices()
    {
        Read();
        Array.Fill(_chosen, PlayerDevice.Auto);
        Save();
    }

    /// <summary>
    /// The controls of player <paramref name="player"/> (1 to 4) with <paramref name="playing"/> people playing: their keys
    /// and controller only. With one playing, Player 1's are this machine's whole <see cref="Game.Input"/>.
    /// </summary>
    public IInput Input(int player, int playing)
    {
        if (_game.Input is not InputState machine || player is < 1 or > Max)
            return player == 1 ? _game.Input : _game.NoControls;
        if (playing <= 1)
            return player == 1 ? _game.Input : _game.NoControls;
        var (keys, pad) = DeviceOf(player, playing);
        if (keys == KeySet.None && pad < 0)
            return _game.NoControls;
        var input = _inputs[player - 1] ??= new LocalPlayerInput(machine);
        input.Use(keys, pad);
        return input;
    }

    /// <summary>The controls of player <paramref name="player"/> with as many playing as the scene has (<see cref="Playing"/>).</summary>
    public IInput Input(int player) => Input(player, Playing);

    /// <summary>
    /// What player <paramref name="player"/> plays with, with <paramref name="playing"/> people playing: which keys, and the
    /// platform's number of their controller (-1 for none).
    /// </summary>
    public (KeySet Keys, int Pad) DeviceOf(int player, int playing)
    {
        if (player is < 1 or > Max)
            return (KeySet.None, -1);
        var machine = _game.Input as InputState;
        int Pad(int nth) => machine?.ConnectedGamepad(nth) ?? -1;
        var chosen = Chosen(player);
        switch (chosen)
        {
            case PlayerDevice.Keyboard:
                return (KeySet.All, -1);
            case PlayerDevice.LeftKeys:
                return (KeySet.Left, -1);
            case PlayerDevice.RightKeys:
                return (KeySet.Right, -1);
            case >= PlayerDevice.Gamepad1:
                return (KeySet.None, Pad(chosen - PlayerDevice.Gamepad1));
        }

        playing = Math.Clamp(playing, 1, Max);
        if (playing == 1)
            return player == 1 ? (KeySet.All, Pad(0)) : (KeySet.None, -1);
        if (player > playing)
            return (KeySet.None, -1);
        var pads = machine?.GamepadCount ?? 0;
        // Everyone has a controller: the first player the keyboard as well.
        if (pads >= playing)
            return (player == 1 ? KeySet.All : KeySet.None, Pad(player - 1));
        // All but the first: the first at the keyboard, the rest a controller each.
        if (pads == playing - 1)
            return player == 1 ? (KeySet.All, -1) : (KeySet.None, Pad(player - 2));
        // Fewer: the first two share the keyboard, and the controllers go to the players after them.
        return player switch
        {
            1 => (KeySet.Left, -1),
            2 => (KeySet.Right, -1),
            _ => (KeySet.None, Pad(player - 3)),
        };
    }

    /// <summary>
    /// What player <paramref name="player"/> plays with, in words: "Left keys (WASD)", "Right keys (arrows)", "Keyboard",
    /// "Gamepad 2", "Keyboard and Gamepad 1", or "Nothing" (not enough keys and controllers to go round).
    /// </summary>
    public string Describe(int player, int playing)
    {
        if (playing <= 1 && player == 1)
            return "Keyboard and mouse";
        var (keys, pad) = DeviceOf(player, playing);
        var keyboard = keys switch
        {
            KeySet.All => "Keyboard",
            KeySet.Left => "Left keys (WASD)",
            KeySet.Right => "Right keys (arrows)",
            _ => null,
        };
        var gamepad = pad >= 0 && _game.Input is InputState machine ? $"Gamepad {Nth(machine, pad) + 1}" : null;
        return (keyboard, gamepad) switch
        {
            ({ } k, { } g) => $"{k} and {g}",
            ({ } k, null) => k,
            (null, { } g) => g,
            _ => "Nothing",
        };

        static int Nth(InputState machine, int pad)
        {
            for (var n = 0; n < InputState.MaxGamepads; n++)
            {
                if (machine.ConnectedGamepad(n) == pad)
                    return n;
            }

            return pad;
        }
    }

    // As each frame starts: how many play now.
    internal void Update(Scene scene)
    {
        var most = 1;
        var played = scene.RunningNow<PlayedBy>();
        for (var i = 0; i < played.Count; i++)
            most = Math.Max(most, Math.Clamp(played[i].Player, 1, Max));
        Playing = most;
    }

    // The players' choices, read from the save data each time it's a new one.
    private void Read()
    {
        var saves = _game.SaveData;
        if (ReferenceEquals(saves, _readFrom))
            return;
        _readFrom = saves;
        Array.Fill(_chosen, PlayerDevice.Auto);
        if (saves.GetString(SaveKey) is not { } json)
            return;
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(json) ?? [];
            for (var i = 0; i < Math.Min(names.Length, Max); i++)
                _chosen[i] = Enum.TryParse<PlayerDevice>(names[i], ignoreCase: true, out var device) ? device : PlayerDevice.Auto;
        }
        catch (JsonException)
        {
            // Unreadable: everyone on Auto.
        }
    }

    private void Save()
    {
        var saves = _game.SaveData;
        _readFrom = saves;
        if (_chosen.All(d => d == PlayerDevice.Auto))
            saves.Delete(SaveKey);
        else
            saves.Set(SaveKey, JsonSerializer.Serialize(_chosen.Select(d => d.ToString()).ToArray()));
    }
}
