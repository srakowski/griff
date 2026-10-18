using System.Numerics;
using Griff.Input;

namespace Griff.Runtime;

/// <summary>
/// One player's share of this machine's controls (see <see cref="LocalPlayers"/>): the keys of their side of the keyboard
/// (or all of it, with the mouse), and their own controller. The actions are the game's, read on those keys and that
/// controller only: on the left keys Punch is F and Kick G, on the right K and L, and the rest are the action's own keys on
/// that side of the keyboard.
/// </summary>
public sealed class LocalPlayerInput : IInput
{
    private readonly InputState _machine;
    private readonly Dictionary<string, double> _usedPress = new(StringComparer.OrdinalIgnoreCase);

    internal LocalPlayerInput(InputState machine) => _machine = machine;

    /// <summary>Which keys it has.</summary>
    public KeySet Keys { get; private set; }

    /// <summary>The platform's number of its controller, or -1 for none.</summary>
    public int Pad { get; private set; } = -1;

    internal void Use(KeySet keys, int pad) => (Keys, Pad) = (keys, pad);

    // ----- The two sides of the keyboard -----

    /// <summary>
    /// The keys the actions have on the left side of the keyboard, where they aren't simply their own keys on that side:
    /// WASD moves, F and G are the first buttons (Punch and Kick, Fire and Attack).
    /// </summary>
    public static IReadOnlyDictionary<string, Key[]> LeftKeys { get; } = new Dictionary<string, Key[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Jump"] = [Key.Space],
        ["Punch"] = [Key.F],
        ["Kick"] = [Key.G],
        ["Fire"] = [Key.F],
        ["Attack"] = [Key.G],
        ["Dash"] = [Key.LeftShift],
        ["Interact"] = [Key.E],
        ["Pause"] = [Key.Escape],
    };

    /// <summary>
    /// The keys the actions have on the right side of the keyboard: the arrows move, K and L (or the number pad's 1 and 2)
    /// are the first buttons.
    /// </summary>
    public static IReadOnlyDictionary<string, Key[]> RightKeys { get; } = new Dictionary<string, Key[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Jump"] = [Key.RightControl, Key.NumPad0],
        ["Punch"] = [Key.K, Key.NumPad1],
        ["Kick"] = [Key.L, Key.NumPad2],
        ["Fire"] = [Key.K, Key.NumPad1],
        ["Attack"] = [Key.L, Key.NumPad2],
        ["Dash"] = [Key.RightShift],
        ["Interact"] = [Key.Enter],
        ["Pause"] = [Key.Backspace],
    };

    /// <summary>True if <paramref name="key"/> is on the left side of the keyboard (as the left keys' player has it).</summary>
    public static bool OnLeft(Key key) => key is Key.Q or Key.W or Key.E or Key.R or Key.T or Key.A or Key.S or Key.D or Key.F or Key.G
        or Key.Z or Key.X or Key.C or Key.V or Key.B or Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.Tab or Key.CapsLock
        or Key.LeftShift or Key.LeftControl or Key.LeftAlt or Key.Space or Key.Escape or Key.Grave or Key.F1 or Key.F2 or Key.F3
        or Key.F4 or Key.F5 or Key.F6;

    /// <summary>The keys <paramref name="action"/> has on <paramref name="side"/> of the keyboard (all of them for <see cref="KeySet.All"/>).</summary>
    public IReadOnlyList<Key> KeysOf(string action, KeySet side)
    {
        if (side == KeySet.None)
            return [];
        if (side != KeySet.All && (side == KeySet.Left ? LeftKeys : RightKeys).TryGetValue(action.Trim(), out var own))
            return own;
        var keys = new List<Key>();
        foreach (var binding in _machine.Bindings(action))
        {
            if (binding.Key is { } key && (side == KeySet.All || OnLeft(key) == (side == KeySet.Left)))
                keys.Add(key);
        }

        return keys;
    }

    // Whether a key of the machine's is this player's, for an action without keys of its own on their side.
    private bool Mine(Key key) => Keys switch
    {
        KeySet.All => true,
        KeySet.Left => OnLeft(key),
        KeySet.Right => !OnLeft(key),
        _ => false,
    };

    // The keys an action has on this player's side of the keyboard, if the side lists its own.
    private Key[]? SideKeys(string action) =>
        Keys == KeySet.Left ? LeftKeys.GetValueOrDefault(action.Trim()) : Keys == KeySet.Right ? RightKeys.GetValueOrDefault(action.Trim()) : null;

    // ----- Actions: by index throughout, so asking every frame makes no garbage -----

    public bool IsDown(string action)
    {
        var side = SideKeys(action);
        if (side is not null)
        {
            foreach (var key in side)
            {
                if (_machine.IsDown(key))
                    return true;
            }
        }

        foreach (var binding in _machine.Bindings(action))
        {
            if (binding.Key is { } key ? side is null && Mine(key) && _machine.IsDown(key) : IsDown(binding))
                return true;
        }

        return false;
    }

    public bool WasPressed(string action)
    {
        var pressed = false;
        var side = SideKeys(action);
        if (side is not null)
        {
            foreach (var key in side)
            {
                if (_machine.WasPressed(key))
                    pressed = true;
                else if (_machine.IsDown(key))
                    return false;
            }
        }

        foreach (var binding in _machine.Bindings(action))
        {
            if (binding.Key is { } key && (side is not null || !Mine(key)))
                continue;
            if (WasPressed(binding))
                pressed = true;
            else if (IsDown(binding))
                return false;
        }

        return pressed;
    }

    public bool WasPressed(string action, float buffer)
    {
        var latest = double.NegativeInfinity;
        var side = SideKeys(action);
        if (side is not null)
        {
            foreach (var key in side)
                latest = Math.Max(latest, _machine.PressedAt(key) ?? double.NegativeInfinity);
        }

        foreach (var binding in _machine.Bindings(action))
        {
            if (binding.Key is { } key)
            {
                if (side is null && Mine(key))
                    latest = Math.Max(latest, _machine.PressedAt(binding) ?? double.NegativeInfinity);
            }
            else if (binding.Mouse is not null)
            {
                if (Keys == KeySet.All)
                    latest = Math.Max(latest, _machine.PressedAt(binding) ?? double.NegativeInfinity);
            }
            else if (binding.Gamepad is { } button)
            {
                latest = Math.Max(latest, _machine.PressedAt(Pad, button) ?? double.NegativeInfinity);
            }
        }

        var clock = _machine.Clock;
        var name = action.Trim();
        if (latest < clock - Math.Max(0, buffer) - 1e-6 || (_usedPress.TryGetValue(name, out var used) && latest <= used))
            return false;
        _usedPress[name] = latest;
        return true;
    }

    public bool WasReleased(string action)
    {
        var released = false;
        var side = SideKeys(action);
        if (side is not null)
        {
            foreach (var key in side)
            {
                if (_machine.IsDown(key))
                    return false;
                released |= _machine.WasReleased(key);
            }
        }

        foreach (var binding in _machine.Bindings(action))
        {
            if (binding.Key is { } key && (side is not null || !Mine(key)))
                continue;
            if (IsDown(binding))
                return false;
            released |= WasReleased(binding);
        }

        return released;
    }

    // A mouse button is the keyboard player's; a controller button this player's own controller's.
    private bool IsDown(InputBinding binding) =>
        binding.Key is { } key ? Mine(key) && _machine.IsDown(key)
        : binding.Mouse is { } mouse ? Keys == KeySet.All && _machine.IsDown(mouse)
        : binding.Gamepad is { } button && _machine.IsDown(Pad, button);

    private bool WasPressed(InputBinding binding) =>
        binding.Key is { } key ? Mine(key) && _machine.WasPressed(key)
        : binding.Mouse is { } mouse ? Keys == KeySet.All && _machine.WasPressed(mouse)
        : binding.Gamepad is { } button && _machine.WasPressed(Pad, button);

    private bool WasReleased(InputBinding binding) =>
        binding.Key is { } key ? Mine(key) && _machine.WasReleased(key)
        : binding.Mouse is { } mouse ? Keys == KeySet.All && _machine.WasReleased(mouse)
        : binding.Gamepad is { } button && _machine.WasReleased(Pad, button);

    public void Bind(string action, params InputBinding[] bindings) => _machine.Bind(action, bindings);

    public void Rebind(string action, params InputBinding[] bindings) => _machine.Rebind(action, bindings);

    public void ResetBindings(string? action = null) => _machine.ResetBindings(action);

    /// <summary>The keys and buttons that are <paramref name="action"/> for this player: their side's keys, and the controller's buttons.</summary>
    public IReadOnlyList<InputBinding> BindingsOf(string action)
    {
        var bindings = new List<InputBinding>();
        foreach (var key in KeysOf(action, Keys))
            bindings.Add(key);
        foreach (var binding in _machine.Bindings(action))
        {
            if (binding.Mouse is not null && Keys == KeySet.All || binding.Gamepad is not null && Pad >= 0)
                bindings.Add(binding);
        }

        return bindings;
    }

    public IReadOnlyCollection<string> Actions => _machine.Actions;

    public InputBinding? AnyPressed
    {
        get
        {
            if (Keys != KeySet.None && _machine.AnyPressed is { Key: { } key } pressed && Mine(key))
                return pressed;
            return _machine.PressedOn(Pad) is { } button ? button : null;
        }
    }

    // ----- Keys, buttons and sticks -----

    public bool IsDown(Key key) => Mine(key) && _machine.IsDown(key);

    public bool WasPressed(Key key) => Mine(key) && _machine.WasPressed(key);

    public bool WasReleased(Key key) => Mine(key) && _machine.WasReleased(key);

    public bool IsDown(MouseButton button) => Keys == KeySet.All && _machine.IsDown(button);

    public bool WasPressed(MouseButton button) => Keys == KeySet.All && _machine.WasPressed(button);

    public bool WasReleased(MouseButton button) => Keys == KeySet.All && _machine.WasReleased(button);

    public bool IsDown(GamepadButton button) => _machine.IsDown(Pad, button);

    public bool WasPressed(GamepadButton button) => _machine.WasPressed(Pad, button);

    public bool WasReleased(GamepadButton button) => _machine.WasReleased(Pad, button);

    public Vector2 LeftStick => _machine.LeftStickOf(Pad);

    public Vector2 RightStick => _machine.RightStickOf(Pad);

    public float LeftTrigger => _machine.LeftTriggerOf(Pad);

    public float RightTrigger => _machine.RightTriggerOf(Pad);

    public bool IsGamepadConnected => _machine.IsGamepadConnectedAt(Pad);

    public bool UsingGamepad => Pad >= 0 && (Keys == KeySet.None || _machine.UsingGamepad);

    public void Rumble(float strength = 0.5f, float seconds = 0.2f) => _machine.Rumble(Pad, strength, seconds);

    public Vector2 MouseWorld => _machine.MouseWorld;

    public Vector2? MouseOnScreen => _machine.MouseOnScreen;

    public bool IsMouseInView => Keys == KeySet.All && _machine.IsMouseInView;

    public float Scroll => Keys == KeySet.All ? _machine.Scroll : 0;

    public Vector2 MouseDelta => Keys == KeySet.All ? _machine.MouseDelta : Vector2.Zero;

    public bool WantsMouseLock
    {
        get => _machine.WantsMouseLock;
        set
        {
            if (Keys == KeySet.All)
                _machine.WantsMouseLock = value;
        }
    }

    public bool IsMouseLocked => Keys == KeySet.All && _machine.IsMouseLocked;

    public MouseCursor Cursor
    {
        get => _machine.Cursor;
        set => _machine.Cursor = value;
    }

    public float Horizontal => Combine(Axis(Key.A, Key.D, Key.Left, Key.Right), PadAxis(GamepadButton.DPadLeft, GamepadButton.DPadRight), LeftStick.X);

    public float Vertical => Combine(Axis(Key.S, Key.W, Key.Down, Key.Up), PadAxis(GamepadButton.DPadDown, GamepadButton.DPadUp), LeftStick.Y);

    public Vector2 Move
    {
        get
        {
            var move = new Vector2(Horizontal, Vertical);
            return move.LengthSquared() > 1 ? Vector2.Normalize(move) : move;
        }
    }

    public Vector2 AimFrom(Vector2 origin)
    {
        if (RightStick != Vector2.Zero)
            return Vector2.Normalize(RightStick);
        if (Keys != KeySet.All)
            return LeftStick != Vector2.Zero ? Vector2.Normalize(LeftStick) : Vector2.Zero;
        return _machine.AimFrom(origin);
    }

    public Vector2 AimFrom(Vector3 origin) => AimFrom(new Vector2(origin.X, origin.Y));

    public string TypedText => Keys == KeySet.All ? _machine.TypedText : "";

    public string TypeInto(string text, int maxLength = int.MaxValue) => Keys == KeySet.All ? _machine.TypeInto(text, maxLength) : text;

    // WASD on the left keys, the arrows on the right, both on the whole keyboard.
    private float Axis(Key leftNegative, Key leftPositive, Key rightNegative, Key rightPositive)
    {
        var (left, right) = (Keys is KeySet.All or KeySet.Left, Keys is KeySet.All or KeySet.Right);
        var positive = left && _machine.IsDown(leftPositive) || right && _machine.IsDown(rightPositive);
        var negative = left && _machine.IsDown(leftNegative) || right && _machine.IsDown(rightNegative);
        return (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }

    private float PadAxis(GamepadButton negative, GamepadButton positive) =>
        (_machine.IsDown(Pad, positive) ? 1f : 0f) - (_machine.IsDown(Pad, negative) ? 1f : 0f);

    // Digital input wins when pressed; otherwise the stick gives analog control.
    private static float Combine(float keys, float dpad, float stick) =>
        Math.Clamp(keys != 0 ? keys : dpad != 0 ? dpad : stick, -1f, 1f);
}
