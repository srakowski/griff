using System.Numerics;
using System.Text;
using System.Text.Json;
using Griff.Input;

namespace Griff.Runtime;

/// <summary>Buttons of an Xbox-style controller.</summary>
public enum GamepadButton
{
    A, B, X, Y,
    LeftShoulder, RightShoulder,
    Start, Back,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftStick, RightStick,

    /// <summary>The left trigger pulled more than halfway. <see cref="IInput.LeftTrigger"/> says exactly how far.</summary>
    LeftTrigger,

    /// <summary>The right trigger pulled more than halfway. <see cref="IInput.RightTrigger"/> says exactly how far.</summary>
    RightTrigger,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// A finger on a touch screen (see <see cref="IInput.Touches"/>): which one it is (the same from when it goes down until it's
/// lifted), where it is in world units, and whether it went down or was lifted this frame.
/// </summary>
public readonly record struct TouchPoint(int Id, Vector2 World, bool Pressed, bool Released);

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

/// <summary>What gameplay code reads. "Pressed" and "released" are true for exactly one frame.</summary>
[NotState("the host's controls")]
public interface IInput
{
    /// <summary>
    /// True while any of the action's keys or buttons is held, e.g. <c>IsDown("Fire")</c>. Games start with Jump, Fire,
    /// Attack, Dash, Interact, Pause and EndTurn; <see cref="Bind"/> changes them or adds more.
    /// </summary>
    bool IsDown(string action);

    /// <summary>True on the frame any of the action's keys or buttons goes down, e.g. <c>WasPressed("Jump")</c>.</summary>
    bool WasPressed(string action);

    /// <summary>
    /// True if the action was pressed within the last <paramref name="buffer"/> seconds and this hasn't returned true for
    /// that press yet. A jump pressed a moment before landing still jumps: <c>if (body.IsGrounded &amp;&amp;
    /// Input.WasPressed("Jump", buffer: 0.15f))</c>. Each press counts once, so it can't jump twice.
    /// </summary>
    bool WasPressed(string action, float buffer);

    /// <summary>True on the frame the action stops being held (the last of its keys and buttons is let go).</summary>
    bool WasReleased(string action);

    /// <summary>
    /// Sets the keys and buttons that trigger <paramref name="action"/>, replacing any it had, e.g.
    /// <c>Bind("Dash", Key.LeftShift, Key.K, GamepadButton.B)</c>. A new name makes a new action. These are the game's
    /// own controls; a player's choice made with <see cref="Rebind"/> takes priority over them.
    /// </summary>
    void Bind(string action, params InputBinding[] bindings);

    /// <summary>
    /// The player's own choice of keys and buttons for <paramref name="action"/>, from a controls screen. It's saved
    /// straight away (in <see cref="Game.SaveData"/>), comes back next time the game runs, and takes priority over
    /// <see cref="Bind"/>. Pair it with <see cref="AnyPressed"/> to rebind to whatever the player presses next.
    /// </summary>
    void Rebind(string action, params InputBinding[] bindings);

    /// <summary>Forgets the player's <see cref="Rebind"/> choices for <paramref name="action"/>, or for every action.</summary>
    void ResetBindings(string? action = null);

    /// <summary>The keys and buttons that trigger <paramref name="action"/>, e.g. to show them in a controls screen.</summary>
    IReadOnlyList<InputBinding> BindingsOf(string action);

    /// <summary>The names of every action: the built-in ones, and any added with <see cref="Bind"/> or <see cref="Rebind"/>.</summary>
    IReadOnlyCollection<string> Actions { get; }

    /// <summary>The key, mouse button or gamepad button pressed this frame, if any: for "press a key for Jump".</summary>
    InputBinding? AnyPressed { get; }

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

    /// <summary>Right stick, each axis -1..1 with a dead zone applied. Up is positive.</summary>
    Vector2 RightStick { get; }

    /// <summary>How far the left trigger is pulled, 0..1.</summary>
    float LeftTrigger { get; }

    /// <summary>How far the right trigger is pulled, 0..1.</summary>
    float RightTrigger { get; }

    /// <summary>True while a controller is plugged in.</summary>
    bool IsGamepadConnected { get; }

    /// <summary>
    /// True when the controller was used more recently than the keyboard and mouse, e.g. to show "Press A" rather
    /// than "Press Space".
    /// </summary>
    bool UsingGamepad { get; }

    /// <summary>
    /// Shakes the controller at <paramref name="strength"/> (0..1) for <paramref name="seconds"/> of real time. While
    /// one rumble is going, another makes it at least as strong and as long as both. Does nothing without a controller.
    /// </summary>
    void Rumble(float strength = 0.5f, float seconds = 0.2f);

    /// <summary>Mouse position in world units.</summary>
    Vector2 MouseWorld { get; }

    /// <summary>
    /// Every finger on a touch screen this frame, each where it is in world units, those lifted this frame included (their
    /// <see cref="TouchPoint.Released"/> set) and those that went down marked <see cref="TouchPoint.Pressed"/>. The first
    /// finger is also the mouse (a tap is a left click), so code that only needs one pointer reads the mouse; this is for
    /// several at once, as rhythm lanes and on-screen pads take them. Empty without a touch screen.
    /// </summary>
    IReadOnlyList<TouchPoint> Touches => [];

    /// <summary>False while the mouse is outside the game's view (or hasn't moved over it yet); buttons ignore it then.</summary>
    bool IsMouseInView { get; }

    /// <summary>How far the mouse wheel turned this frame, in notches: positive away from the player (scrolling up).</summary>
    float Scroll { get; }

    /// <summary>
    /// How far the mouse moved this frame, in pixels (right and up are positive), for turning a first-person view. It keeps
    /// counting while the game has the mouse (<see cref="IsMouseLocked"/>), however far it goes.
    /// </summary>
    Vector2 MouseDelta { get; }

    /// <summary>
    /// Set it to ask for the mouse, as a first-person game does: once the player clicks the game, the cursor is hidden and
    /// kept inside it so <see cref="MouseDelta"/> never runs out, until Esc (or switching away) gives it back.
    /// A first-person game's First Person Movement asks for it by itself.
    /// </summary>
    bool WantsMouseLock { get; set; }

    /// <summary>True while the game has the mouse (see <see cref="WantsMouseLock"/>).</summary>
    bool IsMouseLocked { get; }

    /// <summary>
    /// What the mouse pointer looks like over the game, e.g. <c>Input.Cursor = MouseCursor.Hand</c> over something to
    /// click: the system's own pointers, on the desktop and in a browser. It stays as it's set (an adventure's
    /// <c>Walker</c> sets it over hotspots by itself) and goes back to the arrow as each scene starts.
    /// </summary>
    MouseCursor Cursor { get; set; }

    /// <summary>-1..1 from A/D, the left/right arrows, the D-pad or the left stick.</summary>
    float Horizontal { get; }

    /// <summary>-1..1 from S/W, the down/up arrows, the D-pad or the left stick (up is positive).</summary>
    float Vertical { get; }

    /// <summary>
    /// <see cref="Horizontal"/> and <see cref="Vertical"/> together, never longer than 1, so moving diagonally isn't
    /// faster: <c>body.Velocity = Input.Move * Speed</c>.
    /// </summary>
    Vector2 Move { get; }

    /// <summary>
    /// Which way the player is aiming from <paramref name="origin"/>, as a direction of length 1: the right stick, or with
    /// a controller and the right stick at rest, the way the left stick points; otherwise towards the mouse. Zero when
    /// there's nothing to aim with.
    /// </summary>
    Vector2 AimFrom(Vector2 origin);

    /// <inheritdoc cref="AimFrom(Vector2)"/>
    Vector2 AimFrom(Vector3 origin);

    /// <summary>The characters typed this frame, in the keyboard's layout (with Shift, accents and so on).</summary>
    string TypedText { get; }

    /// <summary>
    /// <paramref name="text"/> with this frame's typing applied: typed characters added and Backspace (held, it repeats)
    /// taking them off, up to <paramref name="maxLength"/>. For name entry: <c>Name = Input.TypeInto(Name, 12)</c>.
    /// </summary>
    string TypeInto(string text, int maxLength = int.MaxValue);
}

/// <summary>
/// Input state fed by a platform backend: call <see cref="SetKey"/>/<see cref="SetMouseButton"/> as events
/// arrive and <see cref="EndFrame"/> after each game tick. The game calls <see cref="BeginFrame"/> at the start of each.
/// </summary>
public sealed class InputState : IInput
{
    /// <summary>Where the player's <see cref="Rebind"/> choices are kept in <see cref="SaveData"/>.</summary>
    public const string BindingsSaveKey = "griff.bindings";

    /// <summary>Stick movement smaller than this is ignored, so a resting stick doesn't drift.</summary>
    public const float DeadZone = 0.25f;

    /// <summary>A trigger pulled less than this reads as 0.</summary>
    public const float TriggerDeadZone = 0.12f;

    private readonly HashSet<Key> _down = [];
    private readonly HashSet<Key> _pressed = [];
    private readonly HashSet<Key> _released = [];
    private readonly HashSet<MouseButton> _mouseDown = [];
    private readonly HashSet<MouseButton> _mousePressed = [];
    private readonly HashSet<MouseButton> _mouseReleased = [];
    private readonly HashSet<GamepadButton> _padDown = [];
    private readonly HashSet<GamepadButton> _padPressed = [];
    private readonly HashSet<GamepadButton> _padReleased = [];
    /// <summary>
    /// The actions every game starts with, and their keys and buttons. Griff's own components use them (Platformer
    /// Movement jumps with Jump, doors and levers wait for Interact), so a game can change their bindings but not
    /// remove them.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> BuiltInActions { get; } =
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

    private readonly Dictionary<string, InputBinding[]> _actions =
        BuiltInActions.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InputBinding[]> _rebound = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _projectActions = new(StringComparer.OrdinalIgnoreCase);

    // Buffered presses: when each binding was last pressed, and the latest press each action has already reported. With
    // room for a game's worth of keys and buttons, so pressing one for the first time mid-game doesn't grow them.
    private readonly Dictionary<InputBinding, double> _pressedAt = new(64);
    private readonly Dictionary<string, double> _usedPress = new(64, StringComparer.OrdinalIgnoreCase);
    private double _clock;

    // Typed characters in order, with '\b' for each Backspace.
    private readonly StringBuilder _typed = new();
    private SaveData? _saveData;
    private float _rumbleSeconds;

    public Vector2 MouseWorld { get; set; }

    // The fingers down (and those lifted this frame), each with where the platform said it was: on the screen (from -1 to 1
    // across the view, placed in the world by PlaceTouches) or already in the world.
    private readonly List<(TouchPoint Touch, Vector2 At, bool OnScreen)> _touches = [];
    private TouchPoint[] _touchList = [];
    private bool _touchesChanged;

    public IReadOnlyList<TouchPoint> Touches
    {
        get
        {
            if (_touchesChanged)
            {
                _touchList = [.. _touches.Select(t => t.Touch)];
                _touchesChanged = false;
            }

            return _touchList;
        }
    }

    /// <summary>
    /// A finger went down, moved or was lifted. <paramref name="at"/> is in world units, or with <paramref name="onScreen"/>
    /// where it is on the view (from -1 to 1 across it, up positive), which <see cref="PlaceTouches"/> puts in the world.
    /// </summary>
    public void SetTouch(int id, Vector2 at, bool down, bool onScreen = false)
    {
        var index = _touches.FindIndex(t => t.Touch.Id == id && !t.Touch.Released);
        if (down)
        {
            if (index < 0)
            {
                _touches.Add((new TouchPoint(id, at, Pressed: true, Released: false), at, onScreen));
                UsingGamepad = false;
            }
            else
            {
                var (touch, _, _) = _touches[index];
                _touches[index] = (touch with { World = onScreen ? touch.World : at }, at, onScreen);
            }
        }
        else if (index >= 0)
        {
            var (touch, _, _) = _touches[index];
            _touches[index] = (touch with { World = onScreen ? touch.World : at, Released = true }, at, onScreen);
        }

        _touchesChanged = true;
    }

    /// <summary>Puts the fingers given on the screen in the world, through <paramref name="toWorld"/> (the view as it is now).</summary>
    public void PlaceTouches(Func<Vector2, Vector2> toWorld)
    {
        for (var i = 0; i < _touches.Count; i++)
        {
            var (touch, at, onScreen) = _touches[i];
            if (onScreen && toWorld(at) is var world && world != touch.World)
            {
                _touches[i] = (touch with { World = world }, at, onScreen);
                _touchesChanged = true;
            }
        }
    }

    /// <summary>Set by the platform as the mouse enters and leaves the view. True by default, for tests and headless runs.</summary>
    public bool IsMouseInView { get; set; } = true;

    public float Scroll { get; private set; }

    public Vector2 MouseDelta { get; private set; }

    public bool WantsMouseLock { get; set; }

    /// <summary>Set by the platform while it holds the mouse for the game (see <see cref="WantsMouseLock"/>).</summary>
    public bool IsMouseLocked { get; set; }

    public MouseCursor Cursor { get; set; }

    /// <summary>The left stick of any controller (the first held off its middle), each axis -1..1. Up is positive.</summary>
    public Vector2 LeftStick { get; private set; }

    /// <summary>The right stick of any controller (the first held off its middle), each axis -1..1. Up is positive.</summary>
    public Vector2 RightStick { get; private set; }

    /// <summary>How far any controller's left trigger is pulled (the furthest), 0..1.</summary>
    public float LeftTrigger { get; private set; }

    /// <summary>How far any controller's right trigger is pulled (the furthest), 0..1.</summary>
    public float RightTrigger { get; private set; }

    /// <summary>
    /// True while any controller is plugged in. Setting it says whether the first is (a platform with one controller);
    /// <see cref="SetGamepadConnected"/> says so of each.
    /// </summary>
    public bool IsGamepadConnected
    {
        get
        {
            foreach (var pad in _pads)
            {
                if (pad.Connected)
                    return true;
            }

            return false;
        }
        set => SetGamepadConnected(0, value);
    }

    public bool UsingGamepad { get; private set; }

    /// <summary>How hard the controller should be shaking right now, 0..1. The platform reads it each frame.</summary>
    public float RumbleStrength { get; private set; }

    public float Horizontal => Combine(Axis(Key.A, Key.Left, Key.D, Key.Right), PadAxis(GamepadButton.DPadLeft, GamepadButton.DPadRight), LeftStick.X);

    public float Vertical => Combine(Axis(Key.S, Key.Down, Key.W, Key.Up), PadAxis(GamepadButton.DPadDown, GamepadButton.DPadUp), LeftStick.Y);

    public Vector2 Move => ClampLength(new Vector2(Horizontal, Vertical));

    public Vector2 AimFrom(Vector2 origin)
    {
        if (RightStick != Vector2.Zero)
            return Vector2.Normalize(RightStick);
        if (UsingGamepad)
            return LeftStick != Vector2.Zero ? Vector2.Normalize(LeftStick) : Vector2.Zero;
        var toMouse = MouseWorld - origin;
        return IsMouseInView && toMouse.LengthSquared() > 1e-8f ? Vector2.Normalize(toMouse) : Vector2.Zero;
    }

    public Vector2 AimFrom(Vector3 origin) => AimFrom(new Vector2(origin.X, origin.Y));

    public string TypedText => _typed.ToString().Replace("\b", "");

    public string TypeInto(string text, int maxLength = int.MaxValue)
    {
        var result = new StringBuilder(text ?? "");
        foreach (var c in _typed.ToString())
        {
            if (c == '\b')
            {
                if (result.Length > 0)
                {
                    // Don't leave half of an emoji (or other surrogate pair) behind.
                    var remove = result.Length > 1 && char.IsLowSurrogate(result[^1]) && char.IsHighSurrogate(result[^2]) ? 2 : 1;
                    result.Length -= remove;
                }
            }
            else if (result.Length < maxLength && !(char.IsHighSurrogate(c) && result.Length + 1 >= maxLength))
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    public InputBinding? AnyPressed =>
        _pressed.Count > 0 ? _pressed.First()
        : _mousePressed.Count > 0 ? _mousePressed.First()
        : _padPressed.Count > 0 ? _padPressed.First()
        : null;

    public bool IsDown(GamepadButton button) => _padDown.Contains(button);

    public bool WasPressed(GamepadButton button) => _padPressed.Contains(button);

    public bool WasReleased(GamepadButton button) => _padReleased.Contains(button);

    // By index throughout: asking about an action every frame makes no garbage.
    public bool IsDown(string action)
    {
        foreach (var binding in Bindings(action))
        {
            if (IsDown(binding))
                return true;
        }

        return false;
    }

    public bool WasPressed(string action)
    {
        var pressed = false;
        foreach (var binding in Bindings(action))
        {
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
        foreach (var binding in Bindings(action))
        {
            if (WasPressed(binding))
                latest = _clock;
            else if (_pressedAt.TryGetValue(binding, out var at))
                latest = Math.Max(latest, at);
        }

        var key = action.Trim();
        if (latest < _clock - Math.Max(0, buffer) - 1e-6 || (_usedPress.TryGetValue(key, out var used) && latest <= used))
            return false;
        _usedPress[key] = latest;
        return true;
    }

    public bool WasReleased(string action)
    {
        var released = false;
        foreach (var binding in Bindings(action))
        {
            if (IsDown(binding))
                return false;
            released |= WasReleased(binding);
        }

        return released;
    }

    public void Bind(string action, params InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        _actions[action.Trim()] = [.. bindings];
        BindingsVersion++;
    }

    /// <summary>
    /// Uses the project's controls (from the editor's Controls window) as the game's own: each action in
    /// <paramref name="controls"/> gets those bindings, like <see cref="Bind"/>. Calling it again with a changed set
    /// (the controls edited while the game runs) puts back built-in actions it no longer changes and drops actions it
    /// no longer has, leaving anything scripts bound alone.
    /// </summary>
    public void UseControls(IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> controls)
    {
        foreach (var gone in _projectActions.Where(a => !controls.ContainsKey(a)).ToList())
        {
            if (BuiltInActions.TryGetValue(gone, out var builtIn))
                _actions[gone] = [.. builtIn];
            else
                _actions.Remove(gone);
        }

        BindingsVersion++;
        _projectActions.Clear();
        foreach (var (action, bindings) in controls)
        {
            if (string.IsNullOrWhiteSpace(action))
                continue;
            _actions[action.Trim()] = [.. bindings];
            _projectActions.Add(action.Trim());
        }
    }

    public void Rebind(string action, params InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        BindingsVersion++;
        _rebound[action.Trim()] = [.. bindings];
        SaveBindings();
    }

    public void ResetBindings(string? action = null)
    {
        if (action is null)
            _rebound.Clear();
        else
            _rebound.Remove(action.Trim());
        SaveBindings();
        BindingsVersion++;
    }

    public IReadOnlyList<InputBinding> BindingsOf(string action) => Bindings(action);

    // Changes whenever an action's bindings might have: bound, rebound, reset, or read from the save data. Prompts on the
    // screen are filled in again only when it (or the device being played with) changes.
    internal int BindingsVersion { get; private set; }

    /// <summary>The names of every action, built in, bound or rebound.</summary>
    public IReadOnlyCollection<string> Actions => _actions.Keys.Union(_rebound.Keys, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Keeps the player's <see cref="Rebind"/> choices in <paramref name="saveData"/>, replacing the current ones with
    /// those saved there. The game calls this whenever its save data is set.
    /// </summary>
    public void UseSaveData(SaveData saveData)
    {
        _saveData = saveData;
        _rebound.Clear();
        BindingsVersion++;
        if (saveData.GetString(BindingsSaveKey) is not { } json)
            return;

        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json) ?? [];
            foreach (var (action, names) in saved)
            {
                if (string.IsNullOrWhiteSpace(action))
                    continue;
                // A hand-edited or out-of-date name is skipped rather than losing the rest.
                _rebound[action.Trim()] = [.. names.Select(n => InputBinding.TryParse(n, out var b) ? b : (InputBinding?)null).OfType<InputBinding>()];
            }
        }
        catch (JsonException)
        {
            // Unreadable: play with the game's own controls.
        }
    }

    private void SaveBindings()
    {
        if (_saveData is null)
            return;
        if (_rebound.Count == 0)
            _saveData.Delete(BindingsSaveKey);
        else
            _saveData.Set(BindingsSaveKey, JsonSerializer.Serialize(_rebound.ToDictionary(p => p.Key, p => p.Value.Select(b => b.ToString()).ToArray())));
    }

    internal InputBinding[] Bindings(string action) =>
        _rebound.TryGetValue(action.Trim(), out var rebound) ? rebound
        : _actions.TryGetValue(action.Trim(), out var bindings) ? bindings
        : throw new ArgumentException(
            $"There's no input action called \"{action}\"; there are {string.Join(", ", Actions)}. Add one with Input.Bind(\"{action}\", ...).",
            nameof(action));

    private bool IsDown(InputBinding binding) =>
        binding.Key is { } key ? IsDown(key) : binding.Mouse is { } mouse ? IsDown(mouse) : binding.Gamepad is { } pad && IsDown(pad);

    private bool WasPressed(InputBinding binding) =>
        binding.Key is { } key ? WasPressed(key) : binding.Mouse is { } mouse ? WasPressed(mouse) : binding.Gamepad is { } pad && WasPressed(pad);

    private bool WasReleased(InputBinding binding) =>
        binding.Key is { } key ? WasReleased(key) : binding.Mouse is { } mouse ? WasReleased(mouse) : binding.Gamepad is { } pad && WasReleased(pad);

    /// <summary>Sets the first controller's state for this frame (called by the platform each tick).</summary>
    public void SetGamepad(IEnumerable<GamepadButton> down, Vector2 leftStick) =>
        SetGamepad(0, down, leftStick, Vector2.Zero);

    /// <summary>Sets the first controller's state for this frame (called by the platform each tick). Triggers are 0..1.</summary>
    public void SetGamepad(IEnumerable<GamepadButton> down, Vector2 leftStick, Vector2 rightStick, float leftTrigger = 0, float rightTrigger = 0) =>
        SetGamepad(0, down, leftStick, rightStick, leftTrigger, rightTrigger);

    /// <summary>How many controllers it keeps apart: up to four, as XInput and most browsers give.</summary>
    public const int MaxGamepads = 4;

    /// <summary>
    /// Sets controller <paramref name="index"/>'s state for this frame (0 to <see cref="MaxGamepads"/> - 1, the platform's own
    /// numbering; called by it each tick). Triggers are 0..1. What the game reads by itself (<see cref="IsDown(GamepadButton)"/>,
    /// the sticks, the actions) is every controller at once; a player at this machine reads their own (see
    /// <see cref="LocalPlayers"/>).
    /// </summary>
    public void SetGamepad(int index, IEnumerable<GamepadButton> down, Vector2 leftStick, Vector2 rightStick, float leftTrigger = 0, float rightTrigger = 0)
    {
        if (index is < 0 or >= MaxGamepads)
            return;
        var pad = _pads[index];
        pad.Raw.Clear();
        pad.Raw.UnionWith(down);
        (pad.RawLeftStick, pad.RawRightStick, pad.RawLeftTrigger, pad.RawRightTrigger) = (leftStick, rightStick, leftTrigger, rightTrigger);
        Apply(pad);
    }

    /// <summary>
    /// Says whether controller <paramref name="index"/> is plugged in (called by the platform as controllers come and go).
    /// Unplugged, it stops counting as plugged in even if <see cref="HoldGamepadButton"/> pretended it was.
    /// </summary>
    public void SetGamepadConnected(int index, bool connected)
    {
        if (index is < 0 or >= MaxGamepads)
            return;
        _pads[index].PlatformConnected = connected;
        if (!connected)
            _pads[index].Pretend = false;
    }

    /// <summary>
    /// Holds (or lets go of) <paramref name="button"/> on controller <paramref name="index"/> as if pressed on it, over what the
    /// controller itself says: for tests, agents and demos playing as a second player's controller. A controller with a button
    /// held this way counts as plugged in from then on.
    /// </summary>
    public void HoldGamepadButton(int index, GamepadButton button, bool down)
    {
        if (index is < 0 or >= MaxGamepads)
            return;
        var pad = _pads[index];
        if (down)
            pad.Pretend = true;
        if (down ? !pad.Held.Add(button) : !pad.Held.Remove(button))
            return;
        Apply(pad);
    }

    /// <summary>How many controllers are plugged in.</summary>
    public int GamepadCount
    {
        get
        {
            var count = 0;
            foreach (var pad in _pads)
            {
                if (pad.Connected)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// The platform's number for the <paramref name="nth"/> controller plugged in (from 0: the first plugged in is 0, whatever
    /// slot it's in), or -1 if there aren't that many.
    /// </summary>
    public int ConnectedGamepad(int nth)
    {
        for (var i = 0; i < MaxGamepads; i++)
        {
            if (!_pads[i].Connected)
                continue;
            if (nth == 0)
                return i;
            nth--;
        }

        return -1;
    }

    /// <summary>True while controller <paramref name="index"/> is plugged in (or has had a button held by <see cref="HoldGamepadButton"/>).</summary>
    public bool IsGamepadConnectedAt(int index) => index is >= 0 and < MaxGamepads && _pads[index].Connected;

    /// <summary>True while <paramref name="button"/> is held on controller <paramref name="index"/>.</summary>
    public bool IsDown(int index, GamepadButton button) => index is >= 0 and < MaxGamepads && _pads[index].Down.Contains(button);

    /// <summary>True on the frame <paramref name="button"/> goes down on controller <paramref name="index"/>.</summary>
    public bool WasPressed(int index, GamepadButton button) => index is >= 0 and < MaxGamepads && _pads[index].Pressed.Contains(button);

    /// <summary>True on the frame <paramref name="button"/> is let go on controller <paramref name="index"/>.</summary>
    public bool WasReleased(int index, GamepadButton button) => index is >= 0 and < MaxGamepads && _pads[index].Released.Contains(button);

    /// <summary>Controller <paramref name="index"/>'s left stick, with the dead zone applied.</summary>
    public Vector2 LeftStickOf(int index) => index is >= 0 and < MaxGamepads ? _pads[index].LeftStick : Vector2.Zero;

    /// <summary>Controller <paramref name="index"/>'s right stick, with the dead zone applied.</summary>
    public Vector2 RightStickOf(int index) => index is >= 0 and < MaxGamepads ? _pads[index].RightStick : Vector2.Zero;

    /// <summary>How far controller <paramref name="index"/>'s left trigger is pulled, 0..1.</summary>
    public float LeftTriggerOf(int index) => index is >= 0 and < MaxGamepads ? _pads[index].LeftTrigger : 0;

    /// <summary>How far controller <paramref name="index"/>'s right trigger is pulled, 0..1.</summary>
    public float RightTriggerOf(int index) => index is >= 0 and < MaxGamepads ? _pads[index].RightTrigger : 0;

    /// <summary>The first button pressed on controller <paramref name="index"/> this frame, if any.</summary>
    public GamepadButton? PressedOn(int index)
    {
        if (index is < 0 or >= MaxGamepads)
            return null;
        foreach (var button in _pads[index].Pressed)
            return button;
        return null;
    }

    /// <summary>
    /// Shakes controller <paramref name="index"/> alone (a player's own), as <see cref="Rumble(float, float)"/> shakes every
    /// controller.
    /// </summary>
    public void Rumble(int index, float strength, float seconds)
    {
        if (index is < 0 or >= MaxGamepads || !float.IsFinite(strength) || !float.IsFinite(seconds) || seconds <= 0 || strength <= 0)
            return;
        var pad = _pads[index];
        pad.RumbleStrength = Math.Max(pad.RumbleSeconds > 0 ? pad.RumbleStrength : 0, Math.Min(strength, 1));
        pad.RumbleSeconds = Math.Max(pad.RumbleSeconds, seconds);
    }

    /// <summary>
    /// How hard controller <paramref name="index"/> should be shaking now, 0..1: its own rumble or every controller's. The
    /// platform reads it each frame.
    /// </summary>
    public float RumbleStrengthOf(int index) =>
        index is >= 0 and < MaxGamepads ? Math.Max(RumbleStrength, _pads[index].RumbleStrength) : RumbleStrength;

    // When a controller's button (or one held for it) went down last, on the game's clock, for buffered presses.
    internal double? PressedAt(int index, GamepadButton button) =>
        index is >= 0 and < MaxGamepads && _pads[index].PressedAt.TryGetValue(button, out var at) ? at : null;

    // When a key or mouse button went down last, on the game's clock.
    internal double? PressedAt(InputBinding binding) => _pressedAt.TryGetValue(binding, out var at) ? at : null;

    // The game's clock, in seconds, that presses are dated by.
    internal double Clock => _clock;

    // One controller: what the platform says it has held (and its sticks and triggers), what's held for it by
    // HoldGamepadButton, and what that comes to this frame.
    private sealed class Pad
    {
        public readonly HashSet<GamepadButton> Raw = [];
        public readonly HashSet<GamepadButton> Held = [];
        public readonly HashSet<GamepadButton> Down = [];
        public readonly HashSet<GamepadButton> Pressed = [];
        public readonly HashSet<GamepadButton> Released = [];
        public readonly HashSet<GamepadButton> Now = [];
        public readonly Dictionary<GamepadButton, double> PressedAt = [];
        public Vector2 RawLeftStick, RawRightStick, LeftStick, RightStick;
        public float RawLeftTrigger, RawRightTrigger, LeftTrigger, RightTrigger;
        public bool PlatformConnected, Pretend;
        public float RumbleStrength, RumbleSeconds;

        public bool Connected => PlatformConnected || Pretend;
    }

    private readonly Pad[] _pads = [new(), new(), new(), new()];

    // A controller's state worked out from what it says and what's held for it; then what the game reads of every one.
    private void Apply(Pad pad)
    {
        pad.LeftTrigger = ApplyDeadZone(Math.Clamp(pad.RawLeftTrigger, 0, 1), TriggerDeadZone);
        pad.RightTrigger = ApplyDeadZone(Math.Clamp(pad.RawRightTrigger, 0, 1), TriggerDeadZone);
        var now = pad.Now;
        now.Clear();
        now.UnionWith(pad.Raw);
        now.UnionWith(pad.Held);
        if (pad.LeftTrigger > 0.5f)
            now.Add(GamepadButton.LeftTrigger);
        if (pad.RightTrigger > 0.5f)
            now.Add(GamepadButton.RightTrigger);

        foreach (var button in now)
        {
            if (pad.Down.Contains(button))
                continue;
            pad.Pressed.Add(button);
            pad.PressedAt[button] = _clock;
            UsingGamepad = true;
        }

        foreach (var button in pad.Down)
        {
            if (!now.Contains(button))
                pad.Released.Add(button);
        }

        pad.Down.Clear();
        pad.Down.UnionWith(now);
        pad.LeftStick = ApplyDeadZone(pad.RawLeftStick);
        pad.RightStick = ApplyDeadZone(pad.RawRightStick);
        if (pad.LeftStick != Vector2.Zero || pad.RightStick != Vector2.Zero)
            UsingGamepad = true;
        CombinePads();
    }

    // Every controller together, as the game reads them by itself: a button is held while it is on any, the sticks are the
    // first held off their middle, the triggers the furthest pulled.
    private void CombinePads()
    {
        _padDown.Clear();
        _padPressed.Clear();
        _padReleased.Clear();
        (LeftStick, RightStick, LeftTrigger, RightTrigger) = (Vector2.Zero, Vector2.Zero, 0, 0);
        foreach (var pad in _pads)
        {
            _padDown.UnionWith(pad.Down);
            _padPressed.UnionWith(pad.Pressed);
            _padReleased.UnionWith(pad.Released);
            if (LeftStick == Vector2.Zero)
                LeftStick = pad.LeftStick;
            if (RightStick == Vector2.Zero)
                RightStick = pad.RightStick;
            LeftTrigger = Math.Max(LeftTrigger, pad.LeftTrigger);
            RightTrigger = Math.Max(RightTrigger, pad.RightTrigger);
        }

        _padReleased.ExceptWith(_padDown);
        foreach (var button in _padPressed)
            _pressedAt[button] = _clock;
    }

    public bool IsDown(Key key) => _down.Contains(key);

    // What's held and what happened this frame, for sending to the host of a network game.
    internal IReadOnlyCollection<Key> DownKeys => _down;

    internal IReadOnlyCollection<Key> PressedKeys => _pressed;

    internal IReadOnlyCollection<Key> ReleasedKeys => _released;

    internal IReadOnlyCollection<MouseButton> DownMouse => _mouseDown;

    internal IReadOnlyCollection<MouseButton> PressedMouse => _mousePressed;

    internal IReadOnlyCollection<MouseButton> ReleasedMouse => _mouseReleased;

    internal IReadOnlyCollection<GamepadButton> DownPad => _padDown;

    internal IReadOnlyCollection<GamepadButton> PressedPad => _padPressed;

    internal IReadOnlyCollection<GamepadButton> ReleasedPad => _padReleased;

    internal string RawTyped => _typed.ToString();

    /// <summary>Uses <paramref name="other"/>'s actions and their bindings (but not its player's rebinding) from now on.</summary>
    internal void CopyBindingsFrom(InputState other)
    {
        _actions.Clear();
        foreach (var (action, bindings) in other._actions)
            _actions[action] = [.. bindings];
        _projectActions.Clear();
        _projectActions.UnionWith(other._projectActions);
    }

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
        {
            _pressed.Add(key);
            _pressedAt[key] = _clock;
            UsingGamepad = false;
        }
        else if (!down && _down.Remove(key))
        {
            _released.Add(key);
        }
    }

    public void SetMouseButton(MouseButton button, bool down)
    {
        if (down && _mouseDown.Add(button))
        {
            _mousePressed.Add(button);
            _pressedAt[button] = _clock;
            UsingGamepad = false;
        }
        else if (!down && _mouseDown.Remove(button))
        {
            _mouseReleased.Add(button);
        }
    }

    /// <summary>Called by the platform when the mouse moves over the game: the player has switched to the mouse.</summary>
    public void MouseMoved() => UsingGamepad = false;

    /// <summary>Adds mouse movement, in pixels (right and up are positive), for <see cref="MouseDelta"/>.</summary>
    public void AddMouseDelta(Vector2 pixels)
    {
        MouseDelta += pixels;
        if (pixels != Vector2.Zero)
            UsingGamepad = false;
    }

    /// <summary>Adds mouse wheel movement, in notches (positive is scrolling up).</summary>
    public void AddScroll(float notches) => Scroll += notches;

    /// <summary>Adds typed characters. Control characters are ignored; Backspace goes through <see cref="TypeBackspace"/>.</summary>
    public void AddText(string text)
    {
        foreach (var c in text.Where(c => !char.IsControl(c)))
            _typed.Append(c);
    }

    /// <summary>Records a Backspace for <see cref="TypeInto"/>, including the repeats while it's held.</summary>
    public void TypeBackspace() => _typed.Append('\b');

    public void Rumble(float strength = 0.5f, float seconds = 0.2f)
    {
        if (!float.IsFinite(strength) || !float.IsFinite(seconds) || seconds <= 0 || strength <= 0)
            return;
        RumbleStrength = Math.Max(_rumbleSeconds > 0 ? RumbleStrength : 0, Math.Min(strength, 1));
        _rumbleSeconds = Math.Max(_rumbleSeconds, seconds);
    }

    /// <summary>Stops any rumble straight away, e.g. when the game pauses or stops.</summary>
    public void StopRumble()
    {
        RumbleStrength = 0;
        _rumbleSeconds = 0;
        foreach (var pad in _pads)
            (pad.RumbleStrength, pad.RumbleSeconds) = (0, 0);
    }

    /// <summary>Releases everything, e.g. when the game window loses focus.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _down.ToList())
            SetKey(key, false);
        // Silently, so a held button doesn't count as a click on whatever is under the cursor.
        _mouseDown.Clear();
        _touches.Clear();
        _touchesChanged = true;
    }

    /// <summary>
    /// Starts a game frame <paramref name="realSeconds"/> after the last one: dates this frame's presses (which arrived
    /// since the last frame) for buffered <see cref="WasPressed(string, float)"/>, and counts down any rumble.
    /// </summary>
    public void BeginFrame(float realSeconds)
    {
        var seconds = Math.Max(0, realSeconds);
        _clock += seconds;
        foreach (var key in _pressed)
            _pressedAt[key] = _clock;
        foreach (var button in _mousePressed)
            _pressedAt[button] = _clock;
        foreach (var button in _padPressed)
            _pressedAt[button] = _clock;
        foreach (var pad in _pads)
        {
            foreach (var button in pad.Pressed)
                pad.PressedAt[button] = _clock;
            if (pad.RumbleSeconds > 0)
            {
                pad.RumbleSeconds -= seconds;
                if (pad.RumbleSeconds <= 0)
                    (pad.RumbleStrength, pad.RumbleSeconds) = (0, 0);
            }
        }

        if (_rumbleSeconds > 0)
        {
            _rumbleSeconds -= seconds;
            if (_rumbleSeconds <= 0)
                StopRumble();
        }
    }

    public void EndFrame()
    {
        _pressed.Clear();
        _released.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        _padPressed.Clear();
        _padReleased.Clear();
        if (_touches.RemoveAll(t => t.Touch.Released) > 0)
            _touchesChanged = true;
        for (var i = 0; i < _touches.Count; i++)
        {
            if (_touches[i].Touch.Pressed)
            {
                _touches[i] = _touches[i] with { Touch = _touches[i].Touch with { Pressed = false } };
                _touchesChanged = true;
            }
        }

        foreach (var pad in _pads)
        {
            pad.Pressed.Clear();
            pad.Released.Clear();
        }

        _typed.Clear();
        Scroll = 0;
        MouseDelta = Vector2.Zero;
    }

    private float PadAxis(GamepadButton negative, GamepadButton positive) =>
        (IsDown(positive) ? 1f : 0f) - (IsDown(negative) ? 1f : 0f);

    // Digital input wins when pressed; otherwise the stick gives analog control.
    private static float Combine(float keys, float dpad, float stick) =>
        Math.Clamp(keys != 0 ? keys : dpad != 0 ? dpad : stick, -1f, 1f);

    private static Vector2 ApplyDeadZone(Vector2 stick) => new(ApplyDeadZone(stick.X, DeadZone), ApplyDeadZone(stick.Y, DeadZone));

    private static float ApplyDeadZone(float value, float deadZone) =>
        Math.Abs(value) < deadZone ? 0 : Math.Sign(value) * Math.Min(1, (Math.Abs(value) - deadZone) / (1 - deadZone));

    private static Vector2 ClampLength(Vector2 value) => value.LengthSquared() > 1 ? Vector2.Normalize(value) : value;

    private float Axis(Key negative, Key negativeAlt, Key positive, Key positiveAlt) =>
        (IsDown(positive) || IsDown(positiveAlt) ? 1f : 0f) - (IsDown(negative) || IsDown(negativeAlt) ? 1f : 0f);
}
