namespace Griff.Data;

/// <summary>
/// A game's tables, by name: what scripts read as <c>Tables</c>, and what Table Values components take their values from.
/// Every host loads the project's <c>.table</c> files into one as the game starts (see <c>UseProjectSettings</c>), so a
/// game reads the same tables in the editor, exported, and in a browser. Formula columns come worked out (see
/// <see cref="TableEvaluation"/>), the same on every machine.
/// </summary>
/// <example><code>
/// float damage = Tables.Get("Spells", "Fireball")["Damage"];
/// var fireball = Tables.Get&lt;Spell&gt;("Spells", "Fireball");     // a class of your own, its properties named like the columns
/// foreach (var spell in Tables.All&lt;Spell&gt;("Spells")) { ... }
/// </code></example>
/// <remarks>
/// A table changed while the game runs (in the Tables panel, or its file written) reaches the game at the start of its next
/// step, never part way through one (see <see cref="Stage"/>), so a step reads one version of every table, and the same
/// changes at the same step play out the same. Formulas reading a changed table are worked out again with it.
/// </remarks>
public sealed class TableSet
{
    private Snapshot _current;
    private readonly Dictionary<string, Entry?> _staged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    private sealed record Entry(Table? Table, string? Error, string? Path);

    // The tables as their files have them, and with their formulas worked out (once, the first time it's needed).
    private sealed class Snapshot
    {
        private readonly Lazy<TableEvaluation> _evaluation;

        public Snapshot(Dictionary<string, Entry> tables)
        {
            Tables = tables;
            _evaluation = new(() => TableEvaluation.Evaluate(
                Tables.Values.Select(e => e.Table).OfType<Table>(),
                Tables.Where(p => p.Value.Table is null).ToDictionary(p => p.Key, p => p.Value.Error ?? "", StringComparer.OrdinalIgnoreCase)));
        }

        public Dictionary<string, Entry> Tables { get; }

        public TableEvaluation Evaluation => _evaluation.Value;
    }

    /// <summary>A set holding <paramref name="tables"/>.</summary>
    public TableSet(IEnumerable<Table>? tables = null) =>
        _current = new Snapshot((tables ?? []).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new Entry(g.First(), null, null), StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The tables in a project's Assets folder (and the folders in it), each named after its file. Two files with the same
    /// name: the first by path wins, as prefabs do. A file that doesn't read is kept as an error, which using it reports.
    /// </summary>
    public static TableSet Load(string assetsFolder)
    {
        var set = new TableSet();
        if (!Directory.Exists(assetsFolder))
            return set;
        var files = Directory.EnumerateFiles(assetsFolder, "*" + TableFile.Extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = TableFile.NameOf(file);
            if (set._current.Tables.ContainsKey(name))
                continue;
            var path = Path.GetRelativePath(assetsFolder, file).Replace('\\', '/');
            try
            {
                set._current.Tables[name] = new Entry(TableFile.Load(file), null, path);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                set._current.Tables[name] = new Entry(null, ex.Message, path);
            }
        }

        return set;
    }

    /// <summary>
    /// Tells this set from any other, so what a game set from its tables (see <c>TableValues.Applied</c>) is set again in a
    /// new game, but not after a rewind within the same one.
    /// </summary>
    public string Session { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Goes up by one each time staged changes reach the game (see <see cref="ApplyStaged"/>).</summary>
    public int Version { get; private set; }

    /// <summary>The tables' names, alphabetically.</summary>
    public IReadOnlyList<string> Names => [.. _current.Tables.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The table called <paramref name="name"/> ("Spells", or its path in Assets, "Tables/Spells.table"), with its formula
    /// columns worked out, or null if there's none. Throws if its file doesn't read, saying why.
    /// </summary>
    public Table? Find(string? name)
    {
        var current = _current;
        if (string.IsNullOrWhiteSpace(name) || !current.Tables.TryGetValue(KeyOf(name), out var entry))
            return null;
        if (entry.Table is null)
            throw new InvalidDataException(entry.Error);
        return current.Evaluation.Find(entry.Table.Name) ?? entry.Table;
    }

    /// <summary>
    /// What's wrong with the tables' formulas (a column, row or table they name that isn't there, a loop of formulas
    /// working each other out, a division by 0), a line per formula column and problem. The game says each once, through
    /// <c>Game.Warning</c>, as it starts and whenever the tables change; their cells are empty (0) until it's put right.
    /// </summary>
    public IReadOnlyList<string> Problems => _current.Evaluation.Warnings;

    /// <summary>What's wrong with the tables' formulas, a line per cell (see <see cref="Problems"/>).</summary>
    public IReadOnlyList<FormulaProblem> FormulaProblems => _current.Evaluation.Problems;

    /// <summary>Why the table called <paramref name="name"/> doesn't read, or null if it does (or there's no such table).</summary>
    public string? ProblemWith(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _current.Tables.TryGetValue(KeyOf(name), out var entry) ? entry.Error : null;

    /// <summary>True if there's a table called <paramref name="name"/>.</summary>
    public bool Has(string? name) => !string.IsNullOrWhiteSpace(name) && _current.Tables.ContainsKey(KeyOf(name));

    /// <summary>The table called <paramref name="name"/>; throws, naming the tables there are, if there's none.</summary>
    public Table this[string name] => Find(name) ?? throw new KeyNotFoundException(NoTable(name));

    /// <summary>
    /// The row called <paramref name="row"/> of the table called <paramref name="table"/>:
    /// <c>Tables.Get("Spells", "Fireball")["Damage"]</c>. Throws, saying what there is, if there's no such table or row.
    /// </summary>
    public TableRow Get(string table, string row) => this[table][row];

    /// <summary>The row, or null if there's no such table or row (see <see cref="Get(string, string)"/>).</summary>
    public TableRow? Find(string? table, string? row) => Find(table)?.Find(row);

    /// <summary>
    /// The row as a new <typeparamref name="T"/>, a class of your own: each of its public properties takes the column of
    /// the same name (ignoring case and spaces, so MaxHealth takes "Max Health"), and a string Name property the row's
    /// name. Properties can be numbers, whole numbers, text, yes/no (bool), <see cref="Dice"/>, an enum (for a choice) or a
    /// <see cref="TableValue"/>.
    /// </summary>
    public T Get<T>(string table, string row) where T : new() => TableMapping.Create<T>(Get(table, row));

    /// <summary>Every row of the table as a new <typeparamref name="T"/> (see <see cref="Get{T}"/>), top to bottom.</summary>
    public IReadOnlyList<T> All<T>(string table) where T : new() => [.. this[table].Rows.Select(TableMapping.Create<T>)];

    /// <summary>
    /// The row a row column's cell names: for a column of <see cref="ColumnType.Row"/>, the row of its table called what
    /// <paramref name="row"/> has in <paramref name="column"/>. Null if the cell is empty; throws if it names no row.
    /// </summary>
    public TableRow? Linked(TableRow row, string column)
    {
        var definition = row.Table.Column(column) ?? throw new KeyNotFoundException(row.Table.NoColumn(column));
        var name = row[column];
        if (name.IsEmpty)
            return null;
        var other = this[definition.Table ?? row.Table.Name];
        return other.Find(name) ?? throw new KeyNotFoundException(other.NoRow(name));
    }

    /// <summary>Says there's no table <paramref name="name"/>, and which there are.</summary>
    public string NoTable(string? name) => _current.Tables.Count == 0
        ? $"There's no table called \"{name}\": the game has no tables. Make one with Data > New Table."
        : $"There's no table called \"{name}\". The tables: {string.Join(", ", Names)}.";

    private static string KeyOf(string nameOrPath)
    {
        var name = nameOrPath.Trim().Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return name.EndsWith(TableFile.Extension, StringComparison.OrdinalIgnoreCase) ? name[..^TableFile.Extension.Length] : name;
    }

    // ----- Changes while the game runs

    /// <summary>
    /// Puts <paramref name="table"/> in place of the table of its name (or adds it) at the start of the game's next step
    /// (<see cref="ApplyStaged"/>), not straight away: a step part way through reads the tables it started with.
    /// </summary>
    public void Stage(Table table)
    {
        lock (_lock)
            _staged[table.Name] = new Entry(table, null, null);
    }

    /// <summary>Marks the table called <paramref name="name"/> as not reading (its file was written wrong), as <see cref="Stage(Table)"/> does.</summary>
    public void StageProblem(string name, string problem)
    {
        lock (_lock)
            _staged[name] = new Entry(null, problem, null);
    }

    /// <summary>Takes the table called <paramref name="name"/> away (its file was deleted), as <see cref="Stage(Table)"/> does.</summary>
    public void StageRemoval(string name)
    {
        lock (_lock)
            _staged[name] = null;
    }

    /// <summary>True while changes wait for the game's next step.</summary>
    public bool HasStaged
    {
        get
        {
            lock (_lock)
                return _staged.Count > 0;
        }
    }

    /// <summary>
    /// Brings in what's been staged, all at once, raising <see cref="Version"/>. The game calls it as each step begins.
    /// True if anything changed.
    /// </summary>
    public bool ApplyStaged()
    {
        lock (_lock)
        {
            if (_staged.Count == 0)
                return false;
            var tables = new Dictionary<string, Entry>(_current.Tables, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, entry) in _staged)
            {
                if (entry is null)
                    tables.Remove(name);
                else
                    tables[name] = entry with { Path = tables.TryGetValue(name, out var old) ? old.Path : null };
            }

            _staged.Clear();
            _current = new Snapshot(tables);
            Version++;
            return true;
        }
    }
}
