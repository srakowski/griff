using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Modules.Rhythm;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Genres.Rhythm;

// Rhythm games: the Chart panel, to record a chart by tapping along to its song and edit it on a timeline.
public sealed class RhythmEditorModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPanel<ChartPanel, ChartPanelView>();
        services
            .AddCommand<EditChartCommand>()
            .AddCommand<PlayChartCommand>()
            .AddCommand<RecordChartCommand>();
    }
}

/// <summary>
/// Opens the Chart panel on a rhythm chart (a table of notes: Beat, Lane, Hold, Difficulty), picked by its name, or the
/// selected Note Lanes' chart, with their song, beat clock and lanes: the notes on a timeline in their lanes over the song's
/// beats and bars, to record by tapping along (Rhythm.RecordChart) and to edit by hand. Read and change it with ui_inspect,
/// ui_set and ui_invoke on the Chart panel (ChartName, Difficulty, Snap, RecordMode, Metronome, AudioOffset, Playhead,
/// NoteLines, Summary, AddNote, RemoveNote, MoveNote, SetHold, RecordTake, PlayFrom, Stop...).
/// </summary>
public sealed class EditChartCommand(ChartPanel panel, PanelLayout layout, IProjectSession projectSession, ISelection selection) : EditorCommand
{
    public override string Id => "Rhythm.EditChart";
    public override string Text => "Edit _Chart";
    public override string? Icon => Icons.Music;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 6);
    public override IReadOnlyList<string>? Choices => panel.ChartNames;
    public override string? Chosen => panel.ChartName;

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (selection.Entity?.GetComponent<NoteLanes>() is { } lanes)
            panel.FollowLanes(lanes);
        layout.Show(panel.Id);
        Outcome = panel.Summary;
        return Task.CompletedTask;
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (!panel.Open(choice))
            throw new ArgumentException(panel.Message ?? $"There's no chart called \"{choice}\".", nameof(choice));
        layout.Show(panel.Id);
        Outcome = panel.Summary;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Plays the chart showing in the Chart panel from its playhead (the song, a tick on each note of the difficulty, and clicks
/// on the beats with the metronome on), or stops it. Set the Chart panel's Playhead first to play from elsewhere.
/// </summary>
public sealed class PlayChartCommand(ChartPanel panel, PanelLayout layout) : EditorCommand
{
    public override string Id => "Rhythm.PlayChart";
    public override string Text => "_Play Chart";
    public override string? Icon => Icons.Play;

    public override bool? IsChecked => panel.IsPlaying && !panel.IsRecording;

    protected override bool CanExecuteCore() => panel.HasChart && !panel.IsRecording;

    protected override Task ExecuteCoreAsync()
    {
        if (panel.IsPlaying)
        {
            panel.Stop();
            Outcome = $"Stopped at {panel.PlayheadText}.";
            return Task.CompletedTask;
        }

        layout.Show(panel.Id);
        panel.Play();
        Outcome = $"Playing {panel.ChartName} from {panel.PlayheadText}.";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Starts recording a take into the Chart panel's chart at its difficulty, from its playhead: the song plays from a bar
/// before it, and each lane's key or button pressed is a note at the beat the player heard it (their audio offset allowed
/// for), snapped to the panel's Snap, a key held half a beat or more a hold. Run again (or press Space) to stop: the take
/// goes in over the notes there (Overdub) or in place of them (Replace), as one undo step, and it says what went in. Agents
/// can record a take of taps at given moments with ui_invoke Chart RecordTake.
/// </summary>
public sealed class RecordChartCommand(ChartPanel panel, PanelLayout layout) : EditorCommand
{
    public override string Id => "Rhythm.RecordChart";
    public override string Text => "_Record Chart";
    public override string? Icon => Icons.Record;

    public override bool? IsChecked => panel.IsRecording;

    protected override bool CanExecuteCore() => panel.HasChart && panel.Notice is null;

    protected override Task ExecuteCoreAsync()
    {
        if (panel.IsRecording)
        {
            panel.Stop();
            Outcome = panel.Message;
            return Task.CompletedTask;
        }

        layout.Show(panel.Id);
        panel.Record();
        Outcome = panel.Message;
        return Task.CompletedTask;
    }
}
