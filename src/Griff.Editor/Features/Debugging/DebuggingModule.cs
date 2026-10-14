using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Themes;
using Griff.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Debugging;

// Debug menu: breakpoints in scripts, and from one, Continue (as the Game menu's Pause / Resume, F6, does) and stepping.
public sealed class DebuggingModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Debugger>();
        services.AddMenu(Menus.Debug, "_Debug", order: 5);
        services
            .AddCommand<ToggleBreakpointCommand>()
            .AddCommand<RemoveAllBreakpointsCommand>()
            .AddCommand<ContinueCommand>()
            .AddCommand<StepOverCommand>()
            .AddCommand<StepIntoCommand>()
            .AddCommand<StepOutCommand>();
    }
}

/// <summary>
/// Puts a breakpoint on the line the caret is on in the script showing, or takes off the one there: the game pauses when a
/// script gets to that line, showing the values in scope, until Resume (F6). Works while the game runs too. An agent sets
/// one on any line with the Debugger's SetBreakpoint (ui_invoke).
/// </summary>
public sealed class ToggleBreakpointCommand(Debugger debugger, ScriptEditors editors, IStatusService status) : EditorCommand
{
    public override string Id => "Debug.ToggleBreakpoint";
    public override string Text => "Toggle _Breakpoint";
    public override string? Icon => Icons.Record;
    public override KeyGesture? Gesture { get; } = new(Key.F9);
    public override MenuPlacement? Placement { get; } = new(Menus.Debug, Group: 0, Order: 0);

    protected override bool CanExecuteCore() => editors.Active is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (editors.Active is not { } script)
            return Task.CompletedTask;
        try
        {
            debugger.ToggleBreakpoint(script.FilePath, script.CaretLine);
        }
        catch (ArgumentException ex)
        {
            status.Show(ex.Message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Takes off every breakpoint in the project's scripts.</summary>
public sealed class RemoveAllBreakpointsCommand(Debugger debugger) : EditorCommand
{
    public override string Id => "Debug.RemoveAllBreakpoints";
    public override string Text => "_Remove All Breakpoints";
    public override string? Icon => Icons.Clear;
    public override KeyGesture? Gesture { get; } = new(Key.F9, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Debug, Group: 0, Order: 1);

    protected override bool CanExecuteCore() => debugger.Breakpoints.Count > 0;

    protected override Task ExecuteCoreAsync()
    {
        debugger.RemoveAllBreakpoints();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Lets the game carry on from the line it's stopped at (or from a pause), until it reaches a breakpoint again: the same as
/// Resume (F6).
/// </summary>
public sealed class ContinueCommand(Debugger debugger, PlayMode play) : EditorCommand
{
    public override string Id => "Debug.Continue";
    public override string Text => "_Continue";
    public override string? Icon => Icons.Play;
    public override KeyGesture? Gesture { get; } = new(Key.F5);
    public override MenuPlacement? Placement { get; } = new(Menus.Debug, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => play.IsPlaying && play.IsPaused;

    protected override Task ExecuteCoreAsync()
    {
        debugger.Continue();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stopped at a line: runs it and stops at the next line of the same method, without stopping in the methods it calls
/// (unless a breakpoint is there). At the method's end it stops at the next line of the script that called it; a method
/// Griff called (Update, say) returns into Griff, and the game stops at the next line of any script that runs, or pauses
/// at the end of the frame if none does. Returns once the step is over, saying where it stopped and what's in scope.
/// </summary>
public sealed class StepOverCommand(Debugger debugger) : EditorCommand
{
    public override string Id => "Debug.StepOver";
    public override string Text => "Step _Over";
    public override string? Icon => Icons.StepOver;
    public override KeyGesture? Gesture { get; } = new(Key.F10);
    public override MenuPlacement? Placement { get; } = new(Menus.Debug, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => debugger.CanStep(StepKind.Over);

    protected override Task ExecuteCoreAsync() => debugger.StepOver();
}

/// <summary>
/// Stopped at a line: stops at the next line of script that runs, going into the methods it calls in this script or
/// another (and on through Griff to a script it runs, such as a spawned thing's Start); a call into Griff that runs no
/// script is stepped over. Paused between frames, plays the next frame up to the first line of script it runs. Returns
/// once the step is over, saying where it stopped and what's in scope.
/// </summary>
public sealed class StepIntoCommand(Debugger debugger) : EditorCommand
{
    public override string Id => "Debug.StepInto";
    public override string Text => "Step _Into";
    public override string? Icon => Icons.StepInto;
    public override KeyGesture? Gesture { get; } = new(Key.F11);
    public override MenuPlacement? Placement { get; } = new(Menus.Debug, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => debugger.CanStep(StepKind.Into);

    protected override Task ExecuteCoreAsync() => debugger.StepInto();
}

/// <summary>
/// Stopped at a line: runs the rest of its method and stops at the next line of the script that called it. A method Griff
/// called returns into Griff: the game stops at the next line of any script that runs, or pauses at the end of the frame
/// if none does. Returns once the step is over, saying where it stopped and what's in scope.
/// </summary>
public sealed class StepOutCommand(Debugger debugger) : EditorCommand
{
    public override string Id => "Debug.StepOut";
    public override string Text => "Step Ou_t";
    public override string? Icon => Icons.StepOut;
    public override KeyGesture? Gesture { get; } = new(Key.F11, ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Debug, Group: 1, Order: 3);

    protected override bool CanExecuteCore() => debugger.CanStep(StepKind.Out);

    protected override Task ExecuteCoreAsync() => debugger.StepOut();
}
