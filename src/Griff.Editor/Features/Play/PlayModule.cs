using System.Windows.Input;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Play;

// Game menu: play controls. Toolbar group 2 sits after undo/redo.
public sealed class PlayModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<PlayMode>();
        services.AddSingleton<TimelineBar>();
        services.AddMenu(Menus.Game, "_Game", order: 4);
        services
            .AddCommand<PlayCommand>()
            .AddCommand<PauseCommand>()
            .AddCommand<StepCommand>()
            .AddCommand<StopCommand>()
            .AddCommand<KeepPlayChangesCommand>();
    }
}

public sealed class PlayCommand(PlayMode play, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Game.Play";
    public override string Text => "_Play";
    public override string? Icon => Icons.Play;
    public override KeyGesture? Gesture { get; } = new(Key.F5);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 2, Order: 0);

    protected override bool CanExecuteCore() => !play.IsPlaying && workspace.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        play.Play();
        return Task.CompletedTask;
    }
}

public sealed class PauseCommand(PlayMode play) : EditorCommand
{
    public override string Id => "Game.Pause";
    public override string Text => "P_ause / Resume";
    public override string? Icon => Icons.Pause;
    public override KeyGesture? Gesture { get; } = new(Key.F6);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 1);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 2, Order: 1);

    protected override bool CanExecuteCore() => play.IsPlaying;

    protected override Task ExecuteCoreAsync()
    {
        play.TogglePause();
        return Task.CompletedTask;
    }
}

public sealed class StepCommand(PlayMode play) : EditorCommand
{
    public override string Id => "Game.Step";
    public override string Text => "_Step Frame";
    public override string? Icon => Icons.Step;
    public override KeyGesture? Gesture { get; } = new(Key.F10);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 2);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 2, Order: 2);

    protected override bool CanExecuteCore() => play.IsPlaying;

    protected override Task ExecuteCoreAsync()
    {
        play.Step();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Copies the selected entity's live values into the edit scene, so tuning done while playing survives Stop.
/// One undo step in the edit scene's history.
/// </summary>
public sealed class KeepPlayChangesCommand(IWorkspace workspace, ISelection selection, IStatusService status) : EditorCommand
{
    public override string Id => "Game.KeepChanges";
    public override string Text => "_Keep Play Changes";
    public override string? Icon => Icons.Save;
    public override KeyGesture? Gesture { get; } = new(Key.K, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 4);

    protected override bool CanExecuteCore() => workspace.IsPlaying && selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (selection.Entity is not { } live || workspace.EditScene is not { } edit)
            return Task.CompletedTask;

        if (edit.Scene.Find(live.Id) is not { } target)
        {
            status.Show($"{live.Name} was created while playing, so there's nothing in the scene to update");
            return Task.CompletedTask;
        }

        var changes = CopyValues.Between(live, target, workspace.Components);
        using (edit.History.BeginGroup($"Keep play changes to {live.Name}"))
        {
            foreach (var change in changes)
                edit.Execute(change);
        }

        status.Show(changes.Count == 0 ? $"{live.Name} has no play changes" : $"Kept {changes.Count} change(s) to {live.Name}");
        return Task.CompletedTask;
    }
}

public sealed class StopCommand(PlayMode play) : EditorCommand
{
    public override string Id => "Game.Stop";
    public override string Text => "S_top";
    public override string? Icon => Icons.Stop;
    public override KeyGesture? Gesture { get; } = new(Key.F5, ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 3);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 2, Order: 3);

    protected override bool CanExecuteCore() => play.IsPlaying;

    protected override Task ExecuteCoreAsync()
    {
        play.Stop();
        return Task.CompletedTask;
    }
}
