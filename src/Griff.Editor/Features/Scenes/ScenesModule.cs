using System.Windows.Input;
using System.IO;
using System.Text.RegularExpressions;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Scenes;

public sealed class ScenesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddCommand<SaveSceneCommand>()
        .AddCommand<NewSceneCommand>()
        .AddCommand<OpenSceneCommand>()
        .AddCommand<SetStartSceneCommand>();
}

/// <summary>Creates a new scene with a camera in Assets/Scenes and opens it.</summary>
public sealed partial class NewSceneCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, IPlaySession playSession, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Scene.New";
    public override string Text => "New Sc_ene...";
    public override string? Icon => Icons.SceneFile;
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => projectSession.Project is not null && !playSession.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (projectSession.Project is not { } project)
            return;

        var name = dialogs.Prompt("New Scene", "Scene name", "Level", n =>
            !SceneName().IsMatch(n) ? "Use letters, digits, spaces, - and _."
            : project.FindScene("Scenes/" + n) is not null ? $"{n} already exists."
            : null);
        if (name is not null)
            await sceneDocuments.CreateSceneAsync(name);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _-]*$")]
    private static partial Regex SceneName();
}

/// <summary>
/// Saves the open scene to its file, or, while a prefab's tab is showing, the prefab, bringing every copy of it up to date
/// (keeping what each copy changed).
/// </summary>
public sealed class SaveSceneCommand(ISceneDocuments sceneDocuments) : EditorCommand
{
    public override string Id => "Scene.Save";
    public override string Text => "_Save Scene";
    public override string? Icon => Icons.Save;
    public override KeyGesture? Gesture { get; } = new(Key.S, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 0, Order: 1);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is { IsRuntime: false };

    protected override Task ExecuteCoreAsync() => sceneDocuments.SaveSceneAsync();
}

/// <summary>Opens another of the project's scenes (asking to save the open one first if it has changes).</summary>
public sealed class OpenSceneCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, IPlaySession playSession, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Scene.Open";
    public override string Text => "Open Scene...";
    public override string? Icon => Icons.Open;
    public override KeyGesture? Gesture { get; } = new(Key.O, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => projectSession.Project is not null && !playSession.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (projectSession.Project is not { } project || dialogs.ShowOpenFileDialog("Griff scenes (*.scene)|*.scene") is not { } chosen)
            return;

        // A scene's name or its path in Assets will do, as well as a full path.
        var path = Path.IsPathRooted(chosen) && File.Exists(chosen) ? chosen : project.FindScene(chosen);
        if (path is null)
        {
            dialogs.ShowError("Open Scene", $"The project has no scene \"{chosen}\".");
            return;
        }

        await sceneDocuments.OpenSceneAsync(path);
    }
}

/// <summary>
/// Makes a scene the one the game starts in (when played from a menu or built): the submenu lists the project's scenes,
/// by name, or by their path in Assets where two share a name. Without a pick, it's the open scene.
/// </summary>
public sealed class SetStartSceneCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments) : EditorCommand
{
    public override string Id => "Scene.SetStart";
    public override string Text => "Set as S_tart Scene";
    public override string? Icon => Icons.Play;
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 3);
    public override IReadOnlyList<string>? Choices => Scenes().Select(s => s.Label).ToList();

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync() =>
        sceneDocuments.EditScene is { } scene ? projectSession.SetStartSceneAsync(scene.FilePath)
        : throw new InvalidOperationException("Open a scene, or pick one, to make it the start scene.");

    protected override Task ChooseCoreAsync(string choice)
    {
        if (projectSession.Project is not { } project)
            return Task.CompletedTask;
        var path = Scenes().FirstOrDefault(s => s.Label.Equals(choice.Trim(), StringComparison.OrdinalIgnoreCase)).Path
            ?? project.FindScene(choice.Trim())
            ?? throw new ArgumentException($"The project has no scene \"{choice}\".", nameof(choice));
        return string.Equals(project.StartScenePath, path, StringComparison.OrdinalIgnoreCase) ? Task.CompletedTask : projectSession.SetStartSceneAsync(path);
    }

    // Each scene's name, or its path in Assets (without .scene) when another scene has the same name.
    private List<(string Label, string? Path)> Scenes()
    {
        if (projectSession.Project is not { } project || !Directory.Exists(project.AssetsPath))
            return [];
        var files = project.FindScenes().ToList();
        var names = files.GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return files.Select(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            return (names[name] > 1 ? Path.ChangeExtension(PrefabFiles.AssetPathOf(project, f), null) : name, (string?)f);
        }).ToList();
    }
}
