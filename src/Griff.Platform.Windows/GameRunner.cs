using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Debugging;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Platform.Windows;

/// <summary>
/// Runs a <see cref="Game"/> on the WPF UI thread: advances it once per frame the screen shows (see
/// <see cref="FramePacer"/>), which takes the game's fixed steps that frame's time holds, renders it to a
/// <see cref="GameSurface"/> between its last two steps, and feeds keyboard input from <paramref name="keySource"/> and
/// mouse input from the surface.
/// </summary>
public sealed class GameRunner : IDisposable
{
    private readonly GameSurface _surface;
    private readonly UIElement _keySource;
    private readonly InputState _input = new();
    // When the compositor let the last frame go (a Stopwatch timestamp): frames are timed from one to the next.
    private long _lastShown;
    private bool _shownAny;
    private RenderFrame? _lastFrame;
    private Vector2 _mouseNdc;
    private Point? _mousePixel;
    private FramePacer? _pacer;
    // While the game has the mouse (see IInput.WantsMouseLock): the screen point the cursor is held at.
    private (int X, int Y)? _lockCenter;

    public GameRunner(Scene scene, GameSurface surface, UIElement keySource)
    {
        _surface = surface;
        _keySource = keySource;
        Game = new Game(scene, _input);
        if (surface.AssetRoot is { } assets)
            Game.Audio = WindowsAudio.Create(assets);

        _keySource.PreviewKeyDown += OnKeyDown;
        _keySource.PreviewKeyUp += OnKeyUp;
        _keySource.LostKeyboardFocus += OnLostFocus;
        _surface.MouseMoved += OnMouseMoved;
        _surface.MouseLeft += OnMouseLeft;
        _surface.MouseButtonChanged += OnMouseButton;
        _surface.MouseWheelTurned += OnMouseWheel;
        _surface.PreviewMouseWheel += OnWpfMouseWheel;

        // Until the mouse moves over the game, it isn't pointing at anything (say, a button in the middle).
        _input.IsMouseInView = false;
    }

    /// <summary>
    /// Raised after every frame in which the game took a step (and after <see cref="Step"/>), e.g. so the editor can
    /// refresh its panels. <see cref="Game.Stepped"/> is raised for each step.
    /// </summary>
    public event EventHandler? Ticked;

    /// <summary>
    /// Raised once a frame the game played is over, its steps taken and its picture built (after each of
    /// <see cref="Step"/> too): the debugger ends a step there that didn't come to a line of script.
    /// </summary>
    public event EventHandler? FrameEnded;

    /// <summary>
    /// Raised when gameplay code throws, ending the frame. The game pauses so the error can be fixed (unless the editor's
    /// debugger held it where it was thrown and let it play on: see <see cref="ScriptProbes.Fault"/>).
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    public Game Game { get; }

    /// <summary>
    /// The runner whose game is taking its steps or building its picture on this thread right now, if any: what a
    /// breakpoint reached on it stops (see <see cref="ScriptProbes"/>), rather than code the editor runs between frames.
    /// </summary>
    public static GameRunner? Running => t_running;

    [ThreadStatic]
    private static GameRunner? t_running;

    public bool IsRunning { get; private set; }

    /// <summary>Holds the game still (the editor's pause), drawn as it is rather than between steps.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            if (!value)
                return;
            Game.ShowAsIs();
            // A paused game gives the mouse back, so the editor can be used (also when it's held at a breakpoint, mid-frame).
            ReleaseMouse();
        }
    }

    private bool _isPaused;

    private bool _drawFaulted;

    /// <summary>
    /// Whether a key pressed now is the game's, asked as each is pressed (null: always, unless typing into a text box or
    /// paused): the editor says no while typing code, so F9 and typing work there while the game runs.
    /// </summary>
    public Func<bool>? TakesKeys { get; set; }

    /// <summary>
    /// Where an agent playtesting the game points the mouse, in world units: while it's set, the game sees the mouse there
    /// (and over the view) rather than wherever the real one is. Moving the real mouse over the game clears it.
    /// </summary>
    public Vector2? PointerOverride { get; set; }

    public void Start()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _lastShown = Stopwatch.GetTimestamp();
        _pacer = new FramePacer(_surface.Dispatcher, OnFrame);
    }

    /// <summary>Takes exactly one step of the game (for stepping through it while paused).</summary>
    public void Step()
    {
        RunSafely(stepOnce: true, 0);
        RenderLatest();
        if (IsRunning)
            FrameEnded?.Invoke(this, EventArgs.Empty);
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
        IsRunning = false;
        _input.StopRumble();
        XInputGamepad.StopRumble();
        Game.Stop();
    }

    private void OnFrame(long shownAt)
    {
        var plays = !IsPaused;
        Frame(shownAt);
        if (plays && IsRunning)
            FrameEnded?.Invoke(this, EventArgs.Empty);
    }

    private void Frame(long shownAt)
    {
        // Time a script stood still at a breakpoint isn't time the game owes: it picks up where it stopped.
        if (_heldUntil > _lastShown)
            _lastShown = Math.Min(_heldUntil, shownAt);
        var seconds = (double)Math.Max(0, shownAt - _lastShown) / Stopwatch.Frequency;
        _lastShown = shownAt;
        var first = !_shownAny;
        _shownAny = true;

        // Timed only while something profiles the game (see Game.Profiler), and only frames it plays: not the first, whose
        // time is the game starting up.
        if (!IsPaused && !first && Game.Profiler is { } profiler)
        {
            // The garbage counted is this thread's and the workers', the game's: the editor's own work between frames isn't counted.
            profiler.BeginFrame();
            var steps = Game.Time.Frame;
            var (allocated, collections) = (Workers.AllocatedBytes(), GC.CollectionCount(0));
            var started = Stopwatch.GetTimestamp();
            var breaks = ScriptProbes.Breaks;
            RunSafely(stepOnce: false, seconds);
            var stepped = Stopwatch.GetTimestamp();
            // A frame held at a breakpoint isn't timed: it took as long as someone looked at it.
            if (ScriptProbes.Breaks != breaks || !IsRunning)
            {
                RenderLatest();
                return;
            }

            // Building the picture times its own parts (what each kind of thing drawn costs: see FrameCostKind.Build).
            RenderLatest(out var built, out var builtAllocated);
            var drawn = Stopwatch.GetTimestamp();
            if (ScriptProbes.Breaks != breaks)
                return;
            profiler.AddCost("Draw", FrameCostKind.Picture, drawn - built, Workers.AllocatedBytes() - builtAllocated);
            profiler.Add(new FrameSample((float)(seconds * 1000), Milliseconds(started, stepped), Milliseconds(stepped, built),
                Milliseconds(built, drawn), (int)(Game.Time.Frame - steps), _lastFrame?.Sprites.Count ?? 0,
                Workers.AllocatedBytes() - allocated, GC.CollectionCount(0) - collections));
            return;
        }

        if (!IsPaused)
        {
            RunSafely(stepOnce: false, seconds);
        }
        else if (_lockCenter is not null)
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

        ShowPointer();
        RenderLatest();
    }

    // A frame of the screen: the game takes the steps `seconds` holds (ending the input's frame after each), or with
    // `stepOnce`, exactly one.
    private void RunSafely(bool stepOnce, double seconds)
    {
        var steps = Game.Time.Frame;
        var breaks = ScriptProbes.Breaks;
        var outer = t_running;
        t_running = this;
        try
        {
            UpdateMouseWorld();
            UpdateMouseLock();
            XInputGamepad.Poll(_input);
            Game.ViewAspect = _surface.Aspect;
            if (stepOnce)
                Game.Tick(Game.Time.FixedDelta);
            else
                Game.Advance(seconds);
        }
        catch (ScriptAbortedException)
        {
            // Stopped (or rewound) at a breakpoint: this game is gone, and nothing went wrong in it.
        }
        catch (Exception ex) when (Hold(ex) is var held)
        {
            // Held where it was thrown and stopped or rewound meanwhile, the game's gone; let go, it stays paused or plays on as
            // it was left, and the frame ends here.
            if (held != ScriptFaultHold.Abandoned)
            {
                if (held == ScriptFaultHold.NotHeld)
                    IsPaused = true;
                Faulted?.Invoke(this, ex);
            }
        }
        finally
        {
            t_running = outer;
            if (stepOnce)
                _input.EndFrame();
            if (ScriptProbes.Breaks != breaks)
                _heldUntil = Stopwatch.GetTimestamp();
        }

        ShowPointer();
        if (IsRunning && Game.Time.Frame != steps)
            Ticked?.Invoke(this, EventArgs.Empty);
    }

    // The pointer the game asks for (see IInput.Cursor) over the surface; the arrow while it's paused, for the editor.
    private void ShowPointer() => _surface.Pointer = IsPaused ? Griff.Input.MouseCursor.Arrow : _input.Cursor;

    // When a script last let go of a breakpoint it stood at in this game's frame (a Stopwatch timestamp).
    private long _heldUntil;

    // The game's frame is ending with an exception nothing in it caught: asked before anything unwinds (from the catch's
    // filter), so the editor's debugger can hold the game at the line of script that threw, as at a breakpoint.
    private static ScriptFaultHold Hold(Exception exception)
    {
        try
        {
            return ScriptProbes.Fault(exception);
        }
        catch (Exception)
        {
            // Something went wrong holding it: the game pauses on the error as it would anyway.
            return ScriptFaultHold.NotHeld;
        }
    }

    // A script's Draw (a drawable component or a system) can throw like its Update can: the game stops the same way, once,
    // and the last good frame stays up.
    private void RenderLatest() => RenderLatest(out _, out _);

    // `built` is when the frame was built, before it's drawn (a Stopwatch timestamp), and `allocated` the bytes this thread
    // had allocated by then.
    private void RenderLatest(out long built, out long allocated)
    {
        RenderFrame frame;
        if (!IsRunning)
        {
            // Stopped while a script stood at a breakpoint in this frame: the game's gone, and so is its picture.
            (built, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
            return;
        }

        var outer = t_running;
        t_running = this;
        try
        {
            frame = SceneRenderer.Build(Game, _surface.Aspect, recycle: _lastFrame);
        }
        catch (ScriptAbortedException)
        {
            (built, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
            return;
        }
        // Held only the first time in a row it fails (a paused game is drawn again every frame).
        catch (Exception ex) when ((_drawFaulted ? ScriptFaultHold.NotHeld : Hold(ex)) is var held)
        {
            (built, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
            if (!_drawFaulted)
            {
                _drawFaulted = true;
                if (held != ScriptFaultHold.Abandoned)
                {
                    if (held == ScriptFaultHold.NotHeld)
                        IsPaused = true;
                    Faulted?.Invoke(this, ex);
                }
            }

            return;
        }
        finally
        {
            t_running = outer;
        }

        (built, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
        _drawFaulted = false;
        _lastFrame = frame;
        if (Game.PictureWanted)
            TakePicture();
        _surface.Render(_lastFrame);
    }

    // A game just saved wants its screenshot: drawn once more without the panels over it, and read back.
    private void TakePicture()
    {
        var outer = t_running;
        t_running = this;
        try
        {
            if (_surface.Capture(SceneRenderer.BuildPicture(Game, _surface.Aspect)) is var (pixels, width, height))
                Game.TakePicture(pixels, width, height);
            else
                Game.TakePicture([], 0, 0);
        }
#pragma warning disable CA1031 // A script that throws while drawing the picture leaves the saved game without one, rather than stopping it.
        catch (Exception)
#pragma warning restore CA1031
        {
            Game.TakePicture([], 0, 0);
        }
        finally
        {
            t_running = outer;
        }
    }

    private static float Milliseconds(long from, long to) => (float)((to - from) * 1000.0 / Stopwatch.Frequency);

    private void UpdateMouseWorld()
    {
        if (PointerOverride is { } pointed)
        {
            (_input.MouseWorld, _input.IsMouseInView) = (pointed, true);
            return;
        }

        if (_lastFrame is not null && Matrix4x4.Invert(_lastFrame.ViewProjection, out var inverse))
        {
            var world = Vector4.Transform(new Vector4(_mouseNdc, 0, 1), inverse);
            _input.MouseWorld = new Vector2(world.X, world.Y) / world.W;
        }
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
        var ndc = _surface.ToNdc(pixel);
        if (ndc != _mouseNdc)
            _input.MouseMoved();
        _mouseNdc = ndc;
        _input.IsMouseInView = true;
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
