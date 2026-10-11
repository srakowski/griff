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
public sealed partial class NewSceneCommand(IWorkspace workspace, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Scene.New";
    public override string Text => "New Sc_ene...";
    public override string? Icon => Icons.SceneFile;
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project)
            return;

        var name = dialogs.Prompt("New Scene", "Scene name", "Level", n =>
            !SceneName().IsMatch(n) ? "Use letters, digits, spaces, - and _."
            : project.FindScene("Scenes/" + n) is not null ? $"{n} already exists."
            : null);
        if (name is not null)
            await workspace.CreateSceneAsync(name);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _-]*$")]
    private static partial Regex SceneName();
}

/// <summary>
/// Saves the open scene to its file, or, while a prefab's tab is showing, the prefab, bringing every copy of it up to date
/// (keeping what each copy changed).
/// </summary>
public sealed class SaveSceneCommand(IWorkspace workspace) : EditorCommand
{
    public override string Id => "Scene.Save";
    public override string Text => "_Save Scene";
    public override string? Icon => Icons.Save;
    public override KeyGesture? Gesture { get; } = new(Key.S, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 0, Order: 1);

    protected override bool CanExecuteCore() => workspace.Scene is { IsRuntime: false };

    protected override Task ExecuteCoreAsync() => workspace.SaveSceneAsync();
}

/// <summary>Opens another of the project's scenes (asking to save the open one first if it has changes).</summary>
public sealed class OpenSceneCommand(IWorkspace workspace, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Scene.Open";
    public override string Text => "Open Scene...";
    public override string? Icon => Icons.Open;
    public override KeyGesture? Gesture { get; } = new(Key.O, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project || dialogs.ShowOpenFileDialog("Griff scenes (*.scene)|*.scene") is not { } chosen)
            return;

        // A scene's name or its path in Assets will do, as well as a full path.
        var path = Path.IsPathRooted(chosen) && File.Exists(chosen) ? chosen : project.FindScene(chosen);
        if (path is null)
        {
            dialogs.ShowError("Open Scene", $"The project has no scene \"{chosen}\".");
            return;
        }

        await workspace.OpenSceneAsync(path);
    }
}

/// <summary>Makes the open scene the one the game starts in (when played from a menu or built).</summary>
public sealed class SetStartSceneCommand(IWorkspace workspace) : EditorCommand
{
    public override string Id => "Scene.SetStart";
    public override string Text => "Set as S_tart Scene";
    public override string? Icon => Icons.Play;
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 3);

    protected override bool CanExecuteCore() => workspace.EditScene is { } scene && workspace.Project is { } project
        && !string.Equals(project.StartScenePath, scene.FilePath, StringComparison.OrdinalIgnoreCase);

    protected override Task ExecuteCoreAsync() =>
        workspace.EditScene is { } scene ? workspace.SetStartSceneAsync(scene.FilePath) : Task.CompletedTask;
}
