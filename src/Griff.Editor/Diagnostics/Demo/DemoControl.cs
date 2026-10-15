#if DEBUG
namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// The demo's transport: pause, playback speed, jumping between chapters and exiting. Every wait in the demo goes
/// through <see cref="Gate"/> or <see cref="Delay(double)"/>, so a pause holds it where it is and a jump or exit stops
/// the chapter that's running (with an <see cref="OperationCanceledException"/>).
/// </summary>
internal sealed class DemoControl : IDisposable
{
    private CancellationTokenSource _cancel = new();
    private bool _paused;

    public static readonly double[] Speeds = [1, 2, 4];

    /// <summary>The control of the demo that's running, for the helpers that wait.</summary>
    public static DemoControl? Active { get; set; }

    public event EventHandler? Changed;

    public IReadOnlyList<string> Chapters { get; set; } = [];

    /// <summary>The chapter running now.</summary>
    public int Current { get; private set; }

    public DateTime CurrentStarted { get; private set; } = DateTime.Now;

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            PauseChanged?.Invoke(this, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised with the new state, so speech and the game can pause too.</summary>
    public event EventHandler<bool>? PauseChanged;

    /// <summary>1, 2 or 4: how fast the demo plays.</summary>
    public double Speed { get; private set; } = 1;

    /// <summary>While set, chapters before this one are hurried through silently.</summary>
    public int? SkipUntil { get; set; }

    public bool Skipping => SkipUntil is { } until && Current < until;

    /// <summary>How much faster than normal everything runs right now.</summary>
    public double Factor => Skipping ? 10 : Speed;

    /// <summary>A chapter to go back to (it's restored from its snapshot); set by <see cref="Back"/> and <see cref="GoTo"/>.</summary>
    public int? RewindTo { get; private set; }

    public bool ExitRequested { get; private set; }

    /// <summary>Chapters that have a snapshot to rewind to.</summary>
    public HashSet<int> Snapshots { get; } = [];

    public CancellationToken Token => _cancel.Token;

    public void Enter(int chapter)
    {
        Current = chapter;
        CurrentStarted = DateTime.Now;
        if (SkipUntil is { } until && chapter >= until)
            SkipUntil = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePause() => Paused = !Paused;

    public void Faster() => SetSpeed(Speeds.FirstOrDefault(s => s > Speed, Speed));

    public void Slower() => SetSpeed(Speeds.LastOrDefault(s => s < Speed, Speed));

    public void SetSpeed(double speed)
    {
        Speed = speed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The narration is shown as captions without being spoken. Kept between demos (see <see cref="DemoPreferences"/>);
    /// GRIFF_MUTE_NARRATION=1 (tools\demo.ps1 -Mute) mutes it for one run.
    /// </summary>
    public bool NarrationMuted
    {
        get => _narrationMuted;
        set
        {
            if (_narrationMuted == value)
                return;
            _narrationMuted = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _narrationMuted;

    public void ToggleNarration() => NarrationMuted = !NarrationMuted;

    /// <summary>Like a media player's back button: restart this chapter, or go to the previous one if it just began.</summary>
    public void Back() => GoTo(DateTime.Now - CurrentStarted > TimeSpan.FromSeconds(4) ? Current : Current - 1);

    public void Next() => GoTo(Current + 1);

    /// <summary>Goes to <paramref name="chapter"/>: back by restoring its snapshot, forward by hurrying through.</summary>
    public void GoTo(int chapter)
    {
        chapter = Math.Clamp(chapter, 0, Math.Max(0, Chapters.Count - 1));
        Paused = false;
        if (chapter > Current && !Snapshots.Contains(chapter))
        {
            SkipUntil = chapter;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        RewindTo = chapter;
        // Counted from now, so pressing Back again straight away goes back one more.
        Current = chapter;
        CurrentStarted = DateTime.Now;
        _cancel.Cancel();
    }

    public void Exit()
    {
        ExitRequested = true;
        Paused = false;
        _cancel.Cancel();
    }

    /// <summary>Called once a jump has been handled, so the next chapter can run.</summary>
    public void Rearm()
    {
        RewindTo = null;
        _cancel = new CancellationTokenSource();
    }

    public void Dispose() => _cancel.Dispose();

    /// <summary>Stops here while paused; throws if a jump or exit was asked for.</summary>
    public async Task Gate()
    {
        Token.ThrowIfCancellationRequested();
        while (Paused)
        {
            await Task.Delay(50);
            Token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Waits <paramref name="ms"/> of demo time (shorter when sped up), holding while paused.</summary>
    public async Task Delay(double ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var elapsed = 0.0;
        while (elapsed < ms)
        {
            await Gate();
            var before = clock.Elapsed.TotalMilliseconds;
            await Task.Delay((int)Math.Clamp((ms - elapsed) / Factor, 1, 40));
            elapsed += (clock.Elapsed.TotalMilliseconds - before) * Factor;
        }
    }
}
#endif
