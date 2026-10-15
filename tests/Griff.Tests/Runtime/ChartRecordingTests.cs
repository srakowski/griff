using Griff.Audio;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Rhythm;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Recording a rhythm chart by tapping along to its song: taps made notes at the beat the player heard (their audio offset
/// allowed for), snapped to a grid, holds from keys held down; takes put into a chart table over what's there or in place
/// of it; notes added, moved, made holds and taken out at one difficulty; and what the Chart panel plays.
/// </summary>
public class ChartRecordingTests
{
    private static readonly ChartTiming At120 = new(120, 4, 0);

    [Fact]
    public void Taps_AreNotesAtTheBeatHeard_SnappedToTheGrid()
    {
        // At 120 a beat is half a second: taps a little off the beat land on it.
        ChartTap[] taps = [new(1, 2.02, 2.1), new(2, 2.49, 2.55), new(3, 2.76, 2.8), new(4, 3.13, 3.2)];

        var eighths = ChartRecording.Notes(taps, At120, ChartSnap.Named("1/8"), 0);
        Assert.Equal([(4.0, 1), (5.0, 2), (5.5, 3), (6.5, 4)], eighths.Select(n => (n.Beat, n.Lane)));

        var sixteenths = ChartRecording.Notes(taps, At120, ChartSnap.Named("1/16"), 0);
        Assert.Equal([4.0, 5.0, 5.5, 6.25], sixteenths.Select(n => n.Beat));

        var quarters = ChartRecording.Notes(taps, At120, ChartSnap.Named("1/4"), 0);
        Assert.Equal([4.0, 5.0, 6.0, 6.0], quarters.Select(n => n.Beat));

        // Triplets: a third of a beat apart, kept to four places.
        var triplets = ChartRecording.Notes([new ChartTap(1, 2 + 0.5 / 3, 2.2), new ChartTap(2, 2 + 1.0 / 3, 2.4)], At120, ChartSnap.Named("1/8 triplets"), 0);
        Assert.Equal([4.3333, 4.6667], triplets.Select(n => n.Beat));
        Assert.Equal(6, ChartSnap.Named("1/16 triplets").Divisions);
        Assert.Equal(3, ChartSnap.Named("12th").Divisions);
        Assert.Equal(4, ChartSnap.Named("16").Divisions);

        // A tempo and a first beat of the lanes' clock: 90 a minute, the first beat 0.4 s in.
        var clocked = ChartRecording.Notes([new ChartTap(1, 0.4 + 4 * (60.0 / 90), 3.2)], new ChartTiming(90, 3, 0.4), ChartSnap.Named("1/8"), 0);
        Assert.Equal(4, clocked.Single().Beat);
    }

    [Fact]
    public void Taps_AllowForThePlayersAudioOffset()
    {
        // Heard 200 ms late: a tap 200 ms after the beat reached the speakers is on it.
        var notes = ChartRecording.Notes([new ChartTap(2, 4.2, 4.3), new ChartTap(3, 4.45, 4.5)], At120, ChartSnap.Named("1/16"), 200);
        Assert.Equal([8.0, 8.5], notes.Select(n => n.Beat));

        // Without the offset, the same taps are a beat-and-a-half's worth of sixteenths late.
        Assert.Equal([8.5, 9.0], ChartRecording.Notes([new ChartTap(2, 4.2, 4.3), new ChartTap(3, 4.45, 4.5)], At120, ChartSnap.Named("1/16"), 0).Select(n => n.Beat));
    }

    [Fact]
    public void AKeyHeldDown_IsAHoldToWhereItWasLetGo()
    {
        ChartTap[] taps =
        [
            // Held a beat and a half (two on a grid of whole beats); held just under half a beat (a tap); held from beat 12 to the end of the take.
            new(1, 4.0, 4.76),
            new(2, 5.0, 5.2),
            new(3, 6.0, double.PositiveInfinity),
            // Held exactly half a beat: the shortest hold, a step of the grid at least.
            new(4, 7.0, 7.25),
        ];
        var notes = ChartRecording.Notes(taps, At120, ChartSnap.Named("1/4"), 0);
        Assert.Equal([(8.0, 1, 2.0), (10.0, 2, 0.0), (12.0, 3, 1.0), (14.0, 4, 1.0)], notes.Select(n => (n.Beat, n.Lane, n.Hold)));
        var eighths = ChartRecording.Notes(taps, At120, ChartSnap.Named("1/8"), 0);
        Assert.Equal([1.5, 0, 0.5, 0.5], eighths.Select(n => n.Hold));
    }

    [Fact]
    public void Taps_BeforeWhereRecordingStarted_OrInLanesThereArent_AreLeftOut_AndTwoOnOneBeatAreOne()
    {
        ChartTap[] taps = [new(1, 1.0, 1.1), new(1, 3.99, 4.05), new(5, 4.0, 4.1), new(2, 4.5, 4.6), new(2, 4.52, 5.4), new(0, 4.5, 4.6)];
        var notes = ChartRecording.Notes(taps, At120, ChartSnap.Named("1/8"), 0, from: 7.9, lanes: 4);
        // From beat 8: the tap at beat 2 was in the bar before (the lead-in), lane 5 and lane 0 aren't there, and the two at
        // beat 9 in lane 2 are one, the hold.
        Assert.Equal([(8.0, 1, 0.0), (9.0, 2, 2.0)], notes.Select(n => (n.Beat, n.Lane, n.Hold)));
    }

    [Fact]
    public void ATake_GoesIntoTheChart_OverWhatsThere_OrInItsPlace()
    {
        var chart = GrooveSong.Chart();
        var normal = Normal(chart);
        Assert.Equal(16, normal.Count(n => n.Beat < 20));

        // Overdub: a note added in bar 2 where there was none; one where there was one already stays one.
        var take = new List<ChartNote> { new(4.5, 4, 0), new(4, normal.First(n => n.Beat == 4).Lane, 0) };
        var (over, added, removed) = ChartTable.Record(chart, take, "Normal", replace: false, 4, 8);
        Assert.Equal((1, 0), (added, removed));
        Assert.Equal(normal.Count + 1, Normal(over).Count);
        Assert.Contains(new ChartNote(4.5, 4, 0), Normal(over));
        // Easy and Hard are as they were.
        Assert.Equal(Notes(chart, "Hard"), Notes(over, "Hard"));
        Assert.Equal(Notes(chart, "Easy"), Notes(over, "Easy"));

        // Replace: bars 2 and 3 of Normal (beats 4 to 11) are the take's notes and nothing else.
        var (replaced, added2, removed2) = ChartTable.Record(chart, [new ChartNote(4, 1, 2), new ChartNote(6, 2, 0), new ChartNote(6, 3, 0)], "Normal", replace: true, 4, 11.9);
        Assert.Equal((3, 8), (added2, removed2));
        Assert.Equal([new ChartNote(4, 1, 2), new ChartNote(6, 2, 0), new ChartNote(6, 3, 0)], Normal(replaced).Where(n => n.Beat is >= 4 and < 12));
        Assert.Equal(normal.Where(n => n.Beat >= 12), Normal(replaced).Where(n => n.Beat >= 12));
        Assert.Equal(Notes(chart, "Hard"), Notes(replaced, "Hard"));

        // The new rows are named for the difficulty, after the notes before them, and the file reads back the same.
        var rows = replaced.Rows.Select(r => r.Name).ToList();
        Assert.Contains("Normal 59", rows);
        Assert.True(rows.IndexOf("Normal 59") > rows.FindLastIndex(r => r.StartsWith("Easy", StringComparison.Ordinal)) && rows.IndexOf("Normal 59") < rows.IndexOf("Normal 9"));
        var again = TableFile.Read(TableFile.Write(replaced), "Groove");
        Assert.Equal(Normal(replaced), Normal(again));
        Assert.Empty(RhythmChart.Read(again, "Normal", 4).Problems);
    }

    [Fact]
    public void ANoteInSeveralDifficulties_IsChangedInTheOneBeingEdited_Only()
    {
        var chart = Chart((4, 1, 0, null), (6, 2, 0, "Normal, Hard"), (8, 3, 0, "Hard"));

        // Moving the note in every difficulty in Hard: it stays where it was for Normal, and moves in Hard alone.
        var (moved, row) = ChartTable.Change(chart, "Note 1", new ChartNote(5, 1, 0), "Hard");
        Assert.Equal([new ChartNote(4, 1, 0), new ChartNote(6, 2, 0)], Notes(moved, "Normal"));
        Assert.Equal([new ChartNote(5, 1, 0), new ChartNote(6, 2, 0), new ChartNote(8, 3, 0)], Notes(moved, "Hard"));
        Assert.Equal("Hard 1", row);

        // Removing the Normal-and-Hard note from Normal leaves it in Hard.
        var removed = ChartTable.Remove(chart, "Note 2", "Normal");
        Assert.Equal([new ChartNote(4, 1, 0)], Notes(removed, "Normal"));
        Assert.Contains(new ChartNote(6, 2, 0), Notes(removed, "Hard"));
        Assert.Equal("Hard", removed["Note 2"]["Difficulty"].ToString());

        // A note only in Hard is changed where it is: a hold now.
        var (held, same) = ChartTable.Change(chart, "Note 3", new ChartNote(8, 3, 1.5), "Hard");
        Assert.Equal(("Note 3", 1.5), (same, held["Note 3"]["Hold"].AsNumber));

        // In a chart whose Difficulty is a choice, a note in every one stays a row for each of the others.
        var groove = ChartTable.Add(ChartTable.Create("Song"), new ChartNote(4, 1, 0), "").Table;
        groove = ChartTable.Add(groove, new ChartNote(5, 2, 0), "Hard").Table;
        var (split, _) = ChartTable.Change(groove, "Note 1", new ChartNote(4, 2, 0), "Normal");
        Assert.Equal([new ChartNote(4, 2, 0)], Notes(split, "Normal"));
        Assert.Equal([new ChartNote(4, 1, 0)], Notes(split, "Easy"));
        Assert.Equal([new ChartNote(4, 1, 0), new ChartNote(5, 2, 0)], Notes(split, "Hard"));
        Assert.All(split.Rows, r => Assert.True(r["Difficulty"].IsEmpty || r["Difficulty"].Kind == TableValueKind.Text));
    }

    [Fact]
    public void Adding_PutsInTheColumnsANoteNeeds()
    {
        // A table with only a Beat and a Lane: a hold gets a Hold column, a difficulty a Difficulty column naming it.
        var bare = new Table("Bare", [new TableColumn("Beat"), new TableColumn("Lane", ColumnType.WholeNumber)], []);
        var (table, row) = ChartTable.Add(bare, new ChartNote(2, 1, 0), "");
        Assert.Equal(("Note 1", 2), (row, table.Columns.Count));
        (table, row) = ChartTable.Add(table, new ChartNote(3, 2, 1), "Expert");
        Assert.Equal(["Beat", "Lane", "Hold", "Difficulty"], table.Columns.Select(c => c.Name));
        Assert.Equal("Expert 1", row);
        Assert.Contains("Expert", RhythmChart.Difficulties(table));
        Assert.Equal([new ChartNote(2, 1, 0), new ChartNote(3, 2, 1)], Notes(table, "Expert"));

        // A formula's Beat can't be written.
        var formula = new Table("Worked", [new TableColumn("Bar"), new TableColumn("Beat") { Formula = "=Bar * 4" }, new TableColumn("Lane", ColumnType.WholeNumber)], []);
        Assert.Contains("formula", ChartTable.WhyNotEditable(formula));
        Assert.Null(ChartTable.WhyNotEditable(table));
        Assert.False(ChartTable.IsChart(new Table("Items", [new TableColumn("Damage")], [])));
    }

    [Fact]
    public void ARecordedTake_PlaysBackPerfect_PressedWhenItWasTapped()
    {
        // A player 150 ms behind their speakers taps along, a little off now and then; the take, snapped to eighths, is the
        // chart, and pressing at the same moments in the game (their offset the same) is Perfect every time.
        double[] beats = [4, 5, 5.5, 6, 7, 8.5, 9, 10];
        var timing = new ChartTiming(120, 4, 0);
        var jitter = new[] { 0.012, -0.018, 0.004, 0.021, -0.009, 0.015, -0.02, 0 };
        var taps = beats.Select((b, i) => new ChartTap(i % 4 + 1, timing.SecondsAt(b) + 0.15 + jitter[i], timing.SecondsAt(b) + 0.25)).ToList();
        var notes = ChartRecording.Notes(taps, timing, ChartSnap.Named("1/8"), 150, from: 4);
        Assert.Equal(beats, notes.Select(n => n.Beat));
        var chart = ChartTable.Record(ChartTable.Create("Take"), notes, "Normal", replace: false, 4, 10).Table;

        var scene = new Scene();
        var entity = new Entity("Lanes");
        entity.AddComponent<BeatClock>().Tempo = 120;
        var lanes = entity.AddComponent<NoteLanes>();
        lanes.Chart = "Take";
        scene.Add(entity);
        var input = new InputState();
        var game = new Game(scene, input) { Audio = new SilentAudio(), Tables = new TableSet([chart]), AudioOffset = 150 };
        var clock = entity.GetComponent<BeatClock>()!;
        Key[] keys = [Key.D, Key.F, Key.J, Key.K];
        for (var i = 0; i < beats.Length; i++)
        {
            while (clock.Beats < beats[i] - 2.0 / 60 - 1e-6)
                Tick(game, input);
            input.SetKey(keys[i % 4], true);
            Tick(game, input);
            input.SetKey(keys[i % 4], false);
            Tick(game, input);
        }

        Assert.Equal((8, 8, 0), (lanes.Notes, lanes.Perfects, lanes.Misses));
    }

    [Fact]
    public void WhatThePanelPlays_HasTheSongFromWhereItStarts_ClicksOnTheBeats_AndTicksOnTheNotes()
    {
        // A second of song: each sample its own number, so where it's playing from shows.
        var rate = 1000;
        var song = new SoundClip([.. Enumerable.Range(0, rate * 4).Select(i => i / 100000f)], 1, rate);
        var timing = new ChartTiming(120, 4, 0);

        // From the start after a second of lead-in (two beats): a click on each of its beats, then the song from its start.
        var stream = new ChartPlaybackStream(song, timing, from: 0, leadIn: 1, volume: 1);
        Assert.Equal(-1, stream.SongSecondsAt(0));
        var buffer = new float[rate * 2];
        Assert.Equal(buffer.Length, stream.Read(buffer));
        Assert.True(buffer[0] > 0.5 && buffer[500] > 0.5);
        Assert.Equal(0, buffer[499]);
        // With the metronome off, past the lead-in it's the song alone.
        Assert.Equal(song.Samples[300], buffer[1300], 5);

        // From part way in after a bar to get ready: the song itself from a bar before.
        var preroll = new ChartPlaybackStream(song, timing, from: 3, leadIn: 2, volume: 1);
        preroll.Read(buffer);
        Assert.Equal(song.Samples[1000 + 200], buffer[200], 5);

        // With the metronome, a click starts on each beat's very sample; ticks on the notes.
        var clicks = new ChartPlaybackStream(null, timing, from: 0) { Metronome = true, Ticks = [0.25] };
        var heard = new float[rate * 2 * 44];
        clicks.Read(heard);
        var perBeat = (int)(0.5 * Mixer.SampleRate);
        Assert.Equal(0, heard[perBeat - 1]);
        Assert.True(heard[perBeat] > 0.3);
        Assert.True(Math.Abs(heard[perBeat / 4 + 1]) > 0.2);
        Assert.Equal(-1, clicks.Frames);

        // A song's stream ends a moment after the song.
        var ending = new ChartPlaybackStream(song, timing, from: 3.5);
        Assert.Equal(500 + rate / 4, ending.Frames);
    }

    // ----- Helpers

    private static List<ChartNote> Normal(Table chart) => Notes(chart, "Normal");

    private static List<ChartNote> Notes(Table chart, string difficulty) =>
        [.. ChartTable.Rows(chart, difficulty).Where(r => r.InDifficulty).Select(r => r.Note).OrderBy(n => n.Beat).ThenBy(n => n.Lane)];

    private static Table Chart(params (double Beat, int Lane, double Hold, string? Difficulty)[] notes)
    {
        var columns = new[]
        {
            new TableColumn("Beat"), new TableColumn("Lane", ColumnType.WholeNumber), new TableColumn("Hold"), new TableColumn("Difficulty", ColumnType.Text),
        };
        var rows = notes.Select((n, i) => ($"Note {i + 1}", (IReadOnlyList<TableValue>)
        [
            TableValue.Of(n.Beat), TableValue.Of((long)n.Lane), n.Hold > 0 ? TableValue.Of(n.Hold) : TableValue.Empty, TableValue.Of(n.Difficulty),
        ]));
        return new Table("Chart", columns, rows);
    }

    private static void Tick(Game game, InputState input)
    {
        game.Tick(1 / 60f);
        input.EndFrame();
    }
}
