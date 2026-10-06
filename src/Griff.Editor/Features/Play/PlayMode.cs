using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
public sealed class PlayMode : ObservableObject
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly IStatusService _status;
    private readonly ILogger<PlayMode> _logger;
    private GameRunner? _runner;
    private SceneDocument? _runtime;
    private Timeline? _timeline;
    private int _position;

    public PlayMode(IWorkspace workspace, ISelection selection, IStatusService status, ILogger<PlayMode> logger)
    {
        _workspace = workspace;
        _selection = selection;
        _status = status;
        _logger = logger;

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

    public bool IsPaused => _runner?.IsPaused == true;

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

    public void Play()
    {
        if (IsPlaying || _workspace.Scene is not { } edit || _workspace.Project is not { } project)
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

    public void TogglePause()
    {
        if (_runner is null)
            return;

        // Resuming from a rewound moment starts a new future from there.
        if (_runner.IsPaused && _timeline is not null)
            _timeline.TruncateAfter(_position);

        _runner.IsPaused = !_runner.IsPaused;
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
            _runner.Game.SceneLoader = name => GameLoader.LoadScene(project, _workspace.Components, name);
        _runner.Game.SceneChanged += OnGameSceneChanged;
        _runner.Start();
    }

    // The game switched scenes (Game.LoadScene): show the new one everywhere.
    private void OnGameSceneChanged(object? sender, Scene scene)
    {
        if (_runtime is null)
            return;
        _runtime = new SceneDocument(scene, _runtime.FilePath, isRuntime: true);
        _workspace.ReplacePlayScene(_runtime);
        _selection.Select(null);
        _logger.LogInformation("Game loaded another scene");
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
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(TimelineCount));
        OnPropertyChanged(nameof(TimelinePosition));
        OnPropertyChanged(nameof(SecondsBehind));
        CommandManager.InvalidateRequerySuggested();
    }
}
