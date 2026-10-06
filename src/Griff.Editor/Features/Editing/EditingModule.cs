using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Editing;

public sealed class EditingModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddCommand<UndoCommand>()
        .AddCommand<RedoCommand>();
}

public sealed class UndoCommand(IWorkspace workspace) : EditorCommand
{
    public override string Id => "Edit.Undo";
    public override string Text => "_Undo";
    public override string? Icon => Icons.Undo;
    public override KeyGesture? Gesture { get; } = new(Key.Z, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 0, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 1, Order: 0);

    protected override bool CanExecuteCore() => workspace.Scene?.History.CanUndo == true;

    protected override Task ExecuteCoreAsync()
    {
        workspace.Scene?.History.Undo();
        return Task.CompletedTask;
    }
}

public sealed class RedoCommand(IWorkspace workspace) : EditorCommand
{
    public override string Id => "Edit.Redo";
    public override string Text => "_Redo";
    public override string? Icon => Icons.Redo;
    public override KeyGesture? Gesture { get; } = new(Key.Y, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 0, Order: 1);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 1, Order: 1);

    protected override bool CanExecuteCore() => workspace.Scene?.History.CanRedo == true;

    protected override Task ExecuteCoreAsync()
    {
        workspace.Scene?.History.Redo();
        return Task.CompletedTask;
    }
}
