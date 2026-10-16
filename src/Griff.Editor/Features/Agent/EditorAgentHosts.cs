using System.IO;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Agent;

/// <summary>The scene tools' view of the editor: the open scene and its undo history, and the selection.</summary>
public sealed class EditorAgentScene(IProjectSession projectSession, ISceneDocuments sceneDocuments, IPlaySession playSession, ISelection selection) : IAgentScene
{
    public Scene? Scene => sceneDocuments.Scene?.Scene;

    public string? ScenePath => projectSession.Project is { } project && sceneDocuments.Scene is { } scene
        ? Path.GetRelativePath(project.AssetsPath, scene.FilePath).Replace('\\', '/')
        : null;

    public bool IsPlaying => playSession.IsPlaying;

    public Entity? TopParent => sceneDocuments.Scene?.ParentFor(null);

    public ComponentRegistry Components => projectSession.Components;

    public void Execute(IEdit edit) => Document.Execute(edit);

    public IDisposable BeginGroup(string description) => Document.History.BeginGroup(description);

    public IReadOnlyList<Entity> Selection
    {
        get => selection.Entities;
        set => selection.SelectEntities(value);
    }

    public Entity? InstantiatePrefab(string name)
    {
        if (projectSession.Project is not { } project || project.FindPrefab(name) is not { } path)
            return null;
        var entity = new SceneSerializer(projectSession.Components).LoadPrefabFromString(File.ReadAllText(path));
        entity.Prefab = PrefabFiles.AssetPathOf(project, path);
        return entity;
    }

    public IReadOnlyCollection<string> PrefabNames =>
        projectSession.Project?.FindPrefabs().Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet() ?? [];

    public KnownNames? Names => projectSession.Names;

    private SceneDocument Document => sceneDocuments.Scene ?? throw new AgentException("No scene is open.");
}

/// <summary>The asset tools' view of the editor: the project's files, and compiling scripts as they're written.</summary>
public sealed class EditorAgentProject(IProjectSession projectSession, ISceneDocuments sceneDocuments, IScriptSession scriptSession, PrefabFiles prefabFiles) : IAgentProject
{
    public string? AssetsPath => projectSession.Project?.AssetsPath;

    // A prefab moved keeps its copies, everywhere they are.
    public Task<AssetFeedback?> AssetMovedAsync(string fromPath, string toPath)
    {
        var update = prefabFiles.Relink(fromPath, toPath);
        var problems = update.Problems.Count > 0 ? "\nNot relinked: " + string.Join("; ", update.Problems) : "";
        return Task.FromResult<AssetFeedback?>(update.Copies > 0 || problems.Length > 0
            ? new AssetFeedback((update.Describe("Relinked") + problems).Trim(), IsError: update.Problems.Count > 0)
            : null);
    }

    public Task<AssetFeedback?> AssetChangedAsync(string assetPath, string? previousText = null)
    {
        // Whatever keeps the file loaded takes it in first (a table reaches the Tables panel and the running game).
        if (projectSession.AssetChanged(assetPath) is { Count: > 0 } said)
            return Task.FromResult<AssetFeedback?>(new AssetFeedback(string.Join("\n", said.Select(f => f.Message)), said.Any(f => f.IsError)));

        if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var result = scriptSession.ReloadScripts();
            if (result is null)
                return Task.FromResult<AssetFeedback?>(null);
            if (!result.Success)
            {
                var errors = result.Diagnostics.Where(d => d.IsError).Select(d => d.ToString()).ToList();
                return Task.FromResult<AssetFeedback?>(new AssetFeedback(
                    $"The scripts don't compile ({errors.Count} error{(errors.Count == 1 ? "" : "s")}); the previous version keeps running:\n" + string.Join("\n", errors), IsError: true));
            }

            var components = result.ComponentTypes.Select(t => t.Name).ToList();
            var warnings = result.Diagnostics.Where(d => !d.IsError).Select(d => "warning " + d).Concat(result.Warnings).ToList();
            return Task.FromResult<AssetFeedback?>(new AssetFeedback(
                (result.Unchanged ? "Scripts unchanged." : $"Scripts compiled. Script components: {(components.Count == 0 ? "none" : string.Join(", ", components))}.")
                + (warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : "")));
        }

        // A prefab written is a prefab saved: its copies, in every scene, are brought up to date, keeping what each
        // changed from the prefab as it was.
        if (assetPath.EndsWith(Griff.Projects.Project.PrefabExtension, StringComparison.OrdinalIgnoreCase) && projectSession.Project is { } owner
            && Path.Combine(owner.AssetsPath, assetPath) is var prefabPath && File.Exists(prefabPath))
        {
            var update = prefabFiles.UpdateCopies(assetPath, File.ReadAllText(prefabPath), previousText, source: null);
            var problems = update.Problems.Count > 0 ? "\nNot updated: " + string.Join("; ", update.Problems) : "";
            return Task.FromResult<AssetFeedback?>(update.Copies > 0 || problems.Length > 0
                ? new AssetFeedback((update.Summary + problems).Trim(), IsError: update.Problems.Count > 0)
                : null);
        }

        if (assetPath.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) && sceneDocuments.EditScene is { } open && projectSession.Project is { } project
            && Path.GetFullPath(open.FilePath).Equals(Path.GetFullPath(Path.Combine(project.AssetsPath, assetPath)), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<AssetFeedback?>(new AssetFeedback("That's the open scene; the editor still shows it as it was. Open it again (Scene_Open) to load the file."));
        }

        return Task.FromResult<AssetFeedback?>(null);
    }
}
