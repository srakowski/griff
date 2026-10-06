using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Scenes;

public sealed class ScenesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddCommand<SaveSceneCommand>();
}

public sealed class SaveSceneCommand(IWorkspace workspace) : EditorCommand
{
    public override string Id => "Scene.Save";
    public override string Text => "_Save Scene";
    public override string? Icon => Icons.Save;
    public override KeyGesture? Gesture { get; } = new(Key.S, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 1, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 0, Order: 1);

    protected override bool CanExecuteCore() => workspace.Scene is not null;

    protected override Task ExecuteCoreAsync() => workspace.SaveSceneAsync();
}
