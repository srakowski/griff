using System.IO;
using System.Windows.Threading;
using Griff.Audio;
using Griff.Data;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Rhythm;
using Griff.Modules.Rhythm;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Chart panel: Note Lanes' chart on a timeline with their song, clock and lanes; takes recorded by tapping along (the
/// player's audio offset allowed for, snapped to the grid, holds from keys held), over what's there or in its place, each
/// one undo step; notes added, moved, held and taken out by hand; and playing from the playhead.
/// </summary>
public sealed class ChartPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A new project with Note Lanes and their song and chart, the lanes selected.
    private (IWorkspace Workspace, ChartPanel Panel, TableStore Tables) Lanes(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        EditorHost.Pump(services.GetRequiredService<CommandRegistry>().Get("Entity.CreateNoteLanes").ExecuteAsync());
        var lanes = workspace.Scene!.Scene.Entities.Single(e => e.GetComponent<NoteLanes>() is not null);
        services.GetRequiredService<ISelection>().Select(lanes);
        return (workspace, services.GetRequiredService<ChartPanel>(), services.GetRequiredService<TableStore>());
    }

    [Fact]
    public void SelectingNoteLanes_ShowsTheirChart_WithTheirSongClockAndLanes() => EditorHost.Run(services =>
    {
        var (_, panel, _) = Lanes(services);
        EditorHost.Pump(services.GetRequiredService<CommandRegistry>().Get("Rhythm.EditChart").ExecuteAsync());
        Assert.Equal("Chart", services.GetRequiredService<PanelLayout>().Panels.Single(p => p is ChartPanel).Id);

        Assert.Null(panel.Notice);
        Assert.Equal(("Groove", "Music/Groove.wav", "Normal", "Note Lanes"), (panel.ChartName, panel.Song, panel.Difficulty, panel.LanesEntity));
        Assert.Equal(["Groove"], panel.ChartNames);
        Assert.Equal(new ChartTiming(120, 4, 0), panel.Timing);
        Assert.Equal("120 BPM, 4/4 from the Beat Clock on Note Lanes", panel.TimingText);
        Assert.Equal(4, panel.LaneCount);
        Assert.Equal("D, F, J, K", panel.LaneKeys);
        Assert.Equal([1, 2, 3, 4], new[] { Griff.Input.Key.D, Griff.Input.Key.F, Griff.Input.Key.J, Griff.Input.Key.K }.Select(panel.LaneForKey));
        Assert.Equal(0, panel.LaneForKey(Griff.Input.Key.Q));
        Assert.Equal(58, panel.NoteCount);
        Assert.Equal("Normal 1: beat 4, lane 4", panel.NoteLines[0]);
        Assert.Equal("Groove in Normal: 58 notes over 4 lanes, bars 2 to 16, 5 holds, 4 chords.", panel.Summary);
        // The song, 16 bars at 120: 32 seconds, 64 beats, drawn from its peaks.
        Assert.Equal(64, panel.LengthInBeats);
        Assert.InRange(panel.SongPeaks.Length, 3190, 3400);
        Assert.Equal(["Easy", "Normal", "Hard"], panel.Difficulties);

        panel.Difficulty = "Hard";
        Assert.Equal(Notes(GrooveSong.Chart(), "Hard").Count, panel.NoteCount);
        Assert.Equal(panel.Rows.Count, GrooveSong.Chart().Rows.Count);
    });

    [Fact]
    public void Notes_AreAddedMovedHeldAndTakenOut_EachAnUndoStepOfTheChart() => EditorHost.Run(services =>
    {
        var (_, panel, tables) = Lanes(services);
        var commands = services.GetRequiredService<CommandRegistry>();
        var notes = panel.NoteCount;

        var row = panel.AddNote(4.5, 3);
        Assert.Equal("Normal 59", row);
        Assert.Equal(notes + 1, panel.NoteCount);
        Assert.Equal(row, panel.SelectedNote);
        Assert.Contains("Normal 59: beat 4.5, lane 3", panel.NoteLines);
        Assert.Throws<InvalidOperationException>(() => panel.AddNote(4.5, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.AddNote(5, 9));

        panel.MoveNote(row, 4.75, 4);
        panel.SetHold(row, 1.25);
        Assert.Equal("Normal 59: beat 4.75, lane 4, hold 1.25", panel.SelectedText);
        Assert.Equal(row, panel.NoteAt(4.75, 4));
        // The table has it, saved to its file.
        var table = tables.Find("Groove")!;
        Assert.Equal((4.75, 4L, 1.25, "Normal"), (table[row]["Beat"].AsNumber, table[row]["Lane"].AsWholeNumber, table[row]["Hold"].AsNumber, table[row]["Difficulty"].ToString()));
        Assert.Contains("Normal 59", File.ReadAllText(Path.Combine(services.GetRequiredService<IWorkspace>().Project!.AssetsPath, "Tables", "Groove.table")));

        panel.RemoveNote("Normal 1");
        Assert.Equal(notes, panel.NoteCount);
        Assert.Null(panel.NoteAt(4, 4));

        // Each was a step of undo, by Edit > Undo (the chart was worked in last).
        Assert.Equal("Remove the note at beat 4", panel.UndoDescription);
        EditorHost.Pump(commands.Get("Edit.Undo").ExecuteAsync());
        Assert.Equal("Normal 1", panel.NoteAt(4, 4));
        EditorHost.Pump(commands.Get("Edit.Undo").ExecuteAsync());
        EditorHost.Pump(commands.Get("Edit.Undo").ExecuteAsync());
        Assert.Equal(notes + 1, panel.NoteCount);
        Assert.Equal(row, panel.NoteAt(4.5, 3));
        EditorHost.Pump(commands.Get("Edit.Undo").ExecuteAsync());
        Assert.Equal(notes, panel.NoteCount);
        Assert.Equal(GrooveSong.Chart().Rows.Count, tables.Find("Groove")!.Rows.Count);
        EditorHost.Pump(commands.Get("Edit.Redo").ExecuteAsync());
        Assert.Equal(notes + 1, panel.NoteCount);
    });

    [Fact]
    public void ATake_IsTapsAtTheBeatsHeard_OverWhatsThere_OrInItsPlace_OneUndoStep() => EditorHost.Run(services =>
    {
        var (_, panel, tables) = Lanes(services);
        panel.AudioOffset = 120;
        panel.Snap = "1/16";
        panel.Difficulty = "Hard";
        var hard = panel.NoteCount;

        // From bar 3 (beat 8, 4 s in): taps a player 120 ms behind their speakers made on beats 8, 8.25 and 9 (held to 10.5).
        panel.Playhead = 8;
        Assert.Equal("Bar 3, beat 1 (8)", panel.PlayheadText);
        panel.RecordMode = "Replace";
        var said = panel.RecordTake("1@4.13, 2@4.245, 4@4.62-5.37");
        var replaced = Notes(GrooveSong.Chart(), "Hard").Count(n => n.Beat is >= 8 and <= 10.5);
        Assert.Equal($"Take 1: 3 notes in Hard from bar 3 to bar 3, in place of {replaced}. Undo takes it out.", said);
        Assert.Equal(["8, 1, 0", "8.25, 2, 0", "9, 4, 1.5"], panel.Rows.Where(r => r.InDifficulty && r.Note.Beat is >= 8 and <= 10.5).OrderBy(r => r.Note.Beat)
            .Select(r => $"{ChartTable.Text(r.Note.Beat)}, {r.Note.Lane}, {ChartTable.Text(r.Note.Hold)}"));
        Assert.Equal(hard - replaced + 3, panel.NoteCount);
        // The other difficulties are as they were.
        Assert.Equal(Notes(GrooveSong.Chart(), "Normal"), Notes(tables.Find("Groove")!, "Normal"));

        // One undo step takes the whole take out.
        Assert.Equal("Record take 1 (3 notes in Hard)", panel.UndoDescription);
        Assert.Contains("chart", EditorHost.AgentUndo(services));
        Assert.Equal(hard, panel.NoteCount);

        // Overdub adds to what's there: a note already there stays one.
        panel.RecordMode = "Overdub";
        var first = Notes(GrooveSong.Chart(), "Hard").First(n => n.Beat == 8);
        said = panel.RecordTake($"{first.Lane}@4.12, 3@4.37");
        Assert.Equal(hard + 1, panel.NoteCount);
        Assert.Equal("Take 2: 1 note in Hard from bar 3 to bar 3 (1 was there already). Undo takes it out.", said);
        Assert.Throws<FormatException>(() => panel.RecordTake("lane one at four"));
    });

    [Fact]
    public void Recording_PlaysFromABarBeforeThePlayhead_AndStoppingPutsTheTakeIn() => EditorHost.Run(services =>
    {
        var (_, panel, _) = Lanes(services);
        var commands = services.GetRequiredService<CommandRegistry>();
        // The song plays through a mixer of the test's own, whose clock moves only when the test moves it: what's heard is
        // where that clock says (the editor's goes by real time), so a busy machine can't make a tap late.
        var seconds = 100.0;
        panel.Output = new Mixer { Clock = () => seconds };
        panel.Playhead = 16;
        var notes = panel.NoteCount;

        EditorHost.Pump(commands.Get("Rhythm.RecordChart").ExecuteAsync());
        Assert.True(panel.IsRecording);
        Assert.True(panel.IsPlaying);
        Assert.Equal(16, panel.RecordingFrom);
        // The bar before is to get ready: heard from beat 12 (two seconds of it at 120).
        Assert.Equal(12, panel.HeardBeat, 6);
        Assert.False(panel.CanChange);
        Assert.False(EditorHost.Undo(services, panel));
        Assert.Throws<InvalidOperationException>(() => panel.PlayFrom(4));

        // A lane pressed and let go in the lead-in (beats 14 to 14.5) is before the playhead: nothing goes in.
        seconds += 1;
        Assert.Equal(14, panel.HeardBeat, 6);
        panel.Press(1);
        seconds += 0.25;
        panel.Release(1);
        EditorHost.Pump(commands.Get("Rhythm.RecordChart").ExecuteAsync());
        Assert.False(panel.IsRecording);
        Assert.False(panel.IsPlaying);
        Assert.Contains("before the playhead", panel.Message);
        Assert.Equal(16, panel.Playhead);
        Assert.Equal(notes, panel.NoteCount);
        Assert.Null(panel.UndoDescription);

        // Pressed from the playhead on, it goes in when the take is stopped: lane 2 held from beat 17 to 19 is a hold there.
        Assert.Null(panel.NoteAt(17, 2));
        EditorHost.Pump(commands.Get("Rhythm.RecordChart").ExecuteAsync());
        Assert.Equal(12, panel.HeardBeat, 6);
        seconds += 2.5;
        panel.Press(2);
        Assert.Equal(17, panel.HeardBeat, 6);
        seconds += 1;
        panel.Release(2);
        seconds += 0.5;
        EditorHost.Pump(commands.Get("Rhythm.RecordChart").ExecuteAsync());
        Assert.False(panel.IsRecording);
        Assert.StartsWith("Take 1: 1 note in Normal", panel.Message);
        Assert.Equal(notes + 1, panel.NoteCount);
        var take = panel.Rows.Single(r => r.Row == panel.NoteAt(17, 2)).Note;
        Assert.Equal((17.0, 2, 2.0), (take.Beat, take.Lane, take.Hold));
        Assert.Equal(16, panel.Playhead);
        Assert.Equal("Record take 1 (1 note in Normal)", panel.UndoDescription);

        // Play goes from the playhead (no lead-in), and again stops.
        EditorHost.Pump(commands.Get("Rhythm.PlayChart").ExecuteAsync());
        Assert.True(panel.IsPlaying);
        Assert.Equal(16, panel.HeardBeat, 6);
        seconds += 0.5;
        Assert.Equal(17, panel.HeardBeat, 6);
        EditorHost.Pump(commands.Get("Rhythm.PlayChart").ExecuteAsync());
        Assert.False(panel.IsPlaying);
    });

    [Fact]
    public void AChartWorkedOutByAFormula_SaysWhyItCantBeEdited() => EditorHost.Run(services =>
    {
        var (workspace, panel, tables) = Lanes(services);
        var table = new Table("Worked", [new TableColumn("Bar"), new TableColumn("Beat") { Formula = "=Bar * 4" }, new TableColumn("Lane", ColumnType.WholeNumber)],
            [("Note 1", [TableValue.Of(1.0), TableValue.Empty, TableValue.Of(2L)])]);
        File.WriteAllText(Path.Combine(workspace.Project!.AssetsPath, "Tables", "Worked.table"), TableFile.Write(table));
        tables.AssetChanged("Tables/Worked.table");

        Assert.True(panel.Open("Worked"));
        Assert.Contains("formula", panel.Notice);
        Assert.Equal("Note 1: beat 4, lane 2", Assert.Single(panel.NoteLines));
        Assert.Throws<InvalidOperationException>(() => panel.AddNote(8, 1));
        Assert.False(services.GetRequiredService<CommandRegistry>().Get("Rhythm.RecordChart").CanExecute());
        Assert.False(panel.Open("Nothing"));
    });

    private static List<ChartNote> Notes(Table chart, string difficulty) =>
        [.. ChartTable.Rows(chart, difficulty).Where(r => r.InDifficulty).Select(r => r.Note).OrderBy(n => n.Beat).ThenBy(n => n.Lane)];
}
