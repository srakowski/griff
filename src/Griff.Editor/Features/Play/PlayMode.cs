using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Platform.Windows;
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
        _runner = new GameRunner(copy, Surface, Application.Current.MainWindow!);
        _runner.Ticked += OnTicked;
        _runner.Faulted += OnFaulted;

        // Keys should drive the game, not whichever field had focus.
        Keyboard.ClearFocus();
        Application.Current.MainWindow?.Focus();

        Started?.Invoke(this, EventArgs.Empty);
        _runner.Start();
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

    private void OnTicked(object? sender, EventArgs e)
    {
        if (_runtime is null)
            return;

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
        CommandManager.InvalidateRequerySuggested();
    }
}
