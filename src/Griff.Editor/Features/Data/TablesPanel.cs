using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Data;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Data;

/// <summary>
/// The Tables panel: a spreadsheet for the project's tables of game data (items, spells, enemies, level curves). Pick a
/// table (<see cref="TableName"/>), then edit it as a sheet: type into cells, add, delete, rename and reorder rows and
/// columns, sort, filter, fill down, and copy and paste with Excel and Google Sheets (tab-separated text, both ways). Each
/// change is saved to the table's file straight away, reaches the running game as its next step begins, and can be
/// undone. Rows are named by <c>row</c> and columns by <c>column</c>; "" (or "Name") as a column is the rows' names.
/// A column can be worked out by a formula (<see cref="SetFormula"/>; cells show what it works out, <see cref="Values"/>
/// and <see cref="Cell"/> read it, <see cref="FormulaProblems"/> says what's wrong), or generated from a curve, a row a
/// level (<see cref="GenerateCurve"/>, or <see cref="Curve"/>'s settings and Generate). <see cref="Chart"/> charts the
/// picked column over the rows or levels.
/// </summary>
public sealed partial class TablesPanel : ToolPanel, IDocumentEditor, IDisposable
{
    private readonly TableStore _store;
    private readonly IProjectSession _projectSession;
    private readonly IStatusService _status;
    private readonly IClipboard _clipboard;
    private string? _name;
    private string _filter = "";
    private string? _message;
    private string _columnsSignature = "";
    private List<(string Row, int Column)> _selection = [];
    private TableColumnView? _selectedColumn;
    private int _pickedColumn = -2;
    private bool _typing;

    public TablesPanel(TableStore store, IProjectSession projectSession, IStatusService status, IClipboard clipboard)
    {
        _store = store;
        _projectSession = projectSession;
        _status = status;
        _clipboard = clipboard;
        store.Changed += OnStoreChanged;
        Curve = new TableCurveModel(this);
        Curve.PropertyChanged += (_, _) => UpdateChart();
        _name = store.FirstName;
        Refresh();
    }

    private void OnStoreChanged(object? sender, string name)
    {
        OnPropertyChanged(nameof(TableNames));
        if (string.IsNullOrEmpty(name) || string.Equals(name, _name, StringComparison.OrdinalIgnoreCase) || _name is null)
        {
            if (_name is not null && _store.Entry(_name) is null)
                _name = null;
            _name ??= _store.FirstName;
            // A change typed into the grid is shown once the grid has finished with it, so it isn't changed under its feet.
            if (_typing)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(Refresh, System.Windows.Threading.DispatcherPriority.Background);
            else
                Refresh();
        }
    }

    /// <summary>Stops following the project's tables (a table's tab has closed).</summary>
    void IDisposable.Dispose() => _store.Changed -= OnStoreChanged;

    public override string Id => "Tables";
    public override string Title => "Tables";
    public override string? Icon => Icons.Table;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 3;

    /// <summary>Raised when the cells picked change from the panel's side (an agent's <see cref="Select"/>, a paste), so the grid shows them.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the columns change (added, removed, renamed, retyped, moved), so the grid lays them out again.</summary>
    public event EventHandler? ColumnsChanged;

    // ----- The table

    /// <summary>The project's tables, to pick from.</summary>
    public IReadOnlyList<string> TableNames => _store.Names;

    /// <summary>The table showing ("Spells"); setting it shows another.</summary>
    public string? TableName
    {
        get => _name;
        set => Open(value);
    }

    /// <summary>The table showing, or null if there's none or its file doesn't read.</summary>
    public Table? Table => _store.Find(_name);

    /// <summary>Why the table's file doesn't read, or null.</summary>
    public string? Problem => _store.Entry(_name)?.Problem;

    public bool HasProblem => Problem is not null;

    public bool HasTable => Table is not null;

    /// <summary>True when the project has no tables yet.</summary>
    public bool IsEmpty => _store.Names.Count == 0;

    /// <summary>Its file's path in Assets ("Tables/Spells.table").</summary>
    public string? AssetPath => _store.Entry(_name)?.AssetPath;

    /// <summary>Shows the table called <paramref name="table"/> (or at that path in Assets). False if there's none.</summary>
    public bool Open(string? table)
    {
        var entry = _store.Entry(table);
        if (entry is null)
        {
            if (table is not null)
                Message = $"There's no table called {table}.";
            return false;
        }

        if (!string.Equals(entry.Name, _name, StringComparison.Ordinal))
        {
            _name = entry.Name;
            _selection = [];
            _pickedColumn = -2;
            _filter = "";
            OnPropertyChanged(nameof(Filter));
            Message = null;
            Refresh();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>Its columns, left to right.</summary>
    public ObservableCollection<TableColumnView> Columns { get; } = [];

    /// <summary>Its rows that pass the <see cref="Filter"/>, top to bottom.</summary>
    public ObservableCollection<TableRowView> Rows { get; } = [];

    /// <summary>Shows only the rows with this in their name or one of their cells (any capitals); empty shows them all.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? ""))
                Refresh();
        }
    }

    /// <summary>How many rows and columns it has, and how many rows pass the filter: "12 rows, 5 columns".</summary>
    public string Summary => Table is not { } table ? ""
        : $"{Count(table.Rows.Count, "row")}, {Count(table.Columns.Count, "column")}" +
          (Rows.Count < table.Rows.Count ? $" ({Rows.Count} shown)" : "");

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";

    /// <summary>What the last change said: what it did, or why it couldn't (a cell that takes a number given text).</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message is not null;

    /// <summary>The column the column bar shows (where the picked cell is), to rename it or change its type, or null.</summary>
    public TableColumnView? SelectedColumn
    {
        get => _selectedColumn;
        private set
        {
            if (SetProperty(ref _selectedColumn, value))
                OnPropertyChanged(nameof(HasSelectedColumn));
        }
    }

    public bool HasSelectedColumn => SelectedColumn is not null;

    /// <summary>The column types to pick from, as tables write them ("number", "whole number", "yes/no"...).</summary>
    public IReadOnlyList<string> Types => TableColumn.TypeNames;

    // ----- Showing it

    /// <summary>True while it brings what it shows up to date with the table, which the grid takes as no picking of its own.</summary>
    public bool IsRefreshing { get; private set; }

    /// <summary>Raised after it has brought what it shows up to date with the table, so the grid shows the picked cells again.</summary>
    public event EventHandler? Refreshed;

    private void Refresh()
    {
        IsRefreshing = true;
        try
        {
            RefreshShown();
        }
        finally
        {
            IsRefreshing = false;
        }

        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshShown()
    {
        OnPropertyChanged(nameof(TableName));
        OnPropertyChanged(nameof(Table));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(HasTable));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AssetPath));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));

        OnPropertyChanged(nameof(Computed));
        OnPropertyChanged(nameof(FormulaProblems));
        OnPropertyChanged(nameof(HasFormulaProblems));
        OnPropertyChanged(nameof(FormulaProblemSummary));

        // The rows show what formulas work out to.
        var table = Computed;
        var signature = table is null ? "" : string.Join("|", table.Columns.Select(c =>
            $"{c.Name}:{c.Type}:{string.Join(",", c.Choices)}:{c.Table}:{c.Formula}:{(c.Curve is null ? "" : TableFile.Write(new Table("", [c], [])))}"));
        var columnsChanged = signature != _columnsSignature || Columns.Count != (table?.Columns.Count ?? 0);
        if (columnsChanged)
        {
            _columnsSignature = signature;
            Columns.Clear();
            for (var i = 0; i < (table?.Columns.Count ?? 0); i++)
                Columns.Add(new TableColumnView(this, i, table!.Columns[i]));
        }

        var shown = table?.Rows.Where(Passes).ToList() ?? [];
        // As many rows as before, under the same columns (a cell set, a row renamed, the rows sorted): the rows shown take
        // their new values in place, so the grid keeps its place and what's picked rather than starting over.
        if (!columnsChanged && Rows.Count == shown.Count)
        {
            for (var i = 0; i < shown.Count; i++)
                Rows[i].Update(shown[i]);
        }
        else
        {
            Rows.Clear();
            foreach (var row in shown)
                Rows.Add(new TableRowView(this, row));
        }

        // Picked cells that are gone (their row deleted or filtered out, their column removed) are let go.
        _selection = [.. _selection.Where(c => shown.Any(r => string.Equals(r.Name, c.Row, StringComparison.OrdinalIgnoreCase)) && c.Column < Columns.Count)];
        if (_pickedColumn >= Columns.Count)
            _pickedColumn = -2;
        UpdateSelectedColumn();
        OnPropertyChanged(nameof(Summary));
        if (columnsChanged)
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        UpdateChart();
    }

    private bool Passes(TableRow row)
    {
        var filter = _filter.Trim();
        return filter.Length == 0 || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Cells.Any(c => c.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateSelectedColumn()
    {
        var column = CurrentColumnIndex;
        SelectedColumn = column >= 0 && column < Columns.Count ? Columns[column] : null;
        Curve.Load(SelectedColumn?.Column, Table);
        UpdateChart();
    }

    // ----- Picking cells

    /// <summary>The cells picked, by row name and column index (-1 for the rows' names).</summary>
    public IReadOnlyList<(string Row, int Column)> Selection => _selection;

    /// <summary>The row the picked cell is in (the first picked), or null.</summary>
    public string? CurrentRow => _selection.Count > 0 ? Table?.Find(_selection[0].Row)?.Name : null;

    /// <summary>The column the picked cell is in ("" for the rows' names), or null.</summary>
    public string? CurrentColumn => _selection.Count == 0 ? null : _selection[0].Column < 0 ? "" : Table?.Columns.ElementAtOrDefault(_selection[0].Column)?.Name;

    private int CurrentColumnIndex => _selection.Count > 0 ? _selection[0].Column : _pickedColumn;

    /// <summary>
    /// Picks a cell, or the block of cells from it to another: <c>Select("Fireball", "Damage")</c>,
    /// <c>Select("Fireball", "Damage", "Frost Bolt", "Cost")</c>. A column of "" is the rows' names. What copy, paste,
    /// fill down, clear and the rest act on.
    /// </summary>
    public void Select(string row, string column, string? toRow = null, string? toColumn = null)
    {
        var table = Table ?? throw new InvalidOperationException("No table is showing.");
        var (r1, c1) = (ShownIndex(row), ColumnOf(table, column));
        var (r2, c2) = (toRow is null ? r1 : ShownIndex(toRow), toColumn is null ? c1 : ColumnOf(table, toColumn));
        var cells = new List<(string, int)>();
        for (var r = Math.Min(r1, r2); r <= Math.Max(r1, r2); r++)
        {
            for (var c = Math.Min(c1, c2); c <= Math.Max(c1, c2); c++)
                cells.Add((Rows[r].Name, c));
        }

        // The cell named first comes first: it's where a paste goes.
        cells.Remove((Rows[r1].Name, c1));
        cells.Insert(0, (Rows[r1].Name, c1));
        _selection = cells;
        _pickedColumn = c1;
        UpdateSelectedColumn();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Picks a whole column ("" for the rows' names), as clicking its heading does: every shown row's cell in it, and the
    /// column the column bar shows, even with no rows yet.
    /// </summary>
    public void SelectColumn(string column)
    {
        var table = Table ?? throw new InvalidOperationException("No table is showing.");
        var index = ColumnOf(table, column);
        _selection = [.. Rows.Select(r => (r.Name, index))];
        _pickedColumn = index;
        UpdateSelectedColumn();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Picks every cell of the rows shown, names and all.</summary>
    public void SelectAll()
    {
        if (Rows.Count == 0)
            return;
        Select(Rows[0].Name, "", Rows[^1].Name, Columns.Count > 0 ? Columns[^1].Name : "");
    }

    /// <summary>The grid's picked cells, as it reports them (row name, column index; -1 for the names). Not for an agent: use <see cref="Select"/>.</summary>
    internal void SetSelection(IReadOnlyList<(string Row, int Column)> cells, (string Row, int Column)? current)
    {
        var list = cells.ToList();
        if (current is { } first && list.Remove(first))
            list.Insert(0, first);
        _selection = list;
        if (list.Count > 0)
            _pickedColumn = list[0].Column;
        UpdateSelectedColumn();
    }

    private int ShownIndex(string row)
    {
        var table = Table!;
        var found = table.Find(row) ?? throw new ArgumentException(table.NoRow(row));
        var index = Rows.ToList().FindIndex(r => r.Index == found.Index);
        return index >= 0 ? index : throw new ArgumentException($"{found.Name} is filtered out; clear the filter first.");
    }

    private static int ColumnOf(Table table, string? column)
    {
        if (string.IsNullOrWhiteSpace(column) || (table.ColumnIndex(column) < 0 && string.Equals(column.Trim(), "Name", StringComparison.OrdinalIgnoreCase)))
            return -1;
        var index = table.ColumnIndex(column);
        return index >= 0 ? index : throw new ArgumentException(table.NoColumn(column));
    }

    // The picked block: shown rows (top to bottom) and columns (left to right, -1 the names), or nothing.
    private (List<TableRowView> Rows, List<int> Columns) Block()
    {
        if (_selection.Count == 0)
            return ([], []);
        var shownIndexes = _selection.Select(c => Rows.ToList().FindIndex(r => string.Equals(r.Name, c.Row, StringComparison.OrdinalIgnoreCase))).Where(i => i >= 0).ToList();
        if (shownIndexes.Count == 0)
            return ([], []);
        var rows = Rows.Skip(shownIndexes.Min()).Take(shownIndexes.Max() - shownIndexes.Min() + 1).ToList();
        var columns = Enumerable.Range(_selection.Min(c => c.Column), _selection.Max(c => c.Column) - _selection.Min(c => c.Column) + 1).ToList();
        return (rows, columns);
    }

    // ----- Changing it

    private Table Current => Table ?? throw new InvalidOperationException(Problem is { } problem ? $"The {_name} table doesn't read: {problem}" : "No table is showing.");

    private void Commit(Table after, string description, string? message = null)
    {
        _store.Commit(after, description);
        Edited?.Invoke(this, EventArgs.Empty);
        Message = message;
        if (message is not null)
            _status.Show(message);
    }

    // A change the table refuses (a name used twice, a cell that won't convert) is said, not thrown, to someone typing.
    private bool Try(Func<Table> change, string description, string? message = null)
    {
        try
        {
            Commit(change(), description, message);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Message = ex.Message;
            _status.Show(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Sets a cell from text as it would be typed: <c>SetCell("Fireball", "Damage", "14")</c>. A column of "" renames the
    /// row. Text that doesn't fit the column (a word in a number column) is refused, saying what it takes. False if refused.
    /// </summary>
    public bool SetCell(string row, string column, string value)
    {
        var table = Current;
        var index = table.RowIndex(row);
        if (index < 0)
            throw new ArgumentException(table.NoRow(row));
        return SetCellAt(index, ColumnOf(table, column), value);
    }

    /// <summary>
    /// The text of a cell, as it shows: <c>Cell("Fireball", "Damage")</c>; a column of "" gives the row's name. A formula's
    /// cell gives what it works out to, or #loop, #ref or #error if it can't (<see cref="FormulaProblems"/> says why).
    /// </summary>
    public string Cell(string row, string column)
    {
        var table = Computed ?? Current;
        var found = table.Find(row) ?? throw new ArgumentException(table.NoRow(row));
        var index = ColumnOf(table, column);
        if (index < 0)
            return found.Name;
        return _store.Evaluation.ProblemAt(table.Name, found.Index, index)?.Mark ?? found[index].ToString();
    }

    // What the grid types in is shown once the grid has finished committing it.
    internal void Typed(Action change)
    {
        _typing = true;
        try
        {
            change();
        }
        finally
        {
            _typing = false;
        }
    }

    internal bool SetCellAt(int row, int column, string? text)
    {
        var table = Current;
        if (column < 0)
            return RenameRow(table.Rows[row].Name, text ?? "");
        var definition = table.Columns[column];
        // "=..." makes the column a formula, as in a spreadsheet's table; a formula's own cells aren't typed into.
        if (text?.TrimStart().StartsWith('=') == true)
            return SetFormula(definition.Name, text);
        if (definition.Formula is not null)
        {
            if (string.Equals(text?.Trim(), Computed?.Rows[row].Cells[column].ToString(), StringComparison.Ordinal))
                return true;
            Message = $"{definition.Name} is worked out by its formula (={definition.Formula}): change the formula above the grid, or clear it to type values.";
            _status.Show(Message);
            return false;
        }

        if (!definition.TryRead(text, out var value, out var problem))
        {
            Message = problem + ".";
            _status.Show(Message);
            return false;
        }

        if (table.Rows[row].Cells[column] == value)
            return true;
        Commit(table.SetCell(row, column, value), $"Set {table.Rows[row].Name}'s {definition.Name}");
        return true;
    }

    /// <summary>
    /// Adds a row: called <paramref name="name"/> (or "Row 4" and so on), after the picked row (or at the bottom), and picks
    /// its name. Returns its name.
    /// </summary>
    public string AddRow(string? name = null)
    {
        var table = Current;
        name = string.IsNullOrWhiteSpace(name) ? UniqueRowName(table, "Row") : name.Trim();
        var at = CurrentRow is { } current ? table.RowIndex(current) + 1 : table.Rows.Count;
        if (!Try(() => table.InsertRow(at, name), $"Add the row {name}", $"Added the row {name}."))
            return name;
        if (Rows.Any(r => r.Name == name))
            Select(name, "");
        return name;
    }

    private static string UniqueRowName(Table table, string stem)
    {
        for (var n = table.Rows.Count + 1; ; n++)
        {
            var name = $"{stem} {n}";
            if (!table.HasRow(name))
                return name;
        }
    }

    /// <summary>Deletes the picked rows (every row with a picked cell).</summary>
    public void DeleteRows()
    {
        var table = Current;
        var rows = _selection.Select(c => table.RowIndex(c.Row)).Where(i => i >= 0).Distinct().ToList();
        if (rows.Count == 0)
        {
            Message = "Pick a row to delete first.";
            return;
        }

        var names = rows.Select(i => table.Rows[i].Name).ToList();
        _selection = [];
        Commit(table.RemoveRows(rows), names.Count == 1 ? $"Delete the row {names[0]}" : $"Delete {names.Count} rows",
            names.Count == 1 ? $"Deleted the row {names[0]}." : $"Deleted {names.Count} rows.");
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes the row called <paramref name="row"/>.</summary>
    public void DeleteRow(string row)
    {
        var table = Current;
        var index = table.RowIndex(row);
        if (index < 0)
            throw new ArgumentException(table.NoRow(row));
        Commit(table.RemoveRows([index]), $"Delete the row {table.Rows[index].Name}", $"Deleted the row {table.Rows[index].Name}.");
    }

    /// <summary>
    /// Renames a row. What names it by its old name (a Table Values component, a script, another table's row column)
    /// still finds it: the table remembers the old name. False if the name is taken.
    /// </summary>
    public bool RenameRow(string row, string name)
    {
        var table = Current;
        var index = table.RowIndex(row);
        if (index < 0)
            throw new ArgumentException(table.NoRow(row));
        name = name.Trim();
        var old = table.Rows[index].Name;
        if (name == old)
            return true;
        if (name.Length == 0)
        {
            Message = "A row needs a name.";
            return false;
        }

        if (table.Rows.Any(r => r.Index != index && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"There's a row called {name} already.";
            return false;
        }

        var renamed = Try(() => table.RenameRow(index, name), $"Rename {old} to {name}", $"Renamed {old} to {name}; what names it {old} still finds it.");
        if (renamed)
            _selection = [.. _selection.Select(c => string.Equals(c.Row, old, StringComparison.OrdinalIgnoreCase) ? (name, c.Column) : c)];
        return renamed;
    }

    /// <summary>Moves the picked rows up (<paramref name="by"/> -1) or down (1) a place.</summary>
    public void MoveRows(int by)
    {
        var table = Current;
        var picked = _selection.Select(c => table.RowIndex(c.Row)).Where(i => i >= 0).Distinct().Order().ToList();
        if (picked.Count == 0 || by == 0)
            return;
        var order = Enumerable.Range(0, table.Rows.Count).ToList();
        if (by < 0 && picked[0] == 0 || by > 0 && picked[^1] == table.Rows.Count - 1)
            return;
        foreach (var index in by < 0 ? picked : Enumerable.Reverse(picked))
        {
            var at = order.IndexOf(index);
            (order[at], order[at + Math.Sign(by)]) = (order[at + Math.Sign(by)], order[at]);
        }

        Commit(table.ReorderRows(order), picked.Count == 1 ? $"Move {table.Rows[picked[0]].Name} {(by < 0 ? "up" : "down")}" : $"Move {picked.Count} rows");
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the row called <paramref name="row"/> to <paramref name="index"/> (0 is the top).</summary>
    public void MoveRow(string row, int index)
    {
        var table = Current;
        var from = table.RowIndex(row);
        if (from < 0)
            throw new ArgumentException(table.NoRow(row));
        Commit(table.MoveRow(from, index), $"Move {table.Rows[from].Name}");
    }

    /// <summary>
    /// Adds a column called <paramref name="name"/> after the picked column (or at the right): of <paramref name="type"/>
    /// ("number", "whole number", "text", "yes/no", "choice", "dice", "sprite", "sound", "prefab" or "row"), with its
    /// <paramref name="choices"/> (comma-separated) for a choice, or the <paramref name="table"/> its rows are in for a row.
    /// </summary>
    public bool AddColumn(string name, string type = "number", string? choices = null, string? table = null)
    {
        var current = Current;
        var column = Define(name, type, choices, table);
        if (current.Columns.Any(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"There's a column called {column.Name} already.";
            return false;
        }

        var at = CurrentColumnIndex >= 0 ? CurrentColumnIndex + 1 : current.Columns.Count;
        if (!Try(() => current.InsertColumn(at, column), $"Add the column {column.Name}", $"Added the column {column.Name}."))
            return false;
        if (Rows.Count > 0)
            Select(Rows[0].Name, column.Name);
        return true;
    }

    private static TableColumn Define(string name, string type, string? choices, string? table)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A column needs a name.");
        if (!TableColumn.TryParseType(type, out var kind))
            throw new ArgumentException($"There's no column type \"{type}\"; the types are {string.Join(", ", TableColumn.TypeNames)}.");
        return new TableColumn(name.Trim(), kind)
        {
            Choices = SplitChoices(choices),
            Table = kind == ColumnType.Row && !string.IsNullOrWhiteSpace(table) ? table.Trim() : null,
        };
    }

    private static List<string> SplitChoices(string? choices) =>
        [.. (choices ?? "").Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Deletes a column (the picked one when <paramref name="column"/> is null), cells and all.</summary>
    public void DeleteColumn(string? column = null)
    {
        var table = Current;
        var index = column is null ? CurrentColumnIndex : ColumnOf(table, column);
        if (index < 0)
        {
            Message = column is null ? "Pick a cell in the column to delete first." : "The rows' names can't be deleted.";
            return;
        }

        var name = table.Columns[index].Name;
        Commit(table.RemoveColumn(index), $"Delete the column {name}", $"Deleted the column {name}.");
    }

    /// <summary>
    /// Changes a column: renames it (what names it by its old name still finds it), or changes its type, choices or the
    /// table its rows are in. Its cells are converted to the new type, or the change is refused, saying which don't fit.
    /// Leave out what stays as it is.
    /// </summary>
    public bool ChangeColumn(string column, string? name = null, string? type = null, string? choices = null, string? table = null)
    {
        var current = Current;
        var index = ColumnOf(current, column);
        if (index < 0)
        {
            Message = "The rows' names aren't a column to change; rename rows instead.";
            return false;
        }

        var old = current.Columns[index];
        var kind = old.Type;
        if (type is not null && !TableColumn.TryParseType(type, out kind))
            throw new ArgumentException($"There's no column type \"{type}\"; the types are {string.Join(", ", TableColumn.TypeNames)}.");
        var changed = old with
        {
            Name = string.IsNullOrWhiteSpace(name) ? old.Name : name.Trim(),
            Type = kind,
            Choices = choices is null ? old.Choices : SplitChoices(choices),
            Table = kind != ColumnType.Row ? null : table is null ? old.Table : string.IsNullOrWhiteSpace(table) ? null : table.Trim(),
        };

        // A column made a choice takes the values its cells have as its choices, if it has none yet.
        if (changed.Type == ColumnType.Choice && changed.Choices.Count == 0)
            changed = changed with { Choices = [.. current.Rows.Select(r => r[index].ToString()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)] };
        if (changed == old)
            return true;
        if (!string.Equals(changed.Name, old.Name, StringComparison.OrdinalIgnoreCase) && current.Columns.Any(c => c != old && string.Equals(c.Name, changed.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"There's a column called {changed.Name} already.";
            return false;
        }

        var what = changed.Name != old.Name ? $"Rename {old.Name} to {changed.Name}"
            : changed.Type != old.Type ? $"Make {old.Name} {TableColumn.TypeName(changed.Type)}"
            : $"Change {old.Name}";
        return Try(() => current.ChangeColumn(index, changed), what, what.Replace("Rename", "Renamed", StringComparison.Ordinal).Replace("Make", "Made", StringComparison.Ordinal) + ".");
    }

    /// <summary>Moves the picked column left (<paramref name="by"/> -1) or right (1) a place.</summary>
    public void MoveColumnBy(int by)
    {
        var table = Current;
        var index = CurrentColumnIndex;
        var to = index + Math.Sign(by);
        if (index < 0 || to < 0 || to >= table.Columns.Count)
            return;
        var name = table.Columns[index].Name;
        Commit(table.MoveColumn(index, to), $"Move {name} {(by < 0 ? "left" : "right")}");
        _selection = [.. _selection.Select(c => (c.Row, c.Column == index ? to : c.Column == to ? index : c.Column))];
        UpdateSelectedColumn();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the column called <paramref name="column"/> to <paramref name="index"/> (0 is the left).</summary>
    public void MoveColumn(string column, int index)
    {
        var table = Current;
        var from = ColumnOf(table, column);
        if (from < 0)
            throw new ArgumentException("The rows' names stay at the left.");
        Commit(table.MoveColumn(from, index), $"Move {table.Columns[from].Name}");
    }

    /// <summary>
    /// Sorts the rows by a column (the picked one when <paramref name="column"/> is null; "" for their names): numbers by
    /// size, text alphabetically, choices in the order of their choices, empty cells last.
    /// </summary>
    public void Sort(string? column = null, bool descending = false)
    {
        var table = Current;
        var index = column is null ? Math.Max(CurrentColumnIndex, -1) : ColumnOf(table, column);
        var by = index < 0 ? "name" : table.Columns[index].Name;
        // By what formulas work out to.
        var order = (Computed ?? table).SortOrder(index, descending);
        Commit(table.ReorderRows(order), $"Sort by {by}", $"Sorted by {by}{(descending ? ", highest first" : "")}.");
    }

    /// <summary>
    /// Fills down: each picked column's top picked cell is copied into the picked cells under it, or with one row picked,
    /// the cell above is copied into it (Ctrl+D in a spreadsheet).
    /// </summary>
    public void FillDown()
    {
        var table = Current;
        var (rows, columns) = Block();
        // A formula's cells are worked out, not filled.
        columns.RemoveAll(c => c < 0 || table.Columns[c].Formula is not null);
        if (rows.Count == 0 || columns.Count == 0)
            return;
        var cells = new List<(int, int, TableValue)>();
        if (rows.Count == 1)
        {
            var above = Rows.IndexOf(rows[0]) - 1;
            if (above < 0)
                return;
            cells.AddRange(columns.Select(c => (rows[0].Index, c, table.Rows[Rows[above].Index].Cells[c])));
        }
        else
        {
            foreach (var column in columns)
                cells.AddRange(rows.Skip(1).Select(r => (r.Index, column, table.Rows[rows[0].Index].Cells[column])));
        }

        Commit(table.SetCells(cells), "Fill down");
    }

    /// <summary>Empties the picked cells (not the rows' names).</summary>
    public void ClearCells()
    {
        var table = Current;
        var cells = _selection.Where(c => c.Column >= 0 && table.Columns[c.Column].Formula is null)
            .Select(c => (table.RowIndex(c.Row), c.Column, TableValue.Empty)).Where(c => c.Item1 >= 0).ToList();
        if (cells.Count > 0)
            Commit(table.SetCells(cells), cells.Count == 1 ? "Clear a cell" : $"Clear {cells.Count} cells");
    }

    // ----- Copy and paste, with spreadsheets

    /// <summary>
    /// Copies the picked cells as a spreadsheet does, as tab-separated text (a line a row), to the clipboard and returns it,
    /// to paste into Excel, Google Sheets or another table. Picking every row copies the column headings too.
    /// </summary>
    public string Copy()
    {
        // What formulas work out to, as a spreadsheet pastes values into another program.
        var table = Computed ?? Current;
        var (rows, columns) = Block();
        if (rows.Count == 0)
            return "";
        var lines = new List<IReadOnlyList<string>>();
        if (rows.Count == Rows.Count && Rows.Count > 1)
            lines.Add([.. columns.Select(c => c < 0 ? NameHeading(table) : table.Columns[c].Name)]);
        lines.AddRange(rows.Select(r => (IReadOnlyList<string>)[.. columns.Select(c => c < 0 ? r.Name : table.Rows[r.Index].Cells[c].ToString())]));
        var text = TableText.Format(lines, '\t');
        _clipboard.SetText(text);
        Message = $"Copied {Count(rows.Count * columns.Count, "cell")}.";
        return text;
    }

    private static string NameHeading(Table table) =>
        table.Columns.Any(c => string.Equals(c.Name, "Name", StringComparison.OrdinalIgnoreCase)) ? "Row" : "Name";

    /// <summary>Copies the picked cells (see <see cref="Copy"/>), then empties them.</summary>
    public string Cut()
    {
        var text = Copy();
        ClearCells();
        Message = "Cut.";
        return text;
    }

    /// <summary>
    /// Pastes tab-separated text (from the clipboard, or <paramref name="text"/>), as copied from Excel, Google Sheets or a
    /// table, at the picked cell: each line into a row and each tab-separated value into a column from there, adding rows
    /// past the bottom (named by the pasted names, if the rows' names are pasted into, or "Row 5" and so on). One value
    /// pasted with several cells picked goes in all of them. Into a table with no rows yet, the first line names the
    /// columns, as importing CSV does. Values that don't fit their column are left out, and said. One undoable step.
    /// </summary>
    public string Paste(string? text = null)
    {
        var table = Current;
        text ??= _clipboard.GetText();
        if (string.IsNullOrEmpty(text))
            return Message = "There's nothing to paste.";
        var block = TableText.Parse(text, '\t');
        if (block.Count == 0)
            return Message = "There's nothing to paste.";

        // A sheet pasted into an empty table brings its columns.
        if (table.Rows.Count == 0 && block.Count > 1)
        {
            var pasted = TableText.FromCsv(text, table.Name, table);
            Commit(new Table(table.Name, pasted.Columns, pasted.Rows.Select(r => (r.Name, (IReadOnlyList<TableValue>)r.Cells)), table.RenamedRows, table.RenamedColumns, table.Kept),
                "Paste a table", $"Pasted {Count(pasted.Rows.Count, "row")} and {Count(pasted.Columns.Count, "column")}.");
            if (Rows.Count > 0)
                Select(Rows[0].Name, Columns.Count > 0 ? Columns[0].Name : "");
            return Message!;
        }

        var startRow = _selection.Count > 0 ? Rows.ToList().FindIndex(r => string.Equals(r.Name, _selection[0].Row, StringComparison.OrdinalIgnoreCase)) : 0;
        var startColumn = _selection.Count > 0 ? _selection[0].Column : table.Columns.Count > 0 ? 0 : -1;
        startRow = Math.Max(startRow, 0);

        // Headings copied along with every row (see Copy) aren't pasted as a row.
        var headings = Enumerable.Range(startColumn, block[0].Count).Select(c => c < 0 ? NameHeading(table) : c < table.Columns.Count ? table.Columns[c].Name : null).ToList();
        if (block.Count > 1 && block[0].Select(h => h.Trim()).SequenceEqual(headings.Select(h => h ?? "\u0000"), StringComparer.OrdinalIgnoreCase))
            block.RemoveAt(0);

        // One value, several cells picked: it goes in each.
        var targets = new List<(int ShownRow, int Column, string Text, int Line)>();
        if (block.Count == 1 && block[0].Count == 1 && _selection.Count > 1)
        {
            foreach (var (row, column) in _selection)
                targets.Add((Rows.ToList().FindIndex(r => string.Equals(r.Name, row, StringComparison.OrdinalIgnoreCase)), column, block[0][0], 0));
        }
        else
        {
            for (var line = 0; line < block.Count; line++)
            {
                for (var c = 0; c < block[line].Count; c++)
                    targets.Add((startRow + line, startColumn + c, block[line][c], line));
            }
        }

        var problems = new List<string>();
        var skipped = targets.Count(t => t.Column >= table.Columns.Count);
        var data = table;
        // Rows to add past the bottom, one per pasted line that reaches past it.
        var shownIndexes = Rows.Select(r => r.Index).ToList();
        var added = new Dictionary<int, int>();
        foreach (var line in targets.Where(t => t.ShownRow >= shownIndexes.Count).Select(t => t.Line).Distinct().Order())
        {
            var named = targets.FirstOrDefault(t => t.Line == line && t.Column == -1).Text?.Trim();
            var name = !string.IsNullOrEmpty(named) && !data.HasRow(named) ? named : UniqueRowName(data, "Row");
            data = data.InsertRow(-1, name);
            added[line] = data.Rows.Count - 1;
        }

        var cells = new List<(int, int, TableValue)>();
        var renames = new List<(int Row, string Name)>();
        foreach (var target in targets.Where(t => t.Column < table.Columns.Count && t.ShownRow >= 0))
        {
            var row = target.ShownRow < shownIndexes.Count ? shownIndexes[target.ShownRow] : added[target.Line];
            if (target.Column < 0)
            {
                if (target.ShownRow < shownIndexes.Count && target.Text.Trim().Length > 0)
                    renames.Add((row, target.Text.Trim()));
                continue;
            }

            if (data.Columns[target.Column].Formula is not null)
            {
                // Pasting what it works out to back over it changes nothing; anything else is said.
                if (target.ShownRow >= shownIndexes.Count || Computed?.Rows[row].Cells[target.Column].ToString() != target.Text.Trim())
                    problems.Add($"{data.Rows[row].Name}: {data.Columns[target.Column].Name} is worked out by its formula");
                continue;
            }

            if (data.Columns[target.Column].TryRead(target.Text, out var value, out var problem))
                cells.Add((row, target.Column, value));
            else
                problems.Add($"{data.Rows[row].Name}: {problem} (not \"{target.Text}\")");
        }

        data = data.SetCells(cells);
        foreach (var (row, name) in renames)
        {
            if (string.Equals(data.Rows[row].Name, name, StringComparison.Ordinal))
                continue;
            if (data.HasRow(name) && data.RowIndex(name) != row)
                problems.Add($"{data.Rows[row].Name} wasn't renamed {name}: there's a row called that already");
            else
                data = data.RenameRow(row, name);
        }

        var message = $"Pasted {Count(cells.Count, "cell")}{(added.Count > 0 ? $", adding {Count(added.Count, "row")}" : "")}." +
                      (skipped > 0 ? $" {Count(skipped, "value")} past the last column {(skipped == 1 ? "was" : "were")} left out." : "") +
                      (problems.Count > 0 ? $" Left out: {string.Join("; ", problems.Take(5))}{(problems.Count > 5 ? "..." : "")}." : "");
        Commit(data, "Paste", message);
        return message;
    }

    // ----- Undo

    /// <summary>The undo history of the table showing: Edit &gt; Undo and Redo take its changes back while it's the active document.</summary>
    public Griff.Editing.EditHistory? History => _store.HistoryOf(_name);

    /// <summary>The table showing, as undoing in it says it: "the Spells table".</summary>
    public string? DocumentName => _name is null ? null : $"the {_name} table";

    /// <summary>Raised after it changes the table showing, which makes it the document Edit &gt; Undo goes to.</summary>
    public event EventHandler? Edited;

    /// <summary>What Undo would undo in the table showing, e.g. "Set Fireball's Damage".</summary>
    public string? UndoDescription => History?.UndoDescription;

    /// <summary>What Redo would redo in the table showing.</summary>
    public string? RedoDescription => History?.RedoDescription;

    // ----- CSV

    /// <summary>
    /// Reads a CSV file (or tab- or semicolon-separated, as spreadsheets save them) into a table: the first line names the
    /// columns and each line after is a row, named by its first value. Into the table called <paramref name="table"/> (by
    /// default, named after the file): a table that's there already has its rows replaced (its columns keep their types,
    /// as one undoable step), otherwise a new one is made. New columns get the type their values all fit. Shows it, and
    /// returns what it did.
    /// </summary>
    public string ImportCsv(string path, string? table = null)
    {
        var name = string.IsNullOrWhiteSpace(table) ? Path.GetFileNameWithoutExtension(path) : table.Trim();
        var text = File.ReadAllText(path);
        if (_store.Entry(name) is { Value: { } existing } entry)
        {
            var imported = TableText.FromCsv(text, entry.Name, existing);
            Commit(imported, $"Import {Path.GetFileName(path)}");
            Open(entry.Name);
            return Message = $"Imported {Count(imported.Rows.Count, "row")} into {entry.Name} from {Path.GetFileName(path)}.";
        }

        if (_store.NameProblem(name) is { } problem)
            throw new ArgumentException(problem);
        var fresh = TableText.FromCsv(text, name);
        _store.Create(name);
        _store.Commit(fresh, $"Import {Path.GetFileName(path)}");
        Edited?.Invoke(this, EventArgs.Empty);
        Open(name);
        return Message = $"Made the {name} table from {Path.GetFileName(path)}: {Count(fresh.Rows.Count, "row")}, {Count(fresh.Columns.Count, "column")}.";
    }

    /// <summary>Writes the table showing to a CSV file at <paramref name="path"/>, its rows' names first, for a spreadsheet.</summary>
    public string ExportCsv(string path)
    {
        // With what its formulas work out to.
        var table = Computed ?? Current;
        File.WriteAllText(path, TableText.ToCsv(table));
        return Message = $"Wrote {table.Name} to {Path.GetFileName(path)}.";
    }

    // ----- What cells offer

    /// <summary>The values a column's cells offer to pick from: its choices, the rows of its table, or the project's pictures, sounds or prefabs.</summary>
    internal IReadOnlyList<string> OptionsFor(TableColumn column)
    {
        var project = _projectSession.Project;
        return column.Type switch
        {
            ColumnType.Choice => column.Choices,
            ColumnType.YesNo => ["yes", "no"],
            ColumnType.Row => _store.Find(column.Table ?? _name)?.Rows.Select(r => r.Name).ToList() ?? [],
            ColumnType.Prefab => project?.FindPrefabs().Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList() ?? [],
            ColumnType.Sprite => Files(project?.AssetsPath, ".png", ".jpg", ".jpeg", ".bmp", ".gif"),
            ColumnType.Sound => Files(project?.AssetsPath, ".wav", ".ogg", ".mp3"),
            _ => [],
        };
    }

    private static List<string> Files(string? assets, params string[] extensions)
    {
        if (assets is null || !Directory.Exists(assets))
            return [];
        return [.. Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetRelativePath(assets, f).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Why a cell is flagged (a picture, sound, prefab or row that isn't there, a formula that can't be worked out), or
    /// null; what dice roll; and for a formula's cell that can't be worked out, the mark it shows (#loop, #ref, #error).
    /// </summary>
    internal (bool Flagged, string? Tip, string? Mark) Check(int row, int columnIndex, TableColumn column, TableValue value)
    {
        if (column.Formula is not null && _name is not null && _store.Evaluation.ProblemAt(_name, row, columnIndex) is { } problem)
            return (true, problem.Message, problem.Mark);
        var (flagged, tip) = Check(column, value);
        if (column.Formula is not null)
            tip = (tip is null ? "" : tip + ". ") + $"Worked out by ={column.Formula}";
        return (flagged, tip, null);
    }

    private (bool Flagged, string? Tip) Check(TableColumn column, TableValue value)
    {
        if (value.IsEmpty)
            return (false, null);
        var text = value.ToString();
        var project = _projectSession.Project;
        return column.Type switch
        {
            ColumnType.Dice => (false, $"Rolls {value.AsDice.Min} to {value.AsDice.Max}, {value.AsDice.Average:0.#} on average"),
            ColumnType.Prefab when project is not null && project.FindPrefab(text) is null => (true, $"There's no prefab called \"{text}\" in the project"),
            ColumnType.Sprite or ColumnType.Sound when project is not null && !File.Exists(Path.Combine(project.AssetsPath, text.Replace('/', Path.DirectorySeparatorChar))) =>
                (true, $"There's no file {text} in Assets"),
            ColumnType.Row when _store.Find(column.Table ?? _name) is { } other && !other.HasRow(text) => (true, other.NoRow(text)),
            ColumnType.Row when _store.Find(column.Table ?? _name) is null => (true, $"There's no table called \"{column.Table}\": set the column's table"),
            _ => (false, null),
        };
    }
}

/// <summary>A column of the table showing in the Tables panel, as its header and the column bar show it.</summary>
public sealed class TableColumnView(TablesPanel owner, int index, TableColumn column) : ObservableObject
{
    /// <summary>Where it is, from 0 at the left.</summary>
    public int Index { get; } = index;

    /// <summary>Its definition.</summary>
    public TableColumn Column { get; } = column;

    /// <summary>Its name; setting it renames it (what names it by its old name still finds it).</summary>
    public string Name
    {
        get => Column.Name;
        set
        {
            owner.ChangeColumn(Column.Name, name: value);
            OnPropertyChanged();
        }
    }

    /// <summary>Its type, as tables write it ("number", "yes/no"...); setting it converts its cells, or says which don't fit.</summary>
    public string Type
    {
        get => TableColumn.TypeName(Column.Type);
        set
        {
            owner.ChangeColumn(Column.Name, type: value);
            OnPropertyChanged();
        }
    }

    /// <summary>For a choice column, its choices, comma-separated ("Fire, Ice, Lightning"); setting it changes them.</summary>
    public string Choices
    {
        get => string.Join(", ", Column.Choices);
        set
        {
            owner.ChangeColumn(Column.Name, choices: value);
            OnPropertyChanged();
        }
    }

    /// <summary>For a row column, the table its rows are in (empty: this table); setting it changes it.</summary>
    public string? Table
    {
        get => Column.Table;
        set
        {
            owner.ChangeColumn(Column.Name, table: value ?? "");
            OnPropertyChanged();
        }
    }

    public ColumnType Kind => Column.Type;

    public bool IsChoice => Column.Type == ColumnType.Choice;

    public bool IsRow => Column.Type == ColumnType.Row;

    public bool IsYesNo => Column.Type == ColumnType.YesNo;

    /// <summary>Numbers line up on the right, as in a spreadsheet.</summary>
    public bool IsNumeric => Column.Type is ColumnType.Number or ColumnType.WholeNumber or ColumnType.Dice;

    /// <summary>True when its cells pick from a list (choices, rows, pictures, sounds, prefabs).</summary>
    public bool HasOptions => Column.Type is ColumnType.Choice or ColumnType.Row or ColumnType.Sprite or ColumnType.Sound or ColumnType.Prefab;

    /// <summary>What its cells can pick from.</summary>
    public IReadOnlyList<string> Options => owner.OptionsFor(Column);

    /// <summary>The header's second line: its type, e.g. "number", "row of Spells", "whole number formula" or "number from a curve".</summary>
    public string Caption => (Column.Type == ColumnType.Row ? $"row of {Column.Table ?? owner.TableName}" : TableColumn.TypeName(Column.Type))
        + (Column.Formula is not null ? " formula" : Column.Curve is not null ? " from a curve" : "");

    /// <summary>
    /// The formula that works its cells out, as "=Base * 1.15 ^ (Level - 1)", or "" for a column of values. Setting it
    /// makes it a formula column (its values go; undo brings them back), or "" a column of values again (keeping what
    /// the formula worked out). A formula that doesn't read is refused, saying why.
    /// </summary>
    public string Formula
    {
        get => Column.Formula is { } formula ? "=" + formula : "";
        set
        {
            owner.SetFormula(Column.Name, value);
            OnPropertyChanged();
        }
    }

    /// <summary>True when a formula works its cells out.</summary>
    public bool HasFormula => Column.Formula is not null;

    /// <summary>True when its values were generated from a curve, which it keeps.</summary>
    public bool HasCurve => Column.Curve is not null;

    /// <summary>True when it can be charted: numbers, whole numbers, dice or yes/no.</summary>
    public bool IsChartable => Column.IsNumeric;

    /// <summary>True when a curve can fill it: numbers or whole numbers.</summary>
    public bool TakesCurve => Column.Type is ColumnType.Number or ColumnType.WholeNumber;

    /// <summary>The tables a row column can name rows of.</summary>
    public IReadOnlyList<string> Tables => owner.TableNames;

    public override string ToString() => Name;
}

/// <summary>A row of the table showing in the Tables panel: its name and its cells.</summary>
public sealed class TableRowView : ObservableObject
{
    private readonly TablesPanel _owner;
    private TableRow _row;

    internal TableRowView(TablesPanel owner, TableRow row)
    {
        _owner = owner;
        _row = row;
        Cells = [.. row.Table.Columns.Select((c, i) => new TableCellView(owner, this, i, c, row.Cells[i]))];
    }

    /// <summary>Its name; setting it renames it (what names it by its old name still finds it).</summary>
    public string Name
    {
        get => _row.Name;
        set
        {
            _owner.Typed(() => _owner.RenameRow(_row.Name, value));
            OnPropertyChanged();
        }
    }

    /// <summary>Where it is in its table, from 0 at the top.</summary>
    public int Index => _row.Index;

    /// <summary>Its cells, one per column.</summary>
    public IReadOnlyList<TableCellView> Cells { get; }

    internal void Update(TableRow row)
    {
        var renamed = row.Name != _row.Name;
        _row = row;
        if (renamed)
            OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Index));
        for (var i = 0; i < Cells.Count; i++)
            Cells[i].Update(row.Table.Columns[i], row.Cells[i]);
    }

    public override string ToString() => Name;
}

/// <summary>A cell of the table showing in the Tables panel.</summary>
public sealed class TableCellView : ObservableObject
{
    private readonly TablesPanel _owner;
    private readonly TableRowView _row;
    private TableColumn _column;
    private TableValue _value;

    internal TableCellView(TablesPanel owner, TableRowView row, int index, TableColumn column, TableValue value)
    {
        _owner = owner;
        _row = row;
        Column = index;
        _column = column;
        _value = value;
        (IsFlagged, Tip, _mark) = owner.Check(row.Index, index, column, value);
    }

    private string? _mark;

    /// <summary>True when its column is worked out by a formula: it can't be typed into.</summary>
    public bool IsWorkedOut => _column.Formula is not null;

    /// <summary>The column it's in, from 0 at the left.</summary>
    public int Column { get; }

    /// <summary>Its column's name, which a cell is found by.</summary>
    public string Label => _column.Name;

    /// <summary>
    /// Its value as it shows (for a formula's cell, what it works out to, or #loop, #ref or #error); setting it sets the
    /// cell, as typing into it does (refused if it doesn't fit; "=..." makes the column a formula).
    /// </summary>
    public string Text
    {
        get => _mark ?? _value.ToString();
        set
        {
            // A list cell's picker gives null for a value that isn't on its list (a prefab since deleted): that leaves it be.
            if (value is not null)
                _owner.Typed(() => _owner.SetCellAt(_row.Index, Column, value));
            OnPropertyChanged();
        }
    }

    /// <summary>For a yes/no cell: ticked for yes.</summary>
    public bool Checked
    {
        get => _value.AsYesNo;
        set
        {
            _owner.Typed(() => _owner.SetCellAt(_row.Index, Column, value ? "yes" : "no"));
            OnPropertyChanged();
        }
    }

    /// <summary>True when it names a picture, sound, prefab or row that isn't there, or its formula can't be worked out.</summary>
    public bool IsFlagged { get; private set; }

    /// <summary>Why it's flagged, what its dice roll, or the formula that works it out; null for nothing to say.</summary>
    public string? Tip { get; private set; }

    internal void Update(TableColumn column, TableValue value)
    {
        _column = column;
        var (flagged, tip, mark) = _owner.Check(_row.Index, Column, column, value);
        if (value == _value && flagged == IsFlagged && tip == Tip && mark == _mark)
            return;
        _value = value;
        (IsFlagged, Tip, _mark) = (flagged, tip, mark);
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Checked));
        OnPropertyChanged(nameof(IsFlagged));
        OnPropertyChanged(nameof(Tip));
    }

    public override string ToString() => Text;
}
