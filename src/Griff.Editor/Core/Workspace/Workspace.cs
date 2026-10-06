using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Core.Workspace;

/// <summary>The open project and scene. Everything that edits game content starts here.</summary>
public interface IWorkspace : INotifyPropertyChanged
{
    Project? Project { get; }

    SceneDocument? Scene { get; }

    ComponentRegistry Components { get; }

    SceneSerializer Serializer { get; }

    IReadOnlyList<string> RecentProjects { get; }

    /// <summary>True while a play-mode copy of the scene is open in place of the edit scene.</summary>
    bool IsPlaying { get; }

    /// <summary>Shows <paramref name="runtime"/> in place of the edit scene until <see cref="ExitPlayMode"/>.</summary>
    void EnterPlayMode(SceneDocument runtime);

    void ExitPlayMode();

    Task CreateProjectAsync(string location, string name, ProjectTemplate template = ProjectTemplate.Empty);

    /// <summary>Opens the project at a folder or project.griff path. Returns false if the user cancelled.</summary>
    Task<bool> OpenProjectAsync(string path);

    /// <returns>False if the user cancelled or the save failed.</returns>
    Task<bool> SaveSceneAsync();

    /// <summary>Prompts to save unsaved changes. Returns true if it's safe to close the scene.</summary>
    Task<bool> ConfirmCloseAsync();

    /// <summary>Raised after scripts compile and are swapped in. Script components may have been replaced.</summary>
    event EventHandler<ReloadResult>? ScriptsReloaded;

    /// <summary>Compiles the project's scripts now and swaps them into the open scenes if they build.</summary>
    ReloadResult? ReloadScripts();
}

public sealed class Workspace(
    ISettingsStore settings,
    IStatusService status,
    IDialogService dialogs,
    ILogger<Workspace> logger) : ObservableObject, IWorkspace
{
    private const int MaxRecentProjects = 10;

    private Project? _project;
    private SceneDocument? _scene;
    private SceneDocument? _editScene;
    private FileSystemWatcher? _scriptWatcher;
    private DispatcherTimer? _scriptDebounce;
    private DispatcherTimer? _recoveryTimer;

    public Project? Project
    {
        get => _project;
        private set => SetProperty(ref _project, value);
    }

    public SceneDocument? Scene
    {
        get => _scene;
        private set => SetProperty(ref _scene, value);
    }

    public ComponentRegistry Components { get; } = ComponentRegistry.CreateDefault();

    public SceneSerializer Serializer => new(Components);

    public ScriptHost Scripts => _scripts ??= new ScriptHost(Components);

    private ScriptHost? _scripts;

    public event EventHandler<ReloadResult>? ScriptsReloaded;

    public ReloadResult? ReloadScripts()
    {
        if (Project is not { } project)
            return null;

        // Rebuild script components everywhere they're live: the edit scene and, while playing, the game.
        var documents = new[] { _editScene, Scene }.OfType<SceneDocument>().Distinct().ToList();
        var result = Scripts.ReloadFolder(project.AssetsPath, documents.Select(d => d.Scene));
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

    public IReadOnlyList<string> RecentProjects => settings.Settings.RecentProjects;

    public bool IsPlaying => _editScene is not null;

    public void EnterPlayMode(SceneDocument runtime)
    {
        if (IsPlaying)
            throw new InvalidOperationException("Already playing.");
        _editScene = Scene;
        Scene = runtime;
        OnPropertyChanged(nameof(IsPlaying));
    }

    public void ExitPlayMode()
    {
        if (!IsPlaying)
            return;
        Scene = _editScene;
        _editScene = null;
        OnPropertyChanged(nameof(IsPlaying));
    }

    public async Task CreateProjectAsync(string location, string name, ProjectTemplate template = ProjectTemplate.Empty)
    {
        if (!await ConfirmCloseAsync())
            return;

        var project = await Project.CreateAsync(location, name, template);
        logger.LogInformation("Created project {Name} at {Path}", project.Name, project.RootPath);
        settings.Settings.LastProjectLocation = location;
        await OpenAsync(project);
    }

    public async Task<bool> OpenProjectAsync(string path)
    {
        if (!await ConfirmCloseAsync())
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
        if (Scene is not { IsRuntime: false } document)
            return false;

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
        return true;
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        // Leaving play mode discards the runtime copy; the edit scene is what matters.
        ExitPlayMode();
        if (Scene is not { IsDirty: true } document)
            return true;

        switch (dialogs.AskSaveChanges(document.Name))
        {
            case SaveChangesChoice.Save:
                return await SaveSceneAsync();
            case SaveChangesChoice.Discard:
                if (Project is { } project)
                    TryRecovery(() => RecoveryFiles.Delete(project, document.FilePath));
                return true;
            default:
                return false;
        }
    }

    private async Task OpenAsync(Project project)
    {
        Scene = null;
        Project = project;
        RememberRecent(project.RootPath);

        // Scripts first, so the scene's script components load as real components.
        ReloadScripts();
        WatchScripts(project);
        StartRecoveryTimer();

        var scenePath = project.StartScenePath is { } start && File.Exists(start)
            ? start
            : project.FindScenes().FirstOrDefault();

        Scene = scenePath is null ? await CreateSceneAsync(project) : await LoadSceneAsync(scenePath);
        status.Show($"Opened {project.Name}");
        logger.LogInformation("Opened project {Name}", project.Name);
    }

    private async Task<SceneDocument> LoadSceneAsync(string path)
    {
        // A recovery copy newer than the scene means the editor closed without saving.
        var source = path;
        var recovered = false;
        if (Project is { } project && RecoveryFiles.FindNewer(project, path) is { } recovery)
        {
            var when = File.GetLastWriteTime(recovery);
            if (dialogs.Confirm("Recover unsaved changes?",
                    $"{Path.GetFileNameWithoutExtension(path)} has unsaved changes from {when:g} that weren't saved before Griff closed.\n\nRecover them?"))
            {
                source = recovery;
                recovered = true;
            }
            else
            {
                TryRecovery(() => RecoveryFiles.Delete(project, path));
            }
        }

        var warnings = new List<string>();
        Scene scene;
        await using (var stream = File.OpenRead(source))
            scene = Serializer.Load(stream, warnings);

        foreach (var warning in warnings)
            logger.LogWarning("{Scene}: {Warning}", Path.GetFileName(path), warning);

        var document = new SceneDocument(scene, path);
        if (recovered)
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
        var document = _editScene ?? Scene;
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

    // A project with no scenes gets an empty one so there's always something to edit.
    private async Task<SceneDocument> CreateSceneAsync(Project project)
    {
        var path = Path.Combine(project.AssetsPath, Griff.Projects.Project.DefaultSceneName + Griff.Projects.Project.SceneExtension);
        Directory.CreateDirectory(project.AssetsPath);
        var document = new SceneDocument(new Scene(), path);
        await using (var stream = File.Create(path))
            Serializer.Save(document.Scene, stream);
        logger.LogWarning("{Project} had no scenes; created {Scene}", project.Name, document.Name);
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
