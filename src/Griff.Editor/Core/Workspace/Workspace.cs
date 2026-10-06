using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
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

    Task CreateProjectAsync(string location, string name);

    /// <summary>Opens the project at a folder or project.griff path. Returns false if the user cancelled.</summary>
    Task<bool> OpenProjectAsync(string path);

    /// <returns>False if the user cancelled or the save failed.</returns>
    Task<bool> SaveSceneAsync();

    /// <summary>Prompts to save unsaved changes. Returns true if it's safe to close the scene.</summary>
    Task<bool> ConfirmCloseAsync();
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

    public IReadOnlyList<string> RecentProjects => settings.Settings.RecentProjects;

    public async Task CreateProjectAsync(string location, string name)
    {
        if (!await ConfirmCloseAsync())
            return;

        var project = await Project.CreateAsync(location, name);
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
        if (Scene is not { } document)
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
        logger.LogInformation("Saved {Scene}", document.Name);
        status.Show($"Saved {document.Name}");
        return true;
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (Scene is not { IsDirty: true } document)
            return true;

        return dialogs.AskSaveChanges(document.Name) switch
        {
            SaveChangesChoice.Save => await SaveSceneAsync(),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private async Task OpenAsync(Project project)
    {
        Scene = null;
        Project = project;
        RememberRecent(project.RootPath);

        var scenePath = project.StartScenePath is { } start && File.Exists(start)
            ? start
            : project.FindScenes().FirstOrDefault();

        Scene = scenePath is null ? await CreateSceneAsync(project) : await LoadSceneAsync(scenePath);
        status.Show($"Opened {project.Name}");
        logger.LogInformation("Opened project {Name}", project.Name);
    }

    private async Task<SceneDocument> LoadSceneAsync(string path)
    {
        var warnings = new List<string>();
        Scene scene;
        await using (var stream = File.OpenRead(path))
            scene = Serializer.Load(stream, warnings);

        foreach (var warning in warnings)
            logger.LogWarning("{Scene}: {Warning}", Path.GetFileName(path), warning);

        return new SceneDocument(scene, path);
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
