namespace Griff.Projects;

/// <summary>
/// Where a game's files come from: its images, sounds, fonts and the rest, in its Assets folder. The host says how they
/// behave: an exported game's are fixed (<see cref="AssetFolder"/>), a browser's were fetched before the game started
/// (<see cref="PreloadedAssets"/>), and the editor's change as they're edited, which it's told of
/// (<see cref="WatchedAssetFolder"/>), so a sprite saved from a paint program shows in the running game. Nothing that
/// reads them checks a file's time: what changes, the source says.
/// </summary>
public interface IAssetSource
{
    /// <summary>The folder the files are in, which every name is relative to.</summary>
    string Root { get; }

    /// <summary>Where the file <paramref name="name"/> names is, or would be ('/' or '\' between folders, relative to <see cref="Root"/>).</summary>
    string PathOf(string name);

    /// <summary>Whether there's a file at <paramref name="path"/> (one <see cref="PathOf"/> gave).</summary>
    bool Exists(string path);

    /// <summary>
    /// Raised with a file's path (as <see cref="PathOf"/> gives it) when it's written, made, renamed or deleted, once it's
    /// done: only by a source whose files change while a game plays (the editor's). It may come on any thread.
    /// </summary>
    event Action<string>? Changed;
}

/// <summary>
/// A game's files in a folder that doesn't change while it plays: an exported game's. Nothing is watched or checked again;
/// a file is read when it's first wanted.
/// </summary>
public class AssetFolder(string? root) : IAssetSource
{
    /// <summary>No files at all (nothing is found).</summary>
    public static AssetFolder None { get; } = new(null);

    public string Root { get; } = string.IsNullOrEmpty(root) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public string PathOf(string name) => Path.Combine(Root, name.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));

    public virtual bool Exists(string path) => Root.Length > 0 && File.Exists(path);

    /// <summary>Never raised: the files are fixed.</summary>
    public virtual event Action<string>? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>
/// A game's files fetched before it started, as a browser has them (it can't look at a server's folder): only the files
/// named are there, found without asking the file system, and they don't change.
/// </summary>
/// <param name="root">The game's Assets folder, where its files were put.</param>
/// <param name="files">The paths of the files fetched (the game's other files may be among them).</param>
public sealed class PreloadedAssets(string root, IEnumerable<string> files) : AssetFolder(root)
{
    private readonly HashSet<string> _paths = files.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public override bool Exists(string path) => _paths.Contains(Path.GetFullPath(path));
}

/// <summary>
/// A game's files in a folder that's edited while the game plays: the editor's project. A file written, made, renamed or
/// deleted is said once it has settled (<see cref="IAssetSource.Changed"/>): written in bursts, or still held by the
/// program saving it, it's said once it has been quiet a moment and can be read.
/// </summary>
public sealed class WatchedAssetFolder : AssetFolder, IDisposable
{
    // How long a file stays quiet before it's said to have changed.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    private readonly FileSystemWatcher? _watcher;
    private readonly Dictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private Action<string>? _changed;

    /// <param name="root">The folder, watched from now on (and made if it isn't there).</param>
    public WatchedAssetFolder(string root)
        : base(root)
    {
        _timer = new Timer(_ => Settled());
        Directory.CreateDirectory(Root);
        _watcher = new FileSystemWatcher(Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += (_, e) => Touch(e.FullPath);
        _watcher.Created += (_, e) => Touch(e.FullPath);
        _watcher.Deleted += (_, e) => Touch(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Touch(e.OldFullPath);
            Touch(e.FullPath);
        };
        _watcher.EnableRaisingEvents = true;
    }

    public override event Action<string>? Changed
    {
        add
        {
            lock (_pending)
                _changed += value;
        }
        remove
        {
            lock (_pending)
                _changed -= value;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
        lock (_pending)
        {
            _pending.Clear();
            _changed = null;
        }
    }

    /// <summary>Says <paramref name="path"/> changed once it has settled, as if the watcher had seen it (for a change made by the editor itself).</summary>
    public void Touch(string path)
    {
        lock (_pending)
        {
            _pending[path] = DateTime.UtcNow;
            _timer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    // The files quiet for long enough, and readable (or gone), are said; the rest are looked at again shortly.
    private void Settled()
    {
        var ready = new List<string>();
        Action<string>? changed;
        lock (_pending)
        {
            var now = DateTime.UtcNow;
            foreach (var (path, at) in _pending)
            {
                if (now - at >= Settle && Readable(path))
                    ready.Add(path);
            }

            foreach (var path in ready)
                _pending.Remove(path);
            if (_pending.Count > 0)
                _timer.Change(Settle, Timeout.InfiniteTimeSpan);
            changed = _changed;
        }

        foreach (var path in ready)
            changed?.Invoke(path);
    }

    // A file still held by the program writing it can't be read yet; one that's gone (or a folder) can be said at once.
    private static bool Readable(string path)
    {
        if (!File.Exists(path))
            return true;
        try
        {
            using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
