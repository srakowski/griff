using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Updates;

namespace Griff.Editor.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private SceneDocument? _scene;
    private SceneDocument? _editScene;

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

    // The scene's tab, whether or not a prefab's tab is showing. While playing, the dirty marker belongs to the edit
    // scene, which isn't on screen.
    public string SceneTitle => Workspace.IsPlaying && Workspace.Scene is { } running ? $"{running.Name} (playing)"
        : Workspace.EditScene is { } scene ? $"{scene.Name}{(scene.IsDirty ? "*" : "")}"
        : "";

    // What's showing: the scene, or a prefab in its tab.
    public string Title => Workspace.Project is { } project
        ? $"{(Workspace.Scene is { IsPrefab: true } prefab ? $"{prefab.Name}{(prefab.IsDirty ? "*" : "")} (prefab)" : SceneTitle)} - {project.Name} - {AppVersion.Title}"
        : AppVersion.Title;

    public string SceneLocation => Workspace.Project is { } project && Workspace.Scene is { } scene
        ? project.GetRelativePath(scene.FilePath)
        : "";

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkspace.Scene))
            Track(Workspace.Scene);
        Raise();
    }

    // Both the scene and the prefab showing (if one is) have dirty markers on screen.
    private void Track(SceneDocument? scene)
    {
        if (_scene is not null)
            _scene.PropertyChanged -= OnSceneChanged;
        if (_editScene is not null)
            _editScene.PropertyChanged -= OnSceneChanged;
        _scene = scene;
        _editScene = Workspace.EditScene == scene ? null : Workspace.EditScene;
        if (_scene is not null)
            _scene.PropertyChanged += OnSceneChanged;
        if (_editScene is not null)
            _editScene.PropertyChanged += OnSceneChanged;
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
