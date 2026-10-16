using System.Collections.Concurrent;
using Griff.Hosting;

namespace Griff.Fonts;

/// <summary>
/// The font files in a project's Assets folder (.ttf, .otf or .ttc), which ship with its game: the game maker's own to
/// license. Each is read the first time it's named and again when its <see cref="IAssetSource"/> says the file changed.
/// </summary>
public sealed class ProjectFonts : IDisposable
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    private readonly IAssetSource? _assets;
    private readonly Dictionary<string, FontFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FontFile> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _changed = new();

    /// <param name="assets">The game's files the fonts are among; null for none.</param>
    public ProjectFonts(IAssetSource? assets)
    {
        _assets = assets is { Root.Length: > 0 } ? assets : null;
        if (_assets is not null)
            _assets.Changed += _changed.Enqueue;
    }

    /// <summary>The Assets folder the files are in; null for none.</summary>
    public string? AssetRoot => _assets?.Root;

    /// <summary>Whether a font name is a font file (rather than an installed font's name).</summary>
    public static bool IsFontFile(string name) => Extensions.Any(e => name.Trim().EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>The font files in the Assets folder, relative to it with '/' between folders, in order.</summary>
    public IReadOnlyList<string> Files() =>
        AssetRoot is not { } root || !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsFontFile)
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>The face in a font file (relative to Assets), or null if it's missing, mid-save or not a font.</summary>
    public FontFace? Find(string path)
    {
        if (!_files.TryGetValue(path, out var file))
        {
            var full = _assets is null ? null : Path.GetFullPath(_assets.PathOf(path.Trim()));
            file = new FontFile(full, _assets);
            file.Load();
            _files[path] = file;
            if (full is not null)
                _byPath[full] = file;
        }

        return file.Face;
    }

    /// <summary>Reads again the files named so far that the source said changed since. True if one did.</summary>
    public bool RefreshChanged()
    {
        var changed = false;
        while (_changed.TryDequeue(out var path))
        {
            if (_byPath.TryGetValue(path, out var file))
            {
                file.Load();
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Stops hearing of the files' changes.</summary>
    public void Dispose()
    {
        if (_assets is not null)
            _assets.Changed -= _changed.Enqueue;
    }

    // A font file, read again when it changes.
    private sealed class FontFile(string? path, IAssetSource? assets)
    {
        public string? Path { get; } = path;

        public FontFace? Face { get; private set; }

        public void Load()
        {
            Face = null;
            if (Path is null || assets?.Exists(Path) != true)
                return;
            try
            {
                Face = FontFace.Load(File.ReadAllBytes(Path), Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
