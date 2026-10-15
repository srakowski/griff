using System.Globalization;

namespace Griff.Data;

/// <summary>
/// What's wrong with a formula's cell, or with a whole formula column (<see cref="Row"/> null): a reference to a column,
/// row or table that isn't there, a loop of formulas working each other out, a sum that can't be done (a division by
/// 0), or a formula that doesn't read. The cell is empty (0 to a script) until it's put right.
/// </summary>
public sealed record FormulaProblem(string Table, string? Row, string Column, string Message, FormulaProblemKind Kind)
{
    /// <summary>What's wrong at the root, when it's a cell the formula reads that can't be worked out (<see cref="Through"/>); otherwise <see cref="Message"/>.</summary>
    public string Root { get; init; } = Message;

    /// <summary>The column (of this table, or "the Levels table's Total") whose cell it reads can't be worked out, or null when the problem is its own.</summary>
    public string? Through { get; init; }

    /// <summary>The short mark the Tables panel shows in the cell: #loop, #ref or #error.</summary>
    public string Mark => Kind switch
    {
        FormulaProblemKind.Loop => "#loop",
        FormulaProblemKind.Reference => "#ref",
        _ => "#error",
    };

    public override string ToString() => Row is null
        ? $"The {Table} table's {Column} formula: {Message}"
        : $"The {Table} table's {Column} of {Row}: {Message}";
}

/// <summary>What kind of problem a <see cref="FormulaProblem"/> is.</summary>
public enum FormulaProblemKind
{
    /// <summary>The formula doesn't read, or a sum can't be done (a division by 0, text where a number goes).</summary>
    Error,

    /// <summary>It names a column, row or table that isn't there.</summary>
    Reference,

    /// <summary>It's worked out from itself, through other formulas.</summary>
    Loop,
}

/// <summary>
/// A game's tables with their formula columns worked out (see <see cref="Formula"/>), and what's wrong with any of them.
/// Every host works them out this way as the tables load and as they change, so the values are the same everywhere: in
/// the editor, an exported game, each player of a network game and the web player.
/// </summary>
/// <remarks>
/// Each cell is worked out once, from what it reads (worked out first, as needed), the tables in the order their formulas
/// need them (alphabetically where it doesn't matter), each top to bottom and left to right, so a running total
/// (<c>=Prev(Total) + XP</c>) reads the row above as it was just worked out. Arithmetic is a double's, and powers and
/// logarithms are worked out the same way on every machine (see <see cref="FormulaMath"/>). A loop of formulas, or a
/// name that isn't there, makes the cells it reaches empty and is said in <see cref="Problems"/>, never thrown.
/// </remarks>
public sealed class TableEvaluation
{
    // A formula reading a formula reading a formula... this deep is surely a mistake, and is said rather than overflowing.
    private const int DeepestNesting = 100;

    private readonly Dictionary<string, Table> _raw = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _broken = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sheet> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Table> _computed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FormulaProblem> _problems = [];
    private readonly List<(Sheet Sheet, int Row, int Column)> _working = [];

    // A table with formula columns, as it's worked out.
    private sealed class Sheet(Table table)
    {
        public Table Table { get; } = table;
        public Formula?[] Formulas { get; } = new Formula?[table.Columns.Count];
        public string?[] Unreadable { get; } = new string?[table.Columns.Count];
        public TableValue[][] Values { get; } = [.. table.Rows.Select(r => r.Cells.ToArray())];
        public FormulaError?[][] Errors { get; } = [.. table.Rows.Select(_ => new FormulaError?[table.Columns.Count])];
        public byte[][] State { get; } = [.. table.Rows.Select(_ => new byte[table.Columns.Count])];
    }

    // What's wrong: at the root, or through a cell read that can't be worked out (its column and its cell, as said).
    private sealed class FormulaError(string root, FormulaProblemKind kind, string? throughColumn = null, string? throughCell = null)
        : Exception(throughCell is null ? root : $"It reads {throughCell}, which can't be worked out: {root}")
    {
        public FormulaProblemKind Kind { get; } = kind;

        public string Root { get; } = root;

        public string? Through { get; } = throughColumn;

        public FormulaProblem Problem(string table, string? row, string column) =>
            new(table, row, column, Message, Kind) { Root = Root, Through = Through };
    }

    private const byte Working = 1;
    private const byte Done = 2;

    private TableEvaluation()
    {
    }

    /// <summary>
    /// Works out the formula columns of <paramref name="tables"/>. <paramref name="broken"/> are tables whose files don't
    /// read (name: why), which a formula naming them is told.
    /// </summary>
    public static TableEvaluation Evaluate(IEnumerable<Table> tables, IReadOnlyDictionary<string, string>? broken = null)
    {
        var evaluation = new TableEvaluation();
        foreach (var table in tables)
            evaluation._raw.TryAdd(table.Name, table);
        foreach (var (name, problem) in broken ?? new Dictionary<string, string>())
            evaluation._broken.TryAdd(name, problem);
        evaluation.Run();
        return evaluation;
    }

    /// <summary>The tables, formula cells and all, by name.</summary>
    public IReadOnlyDictionary<string, Table> Tables => _computed;

    /// <summary>The table called <paramref name="name"/> with its formulas worked out, or null.</summary>
    public Table? Find(string? name) => name is not null && _computed.TryGetValue(name, out var table) ? table : null;

    /// <summary>What's wrong with the formulas, in the order they were worked out.</summary>
    public IReadOnlyList<FormulaProblem> Problems => _problems;

    /// <summary>What's wrong with the table called <paramref name="table"/>'s formulas.</summary>
    public IReadOnlyList<FormulaProblem> ProblemsOf(string? table) =>
        [.. _problems.Where(p => string.Equals(p.Table, table, StringComparison.OrdinalIgnoreCase))];

    /// <summary>What's wrong with a cell's formula (by its row and column index), or null.</summary>
    public FormulaProblem? ProblemAt(string table, int row, int column)
    {
        if (!_sheets.TryGetValue(table, out var sheet) || row < 0 || row >= sheet.Table.Rows.Count || column < 0 || column >= sheet.Table.Columns.Count)
            return null;
        var name = sheet.Table.Columns[column].Name;
        if (sheet.Unreadable[column] is { } unreadable)
            return new FormulaProblem(sheet.Table.Name, sheet.Table.Rows[row].Name, name, unreadable, FormulaProblemKind.Error);
        return sheet.Errors[row][column] is { } error ? error.Problem(sheet.Table.Name, sheet.Table.Rows[row].Name, name) : null;
    }

    /// <summary>
    /// The problems as a game says them (see <c>Game.Warning</c>): one line per formula column and problem, saying how
    /// many rows have it, rather than a line for each cell.
    /// </summary>
    public IReadOnlyList<string> Warnings =>
        [.. _problems.GroupBy(p => (p.Table, p.Column, p.Through, p.Root, Single: p.Through is null ? p.Message : "")).Select(g =>
        {
            var rows = g.Where(p => p.Row is not null).Select(p => p.Row!).ToList();
            var (table, column, through, root, _) = g.Key;
            return rows.Count switch
            {
                0 => $"The {table} table's {column} formula: {g.First().Message}",
                1 => $"The {table} table's {column} of {rows[0]}: {g.First().Message} Its cell is empty until that's put right.",
                _ => $"The {table} table's {column}, in {rows.Count} rows ({string.Join(", ", rows.Take(5))}{(rows.Count > 5 ? "..." : "")}): " +
                     $"{(through is null ? g.First().Message : $"It reads {through}, which can't be worked out: {root}")} Those cells are empty until that's put right.",
            };
        })];

    // ----- Working it out

    private void Run()
    {
        foreach (var table in _raw.Values)
        {
            if (!table.Columns.Any(c => c.Formula is not null))
                continue;
            var sheet = new Sheet(table);
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (table.Columns[c].Formula is not { } text)
                    continue;
                if (Formula.TryParse(text, out var formula, out var problem))
                    sheet.Formulas[c] = formula;
                else
                    sheet.Unreadable[c] = $"It doesn't read: {problem}";
            }

            _sheets[table.Name] = sheet;
        }

        foreach (var sheet in Order())
        {
            for (var c = 0; c < sheet.Table.Columns.Count; c++)
            {
                if (sheet.Unreadable[c] is { } unreadable)
                    _problems.Add(new FormulaProblem(sheet.Table.Name, null, sheet.Table.Columns[c].Name, unreadable, FormulaProblemKind.Error));
            }

            for (var r = 0; r < sheet.Table.Rows.Count; r++)
            {
                for (var c = 0; c < sheet.Table.Columns.Count; c++)
                {
                    if (sheet.Formulas[c] is null)
                        continue;
                    try
                    {
                        Cell(sheet.Table, r, c);
                    }
                    catch (FormulaError)
                    {
                        // Kept with the cell; said below.
                    }

                    if (sheet.Errors[r][c] is { } error)
                        _problems.Add(error.Problem(sheet.Table.Name, sheet.Table.Rows[r].Name, sheet.Table.Columns[c].Name));
                }
            }
        }

        foreach (var table in _raw.Values)
            _computed[table.Name] = _sheets.TryGetValue(table.Name, out var sheet) ? table.WithComputed(sheet.Values) : table;
    }

    // The tables with formulas, each after the tables its formulas mention (alphabetically otherwise), so a lookup into
    // another table finds it worked out rather than working it out deep inside another formula.
    private List<Sheet> Order()
    {
        var order = new List<Sheet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(Sheet sheet)
        {
            if (!seen.Add(sheet.Table.Name))
                return;
            var mentions = sheet.Formulas.OfType<Formula>().SelectMany(f => f.Mentions())
                .Concat(sheet.Table.Columns.Select(c => c.Table).OfType<string>())
                .Where(m => !string.Equals(m, sheet.Table.Name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase);
            foreach (var mention in mentions)
            {
                if (_sheets.TryGetValue(mention, out var other))
                    Visit(other);
            }

            order.Add(sheet);
        }

        foreach (var name in _sheets.Keys.Order(StringComparer.OrdinalIgnoreCase))
            Visit(_sheets[name]);
        return order;
    }

    // A cell's value, worked out if it's a formula's; throws FormulaError if it can't be.
    private TableValue Cell(Table table, int row, int column)
    {
        if (table.Columns[column].Formula is null || !_sheets.TryGetValue(table.Name, out var sheet))
            return table.Rows[row].Cells[column];
        if (sheet.Unreadable[column] is { } unreadable)
            throw new FormulaError($"{table.Columns[column].Name}'s formula doesn't read.", FormulaProblemKind.Error);

        switch (sheet.State[row][column])
        {
            case Done:
                return sheet.Errors[row][column] is { } error ? throw Passed(error, table, row, column) : sheet.Values[row][column];
            case Working:
                var from = _working.FindIndex(w => w.Sheet == sheet && w.Row == row && w.Column == column);
                var loop = _working.Skip(from).Append((Sheet: sheet, Row: row, Column: column)).Select(w => Label(w.Sheet.Table, w.Row, w.Column, table));
                throw new FormulaError($"It's worked out from itself, in a loop: {string.Join(" → ", loop)}.", FormulaProblemKind.Loop);
        }

        if (_working.Count >= DeepestNesting)
            throw new FormulaError($"Its formulas read formulas more than {DeepestNesting} deep.", FormulaProblemKind.Error);

        sheet.State[row][column] = Working;
        _working.Add((sheet, row, column));
        try
        {
            sheet.Values[row][column] = Fit(Evaluate(sheet.Formulas[column]!.Root, new Place(table, row)), table.Columns[column]);
        }
        catch (FormulaError error)
        {
            sheet.Values[row][column] = TableValue.Empty;
            sheet.Errors[row][column] = error;
        }
        finally
        {
            _working.RemoveAt(_working.Count - 1);
            sheet.State[row][column] = Done;
        }

        return sheet.Errors[row][column] is { } failed ? throw Passed(failed, table, row, column) : sheet.Values[row][column];
    }

    private static string Label(Table table, int row, int column, Table from) =>
        (table == from ? "" : table.Name + ": ") + $"{table.Columns[column].Name} of {table.Rows[row].Name}";

    // A problem with a cell, as the formula reading it has it: through that cell, to the problem at the root. A loop is
    // the loop's, whoever reads it.
    private FormulaError Passed(FormulaError error, Table table, int row, int column)
    {
        if (error.Kind == FormulaProblemKind.Loop || _working.Count == 0)
            return error;
        var reader = _working[^1].Sheet.Table;
        var name = table.Columns[column].Name;
        var through = table == reader ? name : $"the {table.Name} table's {name}";
        return new FormulaError(error.Root, error.Kind, through, $"{through} of {table.Rows[row].Name}");
    }

    // What a formula gives, as its column holds it.
    private static TableValue Fit(TableValue value, TableColumn column)
    {
        if (value.IsEmpty)
            return value;
        switch (column.Type)
        {
            case ColumnType.Number:
                return TableValue.Of(Finite(Number(value)));
            case ColumnType.WholeNumber:
                var whole = Math.Round(Finite(Number(value)), MidpointRounding.AwayFromZero);
                return Math.Abs(whole) < 1e15 ? TableValue.Of((long)whole) : throw new FormulaError("It's too big a whole number.", FormulaProblemKind.Error);
            case ColumnType.YesNo:
                return TableValue.Of(value.AsYesNo);
            case ColumnType.Dice when value.Kind == TableValueKind.Dice:
                return value;
            default:
                return column.TryRead(value.ToString(), out var fitted, out var problem)
                    ? fitted
                    : throw new FormulaError($"It gives {value}, but {problem}.", FormulaProblemKind.Error);
        }
    }

    private static double Finite(double value) => double.IsFinite(value) ? value
        : throw new FormulaError(double.IsNaN(value) ? "Its sum has no answer (a negative number to a power that isn't whole, or the root of one?)." : "Its sum is too big to be a number.", FormulaProblemKind.Error);

    // A number: as it is, yes as 1, dice as their average, empty as 0; text that isn't a number is a mistake.
    private static double Number(TableValue value)
    {
        if (value.Kind == TableValueKind.Text && !double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            throw new FormulaError($"\"{value}\" is text, where a number goes.", FormulaProblemKind.Error);
        return value.AsNumber;
    }

    // ----- Reading the tree

    private readonly record struct Place(Table Table, int Row);

    private TableValue Evaluate(Formula.Node node, Place at) => node switch
    {
        Formula.Node.Literal literal => literal.Value,
        Formula.Node.Ref reference => Resolve(reference.Path, at),
        Formula.Node.Unary { Op: "-" } unary => TableValue.Of(Finite(-Number(Evaluate(unary.Operand, at)))),
        Formula.Node.Unary unary => TableValue.Of(!Evaluate(unary.Operand, at).AsYesNo),
        Formula.Node.Binary binary => Binary(binary, at),
        Formula.Node.Call call => Call(call, at),
        _ => throw new FormulaError("It doesn't read.", FormulaProblemKind.Error),
    };

    private TableValue Binary(Formula.Node.Binary binary, Place at)
    {
        switch (binary.Op)
        {
            case "and":
                return TableValue.Of(Evaluate(binary.Left, at).AsYesNo && Evaluate(binary.Right, at).AsYesNo);
            case "or":
                return TableValue.Of(Evaluate(binary.Left, at).AsYesNo || Evaluate(binary.Right, at).AsYesNo);
        }

        var left = Evaluate(binary.Left, at);
        var right = Evaluate(binary.Right, at);
        switch (binary.Op)
        {
            case "&":
                return TableValue.Of(left.ToString() + right.ToString());
            case "=":
                return TableValue.Of(Compare(left, right) == 0);
            case "<>":
                return TableValue.Of(Compare(left, right) != 0);
            case "<":
                return TableValue.Of(Compare(left, right) < 0);
            case "<=":
                return TableValue.Of(Compare(left, right) <= 0);
            case ">":
                return TableValue.Of(Compare(left, right) > 0);
            case ">=":
                return TableValue.Of(Compare(left, right) >= 0);
        }

        // Dice and a whole number added stay dice (1d6 + 2 is 1d6+2), as do dice added together.
        if (binary.Op is "+" or "-" && (left.Kind == TableValueKind.Dice || right.Kind == TableValueKind.Dice))
        {
            if (left.Kind == TableValueKind.Dice && right.Kind == TableValueKind.Dice && binary.Op == "+")
                return TableValue.Of(left.AsDice.Plus(right.AsDice));
            var (dice, other, sign) = left.Kind == TableValueKind.Dice ? (left.AsDice, right, binary.Op == "+" ? 1 : -1) : (right.AsDice, left, 1);
            var number = Number(other);
            if (binary.Op == "+" || left.Kind == TableValueKind.Dice)
            {
                if (number == Math.Floor(number) && Math.Abs(number) < 1e9)
                    return TableValue.Of(dice.Plus((int)number * sign));
            }
        }

        var a = Number(left);
        var b = Number(right);
        return TableValue.Of(Finite(binary.Op switch
        {
            "+" => a + b,
            "-" => a - b,
            "*" => a * b,
            "/" => b == 0 ? throw new FormulaError("It divides by 0.", FormulaProblemKind.Error) : a / b,
            "%" => b == 0 ? throw new FormulaError("It divides by 0 (with %).", FormulaProblemKind.Error) : a - b * Math.Floor(a / b),
            "^" => FormulaMath.Pow(a, b),
            _ => throw new FormulaError($"There's no {binary.Op}.", FormulaProblemKind.Error),
        }));
    }

    // Text against text (or nothing) alphabetically, ignoring capitals; anything else as numbers.
    private static int Compare(TableValue left, TableValue right)
    {
        static bool Wordy(TableValue v) => v.Kind == TableValueKind.Text && !double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (Wordy(left) || Wordy(right))
            return Math.Sign(string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase));
        return left.AsNumber.CompareTo(right.AsNumber);
    }

    // ----- Names

    private TableValue Resolve(IReadOnlyList<string> path, Place at)
    {
        var table = at.Table;
        var first = path[0];
        var column = table.ColumnIndex(first);
        if (column >= 0)
            return Follow(table, at.Row, column, path, 1);
        if (path.Count == 1 && string.Equals(first, "Name", StringComparison.OrdinalIgnoreCase))
            return TableValue.Of(table.Rows[at.Row].Name);

        if (TableNamed(first, required: false) is { } other)
        {
            if (path.Count < 3)
            {
                throw new FormulaError(path.Count == 1
                    ? $"{first} is a table: name a row and a column of it, as in {Formula.NameOf(first)}.[row].[column], or count its rows with Count({Formula.NameOf(first)})."
                    : $"{string.Join(".", path)} is a whole column: name a row too ({Formula.NameOf(first)}.[row].{Formula.NameOf(path[1])}), or add it up with Sum, Min, Max, Average or Count.",
                    FormulaProblemKind.Reference);
            }

            var row = other.Find(path[1]) ?? throw new FormulaError(other.NoRow(path[1]), FormulaProblemKind.Reference);
            var otherColumn = other.ColumnIndex(path[2]);
            if (otherColumn < 0)
                throw new FormulaError(other.NoColumn(path[2]), FormulaProblemKind.Reference);
            return Follow(other, row.Index, otherColumn, path, 3);
        }

        throw new FormulaError($"There's no column or table called \"{first}\". {(table.Columns.Count == 0 ? "" : $"The {table.Name} table's columns: {string.Join(", ", table.Columns.Select(c => c.Name))}.")}",
            FormulaProblemKind.Reference);
    }

    // The cell at row, column, then through each row column in the rest of the path to a column of the row it names.
    private TableValue Follow(Table table, int row, int column, IReadOnlyList<string> path, int next)
    {
        var value = Cell(table, row, column);
        for (var i = next; i < path.Count; i++)
        {
            var definition = table.Columns[column];
            if (definition.Type != ColumnType.Row)
            {
                throw new FormulaError($"{definition.Name}.{path[i]} needs {definition.Name} to be a row column (naming a row of a table), but it's {TableColumn.TypeName(definition.Type)}.",
                    FormulaProblemKind.Reference);
            }

            if (value.IsEmpty)
                return TableValue.Empty;
            var linked = TableNamed(definition.Table ?? table.Name, required: true)!;
            var linkedRow = linked.Find(value.ToString()) ?? throw new FormulaError(linked.NoRow(value.ToString()), FormulaProblemKind.Reference);
            var linkedColumn = linked.ColumnIndex(path[i]);
            if (linkedColumn < 0)
                throw new FormulaError(linked.NoColumn(path[i]), FormulaProblemKind.Reference);
            (table, row, column) = (linked, linkedRow.Index, linkedColumn);
            value = Cell(table, row, column);
        }

        return value;
    }

    private Table? TableNamed(string name, bool required)
    {
        if (_raw.TryGetValue(name, out var table))
            return table;
        if (_broken.TryGetValue(name, out var problem))
            throw new FormulaError($"The {name} table doesn't read: {problem}", FormulaProblemKind.Reference);
        if (!required)
            return null;
        throw new FormulaError(_raw.Count == 0 ? $"There's no table called \"{name}\"."
            : $"There's no table called \"{name}\". The tables: {string.Join(", ", _raw.Keys.Order(StringComparer.OrdinalIgnoreCase))}.", FormulaProblemKind.Reference);
    }

    // A whole column named in Sum, Min and the rest: one of the row's table (Price), or of another (Items.Price). Null
    // if the argument isn't a column's name.
    private (Table Table, int Column)? ColumnNamed(Formula.Node node, Place at)
    {
        if (node is not Formula.Node.Ref { Path: var path })
            return null;
        if (path.Count == 1 && at.Table.ColumnIndex(path[0]) is >= 0 and var own)
            return (at.Table, own);
        if (path.Count == 2 && _raw.TryGetValue(path[0], out var other))
        {
            var column = other.ColumnIndex(path[1]);
            return column >= 0 ? (other, column) : throw new FormulaError(other.NoColumn(path[1]), FormulaProblemKind.Reference);
        }

        return null;
    }

    private IEnumerable<TableValue> ColumnValues((Table Table, int Column) column)
    {
        for (var r = 0; r < column.Table.Rows.Count; r++)
            yield return Cell(column.Table, r, column.Column);
    }

    // ----- Functions

    private TableValue Call(Formula.Node.Call call, Place at)
    {
        var args = call.Args;
        void Takes(int least, int most)
        {
            if (args.Count < least || args.Count > most)
            {
                var count = least == most ? $"{least}" : most == int.MaxValue ? $"at least {least}" : $"{least} to {most}";
                var use = Formula.Functions.FirstOrDefault(f => f.Name == call.Name).Use;
                throw new FormulaError($"{call.Name} takes {count} value{(most == 1 && least == 1 ? "" : "s")}, not {args.Count}{(use is null ? "." : $": {use}.")}", FormulaProblemKind.Error);
            }
        }

        TableValue Arg(int i) => Evaluate(args[i], at);
        double Num(int i) => Number(Arg(i));
        TableValue Of(double value) => TableValue.Of(Finite(value));

        switch (call.Name)
        {
            case "If":
                Takes(2, 3);
                return Arg(0).AsYesNo ? Arg(1) : args.Count > 2 ? Arg(2) : TableValue.Empty;
            case "Not":
                Takes(1, 1);
                return TableValue.Of(!Arg(0).AsYesNo);
            case "Round":
            {
                Takes(1, 2);
                var x = Num(0);
                var digits = args.Count > 1 ? (int)Math.Round(Num(1)) : 0;
                if (digits >= 0)
                    return Of(Math.Round(x, Math.Min(digits, 15), MidpointRounding.AwayFromZero));
                var step = FormulaMath.Pow(10, -digits);
                return Of(Math.Round(x / step, MidpointRounding.AwayFromZero) * step);
            }

            case "Floor":
                Takes(1, 1);
                return Of(Math.Floor(Num(0)));
            case "Ceiling":
                Takes(1, 1);
                return Of(Math.Ceiling(Num(0)));
            case "Abs":
                Takes(1, 1);
                return Of(Math.Abs(Num(0)));
            case "Sqrt":
                Takes(1, 1);
                var root = Num(0);
                return root < 0 ? throw new FormulaError("It takes the square root of a number below 0.", FormulaProblemKind.Error) : Of(Math.Sqrt(root));
            case "Pow":
                Takes(2, 2);
                return Of(FormulaMath.Pow(Num(0), Num(1)));
            case "Exp":
                Takes(1, 1);
                return Of(FormulaMath.Exp(Num(0)));
            case "Ln":
                Takes(1, 1);
                return Of(Log(Num(0)));
            case "Log":
            {
                Takes(1, 2);
                var x = Num(0);
                var b = args.Count > 1 ? Num(1) : 10;
                if (b <= 0 || b == 1)
                    throw new FormulaError("Log's base should be above 0 and not 1.", FormulaProblemKind.Error);
                return Of(Log(x) / Log(b));
            }

            case "Clamp":
                Takes(3, 3);
                var (low, high) = (Num(1), Num(2));
                return Of(Math.Min(Math.Max(Num(0), Math.Min(low, high)), Math.Max(low, high)));
            case "Lerp":
                Takes(3, 3);
                var (from, to, t) = (Num(0), Num(1), Num(2));
                return Of(from + (to - from) * t);
            case "Min" or "Max" or "Sum" or "Average" or "Count":
                return Gather(call, at);
            case "SumIf" or "AverageIf":
            {
                Takes(3, 3);
                var sum = WholeColumn(call, args[0], at);
                var match = WholeColumn(call, args[1], at);
                if (sum.Table != match.Table)
                    throw new FormulaError($"{call.Name}'s two columns should be of the same table.", FormulaProblemKind.Reference);
                var wanted = Arg(2);
                var picked = new List<double>();
                for (var r = 0; r < sum.Table.Rows.Count; r++)
                {
                    if (Compare(Cell(match.Table, r, match.Column), wanted) == 0)
                        picked.Add(Number(Cell(sum.Table, r, sum.Column)));
                }

                return call.Name == "SumIf" ? Of(Total(picked)) : picked.Count == 0 ? TableValue.Empty : Of(Total(picked) / picked.Count);
            }

            case "CountIf":
            {
                Takes(2, 2);
                var match = WholeColumn(call, args[0], at);
                var wanted = Arg(1);
                return TableValue.Of((long)ColumnValues(match).Count(v => Compare(v, wanted) == 0));
            }

            case "Lookup":
            {
                Takes(2, 3);
                Table table;
                int column;
                TableValue rowName;
                if (args.Count == 2)
                {
                    (table, column) = WholeColumn(call, args[0], at);
                    rowName = Arg(1);
                }
                else
                {
                    table = TableNamed(Arg(0).ToString(), required: true)!;
                    rowName = Arg(1);
                    var columnName = Arg(2).ToString();
                    column = table.ColumnIndex(columnName);
                    if (column < 0)
                        throw new FormulaError(table.NoColumn(columnName), FormulaProblemKind.Reference);
                }

                if (rowName.IsEmpty)
                    return TableValue.Empty;
                var row = table.Find(rowName.ToString()) ?? throw new FormulaError(table.NoRow(rowName.ToString()), FormulaProblemKind.Reference);
                return Cell(table, row.Index, column);
            }

            case "Prev":
            {
                Takes(1, 2);
                if (args[0] is not Formula.Node.Ref { Path.Count: 1 } own || at.Table.ColumnIndex(own.Path[0]) is not (>= 0 and var column))
                    throw new FormulaError($"Prev takes a column of the {at.Table.Name} table, as in Prev(Total).", FormulaProblemKind.Reference);
                if (at.Row > 0)
                    return Cell(at.Table, at.Row - 1, column);
                return args.Count > 1 ? Arg(1) : TableValue.Empty;
            }

            case "Row":
                Takes(0, 0);
                return TableValue.Of((long)at.Row + 1);
            case "Rows":
                Takes(0, 0);
                return TableValue.Of((long)at.Table.Rows.Count);
            case "Dice":
            {
                Takes(2, 3);
                var (count, sides, plus) = (Num(0), Num(1), args.Count > 2 ? Num(2) : 0);
                if (count != Math.Floor(count) || sides != Math.Floor(sides) || plus != Math.Floor(plus) || count < 0 || count > 1000 || sides < 1 || sides > 1000 || Math.Abs(plus) > 1e6)
                    throw new FormulaError($"Dice takes whole numbers: how many (0 to 1000), how many sides (1 to 1000) and what's added, not {count}, {sides}, {plus}.", FormulaProblemKind.Error);
                return TableValue.Of(Data.Dice.Of((int)count, (int)sides, (int)plus));
            }

            case "Text":
                Takes(1, 1);
                return TableValue.Of(Arg(0).ToString());
            default:
                throw new FormulaError($"There's no function called {call.Name}.", FormulaProblemKind.Error);
        }
    }

    private static double Log(double x) => x <= 0
        ? throw new FormulaError("It takes the logarithm of a number that isn't above 0.", FormulaProblemKind.Error)
        : FormulaMath.Log(x);

    // Adding up in order, so the answer's last digit is the same everywhere.
    private static double Total(IEnumerable<double> values)
    {
        var total = 0.0;
        foreach (var value in values)
            total += value;
        return total;
    }

    private (Table Table, int Column) WholeColumn(Formula.Node.Call call, Formula.Node node, Place at) =>
        ColumnNamed(node, at) ?? throw new FormulaError($"{call.Name} takes a column here, like Price or Items.Price, not {(node is Formula.Node.Ref r ? r.ToString() : "a sum")}.", FormulaProblemKind.Reference);

    // Min, Max, Sum, Average and Count: of a whole column (one name), a table's rows (Count(Items)), or of the values given.
    private TableValue Gather(Formula.Node.Call call, Place at)
    {
        List<TableValue> values;
        if (call.Args.Count == 1 && ColumnNamed(call.Args[0], at) is { } column)
            values = [.. ColumnValues(column)];
        else if (call.Name == "Count" && call.Args is [Formula.Node.Ref { Path: [var name] }] && at.Table.ColumnIndex(name) < 0 && TableNamed(name, required: false) is { } table)
            return TableValue.Of((long)table.Rows.Count);
        else if (call.Args.Count == 0)
            throw new FormulaError($"{call.Name} takes a column (as in {call.Name}(Price)) or some values.", FormulaProblemKind.Error);
        else
            values = [.. call.Args.Select(a => Evaluate(a, at))];

        var filled = values.Where(v => !v.IsEmpty).ToList();
        if (call.Name == "Count")
            return TableValue.Of((long)filled.Count);
        var numbers = filled.Select(Number).ToList();
        return call.Name switch
        {
            "Sum" => TableValue.Of(Finite(Total(numbers))),
            _ when numbers.Count == 0 => TableValue.Empty,
            "Min" => TableValue.Of(numbers.Min()),
            "Max" => TableValue.Of(numbers.Max()),
            _ => TableValue.Of(Finite(Total(numbers) / numbers.Count)),
        };
    }
}
