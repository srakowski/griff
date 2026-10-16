using Griff.Data;

namespace Griff.Modules.Rhythm;

/// <summary>
/// A rhythm chart read from a table: a row a note, with its <c>Beat</c>, its <c>Lane</c> (from 1), how many beats it's held
/// (<c>Hold</c>, empty or 0 for a tap) and the <c>Difficulty</c> it's in (empty for every one, or names separated by commas).
/// </summary>
public static class RhythmChart
{
    /// <summary>The columns a chart has, in the order Create Note Lanes makes them.</summary>
    public static IReadOnlyList<string> Columns { get; } = ["Beat", "Lane", "Hold", "Difficulty"];

    /// <summary>
    /// The notes of <paramref name="table"/> played at <paramref name="difficulty"/> on <paramref name="lanes"/> lanes,
    /// earliest first (then by lane), with what's wrong with it: a column it hasn't got, a note in a lane there isn't.
    /// </summary>
    internal static (RhythmNote[] Notes, List<string> Problems) Read(Table table, string? difficulty, int lanes)
    {
        var problems = new List<string>();
        var beat = table.ColumnIndex("Beat");
        var lane = table.ColumnIndex("Lane");
        var hold = table.ColumnIndex("Hold");
        if (hold < 0)
            hold = table.ColumnIndex("Length");
        var difficultyColumn = table.ColumnIndex("Difficulty");
        if (beat < 0 || lane < 0)
        {
            problems.Add($"The chart \"{table.Name}\" needs a Beat and a Lane column (it has {List(table.Columns.Select(c => c.Name))}).");
            return ([], problems);
        }

        var wanted = difficulty?.Trim() ?? "";
        var notes = new List<(RhythmNote Note, int Row)>();
        var outside = new List<string>();
        foreach (var row in table.Rows)
        {
            if (difficultyColumn >= 0 && !In(row[difficultyColumn], wanted))
                continue;
            var at = row[beat];
            if (at.IsEmpty)
                continue;
            var number = (int)row[lane].AsWholeNumber;
            if (number < 1 || number > lanes)
            {
                outside.Add(row.Name);
                continue;
            }

            var held = hold >= 0 ? Math.Max(0, row[hold].AsNumber) : 0;
            var when = at.AsNumber;
            if (!double.IsFinite(when) || !double.IsFinite(held))
                continue;
            notes.Add((new RhythmNote(when, number - 1, held, row.Name), row.Index));
        }

        if (outside.Count > 0)
            problems.Add($"The chart \"{table.Name}\" has notes in lanes the lanes haven't got ({lanes}): {List(outside.Take(5))}{(outside.Count > 5 ? "..." : "")}.");
        if (difficultyColumn >= 0 && wanted.Length > 0 && Difficulties(table) is { Count: > 0 } named &&
            !named.Contains(wanted, StringComparer.OrdinalIgnoreCase))
            problems.Add($"The chart \"{table.Name}\" has no difficulty called \"{wanted}\": its difficulties are {List(named)}.");

        var sorted = notes.OrderBy(n => n.Note.Beat).ThenBy(n => n.Note.Lane).ThenBy(n => n.Row).Select(n => n.Note).ToArray();
        return (sorted, problems);
    }

    /// <summary>The difficulties a chart names, in the order it first names them.</summary>
    public static IReadOnlyList<string> Difficulties(Table table)
    {
        var column = table.ColumnIndex("Difficulty");
        var names = new List<string>();
        if (column < 0)
            return names;
        foreach (var choice in table.Columns[column].Choices)
        {
            if (!names.Contains(choice, StringComparer.OrdinalIgnoreCase))
                names.Add(choice);
        }

        foreach (var row in table.Rows)
        {
            foreach (var name in Names(row[column]))
            {
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }

        return names;
    }

    // In every difficulty when it names none; otherwise in those it names.
    private static bool In(TableValue cell, string difficulty)
    {
        var any = false;
        foreach (var name in Names(cell))
        {
            any = true;
            if (name.Equals(difficulty, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !any;
    }

    private static string[] Names(TableValue cell) =>
        cell.IsEmpty ? [] : cell.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string List(IEnumerable<string> names) => names.Any() ? string.Join(", ", names) : "none";
}
