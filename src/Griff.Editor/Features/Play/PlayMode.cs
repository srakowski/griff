using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Networking;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
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
        _timeline = new Timeline(_workspace.Serializer);
        _timeline.Record(copy, 0, force: true);
        _position = 0;
        _sceneName = edit.Name;
        StartRunner(copy, paused: false);

        // Keys should drive the game, not whichever field had focus.
        Keyboard.ClearFocus();
        Application.Current.MainWindow?.Focus();

        Started?.Invoke(this, EventArgs.Empty);
        _status.Show("Playing");
        _logger.LogInformation("Play {Scene}", edit.Name);
        Raise();
    }

    public void Stop()
    {
        if (_runner is not { } runner)
            return;

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
        _position = 0;
        if (selectedId is { } id && _workspace.Scene?.Scene.Find(id) is { } original)
            _selection.Select(original);

        Stopped?.Invoke(this, EventArgs.Empty);
        _status.Show("Stopped");
        Raise();
    }

    /// <summary>Ends a game still running when the editor closes, without the editor's own bookkeeping.</summary>
    public void Dispose()
    {
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

        // Resuming from a rewound moment starts a new future from there.
        if (_runner.IsPaused && _timeline is not null)
            _timeline.TruncateAfter(_position);

        _runner.IsPaused = !_runner.IsPaused;
        foreach (var player in _players)
            player.Runner.IsPaused = _runner.IsPaused;
        _status.Show(_runner.IsPaused ? "Paused" : "Playing");
        Raise();
    }

    /// <summary>Advances one frame and stays paused.</summary>
    public void Step()
    {
        if (_runner is null)
            return;
        _runner.IsPaused = true;
        _runner.Step();
        Raise();
    }

    /// <summary>Pauses and jumps the game to snapshot <paramref name="index"/>.</summary>
    public void Seek(int index)
    {
        if (_runner is null || _timeline is not { Count: > 0 } timeline || _workspace.Project is null)
            return;

        index = Math.Clamp(index, 0, timeline.Count - 1);
        if (index == _position && _runner.IsPaused)
            return;
        if (_players.Count > 0)
        {
            _status.Show("Rewinding works with one player; stop and play again to start over");
            return;
        }

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
        StartRunner(scene, paused: true);
        _status.Show($"Rewound {SecondsBehind:0.0}s. Resume to continue from here.");
        Raise();
    }

    private void StartRunner(Scene scene, bool paused)
    {
        _runner = new GameRunner(scene, Surface, Application.Current.MainWindow!) { IsPaused = paused };
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
            (_runner.Game.Input as InputState)?.UseControls(project.Controls);
        }
        _runner.Game.SceneName = _sceneName;
        _runner.Game.SceneChanged += OnGameSceneChanged;
        _runner.Game.QuitRequested += OnGameQuit;
        _runner.Start();
    }

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
        (game.Input as InputState)?.UseControls(project.Controls);
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

    private void OnTicked(object? sender, EventArgs e)
    {
        if (_runtime is null)
            return;

        if (_runner is { IsPaused: false } runner && _timeline is { } timeline)
        {
            timeline.Record(_runtime.Scene, runner.Game.Time.Total);
            _position = timeline.Count - 1;
            OnPropertyChanged(nameof(TimelineCount));
            OnPropertyChanged(nameof(TimelinePosition));
            OnPropertyChanged(nameof(SecondsBehind));
        }

        // A destroyed selection would leave the inspector showing a ghost.
        if (_selection.Entity is { } selected && selected.Scene != _runtime.Scene)
            _selection.Select(null);

        _runtime.NotifyRuntimeTick();
    }

    private void OnFaulted(object? sender, Exception ex)
    {
        _logger.LogError(ex, "Game error: {Message}", ex.Message);
        _status.Show("Paused on error; see Output");
        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(TimelineCount));
        OnPropertyChanged(nameof(TimelinePosition));
        OnPropertyChanged(nameof(SecondsBehind));
        CommandManager.InvalidateRequerySuggested();
    }
}
