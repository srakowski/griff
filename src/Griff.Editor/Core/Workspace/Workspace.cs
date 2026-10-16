using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Features.Debugging.Probes;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Templates;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// The open project, its scene and the prefabs open in tabs of their own: what a project is made of, one object behind four
/// faces that services take as they need them: <see cref="IProjectSession"/> (the project), <see cref="ISceneDocuments"/>
/// (its scene and prefabs), <see cref="IPlaySession"/> (playing it) and <see cref="IScriptSession"/> (its scripts). Everything
/// that edits game content starts here.
/// </summary>
public sealed class Workspace(
    ISettingsStore settings,
    IStatusService status,
    IDialogService dialogs,
    ILogger<Workspace> logger) : ObservableObject, IProjectSession, ISceneDocuments, IPlaySession, IScriptSession, IDisposable
{
    private const int MaxRecentProjects = 10;

    private Project? _project;

    // The scene, or the running copy while playing (when the scene waits in _editScene); and the prefab showing, if any.
    private SceneDocument? _scene;
    private SceneDocument? _editScene;
    private SceneDocument? _prefab;
    private readonly ObservableCollection<SceneDocument> _prefabs = [];
    private ReadOnlyObservableCollection<SceneDocument>? _readOnlyPrefabs;
    private FileSystemWatcher? _scriptWatcher;
    private Griff.Projects.WatchedAssetFolder? _assets;
    private Griff.Runtime.PrefabLibrary? _prefabLibrary;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _scriptDebounce;
    private DispatcherTimer? _recoveryTimer;
    private readonly List<Func<Task<bool>>> _closeGuards = [];
    private PrefabFiles? _prefabFiles;

    // For bringing the copies of a prefab up to date as it is saved.
    private PrefabFiles PrefabFilesFor() => _prefabFiles ??= new PrefabFiles(this, this, this);

    public Project? Project
    {
        get => _project;
        private set => SetProperty(ref _project, value);
    }

    public Griff.Projects.IAssetSource? Assets => _assets;

    public Griff.Runtime.PrefabLibrary? PrefabLibrary => _prefabLibrary;

    public SceneDocument? Scene => _prefab ?? _scene;

    public ReadOnlyObservableCollection<SceneDocument> Prefabs => _readOnlyPrefabs ??= new(_prefabs);

    private void SetScene(SceneDocument? scene, SceneDocument? prefab = null)
    {
        if (_scene == scene && _prefab == prefab)
            return;
        _scene = scene;
        _prefab = prefab;
        OnPropertyChanged(nameof(Scene));
    }

    public ComponentRegistry Components { get; } = ComponentRegistry.CreateDefault();

    public SceneSerializer Serializer => new(Components);

    private Griff.Editing.KnownNameIndex? _names;

    public Griff.Editing.KnownNames? Names =>
        Project is { } project ? (_names ??= new(Components, p => Griff.Editing.FontChoices.For(p.AssetsPath))).Get(project, Scene?.Scene.Entities, Scene?.FilePath) : null;

    public ScriptHost Scripts => _scripts ??= NewScriptHost();

    public IReadOnlyList<Type> ScriptTypes => Scripts.Assembly?.GetTypes() ?? [];

    public ProbeTable ScriptProbes => Scripts.Attachment as ProbeTable ?? ProbeTable.Empty;

    // The editor builds scripts with breakpoint probes, and reads their /// comments (the Inspector, the component catalog,
    // agents) from the moment they load, before anything else hears they changed.
    private ScriptHost NewScriptHost()
    {
        var host = new ScriptHost(Components) { Compiler = ProbeCompiler.Compile };
        host.Reloaded += (_, result) =>
        {
            if (result.Assembly is { } assembly)
                Griff.Editing.DocComments.Register(assembly, result.Documentation);
        };
        return host;
    }

    private ScriptHost? _scripts;

    private readonly List<Func<IEnumerable<Scene>>> _liveScenes = [];

    public void AddLiveScenes(Func<IEnumerable<Scene>> scenes) => _liveScenes.Add(scenes);

    public event EventHandler<ReloadResult>? ScriptsReloaded;

    public event EventHandler? SceneShown;

    public ReloadResult? ReloadScripts()
    {
        if (Project is not { } project)
            return null;

        // Rebuild script components everywhere they're live: the edit scene, open prefabs and, while playing, the game
        // (and the other players' games).
        var documents = new[] { _editScene, _scene }.Concat(_prefabs).OfType<SceneDocument>().Distinct().ToList();
        var scenes = documents.Select(d => d.Scene).Concat(_liveScenes.SelectMany(live => live())).Distinct().ToList();
        var result = Scripts.ReloadFolder(project.AssetsPath, scenes);
        if (result.Unchanged)
            return result;

        foreach (var diagnostic in result.Diagnostics.Where(d => !d.IsError))
            logger.LogWarning("{Diagnostic}", diagnostic.ToString());

        if (!result.Success)
        {
            foreach (var error in result.Diagnostics.Where(d => d.IsError))
                logger.LogError("{Diagnostic}", error.ToString());
            status.Show("Script errors; the previous scripts are still running. See Output.");
            return result;
        }

        foreach (var warning in result.Warnings)
            logger.LogWarning("{Warning}", warning);
        foreach (var document in documents)
            document.History.RemapComponents(result.Replaced, Components);

        logger.LogInformation("Scripts compiled: {Count} component(s)", result.ComponentTypes.Count);
        ScriptsReloaded?.Invoke(this, result);
        return result;
    }

    // Saving a .cs file in Assets reloads scripts shortly after, once the editor has finished writing.
    private void WatchScripts(Project project)
    {
        _scriptWatcher?.Dispose();
        _scriptDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
        {
            _scriptDebounce!.Stop();
            ReloadScripts();
        }, Dispatcher.CurrentDispatcher);
        _scriptDebounce.Stop();

        Directory.CreateDirectory(project.AssetsPath);
        _scriptWatcher = new FileSystemWatcher(project.AssetsPath, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
        };
        FileSystemEventHandler restart = (_, _) => _scriptDebounce.Dispatcher.InvokeAsync(() =>
        {
            _scriptDebounce.Stop();
            _scriptDebounce.Start();
        });
        _scriptWatcher.Changed += restart;
        _scriptWatcher.Created += restart;
        _scriptWatcher.Deleted += restart;
        _scriptWatcher.Renamed += (s, e) => restart(s, e);
        _scriptWatcher.EnableRaisingEvents = true;
    }

    /// <summary>Stops watching the project's scripts and writing recovery files (the editor is closing).</summary>
    public void Dispose()
    {
        _scriptWatcher?.Dispose();
        _scriptWatcher = null;
        _prefabLibrary?.Dispose();
        _prefabLibrary = null;
        _assets?.Dispose();
        _assets = null;
        _scriptDebounce?.Stop();
        _recoveryTimer?.Stop();
    }

    public IReadOnlyList<string> RecentProjects => settings.Settings.RecentProjects;

    public bool IsPlaying => _editScene is not null;

    public ProblemLog Problems { get; } = new();

    public SceneDocument? EditScene => _editScene ?? _scene;

    // Playing plays the scene, so a prefab showing gives way to it.
    public void EnterPlayMode(SceneDocument runtime)
    {
        if (IsPlaying)
            throw new InvalidOperationException("Already playing.");
        _editScene = _scene;
        SetScene(runtime);
        OnPropertyChanged(nameof(IsPlaying));
    }

    public void ReplacePlayScene(SceneDocument runtime)
    {
        if (!IsPlaying)
            throw new InvalidOperationException("Not playing.");
        SetScene(runtime);
    }

    public void ExitPlayMode()
    {
        if (!IsPlaying)
            return;
        var edit = _editScene;
        _editScene = null;
        SetScene(edit);
        OnPropertyChanged(nameof(IsPlaying));
    }

    public void ShowScene()
    {
        if (IsPlaying)
            return;
        SetScene(_scene);
        SceneShown?.Invoke(this, EventArgs.Empty);
    }

    public void Show(SceneDocument document)
    {
        if (IsPlaying)
            return;
        if (document == _scene)
            SetScene(_scene);
        else if (_prefabs.Contains(document))
            SetScene(_scene, document);
        else
            throw new ArgumentException($"{document.Name} isn't open.", nameof(document));
    }

    public async Task<bool> OpenPrefabAsync(string path)
    {
        // Not while playing, or while the project is still opening its scene.
        if (Project is not { } project || IsPlaying || _scene is null)
            return false;
        var file = Path.IsPathRooted(path) && File.Exists(path) ? path : project.FindPrefab(path);
        if (file is null)
        {
            dialogs.ShowError("Open Prefab", $"The project has no prefab \"{path}\".");
            return false;
        }

        if (_prefabs.FirstOrDefault(p => string.Equals(Path.GetFullPath(p.FilePath), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Show(open);
            return true;
        }

        var warnings = new List<string>();
        Entity root;
        try
        {
            root = Serializer.LoadPrefabFromString(await File.ReadAllTextAsync(file), warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError("Couldn't open {Prefab}: {Message}", Path.GetFileName(file), ex.Message);
            dialogs.ShowError("Couldn't open the prefab", ex.Message);
            return false;
        }

        foreach (var warning in warnings)
            logger.LogWarning("{Prefab}: {Warning}", Path.GetFileName(file), warning);

        var scene = new Scene();
        scene.Add(root);
        var document = new SceneDocument(scene, file);
        _prefabs.Add(document);
        Show(document);
        status.Show($"Editing the {document.Name} prefab: saving it updates every copy");
        logger.LogInformation("Opened prefab {Prefab}", document.Name);
        return true;
    }

    public async Task<bool> ClosePrefabAsync(SceneDocument prefab)
    {
        if (!_prefabs.Contains(prefab))
            return true;
        if (prefab.IsDirty)
        {
            var choice = dialogs.AskSaveChanges(prefab.Name);
            if (choice == SaveChangesChoice.Cancel || (choice == SaveChangesChoice.Save && !await SavePrefabAsync(prefab)))
                return false;
        }

        // Closing the prefab that's showing shows the scene again.
        if (_prefab == prefab)
            SetScene(_scene);
        _prefabs.Remove(prefab);
        return true;
    }

    public async Task CreateProjectAsync(string location, string name, ProjectTemplate? template = null)
    {
        // An empty or relative location would resolve against the editor's working directory.
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location.Trim()))
            throw new ArgumentException("Location must be a full folder path, like C:\\Games.", nameof(location));
        location = location.Trim();

        if (!await ConfirmCloseProjectAsync())
            return;

        var project = await Project.CreateAsync(location, name, template);
        logger.LogInformation("Created project {Name} at {Path}", project.Name, project.RootPath);
        settings.Settings.LastProjectLocation = location;
        await OpenAsync(project);
    }

    public async Task<bool> CloseProjectAsync()
    {
        if (Project is null)
            return true;
        if (!await ConfirmCloseProjectAsync())
            return false;

        // Everything tied to the project goes, so the editor is as it was before any project opened.
        var name = Project.Name;
        SetScene(null);
        _editScene = null;
        _prefabs.Clear();
        _recoveryTimer?.Stop();
        _scriptDebounce?.Stop();
        _scriptWatcher?.Dispose();
        _scriptWatcher = null;
        _scripts?.Unload([]);
        _prefabLibrary?.Dispose();
        _prefabLibrary = null;
        _assets?.Dispose();
        _assets = null;
        _prefabFiles = null;
        _names = null;
        Problems.Clear();
        Project = null;
        OnPropertyChanged(nameof(IsPlaying));
        status.Show($"Closed {name}");
        logger.LogInformation("Closed project {Name}", name);
        return true;
    }

    public async Task<bool> OpenProjectAsync(string path)
    {
        if (!await ConfirmCloseProjectAsync())
            return false;

        Project project;
        try
        {
            project = await Project.LoadAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            logger.LogError("Couldn't open project at {Path}: {Message}", path, ex.Message);
            ForgetRecent(path);
            throw;
        }

        await OpenAsync(project);
        return true;
    }

    public async Task<bool> SaveSceneAsync()
    {
        if (_prefab is { } prefab)
            return await SavePrefabAsync(prefab);
        return Scene is { IsRuntime: false } document && await SaveSceneFileAsync(document);
    }

    // Prefabs first: saving one can update its copies in the scene.
    public async Task<bool> SaveAllAsync()
    {
        foreach (var prefab in _prefabs.Where(p => p.IsDirty).ToList())
        {
            if (!await SavePrefabAsync(prefab))
                return false;
        }

        return EditScene is not { IsDirty: true, IsRuntime: false } scene || await SaveSceneFileAsync(scene);
    }

    private async Task<bool> SaveSceneFileAsync(SceneDocument document)
    {
        try
        {
            // Write to a temporary file first so a failed save never truncates the real one.
            var temp = document.FilePath + ".saving";
            await using (var stream = File.Create(temp))
                Serializer.Save(document.Scene, stream);
            File.Move(temp, document.FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("Couldn't save {Path}: {Message}", document.FilePath, ex.Message);
            dialogs.ShowError("Save failed", ex.Message);
            return false;
        }

        document.MarkSaved();
        if (Project is { } saved)
            TryRecovery(() => RecoveryFiles.Delete(saved, document.FilePath));
        logger.LogInformation("Saved {Scene}", document.Name);
        status.Show($"Saved {document.Name}");
        FollowModules();
        return true;
    }

    // A scene or prefab saved with the first component of a module the project didn't use (or without the last of one it
    // did): the scripts build again, so their usings are those of the modules the project uses (ScriptCompiler).
    private void FollowModules()
    {
        if (Project is not { } project || _scripts?.Modules is not { } built)
            return;
        var scenes = new[] { _editScene, _scene }.Concat(_prefabs).OfType<SceneDocument>().Select(d => d.Scene).Distinct();
        if (!Griff.Runtime.ProjectModules.Of(project.AssetsPath, null, scenes).SequenceEqual(built))
            ReloadScripts();
    }

    // A prefab is one entity, with everything else inside it.
    private async Task<bool> SavePrefabAsync(SceneDocument prefab)
    {
        var roots = prefab.Scene.Roots;
        if (roots.Count != 1)
        {
            dialogs.ShowError("Couldn't save the prefab", roots.Count == 0
                ? $"The {prefab.Name} prefab is empty. Undo to bring back what was deleted, or close its tab without saving."
                : $"A prefab is one entity with everything else inside it, but {prefab.Name} has {roots.Count} at the top " +
                  $"({string.Join(", ", roots.Select(r => r.Name))}). Drag the others onto {roots[0].Name} in the Hierarchy.");
            return false;
        }

        var json = Serializer.SavePrefabToString(roots[0]);
        string? oldJson;
        try
        {
            oldJson = File.Exists(prefab.FilePath) ? await File.ReadAllTextAsync(prefab.FilePath) : null;
            // Write to a temporary file first so a failed save never truncates the real one.
            var temp = prefab.FilePath + ".saving";
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, prefab.FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("Couldn't save {Path}: {Message}", prefab.FilePath, ex.Message);
            dialogs.ShowError("Save failed", ex.Message);
            return false;
        }

        prefab.MarkSaved();
        logger.LogInformation("Saved prefab {Prefab}", prefab.Name);
        FollowModules();
        if (Project is { } project)
        {
            var update = PrefabFilesFor().UpdateCopies(PrefabFiles.AssetPathOf(project, prefab.FilePath), json, oldJson, roots[0]);
            if (update.Problems.Count > 0)
                dialogs.ShowError("Some copies weren't updated", string.Join("\n", update.Problems));
            status.Show($"Saved the {prefab.Name} prefab. {update.Summary}".Trim());
        }

        return true;
    }

    public async Task<bool> ConfirmCloseProjectAsync()
    {
        foreach (var guard in _closeGuards)
        {
            if (!await guard())
                return false;
        }

        foreach (var prefab in _prefabs.Where(p => p.IsDirty).ToList())
        {
            if (!await ClosePrefabAsync(prefab))
                return false;
        }

        return await ConfirmCloseAsync();
    }

    public void AddCloseGuard(Func<Task<bool>> guard) => _closeGuards.Add(guard);

    private readonly List<Func<string, Griff.Agent.AssetFeedback?>> _assetListeners = [];

    public void AddAssetListener(Func<string, Griff.Agent.AssetFeedback?> listener) => _assetListeners.Add(listener);

    public IReadOnlyList<Griff.Agent.AssetFeedback> AssetChanged(string assetPath) =>
        [.. _assetListeners.Select(listener => listener(assetPath)).OfType<Griff.Agent.AssetFeedback>()];

    public async Task<bool> ConfirmCloseAsync()
    {
        // Leaving play mode discards the runtime copy; the edit scene is what matters.
        ExitPlayMode();
        if (_scene is not { IsDirty: true } document)
            return true;

        switch (dialogs.AskSaveChanges(document.Name))
        {
            case SaveChangesChoice.Save:
                return await SaveSceneFileAsync(document);
            case SaveChangesChoice.Discard:
                if (Project is { } project)
                    TryRecovery(() => RecoveryFiles.Delete(project, document.FilePath));
                return true;
            default:
                return false;
        }
    }

    public async Task<bool> OpenSceneAsync(string path)
    {
        if (Project is not { } project || IsPlaying || !await ConfirmCloseAsync())
            return false;

        // A path in Assets ("Scenes/Level 2.scene") or a scene's name works as well as a full path.
        if (!Path.IsPathRooted(path))
            path = File.Exists(Path.Combine(project.AssetsPath, path)) ? Path.Combine(project.AssetsPath, path) : project.FindScene(path) ?? Path.Combine(project.AssetsPath, path);

        var scene = await LoadSceneAsync(path);
        SetScene(scene);
        status.Show($"Opened {scene.Name}");
        logger.LogInformation("Opened scene {Scene}", scene.Name);
        return true;
    }

    public async Task<bool> CreateSceneAsync(string name)
    {
        if (Project is not { } project || IsPlaying || !await ConfirmCloseAsync())
            return false;

        var path = project.CreateScene(name);
        logger.LogInformation("Created {Scene}", project.GetRelativePath(path));
        return await OpenSceneAsync(path);
    }

    public async Task SetStartSceneAsync(string path)
    {
        if (Project is not { } project)
            return;
        await project.SetStartSceneAsync(path);
        status.Show($"{Path.GetFileNameWithoutExtension(path)} is now the start scene");
        OnPropertyChanged(nameof(Project));
    }

    // A prefab file changed other than by the editor's own saving (a pull, an agent, a text editor): its copies follow,
    // each keeping what it changed.
    private void OnPrefabChanged(Griff.Runtime.PrefabChange change)
    {
        if (Project is not { } project || change.Now is null)
            return;
        var update = PrefabFilesFor().Changed(change);
        if (update.Copies == 0 && update.Problems.Count == 0)
            return;
        var problems = update.Problems.Count > 0 ? " Not updated: " + string.Join("; ", update.Problems) : "";
        logger.LogInformation("Prefab {Path} changed outside the editor: {Summary}{Problems}", PrefabFiles.AssetPathOf(project, change.Path), update.Summary, problems);
        status.Show($"{Path.GetFileNameWithoutExtension(change.Path)} changed. {update.Summary}{problems}".Trim());
    }

    private async Task OpenAsync(Project project)
    {
        var scenePath = project.StartScenePath is { } start && File.Exists(start)
            ? start
            : project.FindScenes().FirstOrDefault();

        // Asked before anything changes, so a question an agent's call didn't answer goes back to it with the editor as it
        // was (and while the user's question is up, the editor isn't half way into the project): never with the project
        // open and no scene.
        var recovery = scenePath is null ? null : AskRecovery(project, scenePath);

        SetScene(null);
        _prefabs.Clear();
        _prefabLibrary?.Dispose();
        _assets?.Dispose();
        _assets = new Griff.Projects.WatchedAssetFolder(project.AssetsPath);
        // One library of the project's prefabs for every game played while it's open: what one read the next spawns at once,
        // and a prefab changed (here or outside) is read again, its copies brought up to date.
        _dispatcher = Dispatcher.CurrentDispatcher;
        _prefabLibrary = new Griff.Runtime.PrefabLibrary(project, Components, _assets);
        _prefabLibrary.Changed += change => _dispatcher.InvokeAsync(() => OnPrefabChanged(change));
        Project = project;
        RememberRecent(project.RootPath);

        // Scripts first, so the scene's script components load as real components.
        ReloadScripts();
        WatchScripts(project);
        StartRecoveryTimer();

        // Copies of prefabs changed while the project was closed (a pull, a text editor) catch up before anything opens.
        var caughtUp = PrefabFilesFor().CatchUp();

        var (scene, problem) = await OpenFirstSceneAsync(project, scenePath, recovery);
        SetScene(scene);
        SceneShown?.Invoke(this, EventArgs.Empty);
        status.Show(caughtUp.Copies > 0 ? $"Opened {project.Name}. {caughtUp.Summary}" : $"Opened {project.Name}");
        logger.LogInformation("Opened project {Name}", project.Name);
        if (caughtUp.Problems.Count > 0)
            logger.LogWarning("Copies of prefabs changed while the project was closed weren't all brought up to date: {Problems}", string.Join("; ", caughtUp.Problems));
        if (problem is not null)
            dialogs.ShowError("Couldn't open the start scene", problem);
    }

    // The scene a project opens on: its start scene (or first); if that can't be read, the first other that can; if none
    // can, a new one. A project is never open without a scene. With what went wrong, said once the scene is open.
    private async Task<(SceneDocument Scene, string? Problem)> OpenFirstSceneAsync(Project project, string? path, string? recovery)
    {
        if (path is null)
            return (await CreateSceneAsync(project), null);
        string problem;
        try
        {
            return (await LoadSceneAsync(path, recovery), null);
        }
        catch (Exception ex) when (Unreadable(ex))
        {
            logger.LogError("Couldn't open {Scene}: {Message}", project.GetRelativePath(path), ex.Message);
            problem = $"{Path.GetFileNameWithoutExtension(path)} couldn't be read ({ex.Message}), so ";
        }

        foreach (var other in project.FindScenes())
        {
            if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                // Without asking about its recovery copy (that's kept for when it's opened itself).
                var document = await LoadSceneAsync(other, null);
                return (document, problem + $"{document.Name} is open instead.");
            }
            catch (Exception ex) when (Unreadable(ex))
            {
                logger.LogError("Couldn't open {Scene}: {Message}", project.GetRelativePath(other), ex.Message);
            }
        }

        var created = await CreateSceneAsync(project);
        return (created, problem + $"a new scene, {created.Name}, is open instead.");
    }

    private static bool Unreadable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException or NotSupportedException;

    private async Task<SceneDocument> LoadSceneAsync(string path) =>
        await LoadSceneAsync(path, Project is { } project ? AskRecovery(project, path) : null);

    // A recovery copy newer than the scene means the editor closed without saving: whether to open the scene from it, asked
    // through the dialogs (so an agent answers it with "answers"). The copy to open from, or null.
    private string? AskRecovery(Project project, string path)
    {
        if (RecoveryFiles.FindNewer(project, path) is not { } recovery)
            return null;
        var when = File.GetLastWriteTime(recovery);
        if (dialogs.Confirm(RecoverTitle,
                $"{Path.GetFileNameWithoutExtension(path)} has unsaved changes from {when:g} that weren't saved before Griff closed.\n\nRecover them?"))
            return recovery;
        TryRecovery(() => RecoveryFiles.Delete(project, path));
        return null;
    }

    /// <summary>The question asked when a scene has unsaved changes kept from when the editor closed without saving.</summary>
    public const string RecoverTitle = "Recover unsaved changes?";

    // The scene at `path`, from its recovery copy if one was chosen (or the scene itself, if the copy can't be read).
    private async Task<SceneDocument> LoadSceneAsync(string path, string? recovery)
    {
        var warnings = new List<string>();
        Scene scene;
        try
        {
            await using var stream = File.OpenRead(recovery ?? path);
            scene = Serializer.Load(stream, warnings);
        }
        catch (Exception ex) when (recovery is not null && Unreadable(ex))
        {
            logger.LogWarning("The unsaved changes to {Scene} couldn't be read ({Message}); opened it as it was saved.", Path.GetFileName(path), ex.Message);
            (recovery, warnings) = (null, []);
            await using var stream = File.OpenRead(path);
            scene = Serializer.Load(stream, warnings);
        }

        foreach (var warning in warnings)
            logger.LogWarning("{Scene}: {Warning}", Path.GetFileName(path), warning);

        var document = new SceneDocument(scene, path);
        if (recovery is not null)
        {
            document.MarkRecovered();
            logger.LogInformation("Recovered unsaved changes to {Scene}", document.Name);
        }

        return document;
    }

    // Every 20 seconds, write a recovery copy of the edit scene if it has unsaved changes.
    private void StartRecoveryTimer()
    {
        _recoveryTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => WriteRecovery(), Dispatcher.CurrentDispatcher);
        _recoveryTimer.Start();
    }

    private void WriteRecovery()
    {
        var document = EditScene;
        if (Project is not { } project || document is not { IsDirty: true, IsRuntime: false })
            return;
        var json = Serializer.SaveToString(document.Scene);
        TryRecovery(() => RecoveryFiles.Write(project, document.FilePath, json));
    }

    // Recovery is a safety net; never let it interrupt work.
    private void TryRecovery(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Crash recovery: {Message}", ex.Message);
        }
    }

    // A project with no scenes (or none that can be read) gets an empty one so there's always something to edit, beside any
    // that's there rather than over it.
    private async Task<SceneDocument> CreateSceneAsync(Project project)
    {
        var name = Griff.Projects.Project.DefaultSceneName;
        var path = Path.Combine(project.AssetsPath, name + Griff.Projects.Project.SceneExtension);
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(project.AssetsPath, $"{name} {n}{Griff.Projects.Project.SceneExtension}");
        Directory.CreateDirectory(project.AssetsPath);
        var document = new SceneDocument(new Scene(), path);
        await using (var stream = File.Create(path))
            Serializer.Save(document.Scene, stream);
        logger.LogWarning("{Project} had no scene to open; created {Scene}", project.Name, document.Name);
        return document;
    }

    private void RememberRecent(string rootPath)
    {
        var recent = settings.Settings.RecentProjects;
        recent.RemoveAll(p => string.Equals(p, rootPath, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, rootPath);
        if (recent.Count > MaxRecentProjects)
            recent.RemoveRange(MaxRecentProjects, recent.Count - MaxRecentProjects);
        settings.Save();
        OnPropertyChanged(nameof(RecentProjects));
    }

    private void ForgetRecent(string path)
    {
        if (settings.Settings.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            settings.Save();
            OnPropertyChanged(nameof(RecentProjects));
        }
    }
}
