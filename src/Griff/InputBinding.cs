namespace Griff.Input;

/// <summary>Buttons of an Xbox-style controller.</summary>
public enum GamepadButton
{
    A, B, X, Y,
    LeftShoulder, RightShoulder,
    Start, Back,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftStick, RightStick,

    /// <summary>The left trigger pulled more than halfway. <c>IInput.LeftTrigger</c> says exactly how far.</summary>
    LeftTrigger,

    /// <summary>The right trigger pulled more than halfway. <c>IInput.RightTrigger</c> says exactly how far.</summary>
    RightTrigger,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// A key, mouse button or gamepad button that triggers an action (see <c>IInput.Bind</c>). Any of the three
/// converts to one, so a list of bindings reads <c>Key.Space, GamepadButton.A</c>.
/// </summary>
public readonly record struct InputBinding
{
    public Key? Key { get; private init; }

    public MouseButton? Mouse { get; private init; }

    public GamepadButton? Gamepad { get; private init; }

    public static implicit operator InputBinding(Key key) => new() { Key = key };

    public static implicit operator InputBinding(MouseButton button) => new() { Mouse = button };

    public static implicit operator InputBinding(GamepadButton button) => new() { Gamepad = button };

    /// <summary>"Space", "Mouse Left" or "Gamepad A": what a controls screen can show.</summary>
    public override string ToString() =>
        Key is { } key ? key.ToString() : Mouse is { } mouse ? $"Mouse {mouse}" : $"Gamepad {Gamepad}";

    /// <summary>Reads what <see cref="ToString"/> writes, e.g. "Gamepad A". False for anything else.</summary>
    public static bool TryParse(string? text, out InputBinding binding)
    {
        binding = default;
        var parts = (text ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && parts[0].Equals("Mouse", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<MouseButton>(parts[1], ignoreCase: true, out var mouse) && Enum.IsDefined(mouse))
            binding = mouse;
        else if (parts.Length == 2 && parts[0].Equals("Gamepad", StringComparison.OrdinalIgnoreCase) &&
                 Enum.TryParse<GamepadButton>(parts[1], ignoreCase: true, out var pad) && Enum.IsDefined(pad))
            binding = pad;
        else if (parts.Length == 1 && Enum.TryParse<Griff.Input.Key>(parts[0], ignoreCase: true, out var key) &&
                 Enum.IsDefined(key) && key != Griff.Input.Key.None && !int.TryParse(parts[0], out _))
            binding = key;
        else
            return false;
        return true;
    }
}

/// <summary>The input actions every game has, whatever its project adds.</summary>
public static class InputActions
{
    /// <summary>
    /// The actions every game starts with, and their keys and buttons. Griff's own components use them (Platformer
    /// Movement jumps with Jump, doors and levers wait for Interact), so a game can change their bindings but not
    /// remove them.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> BuiltIn { get; } =
        new Dictionary<string, IReadOnlyList<InputBinding>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jump"] = [Key.Space, GamepadButton.A],
            ["Fire"] = [MouseButton.Left, Key.J, GamepadButton.X, GamepadButton.RightShoulder, GamepadButton.RightTrigger],
            ["Dash"] = [Key.LeftShift, Key.K, GamepadButton.B],
            ["Attack"] = [Key.X, Key.L, MouseButton.Right, GamepadButton.X],
            ["Interact"] = [Key.E, Key.Enter, GamepadButton.Y],
            ["Pause"] = [Key.Escape, Key.P, GamepadButton.Start],
            ["EndTurn"] = [Key.Tab, GamepadButton.Back],
            ["Undo"] = [Key.Z, Key.U, GamepadButton.LeftShoulder],
            ["Restart"] = [Key.R, GamepadButton.LeftTrigger],
            ["Skip"] = [Key.LeftControl, Key.RightControl, GamepadButton.RightTrigger],
            ["Auto"] = [Key.A, GamepadButton.X],
            ["Log"] = [Key.H, GamepadButton.LeftShoulder],
            ["LeftFlipper"] = [Key.LeftShift, Key.Z, GamepadButton.LeftShoulder, GamepadButton.LeftTrigger],
            ["RightFlipper"] = [Key.RightShift, Key.Slash, GamepadButton.RightShoulder, GamepadButton.RightTrigger],
            ["Plunger"] = [Key.Space, Key.Down, GamepadButton.A],
            ["Nudge"] = [Key.N, Key.Up, GamepadButton.B],
            ["Punch"] = [Key.U, Key.J, GamepadButton.X],
            ["Kick"] = [Key.I, Key.K, GamepadButton.A],
            // A rhythm game's four lanes: D F J K under the fingers, the arrows, and the D-pad or the face buttons.
            ["Lane1"] = [Key.D, Key.Left, GamepadButton.DPadLeft, GamepadButton.X],
            ["Lane2"] = [Key.F, Key.Down, GamepadButton.DPadDown, GamepadButton.A],
            ["Lane3"] = [Key.J, Key.Up, GamepadButton.DPadUp, GamepadButton.Y],
            ["Lane4"] = [Key.K, Key.Right, GamepadButton.DPadRight, GamepadButton.B],
        };
}
