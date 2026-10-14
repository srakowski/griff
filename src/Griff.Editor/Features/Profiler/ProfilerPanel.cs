using System.Windows.Threading;
using Griff.Editor.Core.Panels;
using Griff.Editor.Features.Play;
using Griff.Editor.Themes;
using Griff.Runtime;

namespace Griff.Editor.Features.Profiler;

/// <summary>
/// The Profiler panel: how the game playing in the Game tab is running. A graph of its last ten seconds of frames, each
/// split into the game's steps, building the picture and drawing it (the rest is time to spare), the frame rate with its
/// 1% low and the garbage a frame makes, what each kind of script, each of the game's steps and each kind of thing drawn
/// costs a frame, and how many entities the scene holds with how many of each component the active ones have. It records
/// while it's on screen (see <see cref="IsOnScreen"/>) and <see cref="IsRecording"/>; otherwise the game isn't timed at all.
/// </summary>
public sealed class ProfilerPanel : ToolPanel
{
    /// <summary>How often what the panel shows catches up with the game while it records.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(0.1);

    /// <summary>Less time a frame than <see cref="Costs"/> shows, in milliseconds: it would show as 0.00.</summary>
    public const double Negligible = 0.005;

    private readonly PlayMode _play;
    private readonly DispatcherTimer _timer;
    private bool _isOnScreen;
    private bool _isRecording = true;
    private long _shown = -1;

    // Counts the playing scene a slice each refresh, so a big one doesn't hold up a frame.
    private readonly SceneCounter _counter = new();
    private FrameSummary _summary = FrameSummary.None;
    private IReadOnlyList<FrameCost> _costs = [];
    private SceneCounts _counts = SceneCounts.None;

    public ProfilerPanel(PlayMode play)
    {
        _play = play;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshEvery };
        _timer.Tick += (_, _) => Refresh();

        // Each run starts a fresh graph.
        play.Started += (_, _) => Clear();
        play.Stopped += (_, _) => Refresh();
    }

    public override string Id => "Profiler";
    public override string Title => "Profiler";
    public override string? Icon => Icons.Profiler;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 2;

    /// <summary>The frames recorded, newest last: the graph draws these.</summary>
    public FrameProfiler Frames { get; } = new();

    /// <summary>
    /// Whether the panel is on screen (its tab showing). The game is timed only then, so a profiler out of sight costs
    /// nothing. The view keeps it up to date.
    /// </summary>
    public bool IsOnScreen
    {
        get => _isOnScreen;
        set
        {
            if (SetProperty(ref _isOnScreen, value))
                Watch();
        }
    }

    /// <summary>Whether it records the game's frames while it's on screen; off holds the graph still to look at.</summary>
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
                Watch();
        }
    }

    /// <summary>Whether the game is being timed now: the panel is on screen and recording.</summary>
    public bool IsWatching => _isOnScreen && _isRecording;

    /// <summary>How the frames in the graph went: the frame rate, its 1% low, the average and worst frame, and their parts.</summary>
    public FrameSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>
    /// What each kind of script, each of the game's steps (physics, animations, particles...), each system a script added,
    /// each part of building the picture (what each kind of component draws, Sprite, Tilemap, Text..., each system's Draw,
    /// the lights, sorting back to front: see <see cref="FrameCostKind.Build"/>), drawing it, and the editor's own work
    /// (rewind snapshots, its panels following the game) cost a frame over the frames in the graph, the most first: the
    /// time each took on average and at worst, the garbage each made, and its share of the frame's work. Those that took less than <see cref="Negligible"/> and made no
    /// garbage (most of Griff's steps, in a game that doesn't use what they run) are left out.
    /// </summary>
    public IReadOnlyList<FrameCost> Costs
    {
        get => _costs;
        private set => SetProperty(ref _costs, value);
    }

    /// <summary>How many entities the playing scene holds and how many are active, counted a slice at a time while it records (a big scene's every second or two, so a frame isn't held up).</summary>
    public SceneCounts Counts
    {
        get => _counts;
        private set
        {
            if (SetProperty(ref _counts, value))
                OnPropertyChanged(nameof(Components));
        }
    }

    /// <summary>How many of each kind of component the active entities have, the most first.</summary>
    public IReadOnlyList<ComponentCount> Components => _counts.Components;

    /// <summary>The frames the graph shows (as many as it holds), changing whenever new ones come in: the graph's cue to redraw.</summary>
    public long Version => Frames.Recorded;

    /// <summary>What to do to see something, while the graph is empty; null once it has frames.</summary>
    public string? Hint => Frames.Count > 0 ? null
        : !_isRecording ? "Recording is paused."
        : _play.IsPlaying ? "Recording..."
        : "Play the game to see how it runs.";

    /// <summary>Forgets the frames recorded and the counts, starting the graph afresh.</summary>
    public void Clear()
    {
        Frames.Clear();
        _counter.Restart();
        _shown = -1;
        Summary = FrameSummary.None;
        Costs = [];
        Counts = SceneCounts.None;
        Refresh();
    }

    /// <summary>
    /// Catches up with the game now: the summary, the graph and the next slice of the counts, or with
    /// <paramref name="count"/>, all of the counts at once.
    /// </summary>
    public void Refresh(bool count = false)
    {
        if (Frames.Recorded != _shown)
        {
            _shown = Frames.Recorded;
            Summary = Frames.Summarize();
            Costs = Frames.Costs().Where(c => c.Ms >= Negligible || c.Allocated >= 1).ToList();
            OnPropertyChanged(nameof(Version));
        }

        if (_play.Game is { } game)
        {
            if (count)
                _counter.Restart();
            if ((count ? SceneCounts.Of(game.Scene) : _counter.Count(game.Scene)) is { } counts)
                Counts = counts;
        }

        OnPropertyChanged(nameof(Hint));
    }

    // The game is timed, and the panel catches up, only while it's watching.
    private void Watch()
    {
        _play.Profiler = IsWatching ? Frames : null;
        if (IsWatching)
        {
            _counter.Restart();
            Refresh();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            OnPropertyChanged(nameof(Hint));
        }

        OnPropertyChanged(nameof(IsWatching));
    }
}
