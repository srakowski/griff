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

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>What gameplay code reads. "Pressed" and "released" are true for exactly one frame.</summary>
public interface IInput
{
    bool IsDown(Key key);

    bool WasPressed(Key key);

    bool WasReleased(Key key);

    bool IsDown(MouseButton button);

    bool WasPressed(MouseButton button);

    /// <summary>Mouse position in world units.</summary>
    Vector2 MouseWorld { get; }

    /// <summary>-1, 0 or 1 from A/D or the left/right arrows.</summary>
    float Horizontal { get; }

    /// <summary>-1, 0 or 1 from S/W or the down/up arrows (up is positive).</summary>
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

    public Vector2 MouseWorld { get; set; }

    public float Horizontal => Axis(Key.A, Key.Left, Key.D, Key.Right);

    public float Vertical => Axis(Key.S, Key.Down, Key.W, Key.Up);

    public bool IsDown(Key key) => _down.Contains(key);

    public bool WasPressed(Key key) => _pressed.Contains(key);

    public bool WasReleased(Key key) => _released.Contains(key);

    public bool IsDown(MouseButton button) => _mouseDown.Contains(button);

    public bool WasPressed(MouseButton button) => _mousePressed.Contains(button);

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
        else if (!down)
            _mouseDown.Remove(button);
    }

    /// <summary>Releases everything, e.g. when the game window loses focus.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _down.ToList())
            SetKey(key, false);
        _mouseDown.Clear();
    }

    public void EndFrame()
    {
        _pressed.Clear();
        _released.Clear();
        _mousePressed.Clear();
    }

    private float Axis(Key negative, Key negativeAlt, Key positive, Key positiveAlt) =>
        (IsDown(positive) || IsDown(positiveAlt) ? 1f : 0f) - (IsDown(negative) || IsDown(negativeAlt) ? 1f : 0f);
}
