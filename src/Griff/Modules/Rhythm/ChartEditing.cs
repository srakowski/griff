using System.Globalization;
using Griff.Data;

namespace Griff.Modules.Rhythm;

/// <summary>
/// A grid a chart's notes snap to: each beat split into <see cref="Divisions"/>. 1/4 is a note a beat (in 4/4), 1/8 two,
/// 1/16 four, and the triplets three (1/8 triplets) or six (1/16 triplets).
/// </summary>
public readonly record struct ChartSnap(string Name, int Divisions)
{
    /// <summary>The grids to pick from, coarsest first.</summary>
    public static IReadOnlyList<ChartSnap> All { get; } =
    [
        new("1/4", 1),
        new("1/8", 2),
        new("1/8 triplets", 3),
        new("1/16", 4),
        new("1/16 triplets", 6),
    ];

    /// <summary>The grids' names, coarsest first.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. All.Select(s => s.Name)];

    /// <summary>The grid called <paramref name="name"/> ("1/16", "1/8 triplets", or "16", "1/12", "12th"), or 1/8 if there's none.</summary>
    public static ChartSnap Named(string? name)
    {
        var text = (name ?? "").Trim();
        foreach (var snap in All)
        {
            if (string.Equals(snap.Name, text, StringComparison.OrdinalIgnoreCase))
                return snap;
        }

        // "16", "1/16", "16th", "1/12" and "24th" (the triplets, as other charting tools count them).
        var digits = new string(text.Replace("1/", "").TakeWhile(char.IsDigit).ToArray());
        if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            foreach (var snap in All)
            {
                if (snap.Divisions * 4 == n)
                    return snap;
            }
        }

        return All[1];
    }

    /// <summary>How many beats apart the grid's lines are.</summary>
    public double Step => 1.0 / Math.Max(1, Divisions);

    /// <summary>The grid line nearest <paramref name="beat"/>.</summary>
    public double Snap(double beat) => Math.Round(beat * Math.Max(1, Divisions), MidpointRounding.AwayFromZero) / Math.Max(1, Divisions);

    /// <summary>The grid line at or before <paramref name="beat"/>.</summary>
    public double Floor(double beat) => Math.Floor(beat * Math.Max(1, Divisions) + 1e-6) / Math.Max(1, Divisions);

    public override string ToString() => Name;
}

/// <summary>
/// When a chart's beats are: the tempo in beats a minute, the beats in a bar and how many seconds into the song its first
/// beat (beat 0) is, as the lanes' <see cref="Scenes.BeatClock"/> counts them.
/// </summary>
public readonly record struct ChartTiming(double Tempo, int BeatsPerBar, double Offset)
{
    /// <summary>120 beats a minute in 4/4 from the very start.</summary>
    public static ChartTiming Default { get; } = new(120, 4, 0);

    /// <summary>Seconds a beat lasts.</summary>
    public double SecondsPerBeat => 60 / (double.IsFinite(Tempo) && Tempo > 0 ? Math.Clamp(Tempo, 20, 400) : 120);

    /// <summary>Beats in a bar, at least 1.</summary>
    public int PerBar => Math.Max(1, BeatsPerBar);

    /// <summary>The beat (with its fraction) <paramref name="seconds"/> into the song.</summary>
    public double BeatAt(double seconds) => (seconds - Offset) / SecondsPerBeat;

    /// <summary>How many seconds into the song <paramref name="beat"/> is.</summary>
    public double SecondsAt(double beat) => Offset + beat * SecondsPerBeat;
}

/// <summary>
/// A lane pressed while recording a chart: the lane (from 1), and how far into the song had been heard when it was pressed
/// and let go, in seconds (<see cref="double.PositiveInfinity"/> for one still held when the take ended).
/// </summary>
public readonly record struct ChartTap(int Lane, double Pressed, double Released);

/// <summary>A note of a chart: its beat, its lane (from 1) and how many beats it's held (0 for a tap).</summary>
public readonly record struct ChartNote(double Beat, int Lane, double Hold)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"beat {ChartTable.Text(Beat)}, lane {Lane}{(Hold > 0 ? $", hold {ChartTable.Text(Hold)}" : "")}");
}

/// <summary>
/// Turns what was tapped along to a song into a chart's notes: each press at the beat the player heard it (the song as it
/// had reached the speakers, less the player's audio offset, by the lanes' tempo and first beat), snapped to the grid; a
/// key held down for half a beat or more is a hold to where it was let go, snapped too.
/// </summary>
public static class ChartRecording
{
    /// <summary>A press held at least this many beats is a hold note; a shorter one is a tap.</summary>
    public const double HoldAtLeast = 0.5;

    /// <summary>
    /// The notes <paramref name="taps"/> make, earliest first (then by lane): each pressed at the beat it was heard on,
    /// allowing for <paramref name="audioOffset"/> (milliseconds the player hears the sound late), snapped to
    /// <paramref name="snap"/>; those snapping before <paramref name="from"/> (the beat recording started at) or into a
    /// lane past <paramref name="lanes"/> are left out, and two in a lane on one beat are one (the longer hold).
    /// </summary>
    public static List<ChartNote> Notes(IEnumerable<ChartTap> taps, ChartTiming timing, ChartSnap snap, double audioOffset,
        double from = double.NegativeInfinity, int lanes = int.MaxValue)
    {
        var late = double.IsFinite(audioOffset) ? audioOffset / 1000 : 0;
        var start = double.IsFinite(from) ? snap.Snap(from) - 1e-6 : double.NegativeInfinity;
        var notes = new Dictionary<(long Beat, int Lane), ChartNote>();
        foreach (var tap in taps)
        {
            if (tap.Lane < 1 || tap.Lane > lanes || !double.IsFinite(tap.Pressed))
                continue;
            var pressed = timing.BeatAt(tap.Pressed - late);
            var beat = snap.Snap(pressed);
            if (beat < start)
                continue;
            var hold = 0.0;
            if (tap.Released > tap.Pressed)
            {
                var released = double.IsFinite(tap.Released) ? timing.BeatAt(tap.Released - late) : double.PositiveInfinity;
                if (released - pressed >= HoldAtLeast)
                    hold = double.IsFinite(released) ? Math.Max(snap.Step, snap.Snap(released) - beat) : snap.Step;
            }

            var key = ((long)Math.Round(beat * 1e4), tap.Lane);
            if (!notes.TryGetValue(key, out var had) || had.Hold < hold)
                notes[key] = new ChartNote(Round(beat), tap.Lane, Round(hold));
        }

        return [.. notes.Values.OrderBy(n => n.Beat).ThenBy(n => n.Lane)];
    }

    // Beats kept to four places (a sixth of a beat is 0.1667): what a chart's file says.
    /// <summary>A beat to four places, as a chart keeps it.</summary>
    public static double Round(double beat) => Math.Round(beat, 4, MidpointRounding.AwayFromZero);
}

/// <summary>A note of a chart table as an editor shows it: its row, what it is, and whether it's in the difficulty being edited.</summary>
public readonly record struct ChartRow(string Row, int Index, ChartNote Note, bool InDifficulty);

/// <summary>
/// Changes to a chart table (a row a note: Beat, Lane, Hold and Difficulty), as the editor's Chart panel makes them: a
/// recorded take put in (added over what's there, or in place of it), and a note added, taken out, moved or made a hold.
/// Each gives a new table; the rows it adds are named by their difficulty ("Hard 12") and go in after the notes before them.
/// Editing a note that's in several difficulties changes it in the one being edited only.
/// </summary>
public static class ChartTable
{
    /// <summary>True when <paramref name="table"/> is a chart: it has a Beat and a Lane column.</summary>
    public static bool IsChart(Table? table) => table is not null && table.ColumnIndex("Beat") >= 0 && table.ColumnIndex("Lane") >= 0;

    /// <summary>Why notes can't be written into <paramref name="table"/> (its Beat or Lane is worked out by a formula), or null.</summary>
    public static string? WhyNotEditable(Table table)
    {
        foreach (var name in new[] { "Beat", "Lane", "Hold", "Difficulty" })
        {
            if (table.Column(name) is { IsFormula: true })
                return $"The {table.Name} table's {name} is worked out by a formula ({table.Column(name)!.Formula}), so notes can't be written into it here. Change the formula in the Tables panel.";
        }

        return null;
    }

    /// <summary>A new, empty chart table called <paramref name="name"/>: Beat, Lane, Hold and Difficulty (Easy, Normal or Hard).</summary>
    public static Table Create(string name) => new(name, Columns(), []);

    private static TableColumn[] Columns() =>
    [
        new TableColumn("Beat"),
        new TableColumn("Lane", ColumnType.WholeNumber),
        new TableColumn("Hold"),
        new TableColumn("Difficulty", ColumnType.Choice) { Choices = ["Easy", "Normal", "Hard"] },
    ];

    /// <summary>
    /// The notes of <paramref name="table"/>, in its order: each with whether it's in <paramref name="difficulty"/> (it names
    /// it, or names none). With no difficulty, every note is.
    /// </summary>
    public static List<ChartRow> Rows(Table table, string? difficulty)
    {
        var rows = new List<ChartRow>();
        int beat = table.ColumnIndex("Beat"), lane = table.ColumnIndex("Lane"), hold = HoldColumn(table), level = table.ColumnIndex("Difficulty");
        if (beat < 0 || lane < 0)
            return rows;
        var wanted = (difficulty ?? "").Trim();
        foreach (var row in table.Rows)
        {
            if (row[beat].IsEmpty || !double.IsFinite(row[beat].AsNumber))
                continue;
            var held = hold >= 0 && !row[hold].IsEmpty && double.IsFinite(row[hold].AsNumber) ? Math.Max(0, row[hold].AsNumber) : 0;
            var names = level >= 0 ? Names(row[level]) : [];
            var inIt = wanted.Length == 0 || names.Length == 0 || names.Contains(wanted, StringComparer.OrdinalIgnoreCase);
            rows.Add(new ChartRow(row.Name, row.Index, new ChartNote(row[beat].AsNumber, (int)row[lane].AsWholeNumber, held), inIt));
        }

        return rows;
    }

    /// <summary>
    /// Puts a recorded take into the chart at <paramref name="difficulty"/>: <paramref name="notes"/> added, and with
    /// <paramref name="replace"/>, the difficulty's notes from beat <paramref name="from"/> up to <paramref name="to"/> taken
    /// out first (overdubbing keeps them, leaving out a recorded note that's already there). Returns the table and how many
    /// notes went in and came out.
    /// </summary>
    public static (Table Table, int Added, int Removed) Record(Table table, IReadOnlyList<ChartNote> notes, string? difficulty, bool replace, double from, double to)
    {
        difficulty = (difficulty ?? "").Trim();
        table = Ready(table, difficulty);
        var removed = 0;
        if (replace)
        {
            var gone = Rows(table, difficulty).Where(r => r.InDifficulty && r.Note.Beat >= from - 1e-6 && r.Note.Beat <= to + 1e-6).Select(r => r.Row).ToList();
            foreach (var row in gone)
                table = Remove(table, row, difficulty);
            removed = gone.Count;
        }

        var added = 0;
        foreach (var note in notes)
        {
            if (Rows(table, difficulty).Any(r => r.InDifficulty && r.Note.Lane == note.Lane && Math.Abs(r.Note.Beat - note.Beat) < 1e-4))
                continue;
            (table, _) = Add(table, note, difficulty);
            added++;
        }

        return (table, added, removed);
    }

    /// <summary>
    /// With <paramref name="note"/> added at <paramref name="difficulty"/> (named for it, e.g. "Normal 41", or "Note 41" with
    /// none), after the notes of the difficulty before it; and the new row's name.
    /// </summary>
    public static (Table Table, string Row) Add(Table table, ChartNote note, string? difficulty)
    {
        difficulty = (difficulty ?? "").Trim();
        table = Ready(table, difficulty, note.Hold > 0);
        var name = FreeName(table, difficulty.Length > 0 ? difficulty : "Note");
        var at = InsertAt(table, note, difficulty);
        var cells = new TableValue[table.Columns.Count];
        Write(table, cells, note, difficulty);
        return (table.InsertRow(at, name, cells), name);
    }

    /// <summary>
    /// Without the note in <paramref name="row"/> at <paramref name="difficulty"/>: a note in other difficulties as well stays
    /// in those (one in every difficulty is then in the others the chart names).
    /// </summary>
    public static Table Remove(Table table, string row, string? difficulty)
    {
        var (detached, index, kept) = Detach(table, row, difficulty);
        return kept ? detached : detached.RemoveRows([index]);
    }

    /// <summary>
    /// With the note in <paramref name="row"/> made <paramref name="note"/> at <paramref name="difficulty"/> (moved, or its
    /// hold changed); one in other difficulties as well stays as it was in those, and the changed note is a row of its own.
    /// Returns the row the note is in now.
    /// </summary>
    public static (Table Table, string Row) Change(Table table, string row, ChartNote note, string? difficulty)
    {
        difficulty = (difficulty ?? "").Trim();
        var (detached, index, kept) = Detach(table, row, difficulty);
        if (kept)
            return Add(detached, note, difficulty);
        detached = Ready(detached, difficulty, note.Hold > 0);
        var cells = detached.Rows[index].Cells.ToArray();
        Write(detached, cells, note, difficulty);
        var name = detached.Rows[index].Name;
        return (detached.SetCells(cells.Select((c, i) => (index, i, c))), name);
    }

    // The row as a row of its own in the difficulty: (the table, its index, false) if it's only in that one (or the chart has
    // none); otherwise the table with the difficulty taken off the row, and true (the row stays for the others).
    private static (Table Table, int Index, bool Kept) Detach(Table table, string row, string? difficulty)
    {
        var index = table.RowIndex(row);
        if (index < 0)
            throw new ArgumentException($"The {table.Name} table has no note called \"{row}\".", nameof(row));
        difficulty = (difficulty ?? "").Trim();
        var level = table.ColumnIndex("Difficulty");
        if (difficulty.Length == 0 || level < 0)
            return (table, index, false);
        var names = Names(table.Rows[index][level]);
        if (names.Length > 0 && !names.Contains(difficulty, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"The note \"{row}\" isn't in {difficulty}: it's in {string.Join(", ", names)}.", nameof(row));
        var others = (names.Length > 0 ? names : [.. RhythmChart.Difficulties(table)])
            .Where(n => !n.Equals(difficulty, StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count == 0)
            return (table, index, false);
        var column = table.Columns[level];
        if (column.Type != ColumnType.Choice || others.Count == 1)
            return (table.SetCell(index, level, Value(column, string.Join(", ", others))), index, true);

        // A choice holds one difficulty: the row stays for the first of the others, and a copy of it for each of the rest.
        var cells = table.Rows[index].Cells.ToArray();
        table = table.SetCell(index, level, Value(column, others[0]));
        for (var i = 1; i < others.Count; i++)
        {
            var copy = (TableValue[])cells.Clone();
            copy[level] = Value(column, others[i]);
            table = table.InsertRow(index + i, FreeName(table, others[i]), copy);
        }

        return (table, index, true);
    }

    // The table with the columns a note needs: a Hold column for a hold, and a Difficulty column (naming it) for a difficulty.
    private static Table Ready(Table table, string difficulty, bool hold = true)
    {
        if (!IsChart(table))
        {
            foreach (var needed in Columns().Take(2))
            {
                if (table.ColumnIndex(needed.Name) < 0)
                    table = table.InsertColumn(table.Columns.Count, needed);
            }
        }

        if (hold && HoldColumn(table) < 0)
            table = table.InsertColumn(table.ColumnIndex("Lane") + 1, new TableColumn("Hold"));
        if (difficulty.Length == 0)
            return table;
        var level = table.ColumnIndex("Difficulty");
        if (level < 0)
            return table.InsertColumn(table.Columns.Count, Columns()[3]);
        var column = table.Columns[level];
        if (column.Type == ColumnType.Choice && !column.Choices.Contains(difficulty, StringComparer.OrdinalIgnoreCase))
            return table.ChangeColumn(level, column with { Choices = [.. column.Choices, difficulty] });
        return table;
    }

    private static void Write(Table table, TableValue[] cells, ChartNote note, string difficulty)
    {
        cells[table.ColumnIndex("Beat")] = Value(table.Column("Beat")!, Text(note.Beat));
        cells[table.ColumnIndex("Lane")] = Value(table.Column("Lane")!, note.Lane.ToString(CultureInfo.InvariantCulture));
        if (HoldColumn(table) is >= 0 and var hold)
            cells[hold] = note.Hold > 0 ? Value(table.Columns[hold], Text(note.Hold)) : TableValue.Empty;
        if (difficulty.Length > 0 && table.ColumnIndex("Difficulty") is >= 0 and var level)
            cells[level] = Value(table.Columns[level], difficulty);
    }

    // After the last note of the difficulty at or before its beat (then lane); before the difficulty's first if none is; at
    // the end if the difficulty has none.
    private static int InsertAt(Table table, ChartNote note, string difficulty)
    {
        var rows = Rows(table, difficulty).Where(r => difficulty.Length == 0 || r.InDifficulty).ToList();
        if (rows.Count == 0)
            return -1;
        var before = rows.LastOrDefault(r => r.Note.Beat < note.Beat - 1e-6 || Math.Abs(r.Note.Beat - note.Beat) < 1e-6 && r.Note.Lane <= note.Lane);
        return before.Row is not null ? before.Index + 1 : rows[0].Index;
    }

    private static string FreeName(Table table, string prefix)
    {
        var highest = 0;
        foreach (var row in table.Rows)
        {
            if (row.Name.Length > prefix.Length + 1 && row.Name.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(row.Name.AsSpan(prefix.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                highest = Math.Max(highest, n);
        }

        for (var n = highest + 1; ; n++)
        {
            if (!table.HasRow($"{prefix} {n}"))
                return $"{prefix} {n}";
        }
    }

    private static int HoldColumn(Table table) => table.ColumnIndex("Hold") is >= 0 and var hold ? hold : table.ColumnIndex("Length");

    private static TableValue Value(TableColumn column, string text) =>
        column.TryRead(text, out var value, out _) ? value : TableValue.Of(text);

    /// <summary>A beat as a chart writes it: up to four places, "12", "12.5", "12.3333".</summary>
    public static string Text(double beat) => ChartRecording.Round(beat).ToString("0.####", CultureInfo.InvariantCulture);

    private static string[] Names(TableValue cell) =>
        cell.IsEmpty ? [] : cell.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
