using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Griff.Debugging;
using Griff.Networking;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Modules.Fighting;
using Griff.Platform.Windows;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Play;

/// <summary>
/// Runs the open scene inside the editor. Play clones the scene and shows the copy everywhere
/// (hierarchy, inspector, scene view stay live and editable); Stop discards the copy.
/// </summary>
public sealed class PlayMode : ObservableObject, IDisposable
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly IStatusService _status;
    private readonly ILogger<PlayMode> _logger;
    private GameRunner? _runner;
    private SceneDocument? _runtime;
    private Timeline? _timeline;
    private int _position;
    private string? _sceneName;
    private MemoryNetwork? _network;
    private readonly List<PlayerWindow> _players = [];
    private bool _slowConnection;
    private bool _showHitboxes;
    private long _refreshedAt;
    // The Game tab's game's tables: kept through rewinds, which start a new game from a snapshot, so what Table Values set
    // isn't set again (with new dice) unless its row has changed, and a change waiting for the next step still reaches it.
    private Griff.Data.TableSet? _tables;

    /// <summary>How often the panels that show what the game changes (the Hierarchy, the Inspector) catch up while it plays.</summary>
    /// <summary>
    /// The most a step may spend copying the scene out so a script's exception can rewind to just before its step (see
    /// <see cref="Timeline.RememberBudget"/>), for games played from now on. A quarter millisecond, which tests raise so a
    /// slow machine doesn't decide whether a rewind is exact.
    /// </summary>
    public TimeSpan RememberBudget { get; set; } = Timeline.DefaultRememberBudget;

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(0.1);

    // Another player's game, in a window of its own, joined to the one in the Game tab.
    private sealed record PlayerWindow(int Number, Window Window, GameSurface Surface, GameRunner Runner);

    /// <summary>A bad connection to pretend between the players of Play with Players: lag, jitter, a few lost packets.</summary>
    public static readonly NetConditions SlowConditions = new() { Latency = 0.06, Jitter = 0.03, Loss = 0.02 };

    public PlayMode(IWorkspace workspace, ISelection selection, IStatusService status, ILogger<PlayMode> logger)
    {
        _workspace = workspace;
        _selection = selection;
        _status = status;
        _logger = logger;

        // A script reload reaches the other players' games as well as the Game tab's.
        workspace.AddLiveScenes(() => _players.Select(p => p.Runner.Game.Scene).ToList());

        // The workspace can leave play mode on its own (e.g. when closing); follow it.
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.IsPlaying) && !workspace.IsPlaying && _runner is not null)
                Stop();
        };
    }

    public event EventHandler? Started;

    public event EventHandler? Stopped;

    /// <summary>Where the game draws. Hosted by the main window's Game tab.</summary>
    public GameSurface Surface { get; } = new();

    public bool IsPlaying => _runner is not null;

    /// <summary>The running game, for diagnostics.</summary>
    internal Game? Game => _runner?.Game;

    public bool IsPaused => _runner?.IsPaused == true;

    /// <summary>
    /// True while a script stands at a breakpoint (see <see cref="HoldAtBreakpoint"/>): the game is paused in the middle
    /// of its frame, at that line. Resuming (<see cref="TogglePause"/>) lets the script carry on from there.
    /// </summary>
    public bool IsAtBreakpoint => _held is not null;

    // While a script stands at a breakpoint: the editor's own loop, run until it's let go, and whether the game it's in
    // is being stopped (or rewound) rather than resumed.
    private DispatcherFrame? _held;
    private bool _abandonHeld;

    /// <summary>
    /// Where the Game tab's game times its frames (see <see cref="Griff.Runtime.Game.Profiler"/>), from now on and in every
    /// game played after; null times nothing. The Profiler panel sets it while it's on screen.
    /// </summary>
    public FrameProfiler? Profiler
    {
        get => _profiler;
        set
        {
            _profiler = value;
            if (_runner is not null)
                _runner.Game.Profiler = value;
        }
    }

    private FrameProfiler? _profiler;

    /// <summary>How many players are playing: 1, or more with Play with Players (the Game tab is player 1, the host).</summary>
    public int PlayerCount => _runner is null ? 0 : 1 + _players.Count;

    /// <summary>
    /// Pretend the players of Play with Players are on a slow connection (see <see cref="SlowConditions"/>), to feel what
    /// lag does to the game. Takes effect straight away.
    /// </summary>
    public bool SlowConnection
    {
        get => _slowConnection;
        set
        {
            _slowConnection = value;
            foreach (var game in AllGames())
                game.Net.FakeLag = value ? SlowConditions : new NetConditions();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Draws every hitbox and hurtbox in play over the running games (Game &gt; Show Hitboxes), straight away and in every
    /// game played after: the Game tab's, other players' and a rewind's.
    /// </summary>
    public bool ShowHitboxes
    {
        get => _showHitboxes;
        set
        {
            _showHitboxes = value;
            foreach (var game in AllGames())
                game.ShowHitboxes = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Points the Game tab's mouse at <paramref name="world"/> (in world units) for an agent playtesting the game, whatever
    /// the real mouse is doing, until the real one moves over the game; null gives it back.
    /// </summary>
    internal void PointAt(System.Numerics.Vector2? world)
    {
        if (_runner is not null)
            _runner.PointerOverride = world;
    }

    /// <summary>Player <paramref name="number"/>'s game (1 is the Game tab's), for diagnostics.</summary>
    internal Game? PlayerGame(int number) => number == 1 ? Game : _players.FirstOrDefault(p => p.Number == number)?.Runner.Game;

    /// <summary>Where player <paramref name="number"/>'s game draws.</summary>
    internal GameSurface? PlayerSurface(int number) => number == 1 ? Surface : _players.FirstOrDefault(p => p.Number == number)?.Surface;

    /// <summary>Player <paramref name="number"/>'s window (null for player 1, who plays in the Game tab).</summary>
    internal Window? PlayerWindowOf(int number) => _players.FirstOrDefault(p => p.Number == number)?.Window;

    /// <summary>Has every running game (the Game tab's and other players') take <paramref name="rate"/> steps a second from now on.</summary>
    public void UseStepRate(int rate)
    {
        foreach (var game in AllGames())
            game.Time.StepRate = rate;
    }

    /// <summary>
    /// Has every running game (the Game tab's and other players') take <paramref name="table"/> in place of the table of
    /// its name, as its next step begins (see <see cref="Griff.Data.TableSet.Stage"/>): a table edited while playing
    /// shows at once, and Table Values set from its rows set their values again.
    /// </summary>
    public void StageTable(Griff.Data.Table table)
    {
        foreach (var game in AllGames())
            game.Tables.Stage(table);
    }

    /// <summary>Has every running game treat the table called <paramref name="name"/> as not reading, saying <paramref name="problem"/>.</summary>
    public void StageTableProblem(string name, string problem)
    {
        foreach (var game in AllGames())
            game.Tables.StageProblem(name, problem);
    }

    /// <summary>Has every running game do without the table called <paramref name="name"/> (its file was deleted).</summary>
    public void StageTableRemoval(string name)
    {
        foreach (var game in AllGames())
            game.Tables.StageRemoval(name);
    }

    // Something set up without code that can't do its job (a Table Values naming a row that isn't there) goes to Output.
    private void OnGameWarning(object? sender, string message) => _logger.LogWarning("{Warning}", message);

    private IEnumerable<Game> AllGames()
    {
        if (_runner?.Game is { } host)
            yield return host;
        foreach (var player in _players)
            yield return player.Runner.Game;
    }

    /// <summary>Number of rewind snapshots kept (about the last 20 seconds).</summary>
    public int TimelineCount => _timeline?.Count ?? 0;

    /// <summary>The snapshot being shown. Equal to <see cref="TimelineCount"/> - 1 when live.</summary>
    public int TimelinePosition
    {
        get => _position;
        set => Seek(value);
    }

    /// <summary>Seconds behind the newest snapshot, for the timeline label.</summary>
    public double SecondsBehind => _timeline is { Count: > 0 } t ? t.TimeAt(t.Count - 1) - t.TimeAt(Math.Clamp(_position, 0, t.Count - 1)) : 0;

    /// <summary>
    /// Plays with <paramref name="players"/> players: the Game tab hosts a network game (player 1), and each other player
    /// gets a window of their own that joins it, as if on another machine. Click a window to play as that player.
    /// </summary>
    public void Play(int players)
    {
        Play();
        if (_runner is null || players <= 1)
            return;
        StartPlayers(players);
    }

    public void Play()
    {
        if (IsPlaying || _workspace.EditScene is not { } edit || _workspace.Project is not { } project)
            return;

        // Same ids in the copy, so the selection carries across.
        var serializer = _workspace.Serializer;
        var warnings = new List<string>();
        var copy = serializer.LoadFromString(serializer.SaveToString(edit.Scene), warnings);
        var selectedId = _selection.Entity?.Id;

        _runtime = new SceneDocument(copy, edit.FilePath, isRuntime: true);
        _workspace.EnterPlayMode(_runtime);
        if (selectedId is { } id)
            _selection.Select(copy.Find(id));

        Surface.AssetRoot = project.AssetsPath;
        // A big scene is snapshotted less often: copying one out costs a step half a millisecond on average at most (a
        // Debug editor copies 20,000 BunnyMark bunnies out in under 3 ms, so ten times a second).
        _timeline = new Timeline(_workspace.Serializer) { CostPerStep = TimeSpan.FromMilliseconds(0.5), RememberBudget = RememberBudget };
        _timeline.Record(copy, 0, force: true);
        _position = 0;
        _timeOffset = 0;
        (_exceptionSnapshot, _reported) = (null, null);
        _sceneName = edit.Name;
        _tables = null;
        StartRunner(copy, paused: false);

        // Keys should drive the game, not whichever field had focus.
        Keyboard.ClearFocus();
        Application.Current?.MainWindow?.Focus();

        Started?.Invoke(this, EventArgs.Empty);
        _status.Show("Playing");
        _logger.LogInformation("Play {Scene}", edit.Name);
        Raise();
    }

    public void Stop()
    {
        if (_runner is not { } runner)
            return;

        LetGo(abandon: true);
        _runner = null;
        StopPlayers();
        runner.Ticked -= OnTicked;
        runner.Faulted -= OnFaulted;
        try
        {
            runner.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping the game");
        }

        var selectedId = _selection.Entity?.Id;
        _workspace.ExitPlayMode();
        _runtime = null;
        _timeline = null;
        _tables = null;
        _position = 0;
        (_exceptionSnapshot, _reported) = (null, null);
        _nextFrame.TrySetResult();
        if (selectedId is { } id && _workspace.Scene?.Scene.Find(id) is { } original)
            _selection.Select(original);

        Stopped?.Invoke(this, EventArgs.Empty);
        _status.Show("Stopped");
        Raise();
    }

    /// <summary>Ends a game still running when the editor closes, without the editor's own bookkeeping.</summary>
    public void Dispose()
    {
        LetGo(abandon: true);
        StopPlayers();
        var runner = _runner;
        _runner = null;
        try
        {
            runner?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping the game");
        }
    }

    public void TogglePause()
    {
        if (_runner is null)
            return;

        // At a breakpoint, resuming lets the script carry on from that line.
        if (_held is not null)
        {
            LetGo(abandon: false);
            SetPaused(false);
            _status.Show("Playing");
            Raise();
            return;
        }

        // Resuming from a rewound moment starts a new future from there.
        if (_runner.IsPaused && _timeline is not null)
            _timeline.TruncateAfter(_position);

        SetPaused(!_runner.IsPaused);
        if (_runner.IsPaused)
            Refresh();
        _status.Show(_runner.IsPaused ? "Paused" : "Playing");
        Raise();
    }

    /// <summary>The runner of the game in the Game tab, while one plays.</summary>
    internal GameRunner? Runner => _runner;

    /// <summary>Advances one frame and stays paused.</summary>
    public void Step()
    {
        // At a breakpoint the frame is under way: it can only carry on (resume) or be left (stop, rewind).
        if (_runner is null || _held is not null)
            return;
        _runner.IsPaused = true;
        _runner.Step();
        Refresh();
        Raise();
    }

    /// <summary>Pauses and jumps the game to snapshot <paramref name="index"/>.</summary>
    public void Seek(int index) => Seek(index, always: false);

    // `always` restores the snapshot even if it's the one shown: the game may have gone on from it while paused.
    private void Seek(int index, bool always)
    {
        if (_runner is null || _timeline is not { Count: > 0 } timeline || _workspace.Project is null)
            return;

        index = Math.Clamp(index, 0, timeline.Count - 1);
        if (index == _position && _runner.IsPaused && _held is null && !always)
            return;
        if (_players.Count > 0)
        {
            _status.Show("Rewinding works with one player; stop and play again to start over");
            return;
        }

        // Rewinding from a breakpoint leaves the frame it's in: the game goes back to before it.
        LetGo(abandon: true);
        var selectedId = _selection.Entity?.Id;
        var old = _runner;
        old.Ticked -= OnTicked;
        old.Faulted -= OnFaulted;
        old.Dispose();

        var scene = timeline.Restore(index);
        _runtime = new SceneDocument(scene, _runtime!.FilePath, isRuntime: true);
        _workspace.ReplacePlayScene(_runtime);
        _selection.Select(selectedId is { } id ? scene.Find(id) : null);

        _position = index;
        // The game starts its clock again from there, and its next step from that snapshot.
        _timeOffset = timeline.TimeAt(index);
        timeline.RememberSnapshot(index);
        StartRunner(scene, paused: true);
        _status.Show($"Rewound {SecondsBehind:0.0}s. Resume to continue from here.");
        Raise();
    }

    private void StartRunner(Scene scene, bool paused)
    {
        // Keys come from the whole window (or, with no window, as in tests, the game's own view).
        _runner = new GameRunner(scene, Surface, (UIElement?)Application.Current?.MainWindow ?? Surface) { IsPaused = paused, TakesKeys = NotTypingCode };
        _runner.Ticked += OnTicked;
        _runner.Faulted += OnFaulted;
        if (_workspace.Project is { } project)
        {
            _runner.Game.SceneLoader = name => LoadScene(project, name);
            _runner.Game.PrefabLoader = new PrefabLibrary(project, _workspace.Components).Instantiate;
            _runner.Game.SaveData = new SaveData(SaveData.EditorPath(project.RootPath));
            _runner.Game.Title = project.Name;
            _runner.Game.Components = _workspace.Components;
            _runner.Game.Net.FakeLag = _slowConnection ? SlowConditions : new NetConditions();
            _runner.Game.UseProjectSettings(project);
            if (_tables is null)
                _tables = _runner.Game.Tables;
            else
                _runner.Game.Tables = _tables;
        }

        _runner.Game.Warning += OnGameWarning;
        _runner.Game.SceneName = _sceneName;
        _runner.Game.Profiler = _profiler;
        _runner.Game.ShowHitboxes = _showHitboxes;
        _runner.Game.SceneChanged += OnGameSceneChanged;
        _runner.Game.Stepped += OnStepped;
        _runner.Game.QuitRequested += OnGameQuit;
        _runner.Start();
    }

    /// <summary>
    /// Holds the game at a breakpoint a script of <paramref name="runner"/>'s game reached (on this thread, in the middle of
    /// its frame): every player's game pauses, and the editor goes on working, on a loop of its own, until the game is
    /// resumed (<see cref="TogglePause"/>) or stopped or rewound. Returns true if it was stopped or rewound, so the script
    /// goes no further. False straight away if <paramref name="runner"/> isn't one of the games playing, or one is held already.
    /// </summary>
    internal bool HoldAtBreakpoint(GameRunner runner, string where, string? stepped = null)
    {
        if (!CanHold(runner))
            return false;
        _logger.LogInformation("Paused at {Where} ({By})", where, stepped ?? "a breakpoint");
        return Hold(stepped is null
            ? $"Paused at a breakpoint: {where}. Continue (F5 or F6) carries on from there; F10, F11 and Shift+F11 step."
            : $"{stepped}: {where}");
    }

    /// <summary>
    /// Holds the game where a script threw <paramref name="exception"/> (on this thread, before anything unwound), as at a
    /// breakpoint: the error goes to Output, and the timeline keeps the moment just before the step that threw (see
    /// <see cref="RewindBeforeException"/>). Continue lets the exception end the frame and the game play on; stopping or
    /// rewinding leaves it. Returns true if it was stopped or rewound. Null straight away if <paramref name="runner"/>
    /// isn't one of the games playing, or one is held already.
    /// </summary>
    internal bool? HoldAtException(GameRunner runner, Exception exception, string where)
    {
        if (!CanHold(runner))
            return null;

        // Only the Game tab's game has a timeline (rewinding works with one player).
        if (runner == _runner && _timeline is { } timeline && timeline.KeepRemembered(_position) is var (index, exact))
        {
            (_exceptionSnapshot, ExceptionSnapshotExact) = (timeline.SequenceAt(index), exact);
            if (exact)
                _position = timeline.Count - 1;
        }
        else
        {
            _exceptionSnapshot = null;
        }

        _reported = exception;
        _logger.LogError(exception, "{Type} in the game at {Where}: {Message}", exception.GetType().Name, where, exception.Message);
        return Hold($"Paused on {exception.GetType().Name} at {where}: {exception.Message} Continue (F5) plays on; Debug > Rewind to Before the Exception goes back to just before it.");
    }

    private bool CanHold(GameRunner runner) => _held is null && _runner is not null && (runner == _runner || _players.Any(p => p.Runner == runner));

    // The snapshot (by its sequence number) holding the moment just before the step the last exception was thrown in, and
    // the exception reported then (so it isn't reported again as the frame ends).
    private long? _exceptionSnapshot;
    private Exception? _reported;

    /// <summary>Whether the moment <see cref="RewindBeforeException"/> goes back to is exactly the start of the step that threw.</summary>
    internal bool ExceptionSnapshotExact { get; private set; }

    /// <summary>
    /// The snapshot the game goes back to with <see cref="RewindBeforeException"/>, or -1 if there's none (no exception yet
    /// this time it played, or its moment has gone from the timeline).
    /// </summary>
    public int ExceptionSnapshot => _exceptionSnapshot is { } sequence && _timeline is { } timeline ? timeline.IndexOfSequence(sequence) : -1;

    /// <summary>Whether <see cref="RewindBeforeException"/> can go back: there's a moment to, and one player.</summary>
    public bool CanRewindBeforeException => _runner is not null && _players.Count == 0 && ExceptionSnapshot >= 0;

    /// <summary>
    /// Rewinds to just before the last exception a script threw: the game as it was when the step that threw began (or, in a
    /// scene too big to remember every step, the last snapshot before it), paused there. From there a breakpoint and
    /// Continue (or Step Frame) watch it happen again. Works while the game stands at the exception and after it played on,
    /// as long as that moment is on the timeline. False if there's nothing to go back to.
    /// </summary>
    public bool RewindBeforeException()
    {
        if (!CanRewindBeforeException)
            return false;
        var exact = ExceptionSnapshotExact;
        Seek(ExceptionSnapshot, always: true);
        _status.Show(exact
            ? "Rewound to just before the step that threw. Set a breakpoint and Continue, or Step Frame, to watch it again."
            : "Rewound to the last snapshot before the exception (this scene is too big to remember every step).");
        return true;
    }

    // Holds the game in the middle of its frame, on the editor's own loop, until it's let go.
    private bool Hold(string status)
    {
        SetPaused(true);
        var frame = _held = new DispatcherFrame();
        _abandonHeld = false;
        Refresh();
        _status.Show(status);
        Raise();
        _nextFrame.TrySetResult();
        BreakpointReached?.Invoke(this, EventArgs.Empty);
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            if (_held == frame)
                _held = null;
        }

        Raise();
        return _abandonHeld || frame.Dispatcher.HasShutdownStarted;
    }

    /// <summary>Raised as the game stops at a breakpoint, or where a script threw (see <see cref="HoldAtBreakpoint"/> and <see cref="HoldAtException"/>).</summary>
    public event EventHandler? BreakpointReached;

    /// <summary>
    /// Lets a script held at a breakpoint carry on with the game staying paused, for a step of the debugger: it runs until
    /// the debugger holds it again at the line the step ends at, or to the end of the frame, where it stays paused (see
    /// <see cref="PausedAfterStep"/>). False if no script is held.
    /// </summary>
    internal bool StepOn()
    {
        if (_held is null)
            return false;
        LetGo(abandon: false);
        return true;
    }

    /// <summary>A step of the debugger ran to the end of the frame: the game stays paused there, the panels showing it.</summary>
    internal void PausedAfterStep(string message)
    {
        if (_runner is null)
            return;
        SetPaused(true);
        Refresh();
        _status.Show(message);
        Raise();
    }

    // Lets go of a script held at a breakpoint: it carries on once the editor's done with what it's doing, or with
    // `abandon`, stops there (the game is being stopped or rewound).
    private void LetGo(bool abandon)
    {
        if (_held is not { } frame)
            return;
        _held = null;
        _abandonHeld = abandon;
        frame.Continue = false;
    }

    private void SetPaused(bool paused)
    {
        if (_runner is not null)
            _runner.IsPaused = paused;
        foreach (var player in _players)
            player.Runner.IsPaused = paused;
    }

    // Keys typed into a script's code (F9 for a breakpoint too) are the code editor's, not the game's.
    private static bool NotTypingCode() => Keyboard.FocusedElement is not ICSharpCode.AvalonEdit.Editing.TextArea;

    // ----- Play with Players -----

    // The Game tab hosts on a pretend network inside the editor, and each other player's window joins it there.
    private void StartPlayers(int players)
    {
        if (_runner is null || _workspace.Project is not { } project)
            return;
        _network = new MemoryNetwork();
        var host = _runner.Game;
        host.Net.PlayerName = "Player 1";
        host.Net.Host(_network.Open(NetSession.DefaultPort));
        for (var number = 2; number <= Math.Min(players, 8); number++)
            OpenPlayer(project, number, players);
        _status.Show($"Playing with {players} players: click a window to play as that player");
        _logger.LogInformation("Play with {Players} players", players);
        Raise();
    }

    private void OpenPlayer(Griff.Projects.Project project, int number, int players)
    {
        var main = Application.Current.MainWindow!;
        var surface = new GameSurface { AssetRoot = project.AssetsPath };
        var (left, top, width, height) = PlaceFor(main, number, players);
        var window = new Window
        {
            Title = $"{project.Name}: Player {number}",
            Width = width,
            Height = height,
            Left = left,
            Top = top,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Black,
            Content = surface,
            Owner = main,
            ShowActivated = false,
            ShowInTaskbar = false,
        };

        // Its own copy of the scene, which it swaps for the host's as soon as it's in.
        var serializer = _workspace.Serializer;
        var scene = _workspace.EditScene is { } edit ? serializer.LoadFromString(serializer.SaveToString(edit.Scene)) : new Scene();
        window.Show();
        var runner = new GameRunner(scene, surface, window);
        var game = runner.Game;
        game.SceneLoader = name => LoadScene(project, name);
        game.PrefabLoader = new PrefabLibrary(project, _workspace.Components).Instantiate;
        game.SaveData = new SaveData();
        game.Title = project.Name;
        game.Components = _workspace.Components;
        game.SceneName = _sceneName;
        game.Net.FakeLag = _slowConnection ? SlowConditions : new NetConditions();
        game.Net.PlayerName = $"Player {number}";
        game.ShowHitboxes = _showHitboxes;
        game.UseProjectSettings(project);
        game.Warning += OnGameWarning;
        game.Net.Join(_network!.Open(), new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, NetSession.DefaultPort));
        runner.Faulted += OnFaulted;
        runner.Start();

        var player = new PlayerWindow(number, window, surface, runner);
        _players.Add(player);
        window.Closed += (_, _) =>
        {
            if (_players.Remove(player))
            {
                runner.Faulted -= OnFaulted;
                runner.Dispose();
                Raise();
            }
        };
    }

    // Down the right of the editor, a strip of windows the size of a small game view.
    private static (double Left, double Top, double Width, double Height) PlaceFor(Window main, int number, int players)
    {
        var area = SystemParameters.WorkArea;
        var others = Math.Max(1, players - 1);
        var width = Math.Clamp(main.ActualWidth * 0.34, 320, 760);
        var height = Math.Min(width * 9 / 16 + 32, (area.Height - 120) / others - 8);
        width = Math.Min(width, (height - 32) * 16 / 9);
        var right = Math.Min(main.Left + main.ActualWidth, area.Right);
        var left = right - width - 18;
        var top = Math.Max(area.Top, main.Top) + 96 + (number - 2) * (height + 8);
        return (left, top, width, height);
    }

    private void StopPlayers()
    {
        foreach (var player in _players.ToList())
        {
            _players.Remove(player);
            player.Runner.Faulted -= OnFaulted;
            try
            {
                player.Runner.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping player {Number}", player.Number);
            }

            player.Window.Close();
        }

        _network = null;
    }

    // The scene open in the editor plays as it is there, unsaved changes included (so Retry doesn't lose them);
    // other scenes load from disk.
    private Scene LoadScene(Griff.Projects.Project project, string name)
    {
        if (_workspace.EditScene is { } edit && project.FindScene(name) is { } path &&
            string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(edit.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            var serializer = _workspace.Serializer;
            return serializer.LoadFromString(serializer.SaveToString(edit.Scene));
        }

        return GameLoader.LoadScene(project, _workspace.Components, name);
    }

    // The game switched scenes (Game.LoadScene): show the new one everywhere.
    private void OnGameSceneChanged(object? sender, Scene scene)
    {
        if (_runtime is null)
            return;
        // The editor reading the new scene (a script's properties, say) doesn't stop at breakpoints.
        using var quiet = ScriptProbes.Quiet();
        _runtime = new SceneDocument(scene, _runtime.FilePath, isRuntime: true);
        _sceneName = (sender as Game)?.SceneName ?? _sceneName;
        _workspace.ReplacePlayScene(_runtime);
        _selection.Select(null);
        _logger.LogInformation("Game loaded another scene");
    }

    // Game.Quit (or a Quit button): stop playing once the current frame is done.
    private void OnGameQuit(object? sender, EventArgs e)
    {
        _logger.LogInformation("Game quit");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_runner?.Game == sender)
                Stop();
        });
    }

    // Each step of the game counts towards the next rewind snapshot, however many the screen's frames take.
    private void OnStepped(object? sender, EventArgs e)
    {
        if (_runtime is null || _runner is not { } runner || runner.Game != sender || _timeline is not { } timeline)
            return;
        // Paused, a step (Step Frame) takes no snapshot, but its moment is still remembered.
        if (runner.IsPaused && !RemembersSteps)
            return;
        // A snapshot reads scripts' properties for itself: that doesn't stop at breakpoints.
        using var quiet = ScriptProbes.Quiet();
        // The profiler shows what keeping rewind snapshots costs the game's frames, as the editor's own work.
        if (runner.Game.Profiler is { } profiler)
        {
            var (started, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
            Record(runner, timeline);
            profiler.AddCost("Rewind", FrameCostKind.Editor, Stopwatch.GetTimestamp() - started, Workers.AllocatedBytes() - allocated);
        }
        else
            Record(runner, timeline);
    }

    private void Record(GameRunner runner, Timeline timeline)
    {
        var time = _timeOffset + runner.Game.Time.Total;
        var took = false;
        if (!runner.IsPaused)
        {
            took = timeline.Record(_runtime!.Scene, time);
            _position = timeline.Count - 1;
        }

        // The moment the next step starts from, in case a script throws in it (see RewindBeforeException).
        if (!took && RemembersSteps)
            timeline.Remember(_runtime!.Scene, time);
    }

    // The game time of the snapshot the game was last rewound to: its clock starts again from naught there.
    private double _timeOffset;

    /// <summary>
    /// Remember the moment between every two steps of the Game tab's game (not just the snapshots, which are several steps
    /// apart), so that when a script throws, the timeline keeps the moment just before that step (see
    /// <see cref="RewindBeforeException"/>). The debugger turns it on while it pauses on exceptions. It costs a step copying
    /// the scene out, in a scene small enough for that to be quick (see <see cref="Timeline.RememberBudget"/>).
    /// </summary>
    public bool RemembersSteps { get; set; }

    // The profiler shows what the panels following the game cost its frames, as the editor's own work.
    private void OnTicked(object? sender, EventArgs e)
    {
        _nextFrame.TrySetResult();
        if (_runtime is null)
            return;
        if (_runner?.Game.Profiler is not { } profiler)
        {
            FollowGame();
            return;
        }

        var (started, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
        FollowGame();
        profiler.AddCost("Editor Panels", FrameCostKind.Editor, Stopwatch.GetTimestamp() - started, Workers.AllocatedBytes() - allocated);
    }

    // The Inspector, the Hierarchy and the rewind bar keep up with the game.
    private void FollowGame()
    {
        if (_runtime is null)
            return;

        if (_runner is { IsPaused: false })
        {
            OnPropertyChanged(nameof(TimelineCount));
            OnPropertyChanged(nameof(TimelinePosition));
            OnPropertyChanged(nameof(SecondsBehind));
        }

        // A destroyed selection would leave the inspector showing a ghost.
        if (_selection.Entity is { } selected && selected.Scene != _runtime.Scene)
            _selection.Select(null);

        _runtime.NotifyRuntimeTick();
        if (Stopwatch.GetElapsedTime(_refreshedAt) >= RefreshEvery)
            Refresh();
    }

    // The panels catch up with the game.
    private void Refresh()
    {
        _refreshedAt = Stopwatch.GetTimestamp();
        _runtime?.NotifyRuntimeRefresh();
    }

    // An exception ended a frame: reported (unless it was when the game stopped where it was thrown), and the game paused
    // on it, or, let go from there, playing on.
    private void OnFaulted(object? sender, Exception ex)
    {
        var reported = ex == _reported;
        _reported = null;
        if (!reported)
            _logger.LogError(ex, "Game error: {Message}", ex.Message);
        _status.Show((sender as GameRunner)?.IsPaused == false ? $"{ex.GetType().Name} ended a frame; the game plays on (see Output)" : "Paused on error; see Output");
        _nextFrame.TrySetResult();
        Refresh();
        Raise();
    }

    // Completes as the game next plays a frame, stops at a line, ends a frame with an error, or stops.
    private TaskCompletionSource _nextFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Finishes once the game next plays a frame (or stops at a line of script on the way, ends a frame with an error, or
    /// stops): what an agent's Play, Continue or Resume waits for, since each returns before the game's next frame, so a
    /// script that throws or a breakpoint reached straight away is reported by it.
    /// </summary>
    internal Task NextFrame()
    {
        if (_nextFrame.Task.IsCompleted)
            _nextFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _nextFrame.Task;
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsAtBreakpoint));
        OnPropertyChanged(nameof(CanRewindBeforeException));
        OnPropertyChanged(nameof(ExceptionSnapshot));
        OnPropertyChanged(nameof(TimelineCount));
        OnPropertyChanged(nameof(TimelinePosition));
        OnPropertyChanged(nameof(SecondsBehind));
        CommandManager.InvalidateRequerySuggested();
    }
}
