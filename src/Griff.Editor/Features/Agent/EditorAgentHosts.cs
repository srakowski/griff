using System.IO;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Agent;

/// <summary>The scene tools' view of the editor: the open scene and its undo history, and the selection.</summary>
public sealed class EditorAgentScene(IWorkspace workspace, ISelection selection) : IAgentScene
{
    public Scene? Scene => workspace.Scene?.Scene;

    public string? ScenePath => workspace.Project is { } project && workspace.Scene is { } scene
        ? Path.GetRelativePath(project.AssetsPath, scene.FilePath).Replace('\\', '/')
        : null;

    public bool IsPlaying => workspace.IsPlaying;

    public ComponentRegistry Components => workspace.Components;

    public void Execute(IEdit edit) => Document.Execute(edit);

    public IDisposable BeginGroup(string description) => Document.History.BeginGroup(description);

    public IReadOnlyList<Entity> Selection
    {
        get => selection.Entities;
        set => selection.SelectEntities(value);
    }

    public Entity? InstantiatePrefab(string name)
    {
        if (workspace.Project is not { } project || project.FindPrefab(name) is not { } path)
            return null;
        var entity = new SceneSerializer(workspace.Components).LoadPrefabFromString(File.ReadAllText(path));
        entity.Prefab = PrefabFiles.AssetPathOf(project, path);
        return entity;
    }

    public IReadOnlyCollection<string> PrefabNames =>
        workspace.Project?.FindPrefabs().Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet() ?? [];

    private SceneDocument Document => workspace.Scene ?? throw new AgentException("No scene is open.");
}

/// <summary>The asset tools' view of the editor: the project's files, and compiling scripts as they're written.</summary>
public sealed class EditorAgentProject(IWorkspace workspace) : IAgentProject
{
    public string? AssetsPath => workspace.Project?.AssetsPath;

    public Task<AssetFeedback?> AssetChangedAsync(string assetPath)
    {
        if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var result = workspace.ReloadScripts();
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

        // A prefab written is a prefab saved: its copies, in every scene, are brought up to date.
        if (assetPath.EndsWith(Griff.Projects.Project.PrefabExtension, StringComparison.OrdinalIgnoreCase) && workspace.Project is { } owner
            && Path.Combine(owner.AssetsPath, assetPath) is var prefabPath && File.Exists(prefabPath))
        {
            var update = PrefabFiles.UpdateCopies(workspace, assetPath, File.ReadAllText(prefabPath), source: null);
            var problems = update.Problems.Count > 0 ? "\nNot updated: " + string.Join("; ", update.Problems) : "";
            return Task.FromResult<AssetFeedback?>(update.Copies > 0 || problems.Length > 0
                ? new AssetFeedback((update.Summary + problems).Trim(), IsError: update.Problems.Count > 0)
                : null);
        }

        if (assetPath.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) && workspace.Scene is { } open && workspace.Project is { } project
            && Path.GetFullPath(open.FilePath).Equals(Path.GetFullPath(Path.Combine(project.AssetsPath, assetPath)), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<AssetFeedback?>(new AssetFeedback("That's the open scene; the editor still shows it as it was. Open it again (Scene_Open) to load the file."));
        }

        return Task.FromResult<AssetFeedback?>(null);
    }
}
