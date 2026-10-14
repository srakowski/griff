using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Profiler;

public sealed class ProfilerModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPanel<ProfilerPanel, ProfilerPanelView>();
        services
            .AddCommand<ShowProfilerCommand>()
            .AddCommand<RecordProfilerCommand>()
            .AddCommand<ClearProfilerCommand>();
    }
}

/// <summary>
/// Shows the Profiler panel, which records how the game playing in the Game tab runs while it's on screen: a graph of the
/// last ten seconds of frames (each split into the game's steps, building the picture and drawing it), the frame rate
/// and its 1% low, and how many entities the scene holds and how many of each component the active ones have. Read
/// what it shows with ui_inspect on ProfilerPanel (Summary, Counts).
/// </summary>
public sealed class ShowProfilerCommand(PanelLayout layout) : EditorCommand
{
    public override string Id => "Game.Profiler";
    public override string Text => "_Profiler";
    public override string? Icon => Icons.Profiler;
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 5);

    protected override Task ExecuteCoreAsync()
    {
        layout.Show("Profiler");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Pauses the Profiler's recording, holding its graph and numbers still to look at while the game plays on; again
/// records once more.
/// </summary>
public sealed class RecordProfilerCommand(ProfilerPanel profiler) : EditorCommand
{
    public override string Id => "Profiler.Record";
    public override string Text => "_Record";
    public override string? Icon => Icons.Record;

    public override bool? IsChecked => profiler.IsRecording;

    protected override Task ExecuteCoreAsync()
    {
        profiler.IsRecording = !profiler.IsRecording;
        return Task.CompletedTask;
    }
}

/// <summary>Forgets the frames the Profiler has recorded, starting its graph and numbers afresh.</summary>
public sealed class ClearProfilerCommand(ProfilerPanel profiler) : EditorCommand
{
    public override string Id => "Profiler.Clear";
    public override string Text => "_Clear";
    public override string? Icon => Icons.Clear;

    protected override Task ExecuteCoreAsync()
    {
        profiler.Clear();
        return Task.CompletedTask;
    }
}
