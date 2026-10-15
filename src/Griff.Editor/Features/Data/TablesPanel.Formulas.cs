using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Data;

namespace Griff.Editor.Features.Data;

// Formula columns, curves and the chart of the picked column.
public sealed partial class TablesPanel
{
    private bool _showChart = true;

    /// <summary>The table showing, with its formula columns worked out, as the grid shows it and the game has it; null if there's none.</summary>
    public Table? Computed => _store.Computed(_name);

    /// <summary>What's wrong with the table's formulas, a line each: a column, row or table they name that isn't there, a loop, a division by 0.</summary>
    public IReadOnlyList<string> FormulaProblems => [.. _store.ProblemsOf(_name).Select(p => p.ToString())];

    public bool HasFormulaProblems => _store.ProblemsOf(_name).Count > 0;

    /// <summary>The first of <see cref="FormulaProblems"/> and how many more, for the line under the grid.</summary>
    public string? FormulaProblemSummary => _store.ProblemsOf(_name) is { Count: > 0 } problems
        ? $"{problems.Count} formula problem{(problems.Count == 1 ? "" : "s")}: {(problems[0].Row is null ? problems[0].Column : $"{problems[0].Column} of {problems[0].Row}")}: {problems[0].Message}"
        : null;

    /// <summary>
    /// Makes a column worked out by <paramref name="formula"/>, from the row's other columns and other tables:
    /// <c>SetFormula("XP", "=Base * 1.15 ^ (Level - 1)")</c>. Its values go (undo brings them back); its cells show what it
    /// works out to, and change as what it reads changes. An empty formula makes it a column of values again, keeping what
    /// the formula worked out. A formula that doesn't read is refused, saying why; one naming something that isn't there is
    /// kept and flagged (see <see cref="FormulaProblems"/>). False if refused.
    /// </summary>
    public bool SetFormula(string column, string? formula)
    {
        var table = Current;
        var index = ColumnOf(table, column);
        if (index < 0)
        {
            Message = "The rows' names can't be worked out by a formula.";
            return false;
        }

        var old = table.Columns[index];
        var text = string.IsNullOrWhiteSpace(formula) ? null : Formula.Clean(formula);
        if (string.IsNullOrEmpty(text))
            text = null;
        if (text == old.Formula)
            return true;
        if (text is not null && !Formula.TryParse(text, out _, out var problem))
        {
            Message = $"{old.Name}'s formula doesn't read: {problem}";
            _status.Show(Message);
            return false;
        }

        if (text is null)
        {
            // Its values stay as the formula worked them out, to change by hand.
            var worked = Computed;
            var cleared = table.ChangeColumn(index, old with { Formula = null });
            var cells = worked is null ? [] : worked.Rows.Where(r => !r.Cells[index].IsEmpty).Select(r => (r.Index, index, r.Cells[index])).ToList();
            Commit(cleared.SetCells(cells), $"Clear {old.Name}'s formula", $"{old.Name} has no formula now: it keeps the values it worked out, to change by hand.");
            return true;
        }

        var hadValues = old.Formula is null && table.Rows.Any(r => !r.Cells[index].IsEmpty);
        Commit(table.ChangeColumn(index, old with { Formula = text, Curve = null }), $"Set {old.Name}'s formula");
        var problems = _store.ProblemsOf(_name).Where(p => string.Equals(p.Column, old.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        Message = $"{old.Name} is worked out by ={text} now{(hadValues ? " (undo puts its values back)" : "")}." +
                  (problems.Count == 0 ? "" : $" It has {(problems.Count == 1 ? "a problem" : $"{problems.Count} problems")}: {problems[0].Message}");
        _status.Show(Message);
        return true;
    }

    /// <summary>
    /// A column's values, top to bottom, as the cells show them (a formula's worked out, or #loop, #ref or #error):
    /// <c>Values("XP")</c>. "" gives the rows' names.
    /// </summary>
    public IReadOnlyList<string> Values(string column)
    {
        var table = Computed ?? Current;
        var index = ColumnOf(table, column);
        return [.. table.Rows.Select(r => index < 0 ? r.Name : _store.Evaluation.ProblemAt(table.Name, r.Index, index)?.Mark ?? r.Cells[index].ToString())];
    }

    // ----- The chart

    /// <summary>Whether the chart (and the curve editor) shows beside the grid.</summary>
    public bool ShowChart
    {
        get => _showChart;
        set => SetProperty(ref _showChart, value);
    }

    /// <summary>The chart of the picked column over the rows (or levels): what it shows, and its summary.</summary>
    public TableChartModel Chart { get; } = new();

    /// <summary>The curve editor: the settings a level-progression column is generated from (see <see cref="GenerateCurve"/>).</summary>
    public TableCurveModel Curve { get; }

    private void UpdateChart()
    {
        if (Curve is null)
            return;
        var column = SelectedColumn?.Column;
        var table = Computed;
        if (table is null || column is null || SelectedColumn!.Index >= table.Columns.Count)
        {
            Chart.Show(null, null, [], false, Curve);
            return;
        }

        var index = SelectedColumn.Index;
        var points = Rows.Select(r =>
        {
            var value = table.Rows[r.Index].Cells[index];
            var problem = _store.Evaluation.ProblemAt(table.Name, r.Index, index);
            return TableChartPoint.Of(r.Name, problem is null ? value : TableValue.Empty);
        }).ToList();
        // Over the levels when the rows are levels (named 1, 2, 3, or generated from a curve); over the rows otherwise.
        var levels = column.Curve is not null || Rows.Count > 0 && Rows.All(r => int.TryParse(r.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
        Chart.Show(column, table.Name, points, levels, Curve);
    }

    // ----- Curves

    /// <summary>
    /// Fills a column from a curve, a row a level (row 1 is level 1), as one undoable step: <c>GenerateCurve("XP",
    /// "exponential", 100, 50000, 50, roundTo: 10)</c>. The kinds are linear, power, exponential, s-curve and points (with
    /// <paramref name="points"/> as "1: 100, 25: 5000, 50: 50000"). <paramref name="shape"/> is the power for a power curve
    /// and how sharp an S-curve is. Rows are added for levels past the last; rows called "Row 3" and so on are named by their
    /// level. The column keeps the curve, so it can be changed and generated again. False, saying why, if it can't be.
    /// </summary>
    public bool GenerateCurve(string column, string kind = "exponential", double first = 100, double last = 10000, int levels = 20,
        double shape = 2, double roundTo = 0, string? points = null)
    {
        var table = Current;
        var index = ColumnOf(table, column);
        if (index < 0)
        {
            Message = "Pick a number column to generate: the rows' names come from the levels.";
            return false;
        }

        if (!Griff.Data.Curve.TryParseKind(kind, out var curveKind))
        {
            Message = $"There's no curve called {kind}; the kinds are {string.Join(", ", Griff.Data.Curve.KindNames)}.";
            return false;
        }

        if (!Griff.Data.Curve.TryReadPoints(points, out var read, out var problem))
        {
            Message = problem;
            return false;
        }

        return Generate(index, new Griff.Data.Curve { Kind = curveKind, First = first, Last = last, Levels = levels, Shape = shape, RoundTo = roundTo, Points = read });
    }

    internal bool Generate(int index, Griff.Data.Curve curve)
    {
        var table = Current;
        var name = table.Columns[index].Name;
        var what = curve.Kind switch
        {
            CurveKind.Points => "points drawn by hand",
            CurveKind.SCurve => "an S-curve",
            CurveKind.Exponential => "an exponential curve",
            var kind => $"a {Griff.Data.Curve.KindName(kind)} curve",
        };
        var range = curve.Kind == CurveKind.Points ? "" : $", {Format(curve.First)} to {Format(curve.Last)}";
        if (!Try(() => table.Generate(index, curve), $"Generate {name} from {what}", $"Generated {name} for {curve.Levels} levels from {what}{range}."))
            return false;
        Curve.Load(Table?.Columns[index], Table, force: true);
        return true;
    }

    // Says something under the grid and on the status bar.
    internal void Say(string message)
    {
        Message = message;
        _status.Show(message);
    }

    internal static string Format(double value) => value.ToString(Math.Abs(value) >= 1000 ? "#,0.##" : "0.###", CultureInfo.InvariantCulture);
}

/// <summary>A point of the Tables panel's chart: a row (or level), its value, and for dice, the least and most they roll and their spread.</summary>
public sealed record TableChartPoint(string Label, double Value, double Low, double High, double Spread)
{
    /// <summary>True when the cell has nothing to chart (empty, or a formula that can't be worked out).</summary>
    public bool IsEmpty => double.IsNaN(Value);

    /// <summary>What the chart says for it, e.g. "12: 1,250" or "Ogre: 2d6+3 rolls 5 to 15, 10 on average, spread 2.4".</summary>
    public string Readout { get; init; } = "";

    internal static TableChartPoint Of(string label, TableValue value)
    {
        if (value.IsEmpty)
            return new(label, double.NaN, double.NaN, double.NaN, 0) { Readout = $"{label}: empty" };
        if (value.Kind == TableValueKind.Dice)
        {
            var dice = value.AsDice;
            return new(label, dice.Average, dice.Min, dice.Max, dice.Spread)
            {
                Readout = $"{label}: {dice} rolls {dice.Min} to {dice.Max}, {TablesPanel.Format(dice.Average)} on average, spread {TablesPanel.Format(Math.Round(dice.Spread, 2))}",
            };
        }

        var number = value.AsNumber;
        return new(label, number, number, number, 0) { Readout = $"{label}: {(value.Kind == TableValueKind.YesNo ? value.ToString() : TablesPanel.Format(number))}" };
    }
}

/// <summary>
/// The Tables panel's chart of the picked column: its values over the rows (bars) or over the levels (a line), dice as
/// their average, range and spread, and while the curve editor is open, the curve it would generate.
/// </summary>
public sealed class TableChartModel : ObservableObject
{
    /// <summary>What it charts: "XP over the levels", "Damage over the rows".</summary>
    public string Title { get; private set; } = "";

    /// <summary>Why there's no chart (no number column picked), or null.</summary>
    public string? Note { get; private set; } = "Pick a number, whole number, dice or yes/no column to chart it.";

    public bool HasChart => Note is null;

    /// <summary>True when it charts dice: each row's range (least to most), spread and average.</summary>
    public bool IsDice { get; private set; }

    /// <summary>True when it charts over the levels (a line), false over the rows (bars).</summary>
    public bool OverLevels { get; private set; }

    /// <summary>A point a row, top to bottom (the rows the filter shows).</summary>
    public IReadOnlyList<TableChartPoint> Points { get; private set; } = [];

    /// <summary>The values the open curve editor would generate, level 1 first; empty when it's shut.</summary>
    public IReadOnlyList<double> Preview { get; private set; } = [];

    /// <summary>The points of a hand-drawn curve, while the curve editor draws one: dragged, added and taken away on the chart.</summary>
    public IReadOnlyList<CurvePoint> Handles { get; private set; } = [];

    /// <summary>What the values come to: "100 (1) to 50,000 (50), 9,870 on average", or for dice their least, most and spread.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>Goes up each time it changes, so it's drawn again.</summary>
    public int Version { get; private set; }

    internal void Show(TableColumn? column, string? table, IReadOnlyList<TableChartPoint> points, bool levels, TableCurveModel curve)
    {
        if (column is null || !column.IsNumeric)
        {
            Note = column is null ? "Pick a number, whole number, dice or yes/no column to chart it." : $"{column.Name} holds {TableColumn.TypeName(column.Type)}: pick a number, whole number, dice or yes/no column to chart it.";
            Title = "";
            Points = [];
            Preview = [];
            Handles = [];
            Summary = "";
        }
        else
        {
            Note = null;
            IsDice = column.Type == ColumnType.Dice;
            OverLevels = levels || curve.IsOpen;
            // While a curve is drawn by hand, how.
            Title = $"{column.Name} over the {(OverLevels ? "levels" : "rows")}" +
                    (curve.IsOpen && curve.UsesPoints ? ": click to add a point, drag one to move it, right-click to take it away" : "");
            Points = points;
            Preview = curve.IsOpen ? curve.Preview : [];
            Handles = curve.IsOpen && curve.UsesPoints ? curve.PointList : [];
            Summary = Summarize(points);
        }

        Version++;
        OnPropertyChanged(string.Empty);
    }

    private string Summarize(IReadOnlyList<TableChartPoint> points)
    {
        var filled = points.Where(p => !p.IsEmpty).ToList();
        if (filled.Count == 0)
            return "Nothing to chart yet: its cells are empty.";
        var lowest = filled.MinBy(p => p.Value)!;
        var highest = filled.MaxBy(p => p.Value)!;
        var average = filled.Sum(p => p.Value) / filled.Count;
        if (IsDice)
        {
            var least = filled.MinBy(p => p.Low)!;
            var most = filled.MaxBy(p => p.High)!;
            var widest = filled.MaxBy(p => p.Spread)!;
            return $"Rolls {TablesPanel.Format(least.Low)} ({least.Label}) to {TablesPanel.Format(most.High)} ({most.Label}); " +
                   $"{TablesPanel.Format(lowest.Value)} to {TablesPanel.Format(highest.Value)} on average; spread up to {TablesPanel.Format(Math.Round(widest.Spread, 2))} ({widest.Label}).";
        }

        return $"{TablesPanel.Format(lowest.Value)} ({lowest.Label}) to {TablesPanel.Format(highest.Value)} ({highest.Label}), {TablesPanel.Format(Math.Round(average, 2))} on average.";
    }
}

/// <summary>
/// The Tables panel's curve editor, for the picked column: the curve its values are generated from, a row a level. Pick
/// a <see cref="Kind"/>, set <see cref="First"/>, <see cref="Last"/> and <see cref="Levels"/> (or draw
/// <see cref="Points"/> on the chart), see it on the chart, then <see cref="Generate"/>. A column generated from a curve
/// keeps it, and opens with it here, to change and generate again.
/// </summary>
public sealed class TableCurveModel(TablesPanel owner) : ObservableObject
{
    private bool _open;
    private string? _for;
    private CurveKind _kind = CurveKind.Exponential;
    private double _first = 100;
    private double _last = 10000;
    private int _levels = 20;
    private double _shape = 2;
    private double _roundTo;
    private IReadOnlyList<CurvePoint> _points = [];

    /// <summary>Whether the curve editor shows (beside the chart, which then shows the curve too).</summary>
    public bool IsOpen
    {
        get => _open;
        set => SetProperty(ref _open, value);
    }

    /// <summary>The column it's for, or null when the picked column can't take a curve.</summary>
    public string? Column { get; private set; }

    /// <summary>Why it can't generate the picked column (it isn't a number column), or null.</summary>
    public string? Unfit { get; private set; } = "Pick a number or whole number column to generate it from a curve.";

    /// <summary>True when the picked column can be generated from a curve.</summary>
    public bool IsFit => Unfit is null;

    public IReadOnlyList<string> Kinds => Griff.Data.Curve.KindNames;

    /// <summary>The kind of curve: linear, power, exponential, s-curve or points (drawn by hand on the chart).</summary>
    public string Kind
    {
        get => Griff.Data.Curve.KindName(_kind);
        set
        {
            if (!Griff.Data.Curve.TryParseKind(value, out var kind) || kind == _kind)
                return;
            _kind = kind;
            // Points to start from: the ends of the curve it was.
            if (kind == CurveKind.Points && _points.Count < 2)
                _points = [new CurvePoint(1, _first), new CurvePoint(_levels, _last)];
            Changed();
        }
    }

    /// <summary>The value at level 1.</summary>
    public double First
    {
        get => _first;
        set => Set(ref _first, value);
    }

    /// <summary>The value at the last level.</summary>
    public double Last
    {
        get => _last;
        set => Set(ref _last, value);
    }

    /// <summary>How many levels: the rows it fills (adding rows past the last).</summary>
    public double Levels
    {
        get => _levels;
        set => Set(ref _levels, double.IsFinite(value) ? (int)Math.Round(Math.Clamp(value, 0, 100_000)) : _levels);
    }

    /// <summary>For a power curve, the power (2 is a square); for an S-curve, how sharp the middle is (1 is a straight line).</summary>
    public double Shape
    {
        get => _shape;
        set => Set(ref _shape, value);
    }

    /// <summary>Rounds each value to the nearest multiple of this (10 gives 1,250 rather than 1,247); 0 for none.</summary>
    public double RoundTo
    {
        get => _roundTo;
        set => Set(ref _roundTo, value);
    }

    /// <summary>For a hand-drawn curve, its points as "1: 100, 25: 5000, 50: 50000" (a level and its value each); setting it moves them.</summary>
    public string Points
    {
        get => Griff.Data.Curve.WritePoints(_points);
        set
        {
            if (Griff.Data.Curve.TryReadPoints(value, out var points, out var problem))
            {
                _points = points;
                Changed();
            }
            else
            {
                owner.Say(problem!);
            }
        }
    }

    /// <summary>The hand-drawn curve's points, by level, as the chart drags them.</summary>
    public IReadOnlyList<CurvePoint> PointList
    {
        get => _points;
        set
        {
            _points = [.. value.OrderBy(p => p.Level)];
            Changed();
        }
    }

    public bool UsesEnds => _kind != CurveKind.Points;

    public bool UsesShape => _kind is CurveKind.Power or CurveKind.SCurve;

    public bool UsesPoints => _kind == CurveKind.Points;

    /// <summary>The curve as set.</summary>
    public Griff.Data.Curve Settings => new()
    {
        Kind = _kind, First = _first, Last = _last, Levels = _levels, Shape = _shape, RoundTo = _roundTo, Points = _points,
    };

    /// <summary>Why it can't be generated as set, or null.</summary>
    public string? Problem => Unfit ?? Settings.Problem;

    public bool CanGenerate => Problem is null;

    /// <summary>The values it would generate, level 1 first (empty if it can't be).</summary>
    public IReadOnlyList<double> Preview => Settings.Problem is null ? Settings.Values() : [];

    /// <summary>What generating would do: "XP for 50 levels: 100 to 50,000".</summary>
    public string Description => Problem ?? $"{Column} for {_levels} levels: {TablesPanel.Format(Preview[0])} to {TablesPanel.Format(Preview[^1])}.";

    /// <summary>Fills the picked column from the curve as set (one undoable step), and keeps the curve with it. False, saying why, if it can't.</summary>
    public bool Generate()
    {
        if (Problem is { } problem)
        {
            owner.Say(problem);
            return false;
        }

        var index = owner.Table?.ColumnIndex(Column) ?? -1;
        return index >= 0 && owner.Generate(index, Settings);
    }

    /// <summary>Adds a point to the hand-drawn curve (a level and its value), as clicking the chart does.</summary>
    public void AddPoint(double level, double value) => PointList = [.. _points.Where(p => p.Level != Math.Round(level)), new CurvePoint(Math.Round(level), value)];

    /// <summary>Moves the hand-drawn curve's point at <paramref name="index"/> (by level, from 0) to a level and value, as dragging it on the chart does.</summary>
    public void MovePoint(int index, double level, double value)
    {
        var points = _points.ToList();
        points[index] = new CurvePoint(Math.Round(level), value);
        PointList = points;
    }

    /// <summary>Takes away the hand-drawn curve's point at <paramref name="index"/> (by level, from 0), as right-clicking it on the chart does.</summary>
    public void RemovePoint(int index)
    {
        if (_points.Count > 0 && index >= 0 && index < _points.Count)
            PointList = [.. _points.Where((_, i) => i != index)];
    }

    // The picked column's curve: the one it keeps, or one to start from (through its first and last values, over its rows).
    internal void Load(TableColumn? column, Table? table, bool force = false)
    {
        var key = column is null || table is null ? null : table.Name + "\n" + column.Name;
        if (key == _for && !force)
            return;
        _for = key;
        Column = column is { Type: ColumnType.Number or ColumnType.WholeNumber } ? column.Name : null;
        Unfit = column is null ? "Pick a number or whole number column to generate it from a curve."
            : Column is null ? $"A curve fills numbers, and {column.Name} holds {TableColumn.TypeName(column.Type)}: make it a number or whole number column first."
            : null;
        if (column?.Curve is { } kept)
        {
            (_kind, _first, _last, _levels, _shape, _roundTo, _points) = (kept.Kind, kept.First, kept.Last, kept.Levels, kept.Shape, kept.RoundTo, kept.Points);
        }
        else if (column is not null && table is not null && Column is not null)
        {
            var index = table.ColumnIndex(column.Name);
            var values = table.Rows.Select(r => r.Cells[index]).Where(v => !v.IsEmpty).Select(v => v.AsNumber).ToList();
            _levels = table.Rows.Count >= 2 ? table.Rows.Count : 20;
            (_first, _last) = values.Count >= 2 && values[0] > 0 && values[^1] > values[0] ? (values[0], values[^1]) : (100, 10000);
            (_kind, _shape, _roundTo) = (CurveKind.Exponential, 2, column.Type == ColumnType.WholeNumber ? 0 : 0);
            _points = [];
        }

        Changed();
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Changed();
    }

    private void Changed() => OnPropertyChanged(string.Empty);
}
