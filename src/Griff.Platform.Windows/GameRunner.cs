using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Hosting;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>
/// Plays a <see cref="Game"/> its host made (<see cref="LoadedGame.NewGame"/>) in a WPF window: the core's
/// <see cref="GameLoop"/> takes a frame each time the screen shows one (see <see cref="FramePacer"/>) and draws it on the
/// <see cref="GameSurface"/>; this hands it keyboard input from <c>keySource</c>, the mouse and touch from the surface and
/// the controllers, holds the mouse for a first-person game and shows the pointer the game asks for. The game's sound is
/// its host's (a <see cref="SoundOutput"/>'s mixer, see <see cref="WindowsAudio.Create"/>).
/// </summary>
public sealed class GameRunner : IDisposable
{
    private readonly GameSurface _surface;
    private readonly UIElement _keySource;
    private readonly InputState _input;
    // When the compositor let the last frame go (a Stopwatch timestamp): frames are timed from one to the next.
    private long _lastShown;
    private Point? _mousePixel;
    private FramePacer? _pacer;
    // While the game has the mouse (see IInput.WantsMouseLock): the screen point the cursor is held at.
    private (int X, int Y)? _lockCenter;

    /// <param name="game">The game, made by its host with an <see cref="InputState"/> this feeds.</param>
    /// <param name="surface">What it's drawn on, whose mouse and touch it reads.</param>
    /// <param name="keySource">Where its keys come from (its window, or the editor's).</param>
    public GameRunner(Game game, GameSurface surface, UIElement keySource)
    {
        _surface = surface;
        _keySource = keySource;
        Loop = new GameLoop(game, surface) { BeforeSteps = Poll };
        _input = Loop.Input;

        _keySource.PreviewKeyDown += OnKeyDown;
        _keySource.PreviewKeyUp += OnKeyUp;
        _keySource.LostKeyboardFocus += OnLostFocus;
        _surface.MouseMoved += OnMouseMoved;
        _surface.MouseLeft += OnMouseLeft;
        _surface.MouseButtonChanged += OnMouseButton;
        _surface.MouseWheelTurned += OnMouseWheel;
        _surface.PreviewMouseWheel += OnWpfMouseWheel;
        _surface.TouchChanged += OnTouch;

        // Until the mouse moves over the game, it isn't pointing at anything (say, a button in the middle).
        _input.IsMouseInView = false;
    }

    /// <summary>The loop playing the game: its frames, pausing and faults, and the events they raise.</summary>
    public GameLoop Loop { get; }

    public Game Game => Loop.Game;

    public bool IsRunning => Loop.IsRunning;

    /// <summary>Holds the game still (the editor's pause), drawn as it is rather than between steps; it gives the mouse back.</summary>
    public bool IsPaused
    {
        get => Loop.IsPaused;
        set
        {
            Loop.IsPaused = value;
            // A paused game gives the mouse back, so the editor can be used (also when it's held at a breakpoint, mid-frame).
            if (value)
                ReleaseMouse();
        }
    }

    /// <summary>
    /// Whether a key pressed now is the game's, asked as each is pressed (null: always, unless typing into a text box or
    /// paused): the editor says no while typing code, so F9 and typing work there while the game runs.
    /// </summary>
    public Func<bool>? TakesKeys { get; set; }

    /// <summary>
    /// Where an agent playtesting the game points the mouse, in world units: while it's set, the game sees the mouse there
    /// (and over the view) rather than wherever the real one is. Moving the real mouse over the game clears it.
    /// </summary>
    public Vector2? PointerOverride
    {
        get => Loop.PointerOverride;
        set => Loop.PointerOverride = value;
    }

    public void Start()
    {
        if (IsRunning)
            return;
        Loop.Start();
        _lastShown = Stopwatch.GetTimestamp();
        _pacer = new FramePacer(_surface.Dispatcher, OnFrame);
    }

    /// <summary>Takes exactly one step of the game (for stepping through it while paused).</summary>
    public void Step()
    {
        Loop.Step();
        ShowPointer();
    }

    public void Dispose()
    {
        _pacer?.Dispose();
        ReleaseMouse();
        _keySource.PreviewKeyDown -= OnKeyDown;
        _keySource.PreviewKeyUp -= OnKeyUp;
        _keySource.LostKeyboardFocus -= OnLostFocus;
        _surface.MouseMoved -= OnMouseMoved;
        _surface.MouseLeft -= OnMouseLeft;
        _surface.MouseButtonChanged -= OnMouseButton;
        _surface.MouseWheelTurned -= OnMouseWheel;
        _surface.PreviewMouseWheel -= OnWpfMouseWheel;
        _surface.TouchChanged -= OnTouch;
        XInputGamepad.StopRumble();
        Loop.Stop();
    }

    private void OnFrame(long shownAt)
    {
        var seconds = (double)Math.Max(0, shownAt - _lastShown) / Stopwatch.Frequency;
        _lastShown = shownAt;
        if (IsPaused)
        {
            if (_lockCenter is not null)
            {
                // A paused game gives the mouse back, so the editor can be used.
                ReleaseMouse();
            }
            else if (_input.RumbleStrength > 0)
            {
                // A paused game doesn't shake the controller.
                _input.StopRumble();
                XInputGamepad.StopRumble();
            }
        }

        Loop.Frame(seconds);
        ShowPointer();
    }

    // Read before the game's steps: the mouse held for a first-person game, and the controllers.
    private void Poll()
    {
        UpdateMouseLock();
        XInputGamepad.Poll(_input);
    }

    // The pointer the game asks for (see IInput.Cursor) over the surface; the arrow while it's paused, for the editor.
    private void ShowPointer() => _surface.Pointer = IsPaused ? Griff.Input.MouseCursor.Arrow : _input.Cursor;

    // A finger on a touch screen, kept where it is on the view and put in the world at once (and with the mouse each frame,
    // as the camera moves).
    private void OnTouch(int id, Point pixel, bool down)
    {
        _input.SetTouch(id, _surface.ToNdc(pixel), down, onScreen: true);
        Loop.PlaceTouches();
    }

    // The mouse held for a first-person game: how far it moved since last frame, then back to the middle of the view.
    private void UpdateMouseLock()
    {
        if (_lockCenter is not { } center)
            return;
        if (!_input.WantsMouseLock || Window.GetWindow(_surface) is not { IsActive: true })
        {
            ReleaseMouse();
            return;
        }

        if (GetCursorPos(out var cursor))
            _input.AddMouseDelta(new Vector2(cursor.X - center.X, center.Y - cursor.Y));
        SetCursorPos(center.X, center.Y);
    }

    private void LockMouse()
    {
        var (width, height) = _surface.PixelSize;
        if (width == 0 || height == 0 || PresentationSource.FromVisual(_surface) is null)
            return;
        var topLeft = _surface.PointToScreen(new Point(0, 0));
        var bottomRight = _surface.PointToScreen(new Point(_surface.ActualWidth, _surface.ActualHeight));
        var clip = new NativeRect { Left = (int)topLeft.X, Top = (int)topLeft.Y, Right = (int)bottomRight.X, Bottom = (int)bottomRight.Y };
        _lockCenter = ((clip.Left + clip.Right) / 2, (clip.Top + clip.Bottom) / 2);
        ClipCursor(ref clip);
        SetCursorPos(_lockCenter.Value.X, _lockCenter.Value.Y);
        _ = ShowCursor(false);
        _input.IsMouseLocked = true;
    }

    private void ReleaseMouse()
    {
        if (_lockCenter is null)
            return;
        _lockCenter = null;
        ClipCursor(IntPtr.Zero);
        _ = ShowCursor(true);
        _input.IsMouseLocked = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Esc gives back the mouse a first-person game is holding (and still reaches the game, to pause it).
        if (e.Key == System.Windows.Input.Key.Escape)
            ReleaseMouse();

        // Typing into a text field (say, the editor's Inspector while playing) isn't playing the game, and nor is pressing
        // keys while it's paused: those are the editor's (F6 resumes, F9 sets a breakpoint, typing goes into the code).
        if (Keyboard.FocusedElement is TextBoxBase || IsPaused || TakesKeys?.Invoke() == false)
            return;

        var wpfKey = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var key = KeyMap.ToGriff(wpfKey);

        // Typed text comes from here rather than WPF's TextInput, which handling the key would stop.
        if (key == Input.Key.Backspace)
            _input.TypeBackspace();
        else if (TypedText.Of(wpfKey) is { Length: > 0 } text)
            _input.AddText(text);

        if (key == Input.Key.None)
            return;
        if (!e.IsRepeat)
            _input.SetKey(key, true);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToGriff(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
        if (key == Input.Key.None)
            return;
        // Always, so a key let go after focus moves to a text field doesn't stay held in the game.
        _input.SetKey(key, false);
        if (Keyboard.FocusedElement is not TextBoxBase)
            e.Handled = true;
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _input.ReleaseAll();
        ReleaseMouse();
    }

    private void OnMouseMoved(Point pixel)
    {
        // Held, the mouse's movement is measured each frame from the middle of the view instead.
        if (_lockCenter is null && _mousePixel is { } last)
            _input.AddMouseDelta(new Vector2((float)(pixel.X - last.X), (float)(last.Y - pixel.Y)));
        if (_mousePixel != pixel)
            PointerOverride = null;
        _mousePixel = pixel;
        Loop.PointAt(_surface.ToNdc(pixel));
    }

    private void OnMouseWheel(float notches) => _input.AddScroll(notches);

    private void OnWpfMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _input.AddScroll(e.Delta / 120f);
        e.Handled = true;
    }

    private void OnMouseLeft()
    {
        _input.IsMouseInView = false;
        _mousePixel = null;
    }

    private void OnMouseButton(Runtime.MouseButton button, bool down)
    {
        // A first-person game takes the mouse when it's clicked; that click only takes it, it doesn't fire.
        if (down && _lockCenter is null && _input.WantsMouseLock && !IsPaused)
        {
            _keySource.Focus();
            LockMouse();
            if (_lockCenter is not null)
                return;
        }

        // Clicking the game takes the keyboard back from a text field (which commits what was typed there).
        if (down && Keyboard.FocusedElement is TextBoxBase && !_keySource.Focus())
            Keyboard.ClearFocus();
        _input.SetMouseButton(button, down);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr rect);

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool show);
}
