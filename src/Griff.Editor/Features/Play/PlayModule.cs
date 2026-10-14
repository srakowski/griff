using System.IO;
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
            .AddCommand<KeepPlayChangesCommand>()
            .AddCommand<ClearSaveDataCommand>()
            .AddCommand<PlayWithTwoCommand>()
            .AddCommand<PlayWithThreeCommand>()
            .AddCommand<PlayWithFourCommand>()
            .AddCommand<SlowConnectionCommand>()
            .AddCommand<StepRateCommand>();
    }
}

/// <summary>Plays the open scene in the Game tab, on a copy: Stop puts everything back as it was.</summary>
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

/// <summary>
/// Pauses the running game, or resumes it. While paused, the timeline rewinds up to the last 20 seconds. Stopped at a
/// breakpoint, resuming lets the script carry on from that line.
/// </summary>
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

/// <summary>Advances the paused game by one frame (not while it's stopped at a breakpoint, in the middle of one).</summary>
public sealed class StepCommand(PlayMode play) : EditorCommand
{
    public override string Id => "Game.Step";
    public override string Text => "_Step Frame";
    public override string? Icon => Icons.Step;
    public override KeyGesture? Gesture { get; } = new(Key.F10);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 2);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 2, Order: 2);

    protected override bool CanExecuteCore() => play.IsPlaying && !play.IsAtBreakpoint;

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

/// <summary>Stops the game and goes back to the scene as it was before Play.</summary>
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

/// <summary>
/// Plays a multiplayer game with several players at once: the Game tab hosts, and each other player gets a window of their
/// own that joins it, as if on another machine. Click a window to play as that player.
/// </summary>
public abstract class PlayWithPlayersCommand(PlayMode play, IWorkspace workspace, int players) : EditorCommand
{
    public override string Id => $"Game.PlayWith{players}";
    public override string Text => $"Play with _{players} Players";
    public override string? Icon => Icons.Play;
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 1, Order: players);

    protected override bool CanExecuteCore() => !play.IsPlaying && workspace.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        play.Play(players);
        return Task.CompletedTask;
    }
}

/// <summary>Plays as two players at once: the Game tab hosts a network game and player 2 gets a window of their own.</summary>
public sealed class PlayWithTwoCommand(PlayMode play, IWorkspace workspace) : PlayWithPlayersCommand(play, workspace, 2)
{
    public override KeyGesture? Gesture { get; } = new(Key.F5, ModifierKeys.Control);
}

/// <summary>Plays as three players at once: the Game tab hosts and players 2 and 3 get windows of their own.</summary>
public sealed class PlayWithThreeCommand(PlayMode play, IWorkspace workspace) : PlayWithPlayersCommand(play, workspace, 3);

/// <summary>Plays as four players at once: the Game tab hosts and players 2 to 4 get windows of their own.</summary>
public sealed class PlayWithFourCommand(PlayMode play, IWorkspace workspace) : PlayWithPlayersCommand(play, workspace, 4);

/// <summary>Pretends the players of Play with Players are on a slow connection, to feel what lag does. Again turns it off.</summary>
public sealed class SlowConnectionCommand(PlayMode play, IStatusService status) : EditorCommand
{
    public override string Id => "Game.SlowConnection";
    public override string Text => "Pretend a Slow _Connection";
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 1, Order: 9);

    public override bool? IsChecked => play.SlowConnection;

    protected override Task ExecuteCoreAsync()
    {
        play.SlowConnection = !play.SlowConnection;
        status.Show(play.SlowConnection ? "Pretending a slow connection: about 150 ms there and back, a few packets lost" : "Back to a perfect connection");
        return Task.CompletedTask;
    }
}

/// <summary>Deletes what the game saved with Game.SaveData while playing in the editor, to test a first run again.</summary>
[Destructive]
public sealed class ClearSaveDataCommand(IWorkspace workspace, IStatusService status) : EditorCommand
{
    public override string Id => "Game.ClearSaveData";
    public override string Text => "_Clear Save Data";
    public override string? Icon => Icons.Clear;
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 2, Order: 1);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project)
            return Task.CompletedTask;

        var path = Griff.Runtime.SaveData.EditorPath(project.RootPath);
        try
        {
            File.Delete(path);
            status.Show(workspace.IsPlaying ? "Cleared save data; the running game keeps its values until you play again" : "Cleared save data");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Show($"Couldn't clear save data: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
