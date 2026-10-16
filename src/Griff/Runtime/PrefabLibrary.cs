using System.Collections.Concurrent;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Creates entities from a project's prefab files, for <see cref="Game.PrefabLoader"/>. Each prefab is read once into a
/// fixed template (<see cref="EntityTemplate"/>) and every spawn is a copy of it, so spawning reads no file and parses no
/// JSON. Where the files come from says whether they change (<see cref="IAssetSource"/>): an exported game's never do;
/// in the editor, a prefab saved while a game plays is read again and the next spawn is of the new version
/// (<see cref="Changed"/> says so). One library is safe to spawn from on any number of threads at once (a balance
/// simulation's games, each on its own core).
/// </summary>
[NotState("the project's prefabs")]
public sealed class PrefabLibrary : IDisposable
{
    private readonly Project _project;
    private readonly ComponentRegistry _components;
    private readonly IAssetSource? _assets;

    // Where each name led ("Bullet" to its file), and each file's version as last read, with its template once spawned.
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Known> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="assets">
    /// Where the project's files come from, which says when one changes (the editor's are watched); with none, or a source
    /// whose files are fixed (an export's), each prefab is read once and kept.
    /// </param>
    public PrefabLibrary(Project project, ComponentRegistry components, IAssetSource? assets = null)
    {
        (_project, _components, _assets) = (project, components, assets);
        if (assets is not null)
            assets.Changed += OnAssetChanged;
    }

    /// <summary>
    /// Raised when a prefab file is found to be another version than the one last read (or made, or deleted): saved in the
    /// editor, written by an agent, or changed outside it (a pull, a text editor). Not raised for a version the editor said
    /// it wrote (<see cref="Remember"/>). It may come on any thread.
    /// </summary>
    public event Action<PrefabChange>? Changed;

    /// <summary>A new entity from the prefab named <paramref name="nameOrPath"/> ("Bullet" or "Prefabs/Bullet.prefab").</summary>
    /// <exception cref="FileNotFoundException">There's no such prefab.</exception>
    /// <exception cref="InvalidDataException">The prefab can't be read.</exception>
    public Entity Instantiate(string nameOrPath) => TemplateOf(PathOf(nameOrPath)).Instantiate();

    /// <summary>
    /// The version of the prefab file at <paramref name="path"/> (a full path) as this library has it: read now if it hasn't
    /// been, and kept until the file changes. Null if there's no such file or it isn't JSON.
    /// </summary>
    public PrefabVersion? VersionOf(string path)
    {
        path = Path.GetFullPath(path);
        if (_known.TryGetValue(path, out var known))
            return known.Version;
        PrefabVersion? version;
        try
        {
            version = Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            version = null;
        }

        return version is null ? null : _known.GetOrAdd(path, new Known(version, null)).Version;
    }

    /// <summary>
    /// Says the prefab file at <paramref name="path"/> is now <paramref name="version"/>, written by the editor itself, which
    /// has brought its copies up to date already: the next spawn is of it, and its file settling isn't a change.
    /// </summary>
    public void Remember(string path, PrefabVersion version)
    {
        path = Path.GetFullPath(path);
        _known.AddOrUpdate(path, new Known(version, null), (_, known) => known.Version.Equals(version) ? known : new Known(version, null));
        _paths.Clear();
    }

    /// <summary>Stops listening for changes to the files.</summary>
    public void Dispose()
    {
        if (_assets is not null)
            _assets.Changed -= OnAssetChanged;
    }

    // Spawning can happen many times a second; remember where each name led.
    private string PathOf(string nameOrPath)
    {
        if (_paths.TryGetValue(nameOrPath, out var path))
            return path;
        path = _project.FindPrefab(nameOrPath) ?? throw new FileNotFoundException($"No prefab named \"{nameOrPath}\".");
        return _paths.GetOrAdd(nameOrPath, Path.GetFullPath(path));
    }

    private EntityTemplate TemplateOf(string path)
    {
        var revision = _components.Revision;
        if (_known.TryGetValue(path, out var known) && known.Template is { } template && template.Revision == revision)
            return template;

        // Read with the component types as they are now, so reloaded scripts are picked up.
        var version = known?.Version ?? Read(path) ?? throw new FileNotFoundException($"No prefab at {path}.");
        template = new EntityTemplate(new SceneSerializer(_components).LoadPrefabFromString(version.Json), _components);

        // Kept unless the file changed meanwhile (then the next spawn reads the new version).
        var made = new Known(version, template);
        if (known is null)
            _known.TryAdd(path, made);
        else
            _known.TryUpdate(path, made, known);
        return template;
    }

    // A prefab file's version, or null if it's gone.
    private static PrefabVersion? Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        return PrefabVersion.Of(text);
    }

    private void OnAssetChanged(string path)
    {
        path = Path.GetFullPath(path);
        // Names lead elsewhere once a prefab (or a folder holding some) is made, moved or deleted.
        var isPrefab = path.EndsWith(Project.PrefabExtension, StringComparison.OrdinalIgnoreCase);
        if (isPrefab || !File.Exists(path))
            _paths.Clear();
        if (!isPrefab)
            return;

        _known.TryGetValue(path, out var before);
        PrefabVersion? now;
        try
        {
            now = Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Not readable as it is (half written, or not JSON): read again when next wanted, which says what's wrong.
            _known.TryRemove(path, out _);
            return;
        }

        if (now is null)
            _known.TryRemove(path, out _);
        else if (before?.Version.Equals(now) == true)
            return;
        else
            _known[path] = new Known(now, null);

        if (before is not null || now is not null)
            Changed?.Invoke(new PrefabChange(path, before?.Version, now));
    }

    private sealed record Known(PrefabVersion Version, EntityTemplate? Template);
}

/// <summary>A prefab file found to have changed (<see cref="PrefabLibrary.Changed"/>).</summary>
/// <param name="Path">The prefab file's full path.</param>
/// <param name="Before">The version it was, or null if it hadn't been read (or is new).</param>
/// <param name="Now">The version it is now, or null if it was deleted.</param>
public sealed record PrefabChange(string Path, PrefabVersion? Before, PrefabVersion? Now);
