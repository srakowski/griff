using System.Diagnostics;
using System.Numerics;
using System.Windows;
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
    private TimeSpan _lastRenderTime;

    public GameRunner(Scene scene, GameSurface surface, UIElement keySource)
    {
        _surface = surface;
        _keySource = keySource;
        Game = new Game(scene, _input);
        if (surface.AssetRoot is { } assets)
            Game.Audio = new WpfAudio(assets);

        _keySource.PreviewKeyDown += OnKeyDown;
        _keySource.PreviewKeyUp += OnKeyUp;
        _keySource.LostKeyboardFocus += OnLostFocus;
        _surface.MouseMoved += OnMouseMoved;
        _surface.MouseLeft += OnMouseLeft;
        _surface.MouseButtonChanged += OnMouseButton;

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
        _keySource.PreviewKeyDown -= OnKeyDown;
        _keySource.PreviewKeyUp -= OnKeyUp;
        _keySource.LostKeyboardFocus -= OnLostFocus;
        _surface.MouseMoved -= OnMouseMoved;
        _surface.MouseLeft -= OnMouseLeft;
        _surface.MouseButtonChanged -= OnMouseButton;
        IsRunning = false;
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
            TickSafely(seconds);
        RenderLatest();
    }

    private void TickSafely(double seconds)
    {
        try
        {
            UpdateMouseWorld();
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

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToGriff(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
        if (key == Runtime.Key.None)
            return;
        _input.SetKey(key, true);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToGriff(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
        if (key == Runtime.Key.None)
            return;
        _input.SetKey(key, false);
        e.Handled = true;
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _input.ReleaseAll();

    private void OnMouseMoved(Point pixel)
    {
        _mouseNdc = _surface.ToNdc(pixel);
        _input.IsMouseInView = true;
    }

    private void OnMouseLeft() => _input.IsMouseInView = false;

    private void OnMouseButton(Runtime.MouseButton button, bool down) => _input.SetMouseButton(button, down);
}
