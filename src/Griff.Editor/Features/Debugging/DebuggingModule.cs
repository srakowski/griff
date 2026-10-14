using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Debugging;

// Debug menu: breakpoints in scripts. Resuming from one is the Game menu's Pause / Resume (F6).
public sealed class DebuggingModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Debugger>();
        services.AddMenu(Menus.Debug, "_Debug", order: 5);
        services
            .AddCommand<ToggleBreakpointCommand>()
            .AddCommand<RemoveAllBreakpointsCommand>();
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
