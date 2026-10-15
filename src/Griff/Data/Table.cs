using System.Globalization;
using System.Text.Json;

namespace Griff.Data;

/// <summary>
/// A table of game data, like a sheet of a spreadsheet: named rows (Fireball, Frost Bolt) and typed columns (Damage, a
/// number; Element, a choice). It's a <c>.table</c> file in Assets (see <see cref="TableFile"/>), named after the file
/// ("Spells"), edited in the editor's Tables panel and read by scripts and the Table Values component.
/// </summary>
/// <remarks>
/// A table never changes: every edit makes a new one (see <see cref="SetCell"/> and the rest), so a running game, the
/// editor and an undo history can each hold one without anyone changing it under them. Rows and columns are found by
/// name, ignoring case, and still answer to the names they had before they were renamed (see <see cref="RenamedRows"/>),
/// so a scene or script naming a row by its old name keeps working.
/// </remarks>
public sealed class Table
{
    private readonly Dictionary<string, int> _rowIndex;
    private readonly Dictionary<string, int> _columnIndex;

    /// <summary>
    /// A table called <paramref name="name"/> with these columns and rows (each row's cells in the columns' order). A
    /// formula column's cells are left empty: they're worked out (see <see cref="TableEvaluation"/>).
    /// </summary>
    public Table(string name, IEnumerable<TableColumn> columns, IEnumerable<(string Name, IReadOnlyList<TableValue> Cells)> rows,
        IReadOnlyDictionary<string, string>? renamedRows = null, IReadOnlyDictionary<string, string>? renamedColumns = null,
        IReadOnlyList<KeyValuePair<string, JsonElement>>? kept = null)
        : this(name, columns, rows, renamedRows, renamedColumns, kept, computed: false)
    {
    }

    private Table(string name, IEnumerable<TableColumn> columns, IEnumerable<(string Name, IReadOnlyList<TableValue> Cells)> rows,
        IReadOnlyDictionary<string, string>? renamedRows, IReadOnlyDictionary<string, string>? renamedColumns,
        IReadOnlyList<KeyValuePair<string, JsonElement>>? kept, bool computed)
    {
        Name = name;
        Columns = [.. columns];
        _columnIndex = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Columns[i].Name))
                throw new InvalidDataException($"Column {i + 1} of the {name} table has no name.");
            if (!_columnIndex.TryAdd(Columns[i].Name, i))
                throw new InvalidDataException($"The {name} table has two columns called \"{Columns[i].Name}\".");
        }

        var list = new List<TableRow>();
        _rowIndex = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (rowName, cells) in rows)
        {
            if (string.IsNullOrWhiteSpace(rowName))
                throw new InvalidDataException($"Row {list.Count + 1} of the {name} table has no name.");
            if (!_rowIndex.TryAdd(rowName, list.Count))
                throw new InvalidDataException($"The {name} table has two rows called \"{rowName}\".");
            var aligned = new TableValue[Columns.Count];
            for (var i = 0; i < aligned.Length && i < cells.Count; i++)
            {
                if (computed || Columns[i].Formula is null)
                    aligned[i] = cells[i];
            }

            list.Add(new TableRow(this, rowName, list.Count, aligned));
        }

        Rows = list;
        // A former name that's a name again (a row added back under it) is that row's now.
        RenamedRows = Clean(renamedRows, _rowIndex);
        RenamedColumns = Clean(renamedColumns, _columnIndex);
        Kept = kept ?? [];
        IsComputed = computed;
    }

    /// <summary>The same table with every cell as <paramref name="cells"/> has it, formula cells worked out (see <see cref="TableEvaluation"/>).</summary>
    internal Table WithComputed(IReadOnlyList<TableValue[]> cells) =>
        new(Name, Columns, Rows.Select((r, i) => (r.Name, (IReadOnlyList<TableValue>)cells[i])), RenamedRows, RenamedColumns, Kept, computed: true);

    /// <summary>
    /// True when its formula columns' cells are worked out (as a game's tables are, and the Tables panel shows them);
    /// false for a table as its file has it, with those cells empty. Any edit gives a table as its file would have it.
    /// </summary>
    public bool IsComputed { get; }

    /// <summary>True when one of its columns has a <see cref="TableColumn.Formula"/>.</summary>
    public bool HasFormulas => Columns.Any(c => c.Formula is not null);

    private static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? renamed, Dictionary<string, int> names)
    {
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (old, now) in renamed ?? new Dictionary<string, string>())
        {
            if (!names.ContainsKey(old) && names.ContainsKey(now))
                clean[old] = now;
        }

        return clean;
    }

    /// <summary>An empty table with no rows or columns.</summary>
    public static Table Empty(string name) => new(name, [], []);

    /// <summary>Its name: the file's name without <c>.table</c> ("Spells").</summary>
    public string Name { get; }

    /// <summary>Its columns, left to right.</summary>
    public IReadOnlyList<TableColumn> Columns { get; }

    /// <summary>Its rows, top to bottom.</summary>
    public IReadOnlyList<TableRow> Rows { get; }

    /// <summary>
    /// Rows' former names and what each is called now ("Slime" → "Green Slime"): a row renamed in the Tables panel keeps
    /// answering to its old names, so what names it (a Table Values component, a script, another table's row column) still
    /// finds it.
    /// </summary>
    public IReadOnlyDictionary<string, string> RenamedRows { get; }

    /// <summary>Columns' former names and what each is called now, as <see cref="RenamedRows"/> does for rows.</summary>
    public IReadOnlyDictionary<string, string> RenamedColumns { get; }

    /// <summary>What the file says that this version of Griff doesn't know, kept so saving it loses nothing.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonElement>> Kept { get; }

    /// <summary>The row called <paramref name="name"/> (or once called it), or null.</summary>
    public TableRow? Find(string? name)
    {
        if (name is null)
            return null;
        name = name.Trim();
        if (_rowIndex.TryGetValue(name, out var index))
            return Rows[index];
        return RenamedRows.TryGetValue(name, out var now) && _rowIndex.TryGetValue(now, out index) ? Rows[index] : null;
    }

    /// <summary>True if a row is called <paramref name="name"/> (or once was).</summary>
    public bool HasRow(string? name) => Find(name) is not null;

    /// <summary>The row called <paramref name="row"/>; throws, naming the rows there are, if there's none.</summary>
    public TableRow this[string row] => Find(row) ?? throw new KeyNotFoundException(NoRow(row));

    /// <summary>The row at <paramref name="index"/>, from 0 at the top.</summary>
    public TableRow this[int index] => Rows[index];

    /// <summary>Says there's no row <paramref name="row"/>, and which rows there are.</summary>
    public string NoRow(string? row) =>
        $"The {Name} table has no row \"{row}\". {(Rows.Count == 0 ? "It has no rows yet." : $"Its rows: {List(Rows.Select(r => r.Name))}.")}";

    /// <summary>Says there's no column <paramref name="column"/>, and which columns there are.</summary>
    public string NoColumn(string? column) =>
        $"The {Name} table has no column \"{column}\". {(Columns.Count == 0 ? "It has no columns yet." : $"Its columns: {List(Columns.Select(c => c.Name))}.")}";

    private static string List(IEnumerable<string> names)
    {
        var all = names.ToList();
        return string.Join(", ", all.Take(30)) + (all.Count > 30 ? $" and {all.Count - 30} more" : "");
    }

    /// <summary>The index of the column called <paramref name="name"/> (or once called it), or -1.</summary>
    public int ColumnIndex(string? name)
    {
        if (name is null)
            return -1;
        name = name.Trim();
        if (_columnIndex.TryGetValue(name, out var index))
            return index;
        return RenamedColumns.TryGetValue(name, out var now) && _columnIndex.TryGetValue(now, out index) ? index : -1;
    }

    /// <summary>The column called <paramref name="name"/> (or once called it), or null.</summary>
    public TableColumn? Column(string? name) => ColumnIndex(name) is >= 0 and var i ? Columns[i] : null;

    /// <summary>The index of the row called <paramref name="name"/> (or once called it), or -1.</summary>
    public int RowIndex(string? name) => Find(name)?.Index ?? -1;

    // ----- Edits: each makes a new table.

    private Table With(IEnumerable<TableColumn>? columns = null, IEnumerable<(string, IReadOnlyList<TableValue>)>? rows = null,
        IReadOnlyDictionary<string, string>? renamedRows = null, IReadOnlyDictionary<string, string>? renamedColumns = null, string? name = null) =>
        new(name ?? Name, columns ?? Columns, rows ?? RowData(), renamedRows ?? RenamedRows, renamedColumns ?? RenamedColumns, Kept);

    private IEnumerable<(string, IReadOnlyList<TableValue>)> RowData() => Rows.Select(r => (r.Name, (IReadOnlyList<TableValue>)r.Cells));

    /// <summary>The same table under another name (its file renamed).</summary>
    public Table Renamed(string name) => With(name: name);

    /// <summary>With the cell at <paramref name="row"/>, <paramref name="column"/> set to <paramref name="value"/>.</summary>
    public Table SetCell(int row, int column, TableValue value) => SetCells([(row, column, value)]);

    /// <summary>With each of <paramref name="cells"/> set.</summary>
    public Table SetCells(IEnumerable<(int Row, int Column, TableValue Value)> cells)
    {
        var data = Rows.Select(r => r.Cells.ToArray()).ToList();
        foreach (var (row, column, value) in cells)
            data[row][column] = value;
        return With(rows: Rows.Select((r, i) => (r.Name, (IReadOnlyList<TableValue>)data[i])));
    }

    /// <summary>With a row called <paramref name="name"/> put in at <paramref name="at"/> (the end for -1), its cells empty or <paramref name="cells"/>.</summary>
    public Table InsertRow(int at, string name, IReadOnlyList<TableValue>? cells = null)
    {
        var rows = RowData().ToList();
        rows.Insert(at < 0 || at > rows.Count ? rows.Count : at, (name.Trim(), cells ?? new TableValue[Columns.Count]));
        return With(rows: rows);
    }

    /// <summary>Without the rows at <paramref name="indexes"/>.</summary>
    public Table RemoveRows(IEnumerable<int> indexes)
    {
        var gone = indexes.ToHashSet();
        return With(rows: RowData().Where((_, i) => !gone.Contains(i)).ToList());
    }

    /// <summary>
    /// With the row at <paramref name="index"/> called <paramref name="name"/>, remembering its old name (see
    /// <see cref="RenamedRows"/>) so what names it still finds it.
    /// </summary>
    public Table RenameRow(int index, string name)
    {
        name = name.Trim();
        var old = Rows[index].Name;
        var renamed = Rename(RenamedRows, old, name);
        return With(rows: RowData().Select((r, i) => i == index ? (name, r.Item2) : r).ToList(), renamedRows: renamed);
    }

    // The old name and every name that led to it now lead to the new one.
    private static Dictionary<string, string> Rename(IReadOnlyDictionary<string, string> renamed, string old, string now)
    {
        var map = renamed.Where(p => !string.Equals(p.Key, now, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => string.Equals(p.Value, old, StringComparison.OrdinalIgnoreCase) ? now : p.Value, StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(old, now, StringComparison.OrdinalIgnoreCase))
            map[old] = now;
        return map;
    }

    /// <summary>With the rows put in the order of <paramref name="order"/> (indexes of rows now, each once).</summary>
    public Table ReorderRows(IReadOnlyList<int> order)
    {
        var rows = RowData().ToList();
        return With(rows: order.Select(i => rows[i]).ToList());
    }

    /// <summary>With the row at <paramref name="from"/> moved to <paramref name="to"/>.</summary>
    public Table MoveRow(int from, int to)
    {
        var order = Enumerable.Range(0, Rows.Count).ToList();
        order.RemoveAt(from);
        order.Insert(Math.Clamp(to, 0, order.Count), from);
        return ReorderRows(order);
    }

    /// <summary>
    /// With the rows sorted by the column at <paramref name="column"/> (-1 for their names): numbers by size, yes before
    /// no, dice by their average, text and names alphabetically, choices in the order of the column's choices; empty
    /// cells last. Rows that tie keep their order.
    /// </summary>
    public Table SortBy(int column, bool descending = false) => ReorderRows(SortOrder(column, descending));

    /// <summary>
    /// The rows' indexes in the order <see cref="SortBy"/> puts them. Of a table with its formulas worked out, it sorts by
    /// what they work out to; give it to the file's table's <see cref="ReorderRows"/>.
    /// </summary>
    public IReadOnlyList<int> SortOrder(int column, bool descending = false)
    {
        var keyed = Rows.Select(r => (Row: r, Empty: column >= 0 && r.Cells[column].IsEmpty)).ToList();
        var filled = keyed.Where(k => !k.Empty).Select(k => k.Row).ToList();
        var comparer = Comparer<TableRow>.Create((a, b) => CompareCells(column, a, b));
        var sorted = descending ? filled.OrderByDescending(r => r, comparer).ToList() : filled.OrderBy(r => r, comparer).ToList();
        sorted.AddRange(keyed.Where(k => k.Empty).Select(k => k.Row));
        return [.. sorted.Select(r => r.Index)];
    }

    private int CompareCells(int column, TableRow a, TableRow b)
    {
        if (column < 0)
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        var (x, y) = (a.Cells[column], b.Cells[column]);
        var type = Columns[column].Type;
        return type switch
        {
            ColumnType.Number or ColumnType.WholeNumber or ColumnType.Dice => x.AsNumber.CompareTo(y.AsNumber),
            ColumnType.YesNo => y.AsYesNo.CompareTo(x.AsYesNo),
            ColumnType.Choice => ChoiceIndex(column, x).CompareTo(ChoiceIndex(column, y)),
            _ => string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase),
        };
    }

    private int ChoiceIndex(int column, TableValue value) =>
        Columns[column].Choices.ToList().FindIndex(c => string.Equals(c, value.ToString(), StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : int.MaxValue;

    /// <summary>With <paramref name="column"/> put in at <paramref name="at"/> (the end for -1), its cells empty.</summary>
    public Table InsertColumn(int at, TableColumn column)
    {
        var index = at < 0 || at > Columns.Count ? Columns.Count : at;
        var columns = Columns.ToList();
        columns.Insert(index, column with { Name = column.Name.Trim() });
        return With(columns, Rows.Select(r =>
        {
            var cells = r.Cells.ToList();
            cells.Insert(index, TableValue.Empty);
            return (r.Name, (IReadOnlyList<TableValue>)cells);
        }).ToList());
    }

    /// <summary>Without the column at <paramref name="index"/> (and its cells).</summary>
    public Table RemoveColumn(int index) =>
        With(Columns.Where((_, i) => i != index).ToList(), Rows.Select(r => (r.Name, (IReadOnlyList<TableValue>)r.Cells.Where((_, i) => i != index).ToList())).ToList());

    /// <summary>With the column at <paramref name="from"/> moved to <paramref name="to"/>, its cells going with it.</summary>
    public Table MoveColumn(int from, int to)
    {
        to = Math.Clamp(to, 0, Columns.Count - 1);
        List<T> Move<T>(IEnumerable<T> items)
        {
            var list = items.ToList();
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return list;
        }

        return With(Move(Columns), Rows.Select(r => (r.Name, (IReadOnlyList<TableValue>)Move(r.Cells))).ToList());
    }

    /// <summary>
    /// With the column at <paramref name="index"/> set up as <paramref name="column"/>: renamed (remembering its old name,
    /// see <see cref="RenamedColumns"/>), or of another type, its cells converted (a number column made text keeps "12" as
    /// text; text made a number reads each cell as one). Throws <see cref="InvalidDataException"/>, saying which cells,
    /// if some don't convert, rather than losing them.
    /// </summary>
    public Table ChangeColumn(int index, TableColumn column)
    {
        column = column with { Name = column.Name.Trim() };
        var old = Columns[index];
        var data = Rows.Select(r => r.Cells.ToArray()).ToList();
        // A formula column's cells are worked out, so there's nothing of its own to convert.
        if (column.Formula is null && (old.Type != column.Type || !old.Choices.SequenceEqual(column.Choices)))
        {
            var problems = new List<string>();
            for (var row = 0; row < data.Count; row++)
            {
                if (column.TryConvert(data[row][index], out var converted, out _))
                    data[row][index] = converted;
                else
                    problems.Add($"{Rows[row].Name} ({data[row][index]})");
            }

            if (problems.Count > 0)
            {
                throw new InvalidDataException($"{problems.Count} cell{(problems.Count == 1 ? "" : "s")} of {old.Name} " +
                    $"{(problems.Count == 1 ? "isn't" : "aren't")} {Article(column)}: {string.Join(", ", problems.Take(8))}{(problems.Count > 8 ? "..." : "")}. " +
                    "Change or clear them first.");
            }
        }

        var columns = Columns.ToList();
        columns[index] = column;
        var renaming = !string.Equals(old.Name, column.Name, StringComparison.Ordinal);
        var renamed = renaming ? Rename(RenamedColumns, old.Name, column.Name) : RenamedColumns;
        // The table's formulas name it by its new name.
        if (renaming && !string.Equals(old.Name, column.Name, StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Formula is { } formula)
                    columns[i] = columns[i] with { Formula = Data.Formula.RenameColumn(formula, old.Name, column.Name) };
            }
        }

        return With(columns, Rows.Select((r, i) => (r.Name, (IReadOnlyList<TableValue>)data[i])).ToList(), renamedColumns: renamed);
    }

    /// <summary>
    /// With the column at <paramref name="index"/> filled from <paramref name="curve"/>, a row a level: the first row is
    /// level 1. Rows are added for levels past the last (named by their level, "12"), and rows still called "Row 3" and so
    /// on are named by their level. The column keeps the curve (see <see cref="TableColumn.Curve"/>) and stops being a
    /// formula. Throws <see cref="InvalidDataException"/>, saying why, if the column doesn't hold numbers or the curve
    /// can't be generated.
    /// </summary>
    public Table Generate(int index, Curve curve)
    {
        var column = Columns[index];
        if (column.Type is not (ColumnType.Number or ColumnType.WholeNumber))
            throw new InvalidDataException($"A curve fills numbers, and {column.Name} holds {TableColumn.TypeName(column.Type)}: make it a number or whole number column first.");
        var values = curve.Values();

        var columns = Columns.ToList();
        columns[index] = column with { Formula = null, Curve = curve };
        var table = With(columns);
        for (var level = 1; level <= values.Count; level++)
        {
            var name = level.ToString(CultureInfo.InvariantCulture);
            if (level <= table.Rows.Count)
            {
                var row = table.Rows[level - 1];
                if (string.Equals(row.Name, $"Row {level}", StringComparison.OrdinalIgnoreCase) && !table.HasRow(name))
                    table = table.RenameRow(level - 1, name);
                continue;
            }

            if (table.HasRow(name))
                name = $"Level {level}";
            for (var n = 2; table.HasRow(name); n++)
                name = $"Level {level} ({n})";
            table = table.InsertRow(-1, name);
        }

        var cells = values.Select((value, i) => (i, index, column.Type == ColumnType.WholeNumber
            ? TableValue.Of((long)Math.Round(value, MidpointRounding.AwayFromZero))
            : TableValue.Of(value)));
        return table.SetCells(cells);
    }

    private static string Article(TableColumn column) => column.Type switch
    {
        ColumnType.Choice => $"one of {string.Join(", ", column.Choices)}",
        ColumnType.YesNo => "yes or no",
        ColumnType.Dice => "dice",
        _ => (TableColumn.TypeName(column.Type) is var name && "aeiou".Contains(name[0]) ? "an " : "a ") + TableColumn.TypeName(column.Type),
    };
}

/// <summary>
/// A row of a <see cref="Table"/>: its name and a value in each column, read by column name:
/// <c>float damage = row["Damage"];</c>. A renamed column still answers to its old names.
/// </summary>
public sealed class TableRow
{
    internal TableRow(Table table, string name, int index, TableValue[] cells)
    {
        Table = table;
        Name = name;
        Index = index;
        Cells = cells;
    }

    /// <summary>The table it's in.</summary>
    public Table Table { get; }

    /// <summary>Its name ("Fireball").</summary>
    public string Name { get; }

    /// <summary>Where it is in its table, from 0 at the top.</summary>
    public int Index { get; }

    /// <summary>Its values, one per column of its table, in the columns' order.</summary>
    public IReadOnlyList<TableValue> Cells { get; }

    /// <summary>
    /// The value in the column called <paramref name="column"/>: <c>float damage = row["Damage"];</c>. Throws, naming the
    /// columns there are, if the table has no such column.
    /// </summary>
    public TableValue this[string column] =>
        Table.ColumnIndex(column) is >= 0 and var index ? Cells[index] : throw new KeyNotFoundException(Table.NoColumn(column));

    /// <summary>The value in the column at <paramref name="column"/>, from 0 at the left.</summary>
    public TableValue this[int column] => Cells[column];

    /// <summary>The value in the column called <paramref name="column"/>, or false if the table has no such column.</summary>
    public bool TryGet(string column, out TableValue value)
    {
        var index = Table.ColumnIndex(column);
        value = index >= 0 ? Cells[index] : TableValue.Empty;
        return index >= 0;
    }

    /// <summary>The row as a new <typeparamref name="T"/>, its properties set from the columns of the same names (see <see cref="TableSet.Get{T}"/>).</summary>
    public T To<T>() where T : new() => TableMapping.Create<T>(this);

    public override string ToString() => $"{Table.Name}: {Name}";
}
