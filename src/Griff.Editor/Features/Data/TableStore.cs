using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Data;

/// <summary>A table of the open project: its name, its file in Assets, and the table, or why its file doesn't read.</summary>
public sealed record TableEntry(string Name, string AssetPath, Table? Table, string? Problem)
{
    public override string ToString() => Name;
}

/// <summary>
/// The open project's tables, kept loaded: every <c>.table</c> file in Assets, read again when it changes on disk (an
/// agent's asset_write, a text editor, a git checkout). Every change to a table goes through <see cref="Commit"/>, which
/// saves its file straight away and sends it to the running game, and keeps an undo history for each table, so the
/// Tables panel and agents edit the same tables the same way. A change to a file from outside is a step in its history
/// too, so it can be undone.
/// </summary>
public sealed class TableStore : ObservableObject, IDisposable
{
    private readonly IWorkspace _workspace;
    private readonly PlayMode _play;
    private readonly ILogger<TableStore> _logger;
    private readonly Dictionary<string, TableEntry> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, History> _histories = new(StringComparer.OrdinalIgnoreCase);
    // For a table whose file was written so it doesn't read: the table as it last read, which undo brings back.
    private readonly Dictionary<string, Table> _lastGood = new(StringComparer.OrdinalIgnoreCase);
    // What this store last wrote to each file, so its own saves aren't read back as changes from outside.
    private readonly Dictionary<string, string> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _reload;
    private readonly HashSet<string> _changedOnDisk = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    private sealed class History
    {
        public List<(Table Before, Table After, string Description)> Done { get; } = [];
        public List<(Table Before, Table After, string Description)> Undone { get; } = [];
    }

    public TableStore(IWorkspace workspace, PlayMode play, ILogger<TableStore> logger)
    {
        _workspace = workspace;
        _play = play;
        _logger = logger;
        _reload = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => ReloadChanged(), Dispatcher.CurrentDispatcher);
        _reload.Stop();
        workspace.AddAssetListener(AssetChanged);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Project))
                LoadAll();
        };
        LoadAll();
    }

    /// <summary>Raised after a table changes (an edit, an undo, its file changed or deleted), with the table's name.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>The project's tables, alphabetically.</summary>
    public IReadOnlyList<TableEntry> Tables => [.. _tables.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The tables' names, alphabetically.</summary>
    public IReadOnlyList<string> Names => [.. Tables.Select(t => t.Name)];

    /// <summary>The first table's name, alphabetically, or null with none.</summary>
    public string? FirstName => Names is [var first, ..] ? first : null;

    /// <summary>The table called <paramref name="name"/> (or at that path in Assets), or null.</summary>
    public TableEntry? Entry(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _tables.GetValueOrDefault(TableFile.NameOf(name.Trim().Replace('\\', '/')));

    /// <summary>The table called <paramref name="name"/> as its file has it (formula columns empty), or null if there's none (or it doesn't read).</summary>
    public Table? Find(string? name) => Entry(name)?.Table;

    /// <summary>
    /// The table called <paramref name="name"/> with its formula columns worked out, as the game has it and the Tables
    /// panel shows it, or null if there's none (or it doesn't read).
    /// </summary>
    public Table? Computed(string? name) => Entry(name) is { Table: { } table } entry ? _evaluation.Find(entry.Name) ?? table : null;

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
        _evaluation = TableEvaluation.Evaluate(
            _tables.Values.Select(e => e.Table).OfType<Table>(),
            _tables.Values.Where(e => e.Table is null).ToDictionary(e => e.Name, e => e.Problem ?? "", StringComparer.OrdinalIgnoreCase));
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

    /// <summary>Whether the table called <paramref name="name"/> has an edit to undo.</summary>
    public bool CanUndo(string? name) => Entry(name) is { } entry &&
        (entry.Table is null && _lastGood.ContainsKey(entry.Name) || _histories.TryGetValue(entry.Name, out var history) && history.Done.Count > 0);

    /// <summary>Whether the table called <paramref name="name"/> has an undone edit to redo.</summary>
    public bool CanRedo(string? name) => Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Undone.Count > 0;

    /// <summary>What undoing on the table called <paramref name="name"/> would undo, e.g. "Set Fireball's Damage", or null.</summary>
    public string? UndoDescription(string? name) =>
        Entry(name) is { Table: null } broken && _lastGood.ContainsKey(broken.Name) ? "Change the file so it doesn't read" :
        Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Done.Count > 0 ? history.Done[^1].Description : null;

    /// <summary>What redoing on the table called <paramref name="name"/> would redo, or null.</summary>
    public string? RedoDescription(string? name) =>
        Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Undone.Count > 0 ? history.Undone[^1].Description : null;

    /// <summary>
    /// Makes a new table called <paramref name="name"/> in Assets/Tables, with a Value column and a first row to start
    /// from, and returns it. Throws if the name can't be a file's or a table has it already.
    /// </summary>
    public Table Create(string name)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("No project is open.");
        name = name.Trim();
        if (NameProblem(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        var table = new Table(name, [new TableColumn("Value")], [("Row 1", [TableValue.Empty])]);
        var path = project.DefaultTablePath(name);
        Write(table, path);
        _tables[name] = new TableEntry(name, AssetPathOf(path), table, null);
        _histories.Remove(name);
        _play.StageTable(table);
        _logger.LogInformation("Made the {Table} table", name);
        Raise(name);
        return table;
    }

    /// <summary>Why <paramref name="name"/> can't be a new table's name, or null if it can.</summary>
    public string? NameProblem(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "That can't be a table's name (it's its file's name).";
        return _tables.ContainsKey(name) ? $"There's a table called {name} already." : null;
    }

    /// <summary>
    /// Puts <paramref name="after"/> in place of the table of its name, as one undoable step described by
    /// <paramref name="description"/> ("Set Fireball's Damage"): its file is saved straight away and the running game
    /// takes it as its next step begins. Nothing happens if it's the same as the table it replaces.
    /// </summary>
    public void Commit(Table after, string description)
    {
        var entry = Entry(after.Name) ?? throw new InvalidOperationException($"There's no table called {after.Name}.");
        var before = entry.Table ?? throw new InvalidOperationException($"The {entry.Name} table doesn't read, so it can't be changed: {entry.Problem}");
        if (TableFile.Write(before) == TableFile.Write(after))
            return;
        var history = HistoryOf(entry.Name);
        history.Done.Add((before, after, description));
        history.Undone.Clear();
        Use(entry, after);
    }

    /// <summary>Undoes the last edit of the table called <paramref name="name"/>. False if there's none.</summary>
    public bool Undo(string name)
    {
        // A file written so it doesn't read goes back to the table it was.
        if (Entry(name) is { Table: null } broken && _lastGood.Remove(broken.Name, out var good))
        {
            Use(broken, good);
            return true;
        }

        if (Entry(name) is not { } entry || !_histories.TryGetValue(entry.Name, out var history) || history.Done.Count == 0)
            return false;
        var step = history.Done[^1];
        history.Done.RemoveAt(history.Done.Count - 1);
        history.Undone.Add(step);
        Use(entry, step.Before);
        return true;
    }

    /// <summary>Redoes the last edit undone on the table called <paramref name="name"/>. False if there's none.</summary>
    public bool Redo(string name)
    {
        if (Entry(name) is not { } entry || !_histories.TryGetValue(entry.Name, out var history) || history.Undone.Count == 0)
            return false;
        var step = history.Undone[^1];
        history.Undone.RemoveAt(history.Undone.Count - 1);
        history.Done.Add(step);
        Use(entry, step.After);
        return true;
    }

    private History HistoryOf(string name) => _histories.TryGetValue(name, out var history) ? history : _histories[name] = new History();

    private void Use(TableEntry entry, Table table)
    {
        if (_workspace.Project is not { } project)
            return;
        Write(table, Path.Combine(project.AssetsPath, entry.AssetPath));
        _tables[entry.Name] = entry with { Table = table, Problem = null };
        _lastGood.Remove(entry.Name);
        _play.StageTable(table);
        Raise(entry.Name);
    }

    private void Write(Table table, string path)
    {
        var text = TableFile.Write(table);
        _written[Path.GetFullPath(path)] = text;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".saving";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    // Says a table changed, and each table whose formulas read it and came out differently.
    private void Raise(string name)
    {
        var others = Reevaluate();
        OnPropertyChanged(nameof(Tables));
        OnPropertyChanged(nameof(Names));
        Changed?.Invoke(this, name);
        foreach (var other in others.Where(o => !string.Equals(o, name, StringComparison.OrdinalIgnoreCase)))
            Changed?.Invoke(this, other);
    }

    // ----- Files

    private void LoadAll()
    {
        _watcher?.Dispose();
        _watcher = null;
        _tables.Clear();
        _histories.Clear();
        _written.Clear();
        if (_workspace.Project is { } project)
        {
            foreach (var file in project.FindTables())
            {
                var entry = Read(file);
                _tables.TryAdd(entry.Name, entry);
            }

            Watch(project.AssetsPath);
        }

        _worked.Clear();
        Reevaluate();
        OnPropertyChanged(nameof(Tables));
        OnPropertyChanged(nameof(Names));
        Changed?.Invoke(this, "");
    }

    private TableEntry Read(string file)
    {
        var name = TableFile.NameOf(file);
        try
        {
            return new TableEntry(name, AssetPathOf(file), TableFile.Load(file), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new TableEntry(name, AssetPathOf(file), null, ex.Message);
        }
    }

    private string AssetPathOf(string file) =>
        _workspace.Project is { } project ? Path.GetRelativePath(project.AssetsPath, file).Replace('\\', '/') : Path.GetFileName(file);

    private void Watch(string assets)
    {
        if (!Directory.Exists(assets))
            return;
        _watcher = new FileSystemWatcher(assets, "*" + TableFile.Extension)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        void Changed(string path) => _reload.Dispatcher.InvokeAsync(() =>
        {
            _changedOnDisk.Add(path);
            _reload.Stop();
            _reload.Start();
        });
        _watcher.Changed += (_, e) => Changed(e.FullPath);
        _watcher.Created += (_, e) => Changed(e.FullPath);
        _watcher.Deleted += (_, e) => Changed(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Changed(e.OldFullPath);
            Changed(e.FullPath);
        };
        _watcher.EnableRaisingEvents = true;
    }

    private void ReloadChanged()
    {
        _reload.Stop();
        var paths = _changedOnDisk.ToList();
        _changedOnDisk.Clear();
        foreach (var path in paths.Where(p => p.EndsWith(TableFile.Extension, StringComparison.OrdinalIgnoreCase)))
            Reload(path);
    }

    /// <summary>
    /// Reads a table's file again after it changed outside the editor's own windows (an agent wrote it, or it was deleted),
    /// and says what came of it: e.g. "The Spells table: 4 columns, 12 rows", or why it doesn't read. Null for a file
    /// that isn't a table.
    /// </summary>
    public AssetFeedback? AssetChanged(string assetPath)
    {
        if (!assetPath.EndsWith(TableFile.Extension, StringComparison.OrdinalIgnoreCase) || _workspace.Project is not { } project)
            return null;
        var entry = Reload(Path.Combine(project.AssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        return entry switch
        {
            null => new AssetFeedback($"The {TableFile.NameOf(assetPath)} table is gone."),
            { Problem: { } problem } => new AssetFeedback($"The table doesn't read, so the game goes without it until it's fixed: {problem}", IsError: true),
            { Table: { } table } => new AssetFeedback($"The {table.Name} table: {Count(table.Columns.Count, "column")}, {Count(table.Rows.Count, "row")}" +
                $"{(_play.IsPlaying ? " (and in the running game)" : "")}.{FormulaFeedback(table.Name)}"),
            _ => null,
        };
    }

    // What its formulas work out to be wrong, for an agent that just wrote it.
    private string FormulaFeedback(string name)
    {
        var problems = _evaluation.Warnings.Where(w => w.StartsWith($"The {name} table's ", StringComparison.OrdinalIgnoreCase)).ToList();
        if (problems.Count > 0)
            return $" Its formulas have {Count(problems.Count, "problem")} (their cells are empty until they're put right): {string.Join(" ", problems.Take(5))}";
        return _evaluation.Find(name)?.HasFormulas == true ? " Its formulas all work out (read their values with ui_invoke Tables Values or Cell)." : "";
    }

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";

    // The file's table as it is on disk now: in place of what was loaded (a step in its history, so it can be undone), or
    // gone if the file is. Null if it's gone; unchanged if it's what this store last wrote.
    private TableEntry? Reload(string path)
    {
        var full = Path.GetFullPath(path);
        var name = TableFile.NameOf(full);
        _tables.TryGetValue(name, out var old);
        if (!File.Exists(full))
        {
            // Another file of the same name (in another folder) may be the table now.
            var other = _workspace.Project?.FindTables().FirstOrDefault(f => !string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)
                && string.Equals(TableFile.NameOf(f), name, StringComparison.OrdinalIgnoreCase));
            if (old is null || !string.Equals(Path.GetFullPath(Path.Combine(_workspace.Project!.AssetsPath, old.AssetPath)), full, StringComparison.OrdinalIgnoreCase))
                return old;
            _written.Remove(full);
            if (other is not null)
                return Reload(other);
            _tables.Remove(name);
            _histories.Remove(name);
            _play.StageTableRemoval(name);
            _logger.LogInformation("The {Table} table's file is gone", name);
            Raise(name);
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (IOException)
        {
            // Still being written: the watcher will say when it's done.
            return old;
        }

        if (_written.TryGetValue(full, out var ours) && ours == text && old is not null)
            return old;
        _written.Remove(full);

        TableEntry entry;
        try
        {
            entry = new TableEntry(name, AssetPathOf(full), TableFile.Read(text, name), null);
        }
        catch (InvalidDataException ex)
        {
            entry = new TableEntry(name, AssetPathOf(full), null, ex.Message);
        }

        // Read again with nothing changed (the watcher after an agent's write has been taken in already).
        if (entry.Table is { } same && old?.Table is { } loaded && TableFile.Write(same) == TableFile.Write(loaded))
            return old;

        _tables[name] = entry;
        if (entry.Table is { } table)
        {
            if (old?.Table is { } before)
            {
                var history = HistoryOf(name);
                history.Done.Add((before, table, "Change the file"));
                history.Undone.Clear();
            }

            _lastGood.Remove(name);
            _play.StageTable(table);
        }
        else
        {
            _logger.LogWarning("The {Table} table doesn't read: {Problem}", name, entry.Problem);
            if ((old?.Table ?? (_lastGood.TryGetValue(name, out var good) ? good : null)) is { } last)
                _lastGood[name] = last;
            _play.StageTableProblem(name, entry.Problem!);
        }

        Raise(name);
        return entry;
    }

    /// <summary>Stops watching the project's tables (the editor is closing).</summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _reload.Stop();
    }
}
