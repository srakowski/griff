using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Platform.Windows;

/// <summary>
/// Runs a <see cref="Game"/> on the WPF UI thread: ticks it once per display frame, renders to a
/// <see cref="GameSurface"/>, and feeds keyboard input from <paramref name="keySource"/> and mouse input from the surface.
/// </summary>
public sealed class GameRunner : IDisposable
{
    private readonly GameSurface _surface;
    private readonly UIElement _keySource;
    private readonly InputState _input = new();
    private readonly Stopwatch _clock = new();
    private RenderFrame? _lastFrame;
    private Vector2 _mouseNdc;
    private Point? _mousePixel;
    private TimeSpan _lastRenderTime;
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

    /// <summary>Raised after every tick, e.g. so the editor can refresh its panels.</summary>
    public event EventHandler? Ticked;

    /// <summary>Raised when gameplay code throws. The game pauses so the error can be fixed.</summary>
    public event EventHandler<Exception>? Faulted;

    public Game Game { get; }

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; set; }

    public void Start()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Advances exactly one frame (for frame-stepping while paused).</summary>
    public void Step(double seconds = 1.0 / 60)
    {
        TickSafely(seconds);
        RenderLatest();
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
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

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; only act when WPF actually advanced.
        var renderTime = ((RenderingEventArgs)e).RenderingTime;
        if (renderTime == _lastRenderTime)
            return;
        _lastRenderTime = renderTime;

        var seconds = _clock.Elapsed.TotalSeconds;
        _clock.Restart();

        if (!IsPaused)
        {
            TickSafely(seconds);
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

        RenderLatest();
    }

    private void TickSafely(double seconds)
    {
        try
        {
            UpdateMouseWorld();
            UpdateMouseLock();
            XInputGamepad.Poll(_input);
            Game.ViewAspect = _surface.Aspect;
            Game.Tick(seconds);
        }
        catch (Exception ex)
        {
            IsPaused = true;
            Faulted?.Invoke(this, ex);
        }
        finally
        {
            _input.EndFrame();
        }

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private void RenderLatest()
    {
        _lastFrame = SceneRenderer.Build(Game, _surface.Aspect);
        _surface.Render(_lastFrame);
    }

    private void UpdateMouseWorld()
    {
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
        ShowCursor(false);
        _input.IsMouseLocked = true;
    }

    private void ReleaseMouse()
    {
        if (_lockCenter is null)
            return;
        _lockCenter = null;
        ClipCursor(IntPtr.Zero);
        ShowCursor(true);
        _input.IsMouseLocked = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Esc gives back the mouse a first-person game is holding (and still reaches the game, to pause it).
        if (e.Key == System.Windows.Input.Key.Escape)
            ReleaseMouse();

        // Typing into a text field (say, the editor's Inspector while playing) isn't playing the game.
        if (Keyboard.FocusedElement is TextBoxBase)
            return;

        var wpfKey = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var key = KeyMap.ToGriff(wpfKey);

        // Typed text comes from here rather than WPF's TextInput, which handling the key would stop.
        if (key == Runtime.Key.Backspace)
            _input.TypeBackspace();
        else if (TypedText.Of(wpfKey) is { Length: > 0 } text)
            _input.AddText(text);

        if (key == Runtime.Key.None)
            return;
        if (!e.IsRepeat)
            _input.SetKey(key, true);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToGriff(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
        if (key == Runtime.Key.None)
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
