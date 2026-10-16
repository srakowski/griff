using System.Diagnostics;
using System.Numerics;
using Griff.Debugging;
using Griff.Rendering;
using Griff.Runtime;

namespace Griff.Hosting;

/// <summary>
/// What a game is drawn on: the host's window, canvas or off-screen picture. The loop builds each frame's picture and hands
/// it here.
/// </summary>
public interface IGameView
{
    /// <summary>The view's width over its height, which the game's camera fits.</summary>
    float Aspect { get; }

    /// <summary>Draws a frame on the view.</summary>
    void Render(RenderFrame frame);

    /// <summary>Draws <paramref name="frame"/> off the view and reads it back (top-down RGBA rows), or null if it can't.</summary>
    (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame);
}

/// <summary>
/// Sound a host plays a frame at a time (the browser's Web Audio), kept a little ahead once each frame is drawn. A sound
/// device that pulls the game's mixer itself (the desktop's) needs none.
/// </summary>
public interface IFrameSound
{
    /// <summary>Hands the output what the mixer plays next, once a frame.</summary>
    void Pump();
}

/// <summary>
/// How long a <see cref="GameLoop"/>'s frames took since they were last read (<see cref="GameLoop.ReadTimes"/>): its
/// steps, how many and how long (a frame's steps timed together, and the slowest frame's, a step), and its frames, how
/// many and how long building, drawing and pumping the sound took in all.
/// </summary>
public readonly record struct GameLoopTimes(long Steps, double StepMilliseconds, double WorstStepMilliseconds,
    long Frames, double BuildMilliseconds, double DrawMilliseconds, double SoundMilliseconds);

/// <summary>
/// Plays a <see cref="Game"/> a frame of the screen at a time, the same for every host: the steps the frame's time holds
/// (<see cref="Game.Advance"/>), its picture built between the last two and drawn on the host's <see cref="IGameView"/>,
/// its sound pumped (<see cref="IFrameSound"/>), the save's picture taken when a game asks for one, and an error that ends a
/// frame caught, the game paused on it (or held where it was thrown by the editor's debugger, <see cref="ScriptProbes"/>).
/// It has no UI of its own: the host calls <see cref="Frame"/> as its screen shows one (the desktop's compositor, the
/// browser's <c>requestAnimationFrame</c>, a dedicated server's timer, <see cref="Run"/>) and feeds <see cref="Input"/>
/// from its keyboard, mouse, touch and controllers.
/// </summary>
public sealed class GameLoop
{
    private readonly IFrameSound? _sound;
    private RenderFrame? _lastFrame;
    private Vector2 _pointer;
    private bool _isPaused, _drawFaulted, _shownAny;
    // When a script last let go of a breakpoint it stood at in this game's frame (a Stopwatch timestamp), and when the last
    // frame began.
    private long _heldUntil, _frameStarted;
    private GameLoopTimes _times;

    [ThreadStatic]
    private static GameLoop? t_running;

    /// <param name="game">The game it plays, made by its host (<see cref="LoadedGame.NewGame"/>), reading an <see cref="InputState"/>.</param>
    /// <param name="view">What it's drawn on; none for a game nobody watches (a dedicated server), whose picture isn't built.</param>
    /// <param name="sound">Sound the host plays a frame at a time, if it does.</param>
    public GameLoop(Game game, IGameView? view = null, IFrameSound? sound = null)
    {
        Game = game;
        Input = game.Input as InputState ?? throw new ArgumentException("The game reads input a host can't feed: make it with an InputState.", nameof(game));
        View = view;
        _sound = sound;
    }

    /// <summary>
    /// Raised after every frame in which the game took a step (and after <see cref="Step"/>), e.g. so the editor can
    /// refresh its panels. <see cref="Game.Stepped"/> is raised for each step.
    /// </summary>
    public event EventHandler? Ticked;

    /// <summary>
    /// Raised once a frame the game played is over, its steps taken and its picture built (after each of <see cref="Step"/>
    /// too): the debugger ends a step there that didn't come to a line of script.
    /// </summary>
    public event EventHandler? FrameEnded;

    /// <summary>
    /// Raised when gameplay code throws, ending the frame. The game pauses so the error can be fixed (unless the editor's
    /// debugger held it where it was thrown and let it play on: see <see cref="ScriptProbes.Fault"/>).
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>The game it plays.</summary>
    public Game Game { get; }

    /// <summary>The game's input, which the host feeds.</summary>
    public InputState Input { get; }

    /// <summary>What it's drawn on, if anything.</summary>
    public IGameView? View { get; }

    /// <summary>The picture last drawn (its camera puts the mouse in the world), if one was.</summary>
    public RenderFrame? LastFrame => _lastFrame;

    /// <summary>
    /// The loop whose game is taking its steps or building its picture on this thread right now, if any: what a breakpoint
    /// reached in it stops (see <see cref="ScriptProbes"/>), rather than code the editor runs between frames.
    /// </summary>
    public static GameLoop? Running => t_running;

    /// <summary>True from <see cref="Start"/> until <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Holds the game still (the editor's pause, or an error), drawn as it is rather than between steps.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            if (value)
                Game.ShowAsIs();
        }
    }

    /// <summary>
    /// Where an agent playtesting the game points the mouse, in world units: while it's set, the game sees the mouse there
    /// (and over the view) rather than wherever the real one is. The real mouse moving (<see cref="PointAt"/>) clears it.
    /// </summary>
    public Vector2? PointerOverride { get; set; }

    /// <summary>Asked before each frame's steps: the host reads what it polls (controllers, a held mouse).</summary>
    public Action? BeforeSteps { get; set; }

    public void Start()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _frameStarted = Stopwatch.GetTimestamp();
    }

    /// <summary>Stops the game (<see cref="Game.Stop"/>): no more frames, and its controllers stop shaking.</summary>
    public void Stop()
    {
        IsRunning = false;
        Input.StopRumble();
        Game.Stop();
    }

    /// <summary>
    /// The mouse (or the first finger) is over the view at <paramref name="ndc"/> (-1..1 across it, up positive): the game
    /// sees it there, in the world through the last frame's camera, from its next step.
    /// </summary>
    public void PointAt(Vector2 ndc)
    {
        if (ndc != _pointer)
            Input.MouseMoved();
        _pointer = ndc;
        Input.IsMouseInView = true;
    }

    /// <summary>Puts the fingers on the view in the world at once, through the last frame's camera (and each frame after, as it moves).</summary>
    public void PlaceTouches()
    {
        if (_lastFrame is not null && Matrix4x4.Invert(_lastFrame.ViewProjection, out var inverse))
            PlaceTouches(inverse);
    }

    /// <summary>
    /// A frame of the screen, <paramref name="seconds"/> after the last: the game takes the steps that time holds (none while
    /// paused), its picture is built and drawn, and its sound pumped. Time a script stood still at a breakpoint isn't time the
    /// game owes: it picks up where it stopped.
    /// </summary>
    public void Frame(double seconds)
    {
        var started = Stopwatch.GetTimestamp();
        if (_heldUntil > _frameStarted)
            seconds = Math.Min(seconds, Stopwatch.GetElapsedTime(_heldUntil, started).TotalSeconds);
        _frameStarted = started;
        var first = !_shownAny;
        _shownAny = true;
        var plays = !IsPaused;

        // Timed only while something profiles the game (see Game.Profiler), and only frames it plays: not the first, whose
        // time is the game starting up.
        if (plays && !first && Game.Profiler is { } profiler)
            ProfiledFrame(profiler, seconds);
        else
        {
            if (plays)
                TakeSteps(stepOnce: false, seconds);
            Draw();
        }

        PumpSound();
        if (plays && IsRunning)
            FrameEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes exactly one step of the game and draws it (for stepping through it while paused).</summary>
    public void Step()
    {
        TakeSteps(stepOnce: true, 0);
        Draw();
        if (IsRunning)
            FrameEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds the game's picture as it is and draws it, taking no steps (a game held still).</summary>
    public void Draw() => Draw(out _, out _);

    /// <summary>
    /// Takes <paramref name="count"/> steps, a frame of one step each, drawing none of them: a moment of the game reached
    /// exactly, as the web player's still moment is, which a check compares with the desktop's picture of the same moment.
    /// </summary>
    public void AdvanceSteps(int count)
    {
        if (View is not null)
            Game.ViewAspect = View.Aspect;
        var step = 1.0 / Game.Time.StepRate;
        for (var i = 0; i < count; i++)
            Game.Advance(step);
    }

    /// <summary>
    /// Plays the game with nobody watching (a dedicated server) on this thread until <paramref name="stop"/> is cancelled: a
    /// frame about every <paramref name="interval"/>, each taking the steps the real time since the last holds, however
    /// unevenly the waits end.
    /// </summary>
    public void Run(TimeSpan interval, CancellationToken stop)
    {
        Start();
        var last = Stopwatch.GetTimestamp();
        while (!stop.WaitHandle.WaitOne(interval) && IsRunning)
        {
            var now = Stopwatch.GetTimestamp();
            Frame(Stopwatch.GetElapsedTime(last, now).TotalSeconds);
            last = now;
        }
    }

    /// <summary>How long its frames took since this was last asked (and starts counting again).</summary>
    public GameLoopTimes ReadTimes()
    {
        var times = _times;
        _times = default;
        return times;
    }

    private void ProfiledFrame(FrameProfiler profiler, double seconds)
    {
        // The garbage counted is this thread's and the workers', the game's: the host's own work between frames isn't counted.
        profiler.BeginFrame();
        var steps = Game.Time.Frame;
        var (allocated, collections) = (Workers.AllocatedBytes(), GC.CollectionCount(0));
        var started = Stopwatch.GetTimestamp();
        var breaks = ScriptProbes.Breaks;
        TakeSteps(stepOnce: false, seconds);
        var stepped = Stopwatch.GetTimestamp();
        // A frame held at a breakpoint isn't timed: it took as long as someone looked at it.
        if (ScriptProbes.Breaks != breaks || !IsRunning)
        {
            Draw();
            return;
        }

        // Building the picture times its own parts (what each kind of thing drawn costs: see FrameCostKind.Build).
        Draw(out var built, out var builtAllocated);
        var drawn = Stopwatch.GetTimestamp();
        if (ScriptProbes.Breaks != breaks)
            return;
        profiler.AddCost("Draw", FrameCostKind.Picture, drawn - built, Workers.AllocatedBytes() - builtAllocated);
        profiler.Add(new FrameSample((float)(seconds * 1000), (float)Milliseconds(started, stepped), (float)Milliseconds(stepped, built),
            (float)Milliseconds(built, drawn), (int)(Game.Time.Frame - steps), _lastFrame?.Sprites.Count ?? 0,
            Workers.AllocatedBytes() - allocated, GC.CollectionCount(0) - collections));
    }

    // The game takes the steps `seconds` holds, or with `stepOnce`, exactly one (ending the input's frame after it).
    private void TakeSteps(bool stepOnce, double seconds)
    {
        var steps = Game.Time.Frame;
        var breaks = ScriptProbes.Breaks;
        var outer = t_running;
        t_running = this;
        var started = Stopwatch.GetTimestamp();
        try
        {
            PlacePointer();
            BeforeSteps?.Invoke();
            if (View is not null)
                Game.ViewAspect = View.Aspect;
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
                Input.EndFrame();
            if (ScriptProbes.Breaks != breaks)
                _heldUntil = Stopwatch.GetTimestamp();
        }

        var taken = Game.Time.Frame - steps;
        if (taken > 0)
        {
            var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _times = _times with
            {
                Steps = _times.Steps + taken,
                StepMilliseconds = _times.StepMilliseconds + milliseconds,
                WorstStepMilliseconds = Math.Max(_times.WorstStepMilliseconds, milliseconds / taken),
            };
            if (IsRunning)
                Ticked?.Invoke(this, EventArgs.Empty);
        }
    }

    // The game's frame is ending with an exception nothing in it caught: asked before anything unwinds (from the catch's
    // filter), so the editor's debugger can hold the game at the line of script that threw, as at a breakpoint.
    private static ScriptFaultHold Hold(Exception exception)
    {
        try
        {
            return ScriptProbes.Fault(exception);
        }
#pragma warning disable CA1031 // Something went wrong holding it: the game pauses on the error as it would anyway.
        catch (Exception)
#pragma warning restore CA1031
        {
            return ScriptFaultHold.NotHeld;
        }
    }

    // A script's Draw (a drawable component or a system) can throw like its Update can: the game stops the same way, once,
    // and the last good frame stays up. `built` is when the frame was built, before it's drawn (a Stopwatch timestamp), and
    // `allocated` the bytes this thread had allocated by then.
    private void Draw(out long built, out long allocated)
    {
        // Nobody watches: there's no picture to build, and a save's is none.
        if (View is not { } view)
        {
            if (Game.PictureWanted)
                Game.TakePicture([], 0, 0);
            (built, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
            return;
        }

        // Stopped while a script stood at a breakpoint in this frame: the game's gone, and so is its picture.
        if (!IsRunning)
        {
            (built, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
            return;
        }

        RenderFrame frame;
        var outer = t_running;
        t_running = this;
        var started = Stopwatch.GetTimestamp();
        try
        {
            frame = SceneRenderer.Build(Game, view.Aspect, recycle: _lastFrame);
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
            TakePicture(view);
        var pictured = Stopwatch.GetTimestamp();
        view.Render(frame);
        _times = _times with
        {
            Frames = _times.Frames + 1,
            BuildMilliseconds = _times.BuildMilliseconds + Milliseconds(started, built),
            DrawMilliseconds = _times.DrawMilliseconds + Milliseconds(pictured, Stopwatch.GetTimestamp()),
        };
    }

    // A game just saved wants its screenshot: drawn once more without the panels over it, and read back.
    private void TakePicture(IGameView view)
    {
        var outer = t_running;
        t_running = this;
        try
        {
            if (view.Capture(SceneRenderer.BuildPicture(Game, view.Aspect)) is var (pixels, width, height))
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

    private void PumpSound()
    {
        if (_sound is null)
            return;
        var started = Stopwatch.GetTimestamp();
        _sound.Pump();
        _times = _times with { SoundMilliseconds = _times.SoundMilliseconds + Milliseconds(started, Stopwatch.GetTimestamp()) };
    }

    // The mouse and the fingers in the world, through the last frame's camera (or where an agent points).
    private void PlacePointer()
    {
        var inverse = Matrix4x4.Identity;
        var known = _lastFrame is not null && Matrix4x4.Invert(_lastFrame.ViewProjection, out inverse);
        if (known)
            PlaceTouches(inverse);
        if (PointerOverride is { } pointed)
        {
            (Input.MouseWorld, Input.IsMouseInView) = (pointed, true);
            return;
        }

        if (known)
        {
            var world = Vector4.Transform(new Vector4(_pointer, 0, 1), inverse);
            Input.MouseWorld = new Vector2(world.X, world.Y) / world.W;
        }
    }

    private void PlaceTouches(Matrix4x4 inverse)
    {
        if (Input.Touches.Count == 0)
            return;
        Input.PlaceTouches(ndc =>
        {
            var at = Vector4.Transform(new Vector4(ndc, 0, 1), inverse);
            return new Vector2(at.X, at.Y) / at.W;
        });
    }

    private static double Milliseconds(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
}
