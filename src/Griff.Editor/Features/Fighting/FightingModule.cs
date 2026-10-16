using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Fighting;

// Fighting games: the Move List, for a fighter's moves.
public sealed class FightingEditorModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<MoveListEditor>();
        services.AddCommand<OpenMoveListCommand>();
    }
}

/// <summary>
/// Opens the Move List on the selected fighter's moves: each with its input drawn as arrows and what the motion is
/// called, and for the move picked its name, its motion picked from a list (quarter circles, dragon punches, charges) and
/// button, its clip, its startup, active and recovery frames drawn as a bar with what it leaves on hit and on block, where
/// it must be blocked, and what it cancels into. Each change is one undo step. It follows the selection.
/// </summary>
public sealed class OpenMoveListCommand(MoveListEditor editor, IProjectSession projectSession) : EditorCommand
{
    public override string Id => "Fighting.EditMoves";
    public override string Text => "Edit _Moves...";
    public override string? Icon => Icons.Rename;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 5);

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        editor.Open();
        return Task.CompletedTask;
    }
}
