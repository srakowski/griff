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
        .AddCommand<NewSceneCommand>();
}

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
