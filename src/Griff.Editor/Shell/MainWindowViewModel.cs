using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;

namespace Griff.Editor.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private SceneDocument? _scene;

    public MainWindowViewModel(IWorkspace workspace, IStatusService status, ISelection selection, PlayMode play)
    {
        Workspace = workspace;
        Status = status;
        Play = play;
        play.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsPlaying));
        workspace.PropertyChanged += OnWorkspaceChanged;
        selection.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        Track(workspace.Scene);
    }

    public IWorkspace Workspace { get; }

    public IStatusService Status { get; }

    public PlayMode Play { get; }

    public bool IsPlaying => Play.IsPlaying;

    public bool HasProject => Workspace.Project is not null;

    // While playing, the dirty marker belongs to the edit scene, which isn't on screen.
    public string SceneTitle => Workspace.Scene is { } scene
        ? scene.IsRuntime ? $"{scene.Name} (playing)" : $"{scene.Name}{(scene.IsDirty ? "*" : "")}"
        : "";

    public string Title => Workspace.Project is { } project
        ? $"{SceneTitle} - {project.Name} - Griff"
        : "Griff";

    public string SceneLocation => Workspace.Project is { } project && Workspace.Scene is { } scene
        ? project.GetRelativePath(scene.FilePath)
        : "";

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkspace.Scene))
            Track(Workspace.Scene);
        Raise();
    }

    private void Track(SceneDocument? scene)
    {
        if (_scene is not null)
            _scene.PropertyChanged -= OnSceneChanged;
        _scene = scene;
        if (_scene is not null)
            _scene.PropertyChanged += OnSceneChanged;
        Raise();
    }

    private void OnSceneChanged(object? sender, PropertyChangedEventArgs e) => Raise();

    private void Raise()
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(SceneTitle));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SceneLocation));

        // Command availability depends on the project, scene and history.
        CommandManager.InvalidateRequerySuggested();
    }
}
