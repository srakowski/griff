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

    private readonly IProjectSession projectSession;
    private readonly ISceneDocuments sceneDocuments;
    private readonly IPlaySession playSession;

    public MainWindowViewModel(IProjectSession projectSession, ISceneDocuments sceneDocuments, IPlaySession playSession, IStatusService status,
        ISelection selection, PlayMode play)
    {
        (this.projectSession, this.sceneDocuments, this.playSession) = (projectSession, sceneDocuments, playSession);
        Status = status;
        Play = play;
        play.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsPlaying));
        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IProjectSession.Project) or nameof(IProjectSession.RecentProjects))
                Raise();
        };
        sceneDocuments.PropertyChanged += OnScenesChanged;
        playSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IPlaySession.IsPlaying))
                Raise();
        };
        selection.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        Track(sceneDocuments.Scene);
    }

    public IStatusService Status { get; }

    public PlayMode Play { get; }

    public bool IsPlaying => Play.IsPlaying;

    public bool HasProject => projectSession.Project is not null;

    // The scene's tab, whether or not a prefab's tab is showing. While playing, the dirty marker belongs to the edit
    // scene, which isn't on screen.
    public string SceneTitle => playSession.IsPlaying && sceneDocuments.Scene is { } running ? $"{running.Name} (playing)"
        : sceneDocuments.EditScene is { } scene ? $"{scene.Name}{(scene.IsDirty ? "*" : "")}"
        : "";

    // What's showing: the scene, or a prefab in its tab.
    public string Title => projectSession.Project is { } project
        ? $"{(sceneDocuments.Scene is { IsPrefab: true } prefab ? $"{prefab.Name}{(prefab.IsDirty ? "*" : "")} (prefab)" : SceneTitle)} - {project.Name} - {AppVersion.Title}"
        : AppVersion.Title;

    public string SceneLocation => projectSession.Project is { } project && sceneDocuments.Scene is { } scene
        ? project.GetRelativePath(scene.FilePath)
        : "";

    private void OnScenesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ISceneDocuments.Scene))
            return;
        Track(sceneDocuments.Scene);
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
        _editScene = sceneDocuments.EditScene == scene ? null : sceneDocuments.EditScene;
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
