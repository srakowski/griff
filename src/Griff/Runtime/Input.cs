using System.Numerics;

namespace Griff.Runtime;

public enum Key
{
    None,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Space, Enter, Escape, Tab, Backspace,
    Left, Right, Up, Down,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

/// <summary>Buttons of an Xbox-style controller (the first one connected).</summary>
public enum GamepadButton
{
    A, B, X, Y,
    LeftShoulder, RightShoulder,
    Start, Back,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftStick, RightStick,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// A key, mouse button or gamepad button that triggers an action (see <see cref="IInput.Bind"/>). Any of the three
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

    public override string ToString() =>
        Key is { } key ? key.ToString() : Mouse is { } mouse ? $"Mouse {mouse}" : $"Gamepad {Gamepad}";
}

/// <summary>What gameplay code reads. "Pressed" and "released" are true for exactly one frame.</summary>
public interface IInput
{
    /// <summary>
    /// True while any of the action's keys or buttons is held, e.g. <c>IsDown("Fire")</c>. Games start with Jump, Fire,
    /// Dash, Interact and Pause; <see cref="Bind"/> changes them or adds more.
    /// </summary>
    bool IsDown(string action);

    /// <summary>True on the frame any of the action's keys or buttons goes down, e.g. <c>WasPressed("Jump")</c>.</summary>
    bool WasPressed(string action);

    /// <summary>True on the frame the action stops being held (the last of its keys and buttons is let go).</summary>
    bool WasReleased(string action);

    /// <summary>
    /// Sets the keys and buttons that trigger <paramref name="action"/>, replacing any it had, e.g.
    /// <c>Bind("Dash", Key.LeftShift, Key.K, GamepadButton.B)</c>. A new name makes a new action. Lasts until the game stops.
    /// </summary>
    void Bind(string action, params InputBinding[] bindings);

    /// <summary>The keys and buttons that trigger <paramref name="action"/>, e.g. to show them in a controls screen.</summary>
    IReadOnlyList<InputBinding> BindingsOf(string action);

    bool WasReleased(GamepadButton button);

    bool IsDown(Key key);

    bool WasPressed(Key key);

    bool WasReleased(Key key);

    bool IsDown(MouseButton button);

    bool WasPressed(MouseButton button);

    bool WasReleased(MouseButton button);

    bool IsDown(GamepadButton button);

    bool WasPressed(GamepadButton button);

    /// <summary>Left stick, each axis -1..1 with a dead zone applied. Up is positive.</summary>
    Vector2 LeftStick { get; }

    /// <summary>Mouse position in world units.</summary>
    Vector2 MouseWorld { get; }

    /// <summary>False while the mouse is outside the game's view (or hasn't moved over it yet); buttons ignore it then.</summary>
    bool IsMouseInView { get; }

    /// <summary>-1..1 from A/D, the left/right arrows, the D-pad or the left stick.</summary>
    float Horizontal { get; }

    /// <summary>-1..1 from S/W, the down/up arrows, the D-pad or the left stick (up is positive).</summary>
    float Vertical { get; }
}

/// <summary>
/// Input state fed by a platform backend: call <see cref="SetKey"/>/<see cref="SetMouseButton"/> as events
/// arrive and <see cref="EndFrame"/> after each game tick.
/// </summary>
public sealed class InputState : IInput
{
    private readonly HashSet<Key> _down = [];
    private readonly HashSet<Key> _pressed = [];
    private readonly HashSet<Key> _released = [];
    private readonly HashSet<MouseButton> _mouseDown = [];
    private readonly HashSet<MouseButton> _mousePressed = [];
    private readonly HashSet<MouseButton> _mouseReleased = [];
    private readonly HashSet<GamepadButton> _padDown = [];
    private readonly HashSet<GamepadButton> _padPressed = [];
    private readonly HashSet<GamepadButton> _padReleased = [];
    private readonly Dictionary<string, InputBinding[]> _actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jump"] = [Key.Space, GamepadButton.A],
        ["Fire"] = [MouseButton.Left, Key.J, GamepadButton.X, GamepadButton.RightShoulder],
        ["Dash"] = [Key.LeftShift, Key.K, GamepadButton.B],
        ["Interact"] = [Key.E, Key.Enter, GamepadButton.Y],
        ["Pause"] = [Key.Escape, Key.P, GamepadButton.Start],
    };

    /// <summary>Stick movement smaller than this is ignored, so a resting stick doesn't drift.</summary>
    public const float DeadZone = 0.25f;

    public Vector2 MouseWorld { get; set; }

    /// <summary>Set by the platform as the mouse enters and leaves the view. True by default, for tests and headless runs.</summary>
    public bool IsMouseInView { get; set; } = true;

    public Vector2 LeftStick { get; private set; }

    public float Horizontal => Combine(Axis(Key.A, Key.Left, Key.D, Key.Right), PadAxis(GamepadButton.DPadLeft, GamepadButton.DPadRight), LeftStick.X);

    public float Vertical => Combine(Axis(Key.S, Key.Down, Key.W, Key.Up), PadAxis(GamepadButton.DPadDown, GamepadButton.DPadUp), LeftStick.Y);

    public bool IsDown(GamepadButton button) => _padDown.Contains(button);

    public bool WasPressed(GamepadButton button) => _padPressed.Contains(button);

    public bool WasReleased(GamepadButton button) => _padReleased.Contains(button);

    public bool IsDown(string action) => Bindings(action).Any(IsDown);

    public bool WasPressed(string action)
    {
        var bindings = Bindings(action);
        return bindings.Any(WasPressed) && !bindings.Any(b => IsDown(b) && !WasPressed(b));
    }

    public bool WasReleased(string action)
    {
        var bindings = Bindings(action);
        return bindings.Any(WasReleased) && !bindings.Any(IsDown);
    }

    public void Bind(string action, params InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        _actions[action.Trim()] = [.. bindings];
    }

    public IReadOnlyList<InputBinding> BindingsOf(string action) => Bindings(action);

    /// <summary>The names of every action, built in or bound.</summary>
    public IReadOnlyCollection<string> Actions => _actions.Keys;

    private InputBinding[] Bindings(string action) =>
        _actions.TryGetValue(action, out var bindings)
            ? bindings
            : throw new ArgumentException(
                $"There's no input action called \"{action}\"; there are {string.Join(", ", _actions.Keys)}. Add one with Input.Bind(\"{action}\", ...).",
                nameof(action));

    private bool IsDown(InputBinding binding) =>
        binding.Key is { } key ? IsDown(key) : binding.Mouse is { } mouse ? IsDown(mouse) : binding.Gamepad is { } pad && IsDown(pad);

    private bool WasPressed(InputBinding binding) =>
        binding.Key is { } key ? WasPressed(key) : binding.Mouse is { } mouse ? WasPressed(mouse) : binding.Gamepad is { } pad && WasPressed(pad);

    private bool WasReleased(InputBinding binding) =>
        binding.Key is { } key ? WasReleased(key) : binding.Mouse is { } mouse ? WasReleased(mouse) : binding.Gamepad is { } pad && WasReleased(pad);

    /// <summary>Sets the controller state for this frame (called by the platform each tick).</summary>
    public void SetGamepad(IEnumerable<GamepadButton> down, Vector2 leftStick)
    {
        var now = down.ToHashSet();
        foreach (var button in now.Where(b => !_padDown.Contains(b)))
            _padPressed.Add(button);
        foreach (var button in _padDown.Where(b => !now.Contains(b)))
            _padReleased.Add(button);
        _padDown.Clear();
        _padDown.UnionWith(now);
        LeftStick = new Vector2(ApplyDeadZone(leftStick.X), ApplyDeadZone(leftStick.Y));
    }

    public bool IsDown(Key key) => _down.Contains(key);

    public bool WasPressed(Key key) => _pressed.Contains(key);

    public bool WasReleased(Key key) => _released.Contains(key);

    public bool IsDown(MouseButton button) => _mouseDown.Contains(button);

    public bool WasPressed(MouseButton button) => _mousePressed.Contains(button);

    public bool WasReleased(MouseButton button) => _mouseReleased.Contains(button);

    public void SetKey(Key key, bool down)
    {
        if (key == Key.None)
            return;

        if (down && _down.Add(key))
            _pressed.Add(key);
        else if (!down && _down.Remove(key))
            _released.Add(key);
    }

    public void SetMouseButton(MouseButton button, bool down)
    {
        if (down && _mouseDown.Add(button))
            _mousePressed.Add(button);
        else if (!down && _mouseDown.Remove(button))
            _mouseReleased.Add(button);
    }

    /// <summary>Releases everything, e.g. when the game window loses focus.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _down.ToList())
            SetKey(key, false);
        // Silently, so a held button doesn't count as a click on whatever is under the cursor.
        _mouseDown.Clear();
    }

    public void EndFrame()
    {
        _pressed.Clear();
        _released.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        _padPressed.Clear();
        _padReleased.Clear();
    }

    private float PadAxis(GamepadButton negative, GamepadButton positive) =>
        (IsDown(positive) ? 1f : 0f) - (IsDown(negative) ? 1f : 0f);

    // Digital input wins when pressed; otherwise the stick gives analog control.
    private static float Combine(float keys, float dpad, float stick) =>
        Math.Clamp(keys != 0 ? keys : dpad != 0 ? dpad : stick, -1f, 1f);

    private static float ApplyDeadZone(float value) =>
        Math.Abs(value) < DeadZone ? 0 : Math.Sign(value) * (Math.Abs(value) - DeadZone) / (1 - DeadZone);

    private float Axis(Key negative, Key negativeAlt, Key positive, Key positiveAlt) =>
        (IsDown(positive) || IsDown(positiveAlt) ? 1f : 0f) - (IsDown(negative) || IsDown(negativeAlt) ? 1f : 0f);
}
