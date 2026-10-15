using System.Numerics;
using System.Text;
using Griff.Input;
using Griff.Networking;

namespace Griff.Runtime;

/// <summary>
/// Another player's controls, as their machine reports them to the host: what's held, what was pressed, where the mouse
/// is. Griff's components read it for whatever that player controls, so their character moves, shoots and builds on the
/// host just as yours does.
/// </summary>
internal sealed class RemoteInput : IInput
{
    private readonly HashSet<Key> _down = [];
    private readonly HashSet<MouseButton> _mouseDown = [];
    private readonly HashSet<GamepadButton> _padDown = [];
    private readonly HashSet<string> _actionsDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Frame _next = new();
    private Frame _now = new();
    private readonly Dictionary<string, double> _actionPressedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _usedPress = new(StringComparer.OrdinalIgnoreCase);
    private readonly IInput _bindings;
    private uint _lastFrame;
    private uint _lastEvent;
    private double _clock;
    private bool _any;

    /// <param name="bindings">Where the action names and bindings come from (the host's own input).</param>
    public RemoteInput(IInput bindings) => _bindings = bindings;

    /// <summary>A rumble the host asked for, for the session to pass on to the player.</summary>
    public (float Strength, float Seconds)? PendingRumble { get; set; }

    /// <summary>The player's view of the world as of their last report: what their camera shows.</summary>
    public (Vector2 Center, float HalfHeight, float Aspect) View { get; private set; } = (Vector2.Zero, 5, 16f / 9f);

    public bool HasView { get; private set; }

    // What happened between two host frames.
    private sealed class Frame
    {
        public HashSet<Key> Pressed { get; } = [];
        public HashSet<Key> Released { get; } = [];
        public HashSet<MouseButton> MousePressed { get; } = [];
        public HashSet<MouseButton> MouseReleased { get; } = [];
        public HashSet<GamepadButton> PadPressed { get; } = [];
        public HashSet<GamepadButton> PadReleased { get; } = [];
        public HashSet<string> ActionsPressed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ActionsReleased { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Vector2 MouseDelta { get; set; }
        public float Scroll { get; set; }
        public StringBuilder Typed { get; } = new();

        public void Clear()
        {
            Pressed.Clear();
            Released.Clear();
            MousePressed.Clear();
            MouseReleased.Clear();
            PadPressed.Clear();
            PadReleased.Clear();
            ActionsPressed.Clear();
            ActionsReleased.Clear();
            MouseDelta = Vector2.Zero;
            Scroll = 0;
            Typed.Clear();
        }
    }

    // ----- Reading reports -----

    /// <summary>Takes in one report (see <see cref="InputReport.Write"/>). Old or repeated news is ignored.</summary>
    public void Receive(ref NetReader r)
    {
        var frame = r.UInt();
        var fresh = !_any || frame > _lastFrame;
        var keys = new HashSet<Key>();
        var count = r.VarUInt();
        for (var i = 0; i < count; i++)
            keys.Add((Key)r.VarInt());
        var mouse = r.Byte();
        var pad = r.UShort();
        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        count = r.VarUInt();
        for (var i = 0; i < count; i++)
            actions.Add(r.String() ?? "");
        var leftStick = r.Vector2();
        var rightStick = r.Vector2();
        var leftTrigger = r.Float();
        var rightTrigger = r.Float();
        var mouseWorld = r.Vector2();
        var mouseDelta = r.Vector2();
        var scroll = r.Float();
        var flags = r.Byte();
        var typed = r.String() ?? "";
        var view = (r.Vector2(), r.Float(), r.Float());

        // Presses and releases come as numbered events, repeated until the host has them, so none is lost.
        count = r.VarUInt();
        for (var i = 0; i < count; i++)
        {
            var id = r.VarUInt();
            var kind = r.Byte();
            var code = kind is 3 or 7 ? 0 : r.VarInt();
            var action = kind is 3 or 7 ? r.String() ?? "" : null;
            if (_any && id <= _lastEvent)
                continue;
            _lastEvent = Math.Max(_lastEvent, id);
            switch (kind)
            {
                case 0: _next.Pressed.Add((Key)code); break;
                case 1: _next.MousePressed.Add((MouseButton)code); break;
                case 2: _next.PadPressed.Add((GamepadButton)code); break;
                case 3: _next.ActionsPressed.Add(action!); _actionPressedAt[action!] = _clock; break;
                case 4: _next.Released.Add((Key)code); break;
                case 5: _next.MouseReleased.Add((MouseButton)code); break;
                case 6: _next.PadReleased.Add((GamepadButton)code); break;
                case 7: _next.ActionsReleased.Add(action!); break;
            }
        }

        if (!fresh)
            return;
        _any = true;
        _lastFrame = frame;
        Replace(_down, keys);
        _mouseDown.Clear();
        foreach (var button in Enum.GetValues<MouseButton>())
        {
            if ((mouse & (1 << (int)button)) != 0)
                _mouseDown.Add(button);
        }

        _padDown.Clear();
        foreach (var button in Enum.GetValues<GamepadButton>())
        {
            if ((pad & (1 << (int)button)) != 0)
                _padDown.Add(button);
        }

        Replace(_actionsDown, actions);
        (LeftStick, RightStick, LeftTrigger, RightTrigger) = (leftStick, rightStick, leftTrigger, rightTrigger);
        MouseWorld = mouseWorld;
        _next.MouseDelta += mouseDelta;
        _next.Scroll += scroll;
        _next.Typed.Append(typed);
        IsMouseInView = (flags & 1) != 0;
        IsMouseLocked = (flags & 2) != 0;
        UsingGamepad = (flags & 4) != 0;
        IsGamepadConnected = (flags & 8) != 0;
        MouseOnUi = (flags & 16) != 0;
        View = view;
        HasView = true;

        static void Replace<T>(HashSet<T> set, HashSet<T> with)
        {
            set.Clear();
            set.UnionWith(with);
        }
    }

    /// <summary>Starts a host frame: everything reported since the last one counts as this frame's.</summary>
    public void BeginFrame(float seconds)
    {
        _clock += Math.Max(0, seconds);
        (_now, var old) = (Swap(), _now);
        old.Clear();

        Frame Swap()
        {
            var frame = new Frame();
            frame.Pressed.UnionWith(_next.Pressed);
            frame.Released.UnionWith(_next.Released);
            frame.MousePressed.UnionWith(_next.MousePressed);
            frame.MouseReleased.UnionWith(_next.MouseReleased);
            frame.PadPressed.UnionWith(_next.PadPressed);
            frame.PadReleased.UnionWith(_next.PadReleased);
            frame.ActionsPressed.UnionWith(_next.ActionsPressed);
            frame.ActionsReleased.UnionWith(_next.ActionsReleased);
            frame.MouseDelta = _next.MouseDelta;
            frame.Scroll = _next.Scroll;
            frame.Typed.Append(_next.Typed);
            _next.Clear();
            return frame;
        }
    }

    /// <summary>True while the player's mouse is over their own HUD or a menu, so their clicks aren't meant for the world.</summary>
    public bool MouseOnUi { get; private set; }

    // ----- IInput -----

    public bool IsDown(string action) => _actionsDown.Contains(action.Trim()) || _now.ActionsPressed.Contains(action.Trim());

    public bool WasPressed(string action) => _now.ActionsPressed.Contains(action.Trim());

    public bool WasPressed(string action, float buffer)
    {
        var key = action.Trim();
        if (!_actionPressedAt.TryGetValue(key, out var at) || at < _clock - Math.Max(0, buffer) - 1e-6)
            return false;
        if (_usedPress.TryGetValue(key, out var used) && at <= used)
            return false;
        _usedPress[key] = at;
        return true;
    }

    public bool WasReleased(string action) => _now.ActionsReleased.Contains(action.Trim());

    public void Bind(string action, params InputBinding[] bindings) => _bindings.Bind(action, bindings);

    // A player's own choices are made on their own machine.
    public void Rebind(string action, params InputBinding[] bindings)
    {
    }

    public void ResetBindings(string? action = null)
    {
    }

    public IReadOnlyList<InputBinding> BindingsOf(string action) => _bindings.BindingsOf(action);

    public IReadOnlyCollection<string> Actions => _bindings.Actions;

    public InputBinding? AnyPressed =>
        _now.Pressed.Count > 0 ? _now.Pressed.First()
        : _now.MousePressed.Count > 0 ? _now.MousePressed.First()
        : _now.PadPressed.Count > 0 ? _now.PadPressed.First()
        : null;

    public bool WasReleased(GamepadButton button) => _now.PadReleased.Contains(button);

    public bool IsDown(Key key) => _down.Contains(key) || _now.Pressed.Contains(key);

    public bool WasPressed(Key key) => _now.Pressed.Contains(key);

    public bool WasReleased(Key key) => _now.Released.Contains(key);

    public bool IsDown(MouseButton button) => _mouseDown.Contains(button) || _now.MousePressed.Contains(button);

    public bool WasPressed(MouseButton button) => _now.MousePressed.Contains(button);

    public bool WasReleased(MouseButton button) => _now.MouseReleased.Contains(button);

    public bool IsDown(GamepadButton button) => _padDown.Contains(button) || _now.PadPressed.Contains(button);

    public bool WasPressed(GamepadButton button) => _now.PadPressed.Contains(button);

    public Vector2 LeftStick { get; private set; }

    public Vector2 RightStick { get; private set; }

    public float LeftTrigger { get; private set; }

    public float RightTrigger { get; private set; }

    public bool IsGamepadConnected { get; private set; }

    public bool UsingGamepad { get; private set; }

    public void Rumble(float strength = 0.5f, float seconds = 0.2f)
    {
        if (strength > 0 && seconds > 0 && float.IsFinite(strength) && float.IsFinite(seconds))
            PendingRumble = PendingRumble is { } was ? (Math.Max(was.Strength, strength), Math.Max(was.Seconds, seconds)) : (strength, seconds);
    }

    public Vector2 MouseWorld { get; private set; }

    public bool IsMouseInView { get; private set; }

    public float Scroll => _now.Scroll;

    public Vector2 MouseDelta => _now.MouseDelta;

    public bool WantsMouseLock { get; set; }

    public bool IsMouseLocked { get; private set; }

    // The pointer is shown on the player's own machine.
    public MouseCursor Cursor { get; set; }

    public float Horizontal => Combine(Axis(Key.A, Key.Left, Key.D, Key.Right), PadAxis(GamepadButton.DPadLeft, GamepadButton.DPadRight), LeftStick.X);

    public float Vertical => Combine(Axis(Key.S, Key.Down, Key.W, Key.Up), PadAxis(GamepadButton.DPadDown, GamepadButton.DPadUp), LeftStick.Y);

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
        if (UsingGamepad)
            return LeftStick != Vector2.Zero ? Vector2.Normalize(LeftStick) : Vector2.Zero;
        var toMouse = MouseWorld - origin;
        return IsMouseInView && toMouse.LengthSquared() > 1e-8f ? Vector2.Normalize(toMouse) : Vector2.Zero;
    }

    public Vector2 AimFrom(Vector3 origin) => AimFrom(new Vector2(origin.X, origin.Y));

    public string TypedText => _now.Typed.ToString().Replace("\b", "");

    public string TypeInto(string text, int maxLength = int.MaxValue)
    {
        var result = new StringBuilder(text ?? "");
        foreach (var c in _now.Typed.ToString())
        {
            if (c == '\b')
            {
                if (result.Length > 0)
                    result.Length--;
            }
            else if (result.Length < maxLength)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private float PadAxis(GamepadButton negative, GamepadButton positive) =>
        (IsDown(positive) ? 1f : 0f) - (IsDown(negative) ? 1f : 0f);

    private static float Combine(float keys, float dpad, float stick) =>
        Math.Clamp(keys != 0 ? keys : dpad != 0 ? dpad : stick, -1f, 1f);

    private float Axis(Key negative, Key negativeAlt, Key positive, Key positiveAlt) =>
        (IsDown(positive) || IsDown(positiveAlt) ? 1f : 0f) - (IsDown(negative) || IsDown(negativeAlt) ? 1f : 0f);
}

/// <summary>Nothing held, nothing pressed: for a character nobody is controlling right now.</summary>
internal sealed class NoInput(IInput bindings) : IInput
{
    public bool IsDown(string action) => false;
    public bool WasPressed(string action) => false;
    public bool WasPressed(string action, float buffer) => false;
    public bool WasReleased(string action) => false;
    public void Bind(string action, params InputBinding[] b) => bindings.Bind(action, b);
    public void Rebind(string action, params InputBinding[] b) { }
    public void ResetBindings(string? action = null) { }
    public IReadOnlyList<InputBinding> BindingsOf(string action) => bindings.BindingsOf(action);
    public IReadOnlyCollection<string> Actions => bindings.Actions;
    public InputBinding? AnyPressed => null;
    public bool WasReleased(GamepadButton button) => false;
    public bool IsDown(Key key) => false;
    public bool WasPressed(Key key) => false;
    public bool WasReleased(Key key) => false;
    public bool IsDown(MouseButton button) => false;
    public bool WasPressed(MouseButton button) => false;
    public bool WasReleased(MouseButton button) => false;
    public bool IsDown(GamepadButton button) => false;
    public bool WasPressed(GamepadButton button) => false;
    public Vector2 LeftStick => Vector2.Zero;
    public Vector2 RightStick => Vector2.Zero;
    public float LeftTrigger => 0;
    public float RightTrigger => 0;
    public bool IsGamepadConnected => false;
    public bool UsingGamepad => false;
    public void Rumble(float strength = 0.5f, float seconds = 0.2f) { }
    public Vector2 MouseWorld => bindings.MouseWorld;
    public bool IsMouseInView => false;
    public float Scroll => 0;
    public Vector2 MouseDelta => Vector2.Zero;
    public bool WantsMouseLock { get => false; set { } }
    public bool IsMouseLocked => false;
    public MouseCursor Cursor { get => bindings.Cursor; set => bindings.Cursor = value; }
    public float Horizontal => 0;
    public float Vertical => 0;
    public Vector2 Move => Vector2.Zero;
    public Vector2 AimFrom(Vector2 origin) => Vector2.Zero;
    public Vector2 AimFrom(Vector3 origin) => Vector2.Zero;
    public string TypedText => "";
    public string TypeInto(string text, int maxLength = int.MaxValue) => text;
}

/// <summary>Writes this machine's controls as a report for the host, and keeps presses until the host has them.</summary>
internal sealed class InputReport
{
    private readonly List<(uint Id, byte Kind, int Code, string? Action)> _events = [];
    private uint _frame;
    private uint _nextEvent = 1;

    /// <summary>Notes this frame's presses and releases. <paramref name="blocked"/> (a menu open) sends nothing held.</summary>
    public void Sample(InputState input, bool blocked)
    {
        if (blocked)
            return;
        foreach (var key in input.PressedKeys)
            Add(0, (int)key);
        foreach (var button in input.PressedMouse)
            Add(1, (int)button);
        foreach (var button in input.PressedPad)
            Add(2, (int)button);
        foreach (var key in input.ReleasedKeys)
            Add(4, (int)key);
        foreach (var button in input.ReleasedMouse)
            Add(5, (int)button);
        foreach (var button in input.ReleasedPad)
            Add(6, (int)button);
        foreach (var action in input.Actions)
        {
            if (input.WasPressed(action))
                _events.Add((_nextEvent++, 3, 0, action));
            else if (input.WasReleased(action))
                _events.Add((_nextEvent++, 7, 0, action));
        }

        // Don't let a long silence from the host grow the list forever.
        if (_events.Count > 200)
            _events.RemoveRange(0, _events.Count - 200);

        void Add(byte kind, int code) => _events.Add((_nextEvent++, kind, code, null));
    }

    /// <summary>The report to send now, and what to call once the host confirms it arrived.</summary>
    public (byte[] Data, Action Acked) Write(InputState input, bool blocked, bool mouseOnUi, (Vector2 Center, float HalfHeight, float Aspect) view)
    {
        var w = new NetWriter(160);
        w.Byte((byte)NetMessage.Input);
        w.UInt(++_frame);
        if (blocked)
        {
            w.VarUInt(0);
            w.Byte(0);
            w.UShort(0);
            w.VarUInt(0);
            w.Vector2(Vector2.Zero);
            w.Vector2(Vector2.Zero);
            w.Float(0);
            w.Float(0);
        }
        else
        {
            w.VarUInt((uint)input.DownKeys.Count);
            foreach (var key in input.DownKeys)
                w.VarInt((int)key);
            var mouse = 0;
            foreach (var button in input.DownMouse)
                mouse |= 1 << (int)button;
            w.Byte((byte)mouse);
            var pad = 0;
            foreach (var button in input.DownPad)
                pad |= 1 << (int)button;
            w.UShort((ushort)pad);
            var down = input.Actions.Where(input.IsDown).ToList();
            w.VarUInt((uint)down.Count);
            foreach (var action in down)
                w.String(action);
            w.Vector2(input.LeftStick);
            w.Vector2(input.RightStick);
            w.Float(input.LeftTrigger);
            w.Float(input.RightTrigger);
        }

        w.Vector2(input.MouseWorld);
        w.Vector2(blocked ? Vector2.Zero : input.MouseDelta);
        w.Float(blocked ? 0 : input.Scroll);
        var flags = (input.IsMouseInView && !blocked ? 1 : 0) | (input.IsMouseLocked ? 2 : 0) | (input.UsingGamepad ? 4 : 0) |
                    (input.IsGamepadConnected ? 8 : 0) | (mouseOnUi ? 16 : 0);
        w.Byte((byte)flags);
        w.String(blocked ? "" : input.RawTyped);
        w.Vector2(view.Center);
        w.Float(view.HalfHeight);
        w.Float(view.Aspect);

        // As many unconfirmed events as fit, oldest first.
        var sending = new List<uint>();
        var room = NetConnection.MaxUnreliable - w.Length - 8;
        var fit = 0;
        var size = 0;
        foreach (var e in _events)
        {
            var bytes = 6 + (e.Action?.Length ?? 0) * 3;
            if (size + bytes > room)
                break;
            size += bytes;
            fit++;
        }

        w.VarUInt((uint)fit);
        for (var i = 0; i < fit; i++)
        {
            var e = _events[i];
            w.VarUInt(e.Id);
            w.Byte(e.Kind);
            if (e.Kind is 3 or 7)
                w.String(e.Action);
            else
                w.VarInt(e.Code);
            sending.Add(e.Id);
        }

        var last = fit > 0 ? _events[fit - 1].Id : 0;
        return (w.ToArray(), () => _events.RemoveAll(e => e.Id <= last));
    }
}
