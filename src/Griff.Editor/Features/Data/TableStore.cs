using System.IO;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Data;

/// <summary>
/// The open project's tables, kept loaded (see <see cref="FileStore{T}"/>): every <c>.table</c> file in Assets, read again
/// when it changes on disk. Every change to a table goes through <see cref="FileStore{T}.Commit"/>, which saves its file
/// straight away, sends it to the running game and is a step in the table's undo history, so the Tables panel, the Chart
/// panel and agents edit the same tables the same way, and Edit &gt; Undo takes it back. It also works out the tables'
/// formulas, as the game does.
/// </summary>
public sealed class TableStore : FileStore<Table>
{
    private readonly PlayMode _play;

    public TableStore(IWorkspace workspace, PlayMode play, ILogger<TableStore> logger)
        : base(workspace, logger)
    {
        _play = play;
        Start();
    }

    protected override string Extension => TableFile.Extension;

    protected override string Kind => "table";

    protected override Table Read(string text, string name) => TableFile.Read(text, name);

    protected override string Write(Table value) => TableFile.Write(value);

    protected override string NameOf(Table value) => value.Name;

    /// <summary>The project's tables, alphabetically.</summary>
    public IReadOnlyList<StoredFile<Table>> Tables => Files;

    /// <summary>
    /// The table called <paramref name="name"/> with its formula columns worked out, as the game has it and the Tables
    /// panel shows it, or null if there's none (or it doesn't read).
    /// </summary>
    public Table? Computed(string? name) => Entry(name) is { Value: { } table } entry ? _evaluation.Find(entry.Name) ?? table : null;

    /// <summary>The project's tables with their formulas worked out, and what's wrong with any of them.</summary>
    public TableEvaluation Evaluation => _evaluation;

    /// <summary>What's wrong with the formulas of the table called <paramref name="name"/>.</summary>
    public IReadOnlyList<FormulaProblem> ProblemsOf(string? name) => Entry(name) is { } entry ? _evaluation.ProblemsOf(entry.Name) : [];

    private TableEvaluation _evaluation = TableEvaluation.Evaluate([]);

    // What each table's formulas last worked out to, so a change to one table tells the tables whose formulas read it.
    private readonly Dictionary<string, string> _worked = new(StringComparer.OrdinalIgnoreCase);

    // Works the formulas out again; the tables (other than those changed) whose formulas came out differently.
    private List<string> Reevaluate()
    {
        var files = Files;
        _evaluation = TableEvaluation.Evaluate(
            files.Select(e => e.Value).OfType<Table>(),
            files.Where(e => e.Value is null).ToDictionary(e => e.Name, e => e.Problem ?? "", StringComparer.OrdinalIgnoreCase));
        var changed = new List<string>();
        var names = _worked.Keys.Concat(_evaluation.Tables.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in names)
        {
            var now = Worked(name);
            if (_worked.TryGetValue(name, out var before) && before == now)
                continue;
            if (now.Length == 0)
                _worked.Remove(name);
            else
                _worked[name] = now;
            changed.Add(name);
        }

        return changed;
    }

    // A table's formula cells and problems, as text to compare; "" for a table without formulas.
    private string Worked(string name)
    {
        if (_evaluation.Find(name) is not { HasFormulas: true } table)
            return "";
        var text = new System.Text.StringBuilder();
        var formulas = Enumerable.Range(0, table.Columns.Count).Where(c => table.Columns[c].Formula is not null).ToList();
        foreach (var row in table.Rows)
        {
            foreach (var c in formulas)
                text.Append(row.Cells[c].Kind).Append(':').Append(row.Cells[c].ToString()).Append('|');
        }

        foreach (var problem in _evaluation.ProblemsOf(name))
            text.Append(problem).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Makes a new table called <paramref name="name"/> in Assets/Tables, with a Value column and a first row to start
    /// from, and returns it. Throws if the name can't be a file's or a table has it already.
    /// </summary>
    public Table Create(string name)
    {
        var project = Workspace.Project ?? throw new InvalidOperationException("No project is open.");
        name = name.Trim();
        if (NameProblem(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        return Add(project.DefaultTablePath(name), new Table(name, [new TableColumn("Value")], [("Row 1", [TableValue.Empty])]));
    }

    /// <summary>Why <paramref name="name"/> can't be a new table's name, or null if it can.</summary>
    public string? NameProblem(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "That can't be a table's name (it's its file's name).";
        return Entry(name) is not null ? $"There's a table called {name} already." : null;
    }

    // Says a table changed, and each table whose formulas read it and came out differently (all of them, as they're read).
    protected override void OnChanged(string name)
    {
        if (name.Length == 0)
            _worked.Clear();
        var others = Reevaluate();
        OnPropertyChanged(nameof(Tables));
        OnPropertyChanged(nameof(Names));
        RaiseChanged(name);
        if (name.Length == 0)
            return;
        foreach (var other in others.Where(o => !string.Equals(o, name, StringComparison.OrdinalIgnoreCase)))
            RaiseChanged(other);
    }

    // The running game takes each table as it changes, and goes without one that doesn't read (or is gone).
    protected override void OnUsed(StoredFile<Table> entry, Table value) => _play.StageTable(value);

    protected override void OnUnreadable(string name, string problem) => _play.StageTableProblem(name, problem);

    protected override void OnGone(string name) => _play.StageTableRemoval(name);

    /// <summary>
    /// What came of a table's file changing outside the editor's own windows (an agent wrote it, or it was deleted): e.g.
    /// "The Spells table: 4 columns, 12 rows", or why it doesn't read.
    /// </summary>
    protected override AssetFeedback Feedback(string assetPath, StoredFile<Table>? entry) => entry switch
    {
        null => new AssetFeedback($"The {TableFile.NameOf(assetPath)} table is gone."),
        { Problem: { } problem } => new AssetFeedback($"The table doesn't read, so the game goes without it until it's fixed: {problem}", IsError: true),
        { Value: { } table } => new AssetFeedback($"The {table.Name} table: {Count(table.Columns.Count, "column")}, {Count(table.Rows.Count, "row")}" +
            $"{(_play.IsPlaying ? " (and in the running game)" : "")}.{FormulaFeedback(table.Name)}"),
        _ => new AssetFeedback($"The {entry.Name} table."),
    };

    // What its formulas work out to be wrong, for an agent that just wrote it.
    private string FormulaFeedback(string name)
    {
        var problems = _evaluation.Warnings.Where(w => w.StartsWith($"The {name} table's ", StringComparison.OrdinalIgnoreCase)).ToList();
        if (problems.Count > 0)
            return $" Its formulas have {Count(problems.Count, "problem")} (their cells are empty until they're put right): {string.Join(" ", problems.Take(5))}";
        return _evaluation.Find(name)?.HasFormulas == true ? " Its formulas all work out (read their values with ui_invoke Tables Values or Cell)." : "";
    }

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";
}
