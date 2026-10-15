using System.Globalization;
using Griff.Audio;
using Griff.Data;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Writes a rhythm chart from a tune made in Griff, as a charter would by ear: its notes fall on what the player hears
/// (the melody's notes, and the drums in its gaps), each in the lane its pitch reaches for (higher to the right), a phrase's
/// last note held through the rest after it, and the first beat of each phrase a two-note chord. Easy keeps to the main
/// beats, a note every one or two; Normal follows the melody's eighth notes; Hard plays all of it, its sixteenths and the
/// snare under it too. Every chart is fair: a note in a lane comes at least <see cref="SameLaneGap"/> after the one before
/// it there (so no press can be taken for the wrong one), at most two lanes are pressed at once, and nothing's played in a
/// lane while it's held.
/// </summary>
public static class ChartWriter
{
    /// <summary>The lanes a chart is written for.</summary>
    public const int Lanes = 4;

    /// <summary>The least time, in seconds, between a note in a lane and the next in it (or a hold's end and the next note).</summary>
    public const double SameLaneGap = 0.36;

    /// <summary>The difficulties it writes, easiest first.</summary>
    public static IReadOnlyList<string> Difficulties { get; } = ["Easy", "Normal", "Hard"];

    /// <summary>A note of a chart: its beat (from 0, the tune's first), its lane (from 1) and how many beats it's held (0 for a tap).</summary>
    public readonly record struct Note(double Beat, int Lane, double Hold);

    /// <summary>
    /// The chart of <paramref name="tune"/> at each difficulty, as a table named <paramref name="name"/>: a row a note, with
    /// its Beat, Lane, Hold and Difficulty, the notes of each difficulty in order. The first bar is left to get ready, and the
    /// last notes are held from the downbeat of the last bar but one.
    /// </summary>
    public static Table Chart(string name, MusicRecipe tune)
    {
        var columns = new[]
        {
            new TableColumn("Beat"),
            new TableColumn("Lane", ColumnType.WholeNumber),
            new TableColumn("Hold"),
            new TableColumn("Difficulty", ColumnType.Choice) { Choices = [.. Difficulties] },
        };
        var rows = new List<(string Name, IReadOnlyList<TableValue> Cells)>();
        foreach (var difficulty in Difficulties)
        {
            var n = 0;
            foreach (var (beat, lane, hold) in Notes(tune, difficulty))
            {
                rows.Add(($"{difficulty} {++n}", [
                    Cell(columns[0], beat.ToString(CultureInfo.InvariantCulture)),
                    Cell(columns[1], lane.ToString(CultureInfo.InvariantCulture)),
                    hold > 0 ? Cell(columns[2], hold.ToString(CultureInfo.InvariantCulture)) : TableValue.Empty,
                    Cell(columns[3], difficulty),
                ]));
            }
        }

        return new Table(name, columns, rows);
    }

    private static TableValue Cell(TableColumn column, string text) =>
        column.TryRead(text, out var value, out var problem) ? value : throw new InvalidOperationException(problem);

    // How each difficulty is played: the least time between notes at different moments (in beats, and in seconds, whichever
    // is more, kept to the grid), which sixteenths of a beat a melody note can fall on (4 every sixteenth, 2 every eighth, 1
    // on the beat), how long a gap the drums fill, whether the snare doubles the melody, chords, how long a note is before
    // it's held, and whether a hold is played on its own.
    private sealed record Style(double Spacing, double Seconds, int Grid, double FillGap, bool Snares, bool Chords, double HoldFrom, bool HoldsAlone);

    private static Style StyleOf(string difficulty) => difficulty switch
    {
        "Easy" => new(Spacing: 1, Seconds: 0.5, Grid: 1, FillGap: 2, Snares: false, Chords: false, HoldFrom: 3, HoldsAlone: true),
        "Normal" => new(Spacing: 0.5, Seconds: 0.2, Grid: 2, FillGap: 1, Snares: false, Chords: true, HoldFrom: 2, HoldsAlone: false),
        _ => new(Spacing: 0.25, Seconds: 0.1, Grid: 4, FillGap: 0.5, Snares: true, Chords: true, HoldFrom: 1.5, HoldsAlone: false),
    };

    /// <summary>The notes of <paramref name="tune"/>'s chart at <paramref name="difficulty"/> (Easy, Normal or Hard), earliest first.</summary>
    public static IReadOnlyList<Note> Notes(MusicRecipe tune, string difficulty)
    {
        var recipe = tune.Clamped();
        var style = StyleOf(difficulty);
        var secondsPerBeat = 60.0 / recipe.Tempo;
        var grid = 1.0 / style.Grid;
        var spacing = Math.Max(style.Spacing, Math.Ceiling(style.Seconds / secondsPerBeat / grid - 1e-9) * grid);
        var notes = MusicSynth.Notes(recipe);
        var melody = notes.Where(n => n.Part == MusicPart.Melody).ToList();


        // The first bar to get ready; the notes end on the last bar but one's downbeat, held through it.
        const double first = 4;
        var end = recipe.Bars * 4 - 4.0;
        bool OnGrid(MusicNote note) => note.Step % (4 / style.Grid) == 0 && note.Step / 4 >= first && note.Step / 4 < end;

        // What could be played, by beat: the melody's notes on the grid, and the drums where it leaves a gap.
        var wanted = new SortedDictionary<double, (int Pitch, double Length, bool Accent)>();
        foreach (var note in melody.Where(OnGrid))
            wanted.TryAdd(note.Step / 4, (note.Pitch, note.Length / 4, note.Step % 64 == 0));
        var melodyBeats = wanted.Keys.ToList();
        var pitches = wanted.Values.Select(w => w.Pitch).Order().ToArray();
        foreach (var beat in notes.Where(n => n.Part is MusicPart.Kick or MusicPart.Snare && OnGrid(n)).Select(n => n.Step / 4).Distinct())
        {
            if (!melodyBeats.Any(b => Math.Abs(b - beat) < style.FillGap))
                wanted.TryAdd(beat, (PitchAt(melody, beat), 0, beat % 16 == 0));
        }

        // The snare under the melody: a chord with it.
        var snares = style.Snares ? notes.Where(n => n.Part == MusicPart.Snare && OnGrid(n)).Select(n => n.Step / 4).ToHashSet() : [];

        var beats = wanted.Keys.ToList();
        var chart = new List<Note>();
        var free = new double[Lanes + 1];
        Array.Fill(free, double.NegativeInfinity);
        var gap = SameLaneGap / secondsPerBeat;
        var (last, lastLane, lastPitch, heldUntil) = (double.NegativeInfinity, 0, 0, double.NegativeInfinity);
        for (var i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            var (pitch, length, accent) = wanted[beat];
            if (beat - last < spacing - 1e-9 || style.HoldsAlone && beat < heldUntil + spacing - 1e-9)
                continue;
            var busy = Busy(chart, beat);
            if (busy >= 2)
                continue;

            // The lane its pitch reaches for among the tune's (as many of its notes in each lane), a step the way the tune
            // moves when that's the lane it's in, and aside from a lane still busy.
            var reach = LaneOf(pitch, pitches);
            var way = pitch > lastPitch ? 1 : pitch < lastPitch ? -1 : lastLane <= Lanes / 2 ? 1 : -1;
            if (lastLane > 0 && reach == lastLane && pitch != lastPitch)
                reach = Math.Clamp(lastLane + way, 1, Lanes);
            var lane = Nearest(reach, l => free[l] <= beat + 1e-9, way);
            if (lane == 0)
                continue;

            // Held: a long note, or a phrase's last before a rest, to a beat before the next note's due.
            var next = NextPlayable(beats, i, beat + spacing);
            var rest = (next ?? end) - beat;
            var hold = 0.0;
            if (busy == 0 && (length >= style.HoldFrom || rest >= style.HoldFrom + 1))
                hold = Math.Min(Math.Floor((rest - 1) * 2) / 2, Math.Min(3, end - beat));

            // And the first note of each phrase's last bar is held over the notes after it (on Easy, alone).
            if (busy == 0 && hold == 0 && beat % 16 == 12 && (style.HoldsAlone ? rest >= 2.5 : style.Chords))
                hold = style.HoldsAlone ? Math.Min(2, Math.Floor((rest - 1) * 2) / 2) : 1.5;
            if (hold < 1)
                hold = 0;

            chart.Add(new Note(beat, lane, hold));
            free[lane] = beat + hold + gap;
            if (hold > 0)
                heldUntil = beat + hold;

            // A chord on a phrase's first beat (and in Hard on the snare): a lane across from it.
            if (style.Chords && hold == 0 && busy == 0 && (accent || snares.Contains(beat)))
            {
                var other = Nearest(Lanes + 1 - lane, l => l != lane && free[l] <= beat + 1e-9, lane <= Lanes / 2 ? 1 : -1);
                if (other != 0)
                {
                    chart.Add(new Note(beat, other, 0));
                    free[other] = beat + gap;
                }
            }

            (last, lastLane, lastPitch) = (beat, lane, pitch);
        }

        // The end: the last bar but one's downbeat held for two beats, by both hands on Normal and Hard.
        int[] finale = style.Chords ? [2, 3] : [2];
        if (end - last >= spacing && finale.All(l => free[l] <= end && end > heldUntil))
        {
            foreach (var lane in finale)
                chart.Add(new Note(end, lane, 2));
        }

        return [.. chart.OrderBy(n => n.Beat).ThenBy(n => n.Lane)];
    }

    // The first beat from `i` on that's at least `from`: the next a note could be played on.
    private static double? NextPlayable(List<double> beats, int i, double from)
    {
        for (var j = i + 1; j < beats.Count; j++)
        {
            if (beats[j] >= from - 1e-9)
                return beats[j];
        }

        return null;
    }

    // How many lanes are pressed at `beat`: held there, or a note on it.
    private static int Busy(List<Note> chart, double beat)
    {
        var busy = 0;
        for (var i = chart.Count - 1; i >= 0 && i >= chart.Count - 12; i--)
        {
            var note = chart[i];
            if (Math.Abs(note.Beat - beat) < 1e-9 || note.Hold > 0 && note.Beat < beat && note.Beat + note.Hold > beat - 1e-9)
                busy++;
        }

        return busy;
    }

    // The melody's pitch sounding at `beat` (or the last before it).
    private static int PitchAt(List<MusicNote> melody, double beat)
    {
        var pitch = melody.Count > 0 ? melody[0].Pitch : 60;
        foreach (var note in melody)
        {
            if (note.Step / 4 > beat + 1e-9)
                break;
            pitch = note.Pitch;
        }

        return pitch;
    }

    // The lane a pitch reaches for: where it falls among the tune's notes, lowest to the left, so each lane has as many.
    private static int LaneOf(int pitch, int[] pitches)
    {
        if (pitches.Length == 0)
            return 2;
        var below = pitches.Count(p => p < pitch) + pitches.Count(p => p == pitch) / 2.0;
        return Math.Clamp(1 + (int)(below * Lanes / pitches.Length), 1, Lanes);
    }

    // The lane nearest `lane` that's `ok`, trying `way` first at each distance; 0 for none.
    private static int Nearest(int lane, Func<int, bool> ok, int way)
    {
        for (var distance = 0; distance < Lanes; distance++)
        {
            foreach (var side in distance == 0 ? [0] : new[] { way * distance, -way * distance })
            {
                var l = lane + side;
                if (l >= 1 && l <= Lanes && ok(l))
                    return l;
            }
        }

        return 0;
    }
}
