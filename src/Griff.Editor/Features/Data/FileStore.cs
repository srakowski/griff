using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Data;

/// <summary>A file of the open project a store keeps loaded: its name, its path in Assets, what it holds, or why it doesn't read.</summary>
public sealed record StoredFile<T>(string Name, string AssetPath, T? Value, string? Problem)
    where T : class
{
    public override string ToString() => Name;
}

/// <summary>
/// The open project's files of one kind (tables, simulations), kept loaded: every file of its extension in Assets, read
/// again when it changes on disk (an agent's asset_write, a text editor, a git checkout). Every change to one goes through
/// <see cref="Commit"/>, which saves its file straight away and is a step in that file's undo history (an
/// <see cref="EditHistory"/>, as a scene's is), so a panel and agents change them the same way and Edit &gt; Undo takes it
/// back. A change to a file from outside is a step in its history too, so it can be undone, whether or not the file then
/// reads; what the store last wrote to each file isn't read back as a change.
/// </summary>
public abstract class FileStore<T> : ObservableObject, IDisposable
    where T : class
{
    private readonly Dictionary<string, Stored> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EditHistory> _histories = new(StringComparer.OrdinalIgnoreCase);
    // What this store last wrote to each file (by its full path), so its own saves aren't read back as changes from outside.
    private readonly Dictionary<string, string> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changedOnDisk = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _reload;
    private FileSystemWatcher? _watcher;

    // A file as it's known: its entry, and its text (what undo writes back).
    private sealed record Stored(StoredFile<T> Entry, string Text);

    /// <summary>A file's contents at one step of its history: what it holds (or why it doesn't read) and its text.</summary>
    private sealed record State(T? Value, string Text, string? Problem);

    protected FileStore(IProjectSession session, ILogger logger)
    {
        Session = session;
        Logger = logger;
        _reload = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => ReloadChanged(), Dispatcher.CurrentDispatcher);
        _reload.Stop();
    }

    /// <summary>The open project.</summary>
    protected IProjectSession Session { get; }

    protected ILogger Logger { get; }

    /// <summary>The files' extension, ".table".</summary>
    protected abstract string Extension { get; }

    /// <summary>What one is called, "table".</summary>
    protected abstract string Kind { get; }

    /// <summary>Reads a file's text as the one called <paramref name="name"/>; throws <see cref="InvalidDataException"/> if it doesn't read.</summary>
    protected abstract T Read(string text, string name);

    /// <summary>A file's text for <paramref name="value"/>.</summary>
    protected abstract string Write(T value);

    /// <summary>The name <paramref name="value"/> goes by, which is its file's.</summary>
    protected abstract string NameOf(T value);

    /// <summary><paramref name="value"/> named <paramref name="name"/> (its file's), for one whose name is kept in it.</summary>
    protected virtual T Named(T value, string name) => value;

    /// <summary>After a file's value changed (an edit, an undo, a change on disk): says so, and whatever else it means.</summary>
    protected virtual void OnChanged(string name)
    {
        OnPropertyChanged(nameof(Names));
        RaiseChanged(name);
    }

    /// <summary>After a file reads as <paramref name="value"/> (made, changed, undone, or changed on disk).</summary>
    protected virtual void OnUsed(StoredFile<T> entry, T value)
    {
    }

    /// <summary>After a file was changed on disk so it doesn't read.</summary>
    protected virtual void OnUnreadable(string name, string problem)
    {
    }

    /// <summary>After a file was deleted.</summary>
    protected virtual void OnGone(string name)
    {
    }

    /// <summary>
    /// Says what came of an agent writing a file (or deleting it: <paramref name="entry"/> is null then), e.g. "The Spells
    /// table: 4 columns, 12 rows".
    /// </summary>
    protected abstract AssetFeedback Feedback(string assetPath, StoredFile<T>? entry);

    /// <summary>Raised after a file changes (an edit, an undo, its file changed or deleted), with its name ("" for all of them).</summary>
    public event EventHandler<string>? Changed;

    protected void RaiseChanged(string name) => Changed?.Invoke(this, name);

    /// <summary>The project's files, alphabetically.</summary>
    public IReadOnlyList<StoredFile<T>> Files => [.. _files.Values.Select(f => f.Entry).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Their names, alphabetically.</summary>
    public IReadOnlyList<string> Names => [.. Files.Select(f => f.Name)];

    /// <summary>The first one's name, alphabetically, or null with none.</summary>
    public string? FirstName => Names is [var first, ..] ? first : null;

    /// <summary>The one called <paramref name="name"/> (or at that path in Assets), or null.</summary>
    public StoredFile<T>? Entry(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _files.GetValueOrDefault(Path.GetFileNameWithoutExtension(name.Trim().Replace('\\', '/')))?.Entry;

    /// <summary>What the one called <paramref name="name"/> holds, or null if there's none (or its file doesn't read).</summary>
    public T? Find(string? name) => Entry(name)?.Value;

    /// <summary>The undo history of the one called <paramref name="name"/>, or null if there's none.</summary>
    public EditHistory? HistoryOf(string? name) =>
        Entry(name) is { } entry ? _histories.TryGetValue(entry.Name, out var history) ? history : _histories[entry.Name] = new EditHistory() : null;

    /// <summary>Reads the project's files and watches them; a store calls it once it's set up.</summary>
    protected void Start()
    {
        Session.AddAssetListener(AssetChanged);
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
                LoadAll();
        };
        LoadAll();
    }

    /// <summary>
    /// Makes a new file at <paramref name="path"/> holding <paramref name="value"/>, with a history of its own, and returns
    /// what it holds.
    /// </summary>
    protected T Add(string path, T value)
    {
        var name = NameOf(value);
        var text = Write(value);
        WriteFile(path, text);
        var entry = new StoredFile<T>(name, AssetPathOf(path), value, null);
        _files[name] = new Stored(entry, text);
        _histories.Remove(name);
        Logger.LogInformation("Made the {Name} {Kind}", name, Kind);
        OnUsed(entry, value);
        OnChanged(name);
        return value;
    }

    /// <summary>
    /// Puts <paramref name="after"/> in place of the file of its name, as one step of its undo history described by
    /// <paramref name="description"/> ("Set Fireball's Damage"): its file is saved straight away. Nothing happens if it's the
    /// same as what it replaces.
    /// </summary>
    public void Commit(T after, string description)
    {
        var name = NameOf(after);
        var stored = Entry(name) is { } entry ? _files[entry.Name] : throw new InvalidOperationException($"There's no {Kind} called {name}.");
        if (stored.Entry.Value is not { } before)
            throw new InvalidOperationException($"The {stored.Entry.Name} {Kind} doesn't read, so it can't be changed: {stored.Entry.Problem}");
        var text = Write(after);
        if (text == stored.Text || text == Write(before))
            return;
        HistoryOf(stored.Entry.Name)!.Execute(new FileEdit(this, stored.Entry.Name, description,
            new State(before, stored.Text, null), new State(after, text, null)));
    }

    // Puts a file as it was at one step of its history: written, and read (or not) as that step had it.
    private void Put(string name, State state)
    {
        if (!_files.TryGetValue(name, out var stored) || Session.Project is not { } project)
            return;
        WriteFile(Path.Combine(project.AssetsPath, stored.Entry.AssetPath.Replace('/', Path.DirectorySeparatorChar)), state.Text);
        var value = state.Value is null ? null : Named(state.Value, stored.Entry.Name);
        var entry = stored.Entry with { Value = value, Problem = state.Problem };
        _files[name] = new Stored(entry, state.Text);
        if (value is not null)
            OnUsed(entry, value);
        else
            OnUnreadable(name, state.Problem ?? "");
        OnChanged(name);
    }

    private void WriteFile(string path, string text)
    {
        _written[Path.GetFullPath(path)] = text;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".saving";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    private string AssetPathOf(string path) =>
        Session.Project is { } project ? Path.GetRelativePath(project.AssetsPath, path).Replace('\\', '/') : Path.GetFileName(path);

    // A step of a file's history: its text and what it held, before and after.
    private sealed class FileEdit(FileStore<T> store, string name, string description, State before, State after) : IEdit
    {
        public string Description => description;

        public void Apply() => store.Put(name, after);

        public void Revert() => store.Put(name, before);
    }

    // ----- Files

    private void LoadAll()
    {
        _watcher?.Dispose();
        _watcher = null;
        _files.Clear();
        _histories.Clear();
        _written.Clear();
        if (Session.Project is { } project && Directory.Exists(project.AssetsPath))
        {
            foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*" + Extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (ReadFile(file) is { } stored)
                    _files.TryAdd(stored.Entry.Name, stored);
            }

            Watch(project.AssetsPath);
        }

        OnChanged("");
    }

    // The file as it is on disk, or null if it can't be read just now (still being written).
    private Stored? ReadFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Stored(new StoredFile<T>(Path.GetFileNameWithoutExtension(path), AssetPathOf(path), null, ex.Message), "");
        }

        var state = Parse(text, Path.GetFileNameWithoutExtension(path));
        return new Stored(new StoredFile<T>(Path.GetFileNameWithoutExtension(path), AssetPathOf(path), state.Value, state.Problem), text);
    }

    private State Parse(string text, string name)
    {
        try
        {
            return new State(Named(Read(text, name), name), text, null);
        }
        catch (InvalidDataException ex)
        {
            return new State(null, text, ex.Message);
        }
    }

    // Changes made outside the editor (a text editor, git) are read once they've settled.
    private void Watch(string assets)
    {
        try
        {
            _watcher = new FileSystemWatcher(assets, "*" + Extension)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            Logger.LogWarning(ex, "Can't watch the project's {Kind} files for changes", Kind);
            return;
        }

        void Changed(string path)
        {
            lock (_changedOnDisk)
                _changedOnDisk.Add(path);
            _reload.Dispatcher.InvokeAsync(() =>
            {
                _reload.Stop();
                _reload.Start();
            });
        }

        _watcher.Changed += (_, e) => Changed(e.FullPath);
        _watcher.Created += (_, e) => Changed(e.FullPath);
        _watcher.Deleted += (_, e) => Changed(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Changed(e.OldFullPath);
            Changed(e.FullPath);
        };
    }

    private void ReloadChanged()
    {
        _reload.Stop();
        List<string> paths;
        lock (_changedOnDisk)
        {
            paths = [.. _changedOnDisk];
            _changedOnDisk.Clear();
        }

        foreach (var path in paths.Where(p => p.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)))
            Reload(path);
    }

    /// <summary>
    /// Reads a file again after it changed outside the editor's own windows (an agent wrote it, or it was deleted), and
    /// says what came of it. Null for a file of another kind.
    /// </summary>
    public AssetFeedback? AssetChanged(string assetPath)
    {
        if (!assetPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || Session.Project is not { } project)
            return null;
        return Feedback(assetPath, Reload(Path.Combine(project.AssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    // The file as it is on disk now: in place of what was loaded (a step in its history, so it can be undone), or gone if
    // the file is. Null if it's gone; unchanged if it's what this store last wrote.
    private StoredFile<T>? Reload(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileNameWithoutExtension(full);
        _files.TryGetValue(name, out var old);
        if (!File.Exists(full))
        {
            if (old is null || !string.Equals(Path.GetFullPath(Path.Combine(Session.Project!.AssetsPath, old.Entry.AssetPath)), full, StringComparison.OrdinalIgnoreCase))
                return old?.Entry;
            _written.Remove(full);
            // Another file of the same name (in another folder) may be the one now.
            var other = Directory.EnumerateFiles(Session.Project.AssetsPath, "*" + Extension, SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(f => !string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            _files.Remove(name);
            _histories.Remove(name);
            if (other is not null && ReadFile(other) is { } moved)
            {
                _files[name] = moved;
                if (moved.Entry.Value is { } value)
                    OnUsed(moved.Entry, value);
                OnChanged(name);
                return moved.Entry;
            }

            Logger.LogInformation("The {Name} {Kind}'s file is gone", name, Kind);
            OnGone(name);
            OnChanged(name);
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
            return old?.Entry;
        }

        if (_written.TryGetValue(full, out var ours) && ours == text && old is not null)
            return old.Entry;
        _written.Remove(full);

        var state = Parse(text, name);
        // Read again with nothing changed (the watcher after an agent's write has been taken in already).
        if (old is not null && (text == old.Text || state.Value is { } same && old.Entry.Value is { } loaded && Write(same) == Write(loaded)))
            return old.Entry;

        var entry = new StoredFile<T>(name, AssetPathOf(full), state.Value, state.Problem);
        _files[name] = new Stored(entry, text);
        if (old is not null)
        {
            HistoryOf(name)!.RecordDone(new FileEdit(this, name, state.Value is null ? "Change the file so it doesn't read" : "Change the file",
                new State(old.Entry.Value, old.Text, old.Entry.Problem), state));
        }

        if (state.Value is { } read)
        {
            OnUsed(entry, read);
        }
        else
        {
            Logger.LogWarning("The {Name} {Kind} doesn't read: {Problem}", name, Kind, state.Problem);
            OnUnreadable(name, state.Problem!);
        }

        OnChanged(name);
        return entry;
    }

    /// <summary>Stops watching the project's files (the editor is closing).</summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _reload.Stop();
        GC.SuppressFinalize(this);
    }
}
