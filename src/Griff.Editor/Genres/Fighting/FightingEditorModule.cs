using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Play;
using Griff.Editor.Themes;
using Griff.Modules.Fighting;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Genres.Fighting;

// Fighting games: the Move List, for a fighter's moves; the hitboxes and hurtboxes in the Clip Editor; and Game > Show
// Hitboxes, over the running games.
public sealed class FightingEditorModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<MoveListEditor>();
        services.AddSingleton<IClipOverlayMaker, HitboxOverlayMaker>();
        services.AddCommand<OpenMoveListCommand>()
            .AddCommand<ShowHitboxesCommand>();
    }
}

/// <summary>
/// Shows every hitbox and hurtbox in play over the running game (a Hitboxes' boxes on the frame each entity shows):
/// hitboxes red, hurtboxes blue, yellow while in hitstun, and hitboxes that have hit already this swing faint. Again
/// hides them. It stays on for the games played after. (The Fighting module's debug drawing, "Hitboxes".)
/// </summary>
public sealed class ShowHitboxesCommand(PlayMode play, IStatusService status) : EditorCommand
{
    public override string Id => "Game.ShowHitboxes";
    public override string Text => "Show _Hitboxes";
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 7);

    public override bool? IsChecked => play.IsDrawing(FightingGame.HitboxesDrawing);

    protected override Task ExecuteCoreAsync()
    {
        var show = !play.IsDrawing(FightingGame.HitboxesDrawing);
        play.ShowDrawing(FightingGame.HitboxesDrawing, show);
        status.Show(show ? "Showing hitboxes (red) and hurtboxes (blue) in play" : "Hitboxes hidden");
        return Task.CompletedTask;
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
