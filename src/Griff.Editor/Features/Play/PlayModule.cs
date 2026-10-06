using System.Windows.Input;
using Griff.Editor.Core.Commands;
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
        services.AddMenu(Menus.Game, "_Game", order: 4);
        services
            .AddCommand<PlayCommand>()
            .AddCommand<PauseCommand>()
            .AddCommand<StepCommand>()
            .AddCommand<StopCommand>();
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
